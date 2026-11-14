/* Continuum native RemoteConsole browser client. BSD-3-Clause. */
(() => {
    'use strict';
    const workspace = document.getElementById('continuum_console_workspace');
    if (!workspace) return;
    const connectForm = workspace.querySelector('#continuum_console_connect');
    if (!connectForm) return;
    const endpoint = workspace.querySelector('#console-endpoint');
    const password = workspace.querySelector('#console-password');
    const commandForm = workspace.querySelector('#continuum_console_command');
    const command = workspace.querySelector('#console-command');
    const confirmation = workspace.querySelector('#console-command-confirm');
    const status = workspace.querySelector('#continuum_console_status');
    const output = workspace.querySelector('#continuum_console_output');
    let session = null;
    let pending = false;
    let connecting = false;
    let generation = 0;
    const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

    function controls() {
        for (const control of commandForm.elements) control.disabled = !session || pending;
        for (const control of connectForm.elements) {
            if (control.id !== 'continuum_console_disconnect') control.disabled = Boolean(session) || connecting;
        }
    }
    async function request(base, path, fields, signal) {
        const response = await fetch(new URL(path, base), {
            method: fields ? 'POST' : 'GET',
            headers: fields ? { 'Content-Type': 'application/x-www-form-urlencoded;charset=UTF-8' } : {},
            body: fields ? new URLSearchParams(fields) : undefined,
            redirect: 'error', credentials: 'omit', cache: 'no-store', referrerPolicy: 'no-referrer', signal
        });
        if (!response.ok || !response.body) throw new Error('Console request failed');
        const reader = response.body.getReader();
        const decoder = new TextDecoder('utf-8', { fatal: true });
        let count = 0;
        let text = '';
        try {
            for (;;) {
                const part = await reader.read();
                if (part.done) break;
                count += part.value.byteLength;
                if (count > 262144) throw new Error('Console response exceeded its bound');
                text += decoder.decode(part.value, { stream: true });
            }
            text += decoder.decode();
        } finally { await reader.cancel().catch(() => {}); }
        if (/<!DOCTYPE|<!ENTITY/i.test(text)) throw new Error('Unexpected console XML');
        const xml = new DOMParser().parseFromString(text, 'application/xml');
        if (xml.querySelector('parsererror') || xml.documentElement?.tagName !== 'ConsoleSession') throw new Error('Invalid console response');
        return xml;
    }
    async function bounded(base, path, fields) {
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 28000);
        try { return await request(base, path, fields, controller.signal); }
        finally { clearTimeout(timeout); }
    }
    function close(current) {
        if (!current) return;
        current.controller?.abort();
        // Session capability stays in memory; no cookies, browser storage or URL bar.
        bounded(current.base, 'CloseSession', { ID: current.id }).catch(() => {});
    }
    function disconnect(message) {
        const current = session;
        generation++;
        session = null;
        pending = false;
        password.value = '';
        command.value = '';
        confirmation.checked = false;
        close(current);
        status.textContent = message;
        controls();
    }
    async function poll(current) {
        while (session === current) {
            if (!workspace.isConnected || Date.now() - current.started >= 15 * 60 * 1000) {
                disconnect('Disconnected: page closed or session time limit reached');
                return;
            }
            const controller = new AbortController();
            current.controller = controller;
            const timeout = setTimeout(() => controller.abort(), 28000);
            try {
                const xml = await request(current.base, 'ReadResponses/' + current.id, null, controller.signal);
                if (session !== current) return;
                const lines = Array.from(xml.querySelectorAll('Line')).slice(-1000).map(line => line.textContent.slice(0, 8192));
                if (lines.length) {
                    const combined = output.textContent.split('\n').concat(lines);
                    output.textContent = combined.slice(-500).join('\n').slice(-262144);
                    output.scrollTop = output.scrollHeight;
                }
            } catch (_) {
                if (session === current) disconnect('Console connection interrupted. Command completion may be unknown; inspect the host before retrying.');
                return;
            } finally { clearTimeout(timeout); }
        }
    }
    connectForm.addEventListener('submit', async event => {
        event.preventDefault();
        if (connecting || session || !connectForm.reportValidity()) return;
        const selected = endpoint.selectedOptions[0];
        const base = selected?.value;
        if (!base) return;
        const connectGeneration = generation;
        connecting = true;
        controls();
        status.textContent = 'Connecting to ' + selected.textContent;
        const credentials = { USER: workspace.querySelector('#console-user').value, PASS: password.value };
        password.value = '';
        try {
            const xml = await bounded(base, 'StartSession', credentials);
            credentials.PASS = '';
            const id = xml.querySelector('SessionID')?.textContent;
            if (!id || !uuid.test(id) || id === '00000000-0000-0000-0000-000000000000') throw new Error('Invalid session');
            const current = { base, id, started: Date.now(), controller: null };
            if (!workspace.isConnected || generation !== connectGeneration) { close(current); return; }
            session = current;
            output.textContent = '';
            status.textContent = 'Connected to ' + selected.textContent;
            void poll(current);
        } catch (_) { status.textContent = 'Connection failed. Check console credentials, TLS and the host ConsoleAllowedOrigin setting.'; }
        finally { credentials.PASS = ''; connecting = false; controls(); }
    });
    workspace.querySelector('#continuum_console_disconnect').addEventListener('click', () => disconnect('Disconnected'));
    commandForm.addEventListener('submit', async event => {
        event.preventDefault();
        if (!session || pending || !commandForm.reportValidity()) return;
        const current = session;
        const value = command.value;
        if (!value.trim() || value.length > 2048 || /[\r\n\x00]/.test(value)) return;
        pending = true;
        confirmation.checked = false;
        controls();
        try {
            const xml = await bounded(current.base, 'SessionCommand', { ID: current.id, COMMAND: value });
            if (xml.querySelector('Result')?.textContent !== 'OK') throw new Error('Command not acknowledged');
            if (session === current) { command.value = ''; status.textContent = 'Command submitted. Inspect console output for completion.'; }
        } catch (_) {
            if (session === current) status.textContent = 'Command outcome is unconfirmed. Inspect the host and console output before retrying.';
        } finally { if (session === current) { pending = false; controls(); } }
    });
    window.addEventListener('pagehide', () => disconnect('Disconnected'), { once: true });
    controls();
})();
