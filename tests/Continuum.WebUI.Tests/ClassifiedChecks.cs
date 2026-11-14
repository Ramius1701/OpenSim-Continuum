using System.Collections;
using System.Data.Common;
using System.Data.SQLite;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using Nini.Config;
using Nwc.XmlRpc;
using OpenMetaverse;
using OpenSim.Continuum.Economy;
using OpenSim.Continuum.WebUI;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

static class ClassifiedChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool value, string text) { if (!value) throw new Exception(text); }
    public static void Run(string root)
    {
        OpenSim.Framework.MainConsole.Instance ??= new OpenSim.Framework.Console.LocalConsole("Classified tests");
        var backend = EconomyProviderFactory.Create("SQLite", "Data Source=" + Path.Combine(root, "classified-payments.sqlite") + ";Version=3;");
        backend.Ledger.EnsureSchema();
        Guid buyer = Guid.NewGuid(), system = Guid.NewGuid();
        Check(backend.Ledger.Adjust(new LedgerAdjustmentRequest { OperationID = Guid.NewGuid(), AccountID = buyer, ActorID = system,
            Amount = 150, Kind = LedgerAdjustmentKind.Credit, Reason = "Classified fixture" }).Succeeded, "Classified funding failed");
        string secret = new('c', 48), regionSecret = new('r', 48);
        IConfigSource config = new IniConfigSource(); IConfig section = config.AddConfig("ContinuumEconomyService");
        section.Set("RegionSharedSecret", regionSecret); section.Set("SystemActorID", system.ToString()); section.Set("WebUIClassifiedSecret", secret);
        Type rpcType = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "ContinuumEconomy.Service.dll")).GetType("ContinuumEconomy.Service.ContinuumEconomyRpc", true);
        using var money = (IDisposable)Activator.CreateInstance(rpcType, Private, null, new object[] { backend, section }, null);
        MethodInfo charge = rpcType.GetMethod("WebUIClassifiedCharge", Private);
        XmlRpcResponse Call(XmlRpcRequest r, IPEndPoint ep) => (XmlRpcResponse)charge.Invoke(money, new object[] { r, ep });
        Hashtable Fields(Guid id, string key, int amount = 50) => new() { ["classifiedSecret"] = key, ["purchaseID"] = id.ToString(), ["buyerID"] = buyer.ToString(), ["amount"] = amount.ToString() };
        Hashtable Direct(Hashtable fields) => (Hashtable)Call(new XmlRpcRequest("WebUIClassifiedCharge", new ArrayList { fields }), null).Value;
        Check(!(bool)Direct(Fields(Guid.NewGuid(), regionSecret))["success"] && backend.Ledger.GetBalance(buyer) == 150, "Region secret bypassed dedicated classified authority");
        Check(!(bool)Direct(Fields(Guid.NewGuid(), secret, 0))["success"] && backend.Ledger.GetBalance(buyer) == 150, "Nonpositive classified fee accepted");
        var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); int port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        var server = new BaseHttpServer((uint)port);
        server.AddXmlRPCHandler("WebUIClassifiedCharge", Call);
        server.AddXmlRPCHandler("continuum_event_write", (r, ep) => new XmlRpcResponse { Value = new Hashtable
            { ["success"] = true, ["data"] = new ArrayList { new Hashtable { ["event_id"] = 17 } } } });
        Guid classified = Guid.NewGuid(), region = Guid.NewGuid();
        UserAccount account = new() { PrincipalID = new UUID(buyer.ToString()), UserLevel = 0, FirstName = "Fixture", LastName = "Resident" };
        string portalSecret = new('p', 48);
        Hashtable received = null;
        server.AddXmlRPCHandler("continuum_classified_manage", (r, ep) =>
        {
            received = (Hashtable)r.Params[0];
            Check(received["actor"].ToString() == buyer.ToString() && received["writeSecret"].ToString() == portalSecret, "Portal actor or private key lost");
            string action = received["action"].ToString();
            ArrayList data = action == "list" ? new ArrayList { new Hashtable { ["minimumPrice"] = 50, ["paymentsAvailable"] = true,
                ["listings"] = new ArrayList(), ["pending"] = new ArrayList { new Hashtable { ["requestID"] = classified.ToString(), ["name"] = "Pending <script>bad()</script>", ["price"] = 50 } } } }
                : action == "detail" ? new ArrayList { new Hashtable { ["classifieduuid"] = classified.ToString(), ["creatoruuid"] = buyer.ToString(),
                    ["name"] = "<script>bad()</script>", ["description"] = "<img src=x onerror=bad()>", ["editToken"] = new string('A', 64), ["category"] = 1,
                    ["snapshotuuid"] = Guid.Empty.ToString(), ["priceforlisting"] = 50, ["expirationdate"] = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds().ToString() } }
                : new ArrayList { new Hashtable { ["classifieduuid"] = classified.ToString() } };
            return new XmlRpcResponse { Value = new Hashtable { ["success"] = true, ["data"] = data } };
        });
        server.Start();
        try
        {
            string url = "http://127.0.0.1:" + port;
            Type searchRpc = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "ContinuumSearch.Service.dll")).GetType("ContinuumSearch.Service.SearchRpc", true);
            IConfig payments = config.AddConfig("ContinuumSearchService"); payments.Set("ClassifiedEconomyURL", url); payments.Set("ClassifiedEconomySecret", secret);
            var pay = (Func<Guid, Guid, int, Hashtable>)searchRpc.GetMethod("ClassifiedPayments", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { payments });
            Guid purchase = Guid.NewGuid();
            Check(pay(purchase, buyer, 50)["state"].ToString() == "Captured", "Real classified payment did not capture");
            Check(pay(purchase, buyer, 50)["state"].ToString() == "Captured" && backend.Ledger.GetBalance(buyer) == 100
                && backend.Ledger.GetBalance(system) == 50 && backend.Ledger.GetAvailableBalance(buyer) == 100, "Payment replay double charged or left a hold");
            Check(!(bool)pay(purchase, buyer, 51)["success"] && backend.Ledger.GetBalance(buyer) == 100, "Purchase ID accepted changed price");
            Hashtable spoofed = Fields(Guid.NewGuid(), secret); spoofed["sellerID"] = Guid.NewGuid().ToString(); spoofed["transactionType"] = "1";
            Check((bool)Direct(spoofed)["success"] && backend.Ledger.GetBalance(system) == 100, "Caller redirected classified recipient");
            Check(pay(Guid.NewGuid(), Guid.NewGuid(), 50)["state"].ToString() == "InsufficientFunds", "Insufficient balance not rejected");
            DurablePublication(root, searchRpc.Assembly, pay, backend, buyer, system);
            section.Set("WebUIClassifiedSecret", "");
            using var disabled = (IDisposable)Activator.CreateInstance(rpcType, Private, null, new object[] { backend, section }, null);
            var disabledReply = (XmlRpcResponse)charge.Invoke(disabled, new object[] { new XmlRpcRequest("WebUIClassifiedCharge", new ArrayList { Fields(Guid.NewGuid(), secret) }), null });
            Check(!(bool)((Hashtable)disabledReply.Value)["success"], "Disabled classified payment authority accepted a charge");
            Portal(url, portalSecret, account, region, classified, () => received);
        }
        finally { server.Stop(); }
        Console.WriteLine("PASS: real SQLite classified fees, publication restart recovery, native provider lifetime/ownership/price interoperability, private HTTP authority and embedded editor/pending forms.");
    }

    static void DurablePublication(string root, Assembly assembly, Func<Guid, Guid, int, Hashtable> pay, EconomyBackend backend, Guid buyer, Guid system)
    {
        Type storeType = assembly.GetType("ContinuumSearch.Service.SearchStore", true);
        string connection = "Data Source=" + Path.Combine(root, "classified-publication.sqlite") + ";Version=3;";
        Func<DbConnection> factory = () => new SQLiteConnection(connection);
        object Open() => Activator.CreateInstance(storeType, Private, null, new object[] { "SQLite", connection, factory }, null);
        object store = Open();
        storeType.GetMethod("EnsureSchema", Private).Invoke(store, null);
        using (DbConnection c = factory())
        {
            c.Open(); using DbCommand sql = c.CreateCommand();
            sql.CommandText = "CREATE TABLE classifieds(classifieduuid VARCHAR(36) PRIMARY KEY,creatoruuid VARCHAR(36),creationdate INTEGER,expirationdate INTEGER,category INTEGER,name VARCHAR(255),description TEXT,parceluuid VARCHAR(36),parentestate INTEGER,snapshotuuid VARCHAR(36),simname VARCHAR(255),posglobal VARCHAR(255),parcelname VARCHAR(255),classifiedflags INTEGER,priceforlisting INTEGER)";
            sql.ExecuteNonQuery();
        }
        Guid region = Guid.NewGuid(), request = Guid.NewGuid();
        Type regionType = assembly.GetType("ContinuumSearch.Service.SearchRegion", true); object indexed = Activator.CreateInstance(regionType, true);
        foreach (var field in new[] { ("ID", region.ToString()), ("Name", "Publication fixture"), ("Handle", "1099511628032000"), ("OwnerID", buyer.ToString()) })
            regionType.GetField(field.Item1, Private).SetValue(indexed, field.Item2);
        storeType.GetMethod("ReplaceRegion", Private).Invoke(store, new[] { indexed });
        Hashtable form = new() { ["action"] = "create", ["actor"] = buyer.ToString(), ["requestID"] = request.ToString(),
            ["name"] = "Paid publication", ["description"] = "Real ledger and profile database", ["category"] = "1", ["snapshotID"] = Guid.Empty.ToString(),
            ["price"] = "50", ["regionID"] = region.ToString(), ["maturity"] = "0", ["x"] = "128", ["y"] = "128", ["z"] = "25" };
        MethodInfo manage = storeType.GetMethod("ManageClassified", Private);
        bool dropReply = true;
        Hashtable Unreliable(Guid id, Guid actor, int amount)
        { Hashtable response = pay(id, actor, amount); if (dropReply) throw new IOException("Simulated lost response after actual ledger capture"); return response; }
        bool lost = false;
        try { manage.Invoke(store, new object[] { form, (Func<Guid, Guid, int, Hashtable>)Unreliable, 50 }); }
        catch (TargetInvocationException e) when (e.InnerException is IOException) { lost = true; }
        Check(lost && backend.Ledger.GetBalance(buyer) == 0, "Actual captured fee was not retained for recovery");
        ArrayList Read() => (ArrayList)storeType.GetMethod("GetClassified", Private).Invoke(store, new object[] { new Hashtable { ["classifiedID"] = request.ToString() } });
        Check(Read().Count == 0, "Unconfirmed real payment published a listing");
        ((IDisposable)store).Dispose(); store = Open(); dropReply = false; form["action"] = "resume";
        manage.Invoke(store, new object[] { form, pay, 50 });
        Check(Read().Count == 1 && backend.Ledger.GetBalance(buyer) == 0 && backend.Ledger.GetBalance(system) == 150,
            "Restarted real publication failed or charged a second fee");
        manage.Invoke(store, new object[] { form, pay, 50 });
        Check(Read().Count == 1 && backend.Ledger.GetAvailableBalance(buyer) == 0, "Repeat resume duplicated publication or left escrow");
        using (DbConnection c = factory())
        {
            c.Open(); var provider = new OpenSim.Data.SQLite.SQLiteUserProfilesData();
            provider.GetType().GetField("m_connection", Private).SetValue(provider, c);
            var ad = new OpenSim.Framework.UserClassifiedAdd { ClassifiedId = new UUID(request.ToString()) }; string message = "";
            Check(provider.GetClassifiedInfo(ref ad, ref message) && ad.CreatorId == new UUID(buyer.ToString()), "Native profile provider cannot read web listing");
            int created = ad.CreationDate, expires = ad.ExpirationDate;
            ad.Name = "Edited in the viewer"; ad.Price = 99999; ad.CreationDate = 0; ad.ExpirationDate = Int32.MaxValue;
            Check(provider.UpdateClassifiedRecord(ad, ref message), "Native viewer edit failed");
            var reloaded = new OpenSim.Framework.UserClassifiedAdd { ClassifiedId = ad.ClassifiedId };
            Check(provider.GetClassifiedInfo(ref reloaded, ref message) && reloaded.CreationDate == created && reloaded.ExpirationDate == expires
                && reloaded.Price == 50 && reloaded.Name == "Edited in the viewer", "Native viewer edit renewed or repriced a paid web listing");
            ad.CreatorId = UUID.Random(); ad.Name = "Foreign overwrite";
            Check(!provider.UpdateClassifiedRecord(ad, ref message), "Native provider allowed another creator to overwrite the listing");
            var expensive = new OpenSim.Framework.UserClassifiedAdd { ClassifiedId = UUID.Random(), CreatorId = new UUID(buyer.ToString()),
                Name = "High-value native classified", Description = "Int32 price", Price = 50000 };
            Check(provider.UpdateClassifiedRecord(expensive, ref message), "Native high-value listing failed");
            var high = new OpenSim.Framework.UserClassifiedAdd { ClassifiedId = expensive.ClassifiedId };
            Check(provider.GetClassifiedInfo(ref high, ref message) && high.Price == 50000, "Native profile reader truncated the classified price");
            Check(provider.DeleteClassifiedRecord(expensive.ClassifiedId), "Native classified delete parameter failed");
            Check(Read().Count == 1, "Native editing affected an unrelated classified");
        }
        ((IDisposable)store).Dispose();
    }

    static void Portal(string url, string secret, UserAccount account, Guid region, Guid classified, Func<Hashtable> last)
    {
        Type adapter = typeof(WebUIContentStore).Assembly.GetType("OpenSim.Continuum.WebUI.WebUIServiceIntegration", true);
        object integration = RuntimeHelpers.GetUninitializedObject(adapter);
        void Set(string field, object value) => adapter.GetField(field, Private).SetValue(integration, value);
        Set("_searchUrl", url); Set("_searchWriteSecret", secret); Set("_rpcTimeoutMs", 5000); Set("_hopBase", "hop://grid.example.test"); Set("_adminLevel", 200);
        IGridService grid = DispatchProxy.Create<IGridService, ClassifiedGrid>();
        ((ClassifiedGrid)(object)grid).Region = new GridRegion { RegionID = new UUID(region.ToString()), EstateOwner = account.PrincipalID, RegionName = "Fixture region", RegionLocX = 256000, RegionLocY = 256000, RegionSizeX = 512, RegionSizeY = 512 };
        Set("_grid", grid);
        object[] landing = { "<256400, 256400, 25000>", 0, 0, 0, "Fixture region" };
        adapter.GetMethod("DirectoryLocation", Private).Invoke(integration, landing);
        Check((int)landing[1] == 400 && (int)landing[2] == 400 && (int)landing[3] == 25000, "Classified hop truncated variable-region coordinates or height");
        Type siteType = typeof(WebUIContentStore).Assembly.GetType("OpenSim.Continuum.WebUI.WhiteCoreSite", true);
        object site = RuntimeHelpers.GetUninitializedObject(siteType);
        siteType.GetField("_mountPath", Private).SetValue(site, "/portal"); siteType.GetField("_systemName", Private).SetValue(site, "Fixture grid"); siteType.GetField("_integration", Private).SetValue(site, integration);
        siteType.GetField("_attemptsLock", Private).SetValue(site, new object());
        siteType.GetField("_attempts", Private).SetValue(site, new Dictionary<string, Queue<DateTime>>());
        MethodInfo defaults = siteType.GetMethod("DefaultVariables", Private), render = siteType.GetMethod("Render", Private);
        MethodInfo resource = siteType.GetMethod("ReadResource", BindingFlags.Static | BindingFlags.NonPublic), editor = adapter.GetMethod("AddClassifiedEditor", Private);
        string Render(string page, Dictionary<string, object> values)
        {
            byte[] bytes = (byte[])resource.Invoke(null, new object[] { page }); Check(bytes != null, "Classified asset missing");
            return (string)render.Invoke(site, new object[] { page, System.Text.Encoding.UTF8.GetString(bytes), values, true, false, 0 });
        }
        foreach (string page in new[] { "classifieds/add_classified.html", "user/classified_edit.html" })
        {
            var values = (Dictionary<string, object>)defaults.Invoke(site, new object[] { account });
            editor.Invoke(integration, new object[] { page, new Dictionary<string, string> { ["classifiedid"] = classified.ToString() }, account, values });
            string html = Render(page, values);
            Check(html.Contains("id=\"classified-form\"") && !html.Contains(secret) && !html.Contains("<script>bad()") && !html.Contains("<img src=x"), "Editor missing, leaked key or injected markup");
            if (page.StartsWith("classifieds/")) Check(html.Contains("Fixture region") && html.Contains("name=\"confirmPayment\"") && !html.Contains("id=\"classified-delete\""), "New classified controls incorrect");
            else Check(html.Contains("id=\"classified-delete\"") && !html.Contains("name=\"confirmPayment\"") && html.Contains("&lt;script&gt;"), "Edit controls or stored escaping incorrect");
        }
        var listing = (Dictionary<string, object>)defaults.Invoke(site, new object[] { account });
        adapter.GetMethod("AddOwnedClassifieds", Private).Invoke(integration, new object[] { listing, account });
        adapter.GetMethod("AddClassifiedManagement", Private).Invoke(integration, new object[] { listing, account });
        string pendingHtml = Render("user/classifieds.html", listing);
        Check(pendingHtml.Contains("pending-" + classified) && pendingHtml.Contains("&lt;script&gt;") && !pendingHtml.Contains("<script>bad()"), "Pending workflow absent or unsafe");
        MethodInfo post = adapter.GetMethod("HandlePost", Private);
        var anonymous = DispatchProxy.Create<IOSHttpRequest, HttpProxy>();
        var anonymousValues = ((HttpProxy)(object)anonymous).Values;
        anonymousValues["UriPath"] = "/portal/classifieds/add_classified.html"; anonymousValues["HttpMethod"] = "GET";
        anonymousValues["Url"] = new Uri("https://grid.example/portal/classifieds/add_classified.html");
        anonymousValues["Headers"] = new System.Collections.Specialized.NameValueCollection();
        anonymousValues["QueryAsDictionary"] = new Dictionary<string, string>();
        var denied = DispatchProxy.Create<IOSHttpResponse, HttpProxy>();
        siteType.GetMethod("Handle", Private).Invoke(site, new object[] { anonymous, denied });
        Check((int)((HttpProxy)(object)denied).Values["StatusCode"] == 401, "Anonymous classified HTTP route accepted");
        anonymousValues["HttpMethod"] = "POST";
        anonymousValues["Url"] = new Uri("http://grid.example/portal/classifieds/add_classified.html");
        anonymousValues["RemoteIPEndPoint"] = new IPEndPoint(IPAddress.Parse("203.0.113.8"), 1234);
        ((System.Collections.Specialized.NameValueCollection)anonymousValues["Headers"])["X-Forwarded-Proto"] = "https";
        denied = DispatchProxy.Create<IOSHttpResponse, HttpProxy>();
        siteType.GetMethod("Handle", Private).Invoke(site, new object[] { anonymous, denied });
        Check((int)((HttpProxy)(object)denied).Values["StatusCode"] == 403, "Plaintext classified operations accepted a forged proxy header");
        var authentication = DispatchProxy.Create<IAuthenticationService, RecoveryProxy>();
        ((RecoveryProxy)(object)authentication).Handler = (method, args) => method.Name == "Verify" ? true : null;
        var accounts = DispatchProxy.Create<IUserAccountService, RecoveryProxy>();
        ((RecoveryProxy)(object)accounts).Handler = (method, args) => method.Name == "GetUserAccount" ? account : null;
        siteType.GetField("_authentication", Private).SetValue(site, authentication);
        siteType.GetField("_accounts", Private).SetValue(site, accounts);
        anonymousValues["IsSecured"] = true;
        var headers = (System.Collections.Specialized.NameValueCollection)anonymousValues["Headers"];
        headers["Cookie"] = "ContinuumWebUI=" + account.PrincipalID + ".valid-fixture-token"; headers["Origin"] = "https://grid.example";
        foreach (var operation in new[] {
            ("user/classified_edit.html", "update", "Successfully saved classified"),
            ("user/classified_edit.html", "delete", "Successfully deleted classified"),
            ("user/classifieds.html", "resume", "Successfully saved classified"),
            ("events/add_event.html", "create", "Successfully saved event"),
            ("user/edit_event.html", "delete", "Successfully deleted event") })
        {
            var fields = new Dictionary<string, string> { ["action"] = operation.Item2, ["name"] = "Fixture listing", ["description"] = "HTTP response test",
                ["x"] = "128", ["y"] = "128", ["z"] = "25", ["category"] = "1" };
            if (operation.Item1.Contains("classified"))
            { fields["classifiedID"] = classified.ToString(); fields["requestID"] = classified.ToString(); fields["snapshotID"] = Guid.Empty.ToString(); fields["editToken"] = new string('A', 64); }
            else
            { fields["eventid"] = "17"; fields["revision"] = "1"; fields["requestid"] = Guid.NewGuid().ToString(); fields["regionid"] = region.ToString(); fields["start"] = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm"); fields["duration"] = "60"; fields["eventflags"] = "0"; fields["category"] = "18"; }
            byte[] body = System.Text.Encoding.UTF8.GetBytes(String.Join("&", fields.Select(x => WebUtility.UrlEncode(x.Key) + "=" + WebUtility.UrlEncode(x.Value))));
            anonymousValues["UriPath"] = "/portal/" + operation.Item1; anonymousValues["Url"] = new Uri("https://grid.example/portal/" + operation.Item1);
            anonymousValues["InputStream"] = new MemoryStream(body); anonymousValues["ContentLength64"] = (long)body.Length; anonymousValues["ContentEncoding"] = System.Text.Encoding.UTF8;
            var completed = DispatchProxy.Create<IOSHttpResponse, HttpProxy>();
            siteType.GetMethod("Handle", Private).Invoke(site, new object[] { anonymous, completed });
            var output = ((HttpProxy)(object)completed).Values;
            Check((int)output["StatusCode"] == 200 && System.Text.Encoding.UTF8.GetString((byte[])output["RawBuffer"]) == operation.Item3,
                "Successful " + operation.Item1 + " operation was presented as an error");
        }
        headers["Origin"] = "https://attacker.example";
        var crossSite = DispatchProxy.Create<IOSHttpResponse, HttpProxy>();
        siteType.GetMethod("Handle", Private).Invoke(site, new object[] { anonymous, crossSite });
        Check((int)((HttpProxy)(object)crossSite).Values["StatusCode"] == 403, "Cross-site event/classified mutation accepted");
        headers["Origin"] = "https://grid.example";
        anonymousValues["UriPath"] = "/portal/user/classified_edit.html"; anonymousValues["Url"] = new Uri("https://grid.example/portal/user/classified_edit.html");
        byte[] repeated = System.Text.Encoding.UTF8.GetBytes("action=delete&classifiedID=" + classified);
        anonymousValues["ContentLength64"] = (long)repeated.Length;
        for (int i = 0; i < 28; i++)
        {
            anonymousValues["UriPath"] = i % 2 == 0 ? "/portal/user/classified_edit.html" : "/portal/USER/CLASSIFIED_EDIT.HTML";
            anonymousValues["InputStream"] = new MemoryStream(repeated);
            var limited = DispatchProxy.Create<IOSHttpResponse, HttpProxy>();
            siteType.GetMethod("Handle", Private).Invoke(site, new object[] { anonymous, limited });
            Check((int)((HttpProxy)(object)limited).Values["StatusCode"] == (i < 27 ? 200 : 429), "Classified account rate bound failed");
        }
        var form = new Dictionary<string, string> { ["action"] = "create", ["regionID"] = region.ToString(), ["x"] = "400", ["y"] = "400", ["actor"] = Guid.NewGuid().ToString() };
        object[] args = { "classifieds/add_classified.html", form, account, null }; post.Invoke(integration, args);
        Check((string)args[3] == "Confirm the one-time listing payment before publishing", "No explicit payment confirmation");
        form["confirmPayment"] = "yes"; post.Invoke(integration, args);
        Check((string)args[3] == "Successfully saved classified" && last()["actor"].ToString() == account.PrincipalID.ToString(), "Portal trusted a forged actor or rejected variable-region landing");
        form["x"] = "512"; post.Invoke(integration, args);
        Check((string)args[3] == "The landing coordinates must be inside the chosen region", "Out-of-region landing accepted");
        args[2] = null; post.Invoke(integration, args); Check((string)args[3] == "Log in to manage classifieds", "Anonymous classified mutation accepted");
        Set("_searchUrl", "");
        var unavailable = (Dictionary<string, object>)defaults.Invoke(site, new object[] { account });
        adapter.GetMethod("AddOwnedClassifieds", Private).Invoke(integration, new object[] { unavailable, account });
        adapter.GetMethod("AddClassifiedManagement", Private).Invoke(integration, new object[] { unavailable, account });
        string outage = Render("user/classifieds.html", unavailable);
        Check(outage.Contains("Classified management is unavailable") && !outage.Contains("Create a paid classified"), "Backend outage encouraged a replacement payment");
        Set("_searchWriteSecret", "");
        var off = (Dictionary<string, object>)defaults.Invoke(site, new object[] { account });
        editor.Invoke(integration, new object[] { "classifieds/add_classified.html", new Dictionary<string, string>(), account, off });
        Check(!Render("classifieds/add_classified.html", off).Contains("id=\"classified-form\""), "Disabled classified editor offers payment");
    }
}

public class ClassifiedGrid : DispatchProxy
{
    public GridRegion Region;
    protected override object Invoke(MethodInfo method, object[] args) => method.Name switch
    {
        "GetOnlineRegions" => new List<GridRegion> { Region },
        "GetRegionByUUID" => args[1].Equals(Region.RegionID) ? Region : null,
        "GetRegionByName" => Region,
        _ => method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null
    };
}
