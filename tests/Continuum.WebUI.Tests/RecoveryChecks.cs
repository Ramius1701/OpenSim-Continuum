using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using OpenSim.Continuum.WebUI;
using OpenSim.Services.Interfaces;
using OpenSim.Framework.Servers.HttpServer;
using OpenMetaverse;
using Nini.Config;

static class RecoveryChecks
{
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run(string root)
    {
        string file = Path.Combine(root, "recovery.json");
        DateTime now = DateTime.UtcNow;
        var store = new PasswordRecoveryStore(file, () => now);
        Guid account = Guid.NewGuid();
        string proof = PasswordRecoveryStore.Hash("credential-one");
        var identity = new RecoveryIdentity { Account = account, Email = "resident@example.test", CredentialProof = proof };
        RecoveryIdentity Find(Guid id) => id == account ? identity : null;
        string token = store.Issue(identity);
        Assert(token.Length == 64 && !File.ReadAllText(file).Contains(token), "Raw token persisted");
        var afterRestart = new PasswordRecoveryStore(file, () => now);
        Assert(afterRestart.Consume(token, Find) == account && store.Consume(token, Find) == Guid.Empty, "Token reuse/restart handling failed");
        token = store.Issue(identity); identity.Email = "changed@example.test";
        Assert(store.Consume(token, Find) == Guid.Empty, "Token survived an account email change");
        identity.Email = "resident@example.test";
        token = store.Issue(identity); identity.CredentialProof = PasswordRecoveryStore.Hash("new-credential");
        Assert(store.Consume(token, Find) == Guid.Empty, "Token survived a password change");
        Assert(store.Issue(identity) == null, "Per-account request limit failed");
        now = now.AddMinutes(31);
        token = store.Issue(identity);
        now = now.AddMinutes(31);
        Assert(store.Consume(token, Find) == Guid.Empty, "Expired token accepted");
        token = store.Issue(identity);
        int consumed = 0;
        System.Threading.Tasks.Parallel.For(0, 16, _ => { if (store.Consume(token, Find) == account) Interlocked.Increment(ref consumed); });
        Assert(consumed == 1, "Concurrent token replay accepted more than once");

        var sent = new ConcurrentQueue<string>();
        using var delivered = new AutoResetEvent(false);
        int writes = 0;
        var engine = new WebUIPasswordRecovery(store, new Uri("https://grid.example/portal"),
            (name, email) => name == "Resident Example" && email == identity.Email ? identity : null, Find,
            (id, password) => { Interlocked.Increment(ref writes); return true; },
            (email, link) => { sent.Enqueue(link); delivered.Set(); });
        string generic = engine.Request("missing user", "unknown@example.test");
        Assert(generic == engine.Request("Resident Example", identity.Email), "Request response enumerates accounts");
        string link = null;
        Assert(delivered.WaitOne(TimeSpan.FromSeconds(10)) && sent.TryDequeue(out link), "Recovery dispatch failed");
        var uri = new Uri(link);
        Assert(uri.AbsolutePath == "/portal/" && uri.Query == "?page=password-recovery" && uri.Fragment.StartsWith("#reset="), "Recovery link misrouted");
        Assert(!uri.Query.Contains("reset") && uri.Scheme == "https", "Recovery secret appears in logged URL");
        token = uri.Fragment.Substring("#reset=".Length);
        Assert(!engine.Reset(token, "mismatch", "different") && writes == 0, "Invalid passwords changed credentials");
        Assert(engine.Reset(token, "a new password", "a new password") && writes == 1, "Password change did not execute once");
        Assert(!engine.Reset(token, "another password", "another password") && writes == 1, "Reset token replayed");
        string failedToken = store.Issue(identity);
        var failed = new WebUIPasswordRecovery(store, new Uri("https://grid.example/portal/"),
            (_, _) => identity, Find, (_, _) => false, (_, _) => { });
        Assert(!failed.Reset(failedToken, "long password", "long password")
            && store.Consume(failedToken, Find) == Guid.Empty, "Failed credential write left a reusable consumed token");
        now = now.AddMinutes(31);
        string ambiguousToken = store.Issue(identity);
        int ambiguousWrites = 0;
        var ambiguous = new WebUIPasswordRecovery(store, new Uri("https://grid.example/portal/"),
            (_, _) => identity, Find, (_, _) => { ambiguousWrites++; throw new IOException("lost password-write reply"); }, (_, _) => { });
        bool threw = false;
        try { ambiguous.Reset(ambiguousToken, "long password", "long password"); } catch (IOException) { threw = true; }
        Assert(threw && ambiguousWrites == 1 && !ambiguous.Reset(ambiguousToken, "another password", "another password"),
            "Ambiguous password write permitted token replay");

        // Exercise the compiled adapter's credential-bound session stamps and cookie parser.
        var assembly = typeof(WebUIContentStore).Assembly;
        var type = assembly.GetType("OpenSim.Continuum.WebUI.WebUIServiceIntegration", true);
        var integration = RuntimeHelpers.GetUninitializedObject(type);
        var configuration = new IniConfigSource();
        var settings = configuration.AddConfig("ContinuumWebUI");
        var configure = type.GetMethod("ConfigureRecovery", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(configure.Invoke(integration, new object[] { settings }) == null, "Recovery enabled without operator configuration");
        settings.Set("PasswordRecoveryEnabled", true);
        settings.Set("PasswordRecoveryURL", "https://grid.example/portal/");
        settings.Set("PasswordRecoveryStorePath", Path.Combine(root, "configured-recovery.json"));
        settings.Set("PasswordRecoverySMTPHost", "smtp.example.test");
        settings.Set("PasswordRecoveryFrom", "grid@example.test");
        Assert(configure.Invoke(integration, new object[] { settings }) is WebUIPasswordRecovery, "Valid recovery configuration rejected");
        settings.Set("PasswordRecoverySMTPSecurity", "None");
        bool insecureRejected = false;
        try { configure.Invoke(integration, new object[] { settings }); }
        catch (TargetInvocationException error) { insecureRejected = error.InnerException is InvalidOperationException; }
        Assert(insecureRejected, "Plaintext SMTP configuration accepted");
        type.GetField("_recovery", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(integration, engine);
        var principal = new UUID(account.ToString());
        var authentication = DispatchProxy.Create<IAuthenticationService, RecoveryProxy>();
        var auth = new AuthInfo { PrincipalID = principal, PasswordHash = "stored-password-hash", PasswordSalt = "salt" };
        ((RecoveryProxy)(object)authentication).Handler = (method, args) => method.Name == "GetAuthInfo" ? auth : null;
        type.GetField("_authenticationForPassword", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(integration, authentication);
        var stampMethod = type.GetMethod("WebSessionStamp", BindingFlags.Instance | BindingFlags.NonPublic);
        var validMethod = type.GetMethod("ValidWebSessionStamp", BindingFlags.Instance | BindingFlags.NonPublic);
        string session = "opaque token:with symbols";
        string stamp = (string)stampMethod.Invoke(integration, new object[] { principal, session });
        Assert((bool)validMethod.Invoke(integration, new object[] { principal, session, stamp }), "Valid portal stamp rejected");
        Assert(!(bool)validMethod.Invoke(integration, new object[] { principal, "different token", stamp }), "Stamp not bound to session");
        auth.PasswordHash = "changed-password-hash";
        Assert(!(bool)validMethod.Invoke(integration, new object[] { principal, session, stamp }), "Old portal session survived password reset");
        var request = DispatchProxy.Create<IOSHttpRequest, HttpProxy>();
        ((HttpProxy)(object)request).Values["Headers"] = new NameValueCollection {
            ["Cookie"] = "ContinuumWebUI=" + principal + ".v2:" + Uri.EscapeDataString(session) + "." + stamp };
        var site = assembly.GetType("OpenSim.Continuum.WebUI.WhiteCoreSite", true);
        var parse = site.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single(x => x.Name == "TryReadCookie" && x.GetParameters().Length == 4);
        object[] values = { request, UUID.Zero, null, null };
        Assert((bool)parse.Invoke(null, values) && (string)values[2] == session && (string)values[3] == stamp, "Versioned cookie cannot round-trip");
        var addressMethod = type.GetMethod("RequestAddress", BindingFlags.Instance | BindingFlags.NonPublic);
        var headers = new NameValueCollection { ["X-Forwarded-For"] = "203.0.113.8, 198.51.100.4" };
        var requestValues = ((HttpProxy)(object)request).Values;
        requestValues["Headers"] = headers;
        requestValues["RemoteIPEndPoint"] = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 2000);
        Assert((string)addressMethod.Invoke(integration, new object[] { request }) == "127.0.0.1", "Untrusted forwarded address accepted");
        var trusted = new HashSet<System.Net.IPAddress> { System.Net.IPAddress.Loopback.MapToIPv6() };
        type.GetField("_trustedProxies", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(integration, trusted);
        Assert((string)addressMethod.Invoke(integration, new object[] { request }) == "198.51.100.4", "Spoofed leftmost forwarded address accepted");
        var rateSite = Activator.CreateInstance(site, BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { "/portal", "Test grid", null, null, integration, 3600, 10, 300, 3, 3600 }, null);
        requestValues["UriPath"] = "/portal/forgot_pass.html";
        requestValues["HttpMethod"] = "POST";
        requestValues["IsSecured"] = true;
        requestValues["Url"] = new Uri("https://grid.example/portal/forgot_pass.html");
        requestValues["QueryAsDictionary"] = new Dictionary<string, string>();
        headers["Origin"] = "https://grid.example";
        var handle = site.GetMethod("Handle", BindingFlags.Instance | BindingFlags.NonPublic);
        byte[] body = System.Text.Encoding.UTF8.GetBytes("mode=request&username=unknown&UserEmail=unknown%40example.test");
        requestValues["ContentLength64"] = (long)body.Length;
        for (int i = 0; i < 6; i++)
        {
            requestValues["InputStream"] = new MemoryStream(body);
            var response = DispatchProxy.Create<IOSHttpResponse, HttpProxy>();
            handle.Invoke(rateSite, new object[] { request, response });
            var responseValues = ((HttpProxy)(object)response).Values;
            Assert((int)responseValues["StatusCode"] == (i < 5 ? 200 : 429), "Recovery HTTP rate bound failed");
        }
        headers["Origin"] = "https://attacker.example";
        var denied = DispatchProxy.Create<IOSHttpResponse, HttpProxy>();
        handle.Invoke(rateSite, new object[] { request, denied });
        Assert((int)((HttpProxy)(object)denied).Values["StatusCode"] == 403, "Cross-site recovery request accepted");
        var transport = type.GetMethod("AccountTransportAllowed", BindingFlags.Instance | BindingFlags.NonPublic);
        requestValues["IsSecured"] = false;
        headers["X-Forwarded-Proto"] = "https";
        Assert((bool)transport.Invoke(integration, new object[] { request }), "Trusted TLS proxy was rejected");
        trusted.Clear();
        Assert(!(bool)transport.Invoke(integration, new object[] { request }), "Untrusted forwarded TLS bypassed transport policy");
        requestValues["Url"] = new Uri("http://localhost/portal/forgot_pass.html");
        requestValues["RemoteIPEndPoint"] = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("203.0.113.33"), 2000);
        Assert(!(bool)transport.Invoke(integration, new object[] { request }), "Forged loopback host bypassed HTTPS policy");
        requestValues["Url"] = new Uri("https://grid.example/portal/forgot_pass.html");
        requestValues["RemoteIPEndPoint"] = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 2000);
        trusted.Add(System.Net.IPAddress.Loopback.MapToIPv6());
        requestValues["IsSecured"] = true;
        var readResource = site.GetMethod("ReadResource", BindingFlags.Static | BindingFlags.NonPublic);
        var render = site.GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic);
        var defaultVariables = site.GetMethod("DefaultVariables", BindingFlags.Instance | BindingFlags.NonPublic);
        var variables = (Dictionary<string, object>)defaultVariables.Invoke(rateSite, new object[] { null });
        variables["PasswordRecoveryEnabled"] = true;
        variables["PasswordRecoveryDisabled"] = false;
        string html = System.Text.Encoding.UTF8.GetString((byte[])readResource.Invoke(null, new object[] { "forgot_pass.html" }));
        string rendered = (string)render.Invoke(rateSite, new object[] { "forgot_pass.html", html, variables, false, false, 0 });
        Assert(rendered.Contains("id=\"recovery-reset\"") && rendered.Contains("window.location.hash")
            && !rendered.Contains("not available") && !rendered.Contains("{IfPasswordRecovery"), "Recovery form failed to render");
        variables["PasswordRecoveryEnabled"] = false; variables["PasswordRecoveryDisabled"] = true;
        rendered = (string)render.Invoke(rateSite, new object[] { "forgot_pass.html", html, variables, false, false, 0 });
        Assert(rendered.Contains("not available") && !rendered.Contains("id=\"recovery-reset\""), "Disabled recovery showed a working reset form");
        // An old-password login racing a reset must not receive a stamp for the new credentials.
        var accounts = DispatchProxy.Create<IUserAccountService, RecoveryProxy>();
        ((RecoveryProxy)(object)accounts).Handler = (method, _) => method.Name == "GetUserAccount"
            ? new UserAccount { PrincipalID = principal, FirstName = "Test", LastName = "Resident", UserLevel = 0 } : null;
        auth.PasswordHash = "before-racing-reset";
        bool released = false;
        ((RecoveryProxy)(object)authentication).Handler = (method, _) => {
            if (method.Name == "GetAuthInfo") return auth;
            if (method.Name == "Authenticate") { auth.PasswordHash = "after-racing-reset"; return "racing-token"; }
            if (method.Name == "Release") { released = true; return true; }
            return null;
        };
        var loginSite = Activator.CreateInstance(site, BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { "/portal", "Test grid", accounts, authentication, integration, 3600, 10, 300, 3, 3600 }, null);
        headers["Origin"] = "https://grid.example";
        requestValues["UriPath"] = "/portal/login.html";
        requestValues["Url"] = new Uri("https://grid.example/portal/login.html");
        body = System.Text.Encoding.UTF8.GetBytes("username=Test+Resident&password=old-password");
        requestValues["InputStream"] = new MemoryStream(body); requestValues["ContentLength64"] = (long)body.Length;
        var racingResponse = DispatchProxy.Create<IOSHttpResponse, HttpProxy>();
        handle.Invoke(loginSite, new object[] { request, racingResponse });
        Assert(released && (int)((HttpProxy)(object)racingResponse).Values["StatusCode"] == 503,
            "Login straddling a password reset acquired a fresh credential stamp");
        ((RecoveryProxy)(object)authentication).Handler = (method, _) => method.Name switch {
            "GetAuthInfo" => auth, "Authenticate" => "current-token", "Verify" => true, "Release" => true, _ => null };
        requestValues["InputStream"] = new MemoryStream(body);
        var successful = DispatchProxy.Create<IOSHttpResponse, HttpProxy>();
        handle.Invoke(loginSite, new object[] { request, successful });
        var successfulValues = ((HttpProxy)(object)successful).Values;
        Assert((int)successfulValues["StatusCode"] == 303
            && ((string)successfulValues["Header:Set-Cookie"]).Contains(".v2:"), "Normal recovery-enabled login failed");
        headers["Cookie"] = ((string)successfulValues["Header:Set-Cookie"]).Split(';')[0];
        var authenticated = site.GetMethod("GetAuthenticatedUser", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(authenticated.Invoke(loginSite, new object[] { request }) is UserAccount, "Signed cookie could not authenticate");
        auth.PasswordSalt = "new-password-salt";
        Assert(authenticated.Invoke(loginSite, new object[] { request }) == null, "Cookie authenticated after password reset");
        ((RecoveryProxy)(object)accounts).Handler = (method, _) => method.Name == "GetUserAccount"
            ? new UserAccount { PrincipalID = principal, FirstName = "Test", LastName = "Resident", UserLevel = -1 } : null;
        headers.Remove("Cookie"); requestValues["InputStream"] = new MemoryStream(body);
        var disabled = DispatchProxy.Create<IOSHttpResponse, HttpProxy>();
        handle.Invoke(loginSite, new object[] { request, disabled });
        Assert((int)((HttpProxy)(object)disabled).Values["StatusCode"] == 401, "Disabled account logged into WebUI");
        type.GetField("_recovery", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(integration, null);
        ((RecoveryProxy)(object)accounts).Handler = (method, _) => method.Name == "GetUserAccount"
            ? new UserAccount { PrincipalID = principal, FirstName = "Test", LastName = "Resident", UserLevel = 0 } : null;
        ((RecoveryProxy)(object)authentication).Handler = (method, _) => method.Name switch {
            "GetAuthInfo" => throw new Exception("Recovery-disabled login unexpectedly requested credential metadata"),
            "Authenticate" => "legacy-token", "Verify" => true, "Release" => true, _ => null };
        requestValues["InputStream"] = new MemoryStream(body);
        var legacy = DispatchProxy.Create<IOSHttpResponse, HttpProxy>();
        handle.Invoke(loginSite, new object[] { request, legacy });
        Assert((int)((HttpProxy)(object)legacy).Values["StatusCode"] == 303
            && !((string)((HttpProxy)(object)legacy).Values["Header:Set-Cookie"]).Contains(".v2:"), "Recovery-disabled login compatibility failed");
        Console.WriteLine("PASS: password recovery expiry, one-time/restart/concurrent use, identity binding, neutral dispatch, fragment links, failed-write handling and portal session revocation.");
    }
}

public class RecoveryProxy : DispatchProxy
{
    public Func<MethodInfo, object[], object> Handler;
    protected override object Invoke(MethodInfo target, object[] args) => Handler(target, args);
}
