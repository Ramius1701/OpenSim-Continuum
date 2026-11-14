using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Collections.Specialized;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Continuum.WebUI;
using OpenSim.Data.SQLite;
using OpenSim.Services.Interfaces;
using OpenSim.Services.UserAccountService;
using OpenSim.Framework.Servers.HttpServer;

static class AccountClosureChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run(string root)
    {
        var storage = new SQLiteUserAccountData("Data Source=" + Path.Combine(root, "closure.sqlite") + ";Version=3", "UserAccounts");
        try
        {
        var native = (UserAccountService)RuntimeHelpers.GetUninitializedObject(typeof(UserAccountService));
        typeof(UserAccountServiceBase).GetField("m_Database", Private).SetValue(native, storage);
        UserAccount Account(string name, int level = 0)
        {
            var account = new UserAccount(UUID.Zero, name, "Resident", "resident@example.invalid") { UserLevel = level };
            Check(native.StoreUserAccount(account), "Could not create native closure fixture account"); return account;
        }
        var resident = Account("Closure"); var other = Account("Other"); var admin = Account("Administrator", 200);
        Check(!native.TryArchiveAccount(UUID.Zero) && !native.TryArchiveAccount(admin.PrincipalID)
            && native.GetUserAccount(UUID.Zero, admin.PrincipalID).UserLevel == 200, "Archive changed a protected or privileged account");
        var raced = Account("Concurrent");
        int successes = 0; System.Threading.Tasks.Parallel.For(0, 2, _ => { if (native.TryArchiveAccount(raced.PrincipalID)) Interlocked.Increment(ref successes); });
        Check(successes == 1 && native.GetUserAccount(UUID.Zero, raced.PrincipalID).UserLevel == -2, "Archive was not conditional or replay-safe");
        var assembly = typeof(WebUIContentStore).Assembly; var adapter = assembly.GetType("OpenSim.Continuum.WebUI.WebUIServiceIntegration", true);
        var integration = RuntimeHelpers.GetUninitializedObject(adapter);
        void Set(string field, object value) => adapter.GetField(field, Private).SetValue(integration, value);
        Set("_accounts", native); Set("_allowAccountClosure", true); Set("_adminLevel", 200);
        Set("_protectedClosureAccounts", new HashSet<UUID> { UUID.Zero, other.PrincipalID });
        var grid = DispatchProxy.Create<IGridUserService, RecoveryProxy>(); bool online = false;
        ((RecoveryProxy)(object)grid).Handler = (method, args) => method.Name == "GetGridUserInfo" ? new GridUserInfo { Online = online } : null;
        var estates = DispatchProxy.Create<IEstateDataService, RecoveryProxy>(); bool ownsEstate = false;
        ((RecoveryProxy)(object)estates).Handler = (method, args) => method.Name == "GetEstatesByOwner" ? new List<int>(ownsEstate ? new[] { 101 } : Array.Empty<int>()) : null;
        var auth = DispatchProxy.Create<IAuthenticationService, RecoveryProxy>(); bool passwordValid = false; int releases = 0;
        ((RecoveryProxy)(object)auth).Handler = (method, args) => method.Name switch
        {
            "Authenticate" => passwordValid && (string)args[1] == Util.Md5Hash("CorrectPassword") ? "closure-token" : "",
            "Release" => ++releases > 0, "Verify" => true, _ => null
        };
        Set("_gridUsers", grid); Set("_estates", estates); Set("_authenticationForPassword", auth);
        var post = adapter.GetMethod("HandlePost", Private);
        var form = new Dictionary<string, string> { ["action"] = "close", ["password"] = "CorrectPassword", ["userID"] = other.PrincipalID.ToString() };
        string Change(UserAccount actor)
        {
            object[] args = { "user/deleteaccount.html", form, actor, null }; post.Invoke(integration, args); return (string)args[3];
        }
        Check(Change(resident).Contains("Confirm") && native.GetUserAccount(UUID.Zero, resident.PrincipalID).UserLevel == 0, "Unconfirmed account closure succeeded");
        form["confirm"] = "true";
        Check(Change(resident) == "Current password is incorrect", "Closure did not reauthenticate"); passwordValid = true;
        online = true; Check(Change(resident).Contains("Log out"), "Online viewer account was archived"); online = false;
        ownsEstate = true; Check(Change(resident).Contains("Transfer your estates"), "Estate owner account was archived"); ownsEstate = false;
        foreach (UserAccount account in new UserAccount[] { null, admin, other, new UserAccount { PrincipalID = resident.PrincipalID, LocalToGrid = false } })
            Check(Change(account).Contains("unavailable"), "Anonymous, privileged, protected or foreign account closure was permitted");
        var siteType = assembly.GetType("OpenSim.Continuum.WebUI.WhiteCoreSite", true); var site = RuntimeHelpers.GetUninitializedObject(siteType);
        siteType.GetField("_mountPath", Private).SetValue(site, "/portal"); siteType.GetField("_systemName", Private).SetValue(site, "Fixture");
        siteType.GetField("_integration", Private).SetValue(site, integration); siteType.GetField("_accounts", Private).SetValue(site, native);
        siteType.GetField("_authentication", Private).SetValue(site, auth);
        siteType.GetField("_attemptsLock", Private).SetValue(site, new object());
        siteType.GetField("_attempts", Private).SetValue(site, new Dictionary<string, Queue<DateTime>>(StringComparer.Ordinal));
        var request = DispatchProxy.Create<IOSHttpRequest, HttpProxy>(); var fields = ((HttpProxy)(object)request).Values;
        var headers = new NameValueCollection { ["Cookie"] = "ContinuumWebUI=" + resident.PrincipalID + ".fixture", ["Origin"] = "https://grid.example" };
        fields["Headers"] = headers; fields["UriPath"] = "/portal/user/deleteaccount.html"; fields["HttpMethod"] = "GET";
        fields["Url"] = new Uri("https://grid.example/portal/user/deleteaccount.html"); fields["IsSecured"] = true;
        fields["QueryAsDictionary"] = new Dictionary<string, string>(); fields["ContentEncoding"] = Encoding.UTF8;
        byte[] body = Encoding.UTF8.GetBytes("action=close&confirm=true&password=CorrectPassword&userID=" + other.PrincipalID); fields["ContentLength64"] = (long)body.Length;
        Dictionary<string, object> Request()
        {
            fields["InputStream"] = new MemoryStream(body); var response = DispatchProxy.Create<IOSHttpResponse, HttpProxy>();
            siteType.GetMethod("Handle", Private).Invoke(site, new object[] { request, response }); return ((HttpProxy)(object)response).Values;
        }
        var response = Request(); Check((int)response["StatusCode"] == 200 && Encoding.UTF8.GetString((byte[])response["RawBuffer"]).Contains("account_close_form")
            && native.GetUserAccount(UUID.Zero, resident.PrincipalID).UserLevel == 0, "Closure GET did not render or mutated the account");
        fields["HttpMethod"] = "POST"; headers["Origin"] = "https://attacker.example";
        Check((int)Request()["StatusCode"] == 403, "Cross-site account closure accepted"); headers["Origin"] = "https://grid.example";
        fields["IsSecured"] = false; Check((int)Request()["StatusCode"] == 403, "Insecure closure accepted"); fields["IsSecured"] = true;
        response = Request();
        Check((int)response["StatusCode"] == 200 && Encoding.UTF8.GetString((byte[])response["RawBuffer"]) == "Account closed; identity and records retained"
            && releases == 2 && native.GetUserAccount(UUID.Zero, resident.PrincipalID).UserLevel == -2
            && native.GetUserAccount(UUID.Zero, other.PrincipalID).UserLevel == 0, "HTTP closure failed, targeted posted UUID or retained its web token");
        Check((int)Request()["StatusCode"] == 401, "Archived account retained authenticated WebUI access");
        var login = (OpenSim.Services.LLLoginService.LLLoginService)RuntimeHelpers.GetUninitializedObject(typeof(OpenSim.Services.LLLoginService.LLLoginService));
        typeof(OpenSim.Services.LLLoginService.LLLoginService).GetField("m_UserAccountService", Private).SetValue(login, native);
        typeof(OpenSim.Services.LLLoginService.LLLoginService).GetField("m_MinLoginLevel", Private).SetValue(login, -10);
        var loginResult = login.Login(resident.FirstName, resident.LastName, "$1$fixture", "last", UUID.Zero,
            "Fixture 1", "Fixture", "fixture-mac", "fixture-id", new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 12345));
        Check(ReferenceEquals(loginResult, OpenSim.Services.LLLoginService.LLFailedLoginResponse.LoginBlockedProblem), "Native viewer login admitted an archived account with a negative configured minimum");
        Console.WriteLine("PASS: native conditional SQLite account archival, concurrent/replay and privilege protection, password/offline/estate guards, HTTPS/origin, actor binding and web-session invalidation.");
        }
        finally
        {
            ((System.Data.SQLite.SQLiteConnection)storage.GetType().BaseType.GetField("m_Connection", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Dispose();
        }

    }
}
