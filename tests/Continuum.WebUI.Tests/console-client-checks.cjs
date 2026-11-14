// Execute the browser client's event handlers against a controlled native-console transport.
const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const path = require('node:path');

async function run() {
    const requests = [];
    const elements = new Map();
    function element(id) {
        const value = { id, value: '', textContent: '', checked: false, disabled: false, listeners: {},
            addEventListener(type, handler) { this.listeners[type] = handler; }, reportValidity() { return true; } };
        elements.set('#' + id, value); return value;
    }
    const connect = element('continuum_console_connect');
    const endpoint = element('console-endpoint');
    endpoint.selectedOptions = [{ value: 'https://console.example/native/', textContent: 'Simulator West' }];
    const user = element('console-user'); const password = element('console-password');
    const disconnect = element('continuum_console_disconnect');
    connect.elements = [endpoint, user, password, disconnect, element('connect-button')];
    const commandForm = element('continuum_console_command'); const command = element('console-command');
    const confirmation = element('console-command-confirm'); commandForm.elements = [command, confirmation, element('command-button')];
    commandForm.reportValidity = () => confirmation.checked && Boolean(command.value);
    const status = element('continuum_console_status'); const output = element('continuum_console_output');
    const workspace = { isConnected: true, querySelector: selector => elements.get(selector) };
    const lifecycle = {};
    const sessionID = '11111111-2222-4333-8444-555555555555';
    let failCommand = false;
    let deferredStart = null;
    const context = {
        document: { getElementById: () => workspace },
        window: { addEventListener: (name, callback) => { lifecycle[name] = callback; } },
        URL, URLSearchParams, AbortController, TextDecoder, setTimeout, clearTimeout, Date,
        DOMParser: class {
            parseFromString(text) {
                return { documentElement: { tagName: 'ConsoleSession' }, querySelector(name) {
                    if (name === 'SessionID') return { textContent: sessionID };
                    if (name === 'Result') return { textContent: 'OK' };
                    return null;
                }, querySelectorAll() { return []; } };
            }
        },
        fetch: async (url, options) => {
            requests.push({ url: url.href, options, body: options.body?.toString() });
            if (url.pathname.includes('/ReadResponses/')) return new Promise((resolve, reject) => options.signal.addEventListener('abort', () => reject(new Error('aborted')), { once: true }));
            if (url.pathname.endsWith('/StartSession') && deferredStart) return deferredStart;
            if (url.pathname.endsWith('/SessionCommand') && failCommand) throw new Error('lost response');
            return new Response('<ConsoleSession><SessionID>' + sessionID + '</SessionID><Result>OK</Result></ConsoleSession>');
        }
    };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../addon-modules/ContinuumWebUI/html/static/js/continuum-console.js'), 'utf8'), context);
    const submit = () => ({ preventDefault() {} });
    user.value = 'operator'; password.value = 'fixture-password';
    await connect.listeners.submit(submit());
    assert.match(status.textContent, /^Connected/);
    assert.equal(password.value, '');
    const login = requests.find(item => item.url.endsWith('/StartSession'));
    assert.equal(login.body, 'USER=operator&PASS=fixture-password');
    assert.equal(login.options.credentials, 'omit'); assert.equal(login.options.redirect, 'error'); assert.equal(login.options.referrerPolicy, 'no-referrer');
    command.value = 'show info'; confirmation.checked = false;
    await commandForm.listeners.submit(submit());
    assert.equal(requests.filter(item => item.url.endsWith('/SessionCommand')).length, 0, 'Missing confirmation submitted a command');
    confirmation.checked = true; await commandForm.listeners.submit(submit());
    assert.equal(requests.filter(item => item.url.endsWith('/SessionCommand')).length, 1);
    assert.equal(requests.find(item => item.url.endsWith('/SessionCommand')).body, 'ID=' + sessionID + '&COMMAND=show+info');
    assert.equal(confirmation.checked, false); assert.equal(command.value, '');
    failCommand = true; command.value = 'show regions'; confirmation.checked = true;
    await commandForm.listeners.submit(submit());
    assert.match(status.textContent, /unconfirmed/);
    assert.equal(requests.filter(item => item.url.endsWith('/SessionCommand')).length, 2, 'Unconfirmed command was automatically retried');
    disconnect.listeners.click();
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(command.disabled, true);
    assert.ok(requests.some(item => item.url.endsWith('/CloseSession') && item.body === 'ID=' + sessionID));
    assert.equal(status.textContent, 'Disconnected');
    // Disconnect during an in-flight handshake must close the eventual capability.
    let complete;
    deferredStart = new Promise(resolve => { complete = resolve; });
    password.value = 'another-password';
    const connecting = connect.listeners.submit(submit());
    disconnect.listeners.click();
    complete(new Response('<ConsoleSession><SessionID>' + sessionID + '</SessionID></ConsoleSession>'));
    await connecting;
    assert.equal(status.textContent, 'Disconnected'); assert.equal(command.disabled, true);
    assert.equal(requests.filter(item => item.url.endsWith('/CloseSession')).length, 2);
    lifecycle.pagehide();
    assert.equal(requests.filter(item => item.url.endsWith('/SessionCommand')).length, 2);
    console.log('PASS: browser console native request contract, credential clearing, consent, explicit unconfirmed outcomes, no automatic command retry and disconnect/handshake cleanup.');
}
run().catch(error => { console.error(error); process.exitCode = 1; });
