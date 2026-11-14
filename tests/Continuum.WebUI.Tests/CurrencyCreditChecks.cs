using System.Collections;
using System.Data.SQLite;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Nini.Config;
using Nwc.XmlRpc;
using OpenMetaverse;
using OpenSim.Continuum.Economy;
using OpenSim.Continuum.WebUI;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;

static class CurrencyCreditChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run(string root)
    {
        OpenSim.Framework.MainConsole.Instance ??= new OpenSim.Framework.Console.LocalConsole("Credit tests");
        var search = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "ContinuumSearch.Service.dll")).GetType("ContinuumSearch.Service.SearchStore", true);
        try { search.GetMethod("ConfigureSQLiteNativeLibrary", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null); }
        catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
        string connectionString = "Data Source=" + Path.Combine(root, "credit-history.sqlite") + ";Version=3;";
        var backend = EconomyProviderFactory.Create("SQLite", connectionString); backend.Ledger.EnsureSchema();
        Guid buyer = Guid.NewGuid(), other = Guid.NewGuid(), system = Guid.NewGuid(); var expected = new HashSet<Guid>();
        for (int i = 0; i < 55; i++)
        {
            Guid operation = Guid.NewGuid(); expected.Add(operation);
            Check(backend.Ledger.Adjust(new LedgerAdjustmentRequest { OperationID = operation, AccountID = buyer, ActorID = system,
                Amount = 10, Kind = LedgerAdjustmentKind.Credit, TransactionType = 5010, Reason = "<img src=x onerror=bad()>" }).Succeeded, "Credit fixture adjustment failed");
        }
        backend.Ledger.Adjust(new LedgerAdjustmentRequest { OperationID = Guid.NewGuid(), AccountID = buyer, ActorID = system, Amount = 2, Kind = LedgerAdjustmentKind.Credit, TransactionType = 900, Reason = "Not a purchase" });
        backend.Ledger.Adjust(new LedgerAdjustmentRequest { OperationID = Guid.NewGuid(), AccountID = other, ActorID = system, Amount = 20, Kind = LedgerAdjustmentKind.Credit, TransactionType = 5010, Reason = "Another resident" });
        backend.Ledger.Adjust(new LedgerAdjustmentRequest { OperationID = Guid.NewGuid(), AccountID = buyer, ActorID = system, Amount = 1000, Kind = LedgerAdjustmentKind.Credit, TransactionType = 5010, MaximumBalance = 1, Reason = "Failed purchase" });
        using (var connection = new SQLiteConnection(connectionString))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "UPDATE continuum_economy_adjustments SET created_utc='2026-10-24T00:00:00.000Z'"; command.ExecuteNonQuery();
        }
        var reader = (ICurrencyCreditHistory)backend.Ledger;
        var seen = new HashSet<Guid>(); DateTime? before = null; Guid beforeID = Guid.Empty;
        for (int page = 0; page < 5; page++)
        {
            var entries = reader.GetCurrencyCredits(buyer, before, beforeID, 20);
            foreach (var entry in entries) Check(seen.Add(entry.TransactionID) && entry.AccountID == buyer && entry.Amount == 10, "Credit paging duplicated or mixed accounts/types/status");
            if (entries.Count == 0) break;
            before = entries.Last().CreatedUtc; beforeID = entries.Last().TransactionID;
        }
        Check(seen.SetEquals(expected), "Stable credit cursor skipped records sharing one timestamp");
        var config = new IniConfigSource(); var section = config.AddConfig("ContinuumEconomyService");
        string secret = new('h', 48); section.Set("RegionSharedSecret", secret); section.Set("SystemActorID", system);
        var rpcType = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "ContinuumEconomy.Service.dll")).GetType("ContinuumEconomy.Service.ContinuumEconomyRpc", true);
        using var rpc = (IDisposable)Activator.CreateInstance(rpcType, Private, null, new object[] { backend, section }, null);
        var handler = rpcType.GetMethod("GetCurrencyCreditHistory", Private);
        using var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); int port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        var server = new BaseHttpServer((uint)port); string lastAccount = "";
        server.AddXmlRPCHandler("GetCurrencyCreditHistory", (request, endpoint) =>
        {
            lastAccount = ((Hashtable)request.Params[0])["accountID"].ToString();
            return (XmlRpcResponse)handler.Invoke(rpc, new object[] { request, endpoint });
        });
        server.Start();
        try
        {
            string url = "http://127.0.0.1:" + port;
            Hashtable Call(Hashtable fields) => (Hashtable)new XmlRpcRequest("GetCurrencyCreditHistory", new ArrayList { fields }).Send(url, 5000).Value;
            var first = Call(new Hashtable { ["continuumSecret"] = secret, ["accountID"] = buyer.ToString() });
            Check((bool)first["success"] && ((ArrayList)first["history"]).Count == 50 && (bool)first["hasMore"], "Native credit RPC did not paginate");
            var second = Call(new Hashtable { ["continuumSecret"] = secret, ["accountID"] = buyer.ToString(), ["beforeUtc"] = first["beforeUtc"], ["beforeID"] = first["beforeID"] });
            Check(((ArrayList)second["history"]).Count == 5 && !(bool)second["hasMore"], "Native credit RPC lost its final page");
            Check(!(bool)Call(new Hashtable { ["continuumSecret"] = "wrong", ["accountID"] = buyer.ToString() })["success"], "Credit history accepted wrong authority key");
            var assembly = typeof(WebUIContentStore).Assembly; var adapter = assembly.GetType("OpenSim.Continuum.WebUI.WebUIServiceIntegration", true);
            var integration = RuntimeHelpers.GetUninitializedObject(adapter);
            void Set(string field, object value) => adapter.GetField(field, Private).SetValue(integration, value);
            Set("_economyUrl", url); Set("_economySecret", secret); Set("_rpcTimeoutMs", 5000); Set("_adminLevel", 200);
            var resident = new UserAccount { PrincipalID = new UUID(buyer.ToString()), FirstName = "Buyer", LastName = "Resident" };
            var admin = new UserAccount { PrincipalID = UUID.Random(), UserLevel = 200 };
            var accounts = DispatchProxy.Create<IUserAccountService, RecoveryProxy>();
            ((RecoveryProxy)(object)accounts).Handler = (method, args) => method.Name == "GetUserAccount" && args[1] is UUID id && id == resident.PrincipalID ? resident : null;
            Set("_accounts", accounts);
            var add = adapter.GetMethod("AddCurrencyCredits", Private);
            var parameters = new Dictionary<string, string> { ["resident"] = other.ToString() }; var values = new Dictionary<string, object>();
            add.Invoke(integration, new object[] { "user/purchases.html", parameters, resident, values });
            var rows = (List<Dictionary<string, object>>)values["CurrencyCreditRows"];
            Check(lastAccount == buyer.ToString() && rows.Count == 50 && ((string)rows[0]["CreditDescription"]).Contains("&lt;img"), "Resident credit adapter trusted posted identity or failed escaping");
            bool denied = false;
            try { add.Invoke(integration, new object[] { "admin/purchases.html", parameters, resident, values }); }
            catch (TargetInvocationException e) when (e.InnerException is ArgumentException) { denied = true; }
            Check(denied, "Resident reached administrator credit adapter");
            parameters["resident"] = buyer.ToString(); add.Invoke(integration, new object[] { "admin/purchases.html", parameters, admin, values });
            Check((bool)values["CurrencyCreditAvailable"] && lastAccount == buyer.ToString(), "Administrator credit lookup failed");
            var siteType = assembly.GetType("OpenSim.Continuum.WebUI.WhiteCoreSite", true); var site = RuntimeHelpers.GetUninitializedObject(siteType);
            siteType.GetField("_mountPath", Private).SetValue(site, "/portal"); siteType.GetField("_systemName", Private).SetValue(site, "Fixture"); siteType.GetField("_integration", Private).SetValue(site, integration);
            foreach (string page in new[] { "user/purchases.html", "admin/purchases.html" })
            {
                var actor = page.StartsWith("admin/") ? admin : resident;
                var defaults = (Dictionary<string, object>)siteType.GetMethod("DefaultVariables", Private).Invoke(site, new object[] { actor });
                add.Invoke(integration, new object[] { page, parameters, actor, defaults });
                var bytes = (byte[])siteType.GetMethod("ReadResource", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { page });
                string html = (string)siteType.GetMethod("Render", Private).Invoke(site, new object[] { page, Encoding.UTF8.GetString(bytes), defaults, true, actor == admin, 0 });
                Check(html.Contains("not cash receipts") && html.Contains("Older currency credits") && html.Contains("&lt;img") && !html.Contains("<img src=x"), "Embedded credit page omitted its boundary, pagination or escaping");
            }
            Set("_economySecret", "wrong"); values.Clear(); add.Invoke(integration, new object[] { "user/purchases.html", parameters, resident, values });
            Check((bool)values["CurrencyCreditUnavailable"] && !(bool)values["CurrencyCreditEmpty"], "Credit outage was presented as no purchases");
            Console.WriteLine("PASS: real SQLite currency credit filtering, same-timestamp pagination, actual private HTTP RPC, resident/admin identity scopes, embedded history and no cash-receipt claims.");
        }
        finally { server.Stop(); }
    }
}
