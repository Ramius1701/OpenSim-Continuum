using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Collections.Specialized;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Continuum.WebUI;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.OptionalModules.World.LandAuctions;
using OpenSim.Services.Interfaces;

static class AuctionChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string root)
    {
        OpenSim.Framework.MainConsole.Instance ??= new OpenSim.Framework.Console.LocalConsole("Auction portal tests");
        Guid region = Guid.NewGuid(), seller = Guid.NewGuid(), buyer = Guid.NewGuid(), foreign = Guid.NewGuid(), parcel = Guid.NewGuid();
        string secret = new('a', 48); int reservations = 0;
        var connected = new HashSet<Guid> { seller, buyer };
        using var book = new LandAuctionBook(Path.Combine(root, "web-auctions.json"), _ => { }, _ => { },
            (_, _) => { reservations++; return AuctionPaymentResult.Completed; }, (_, _) => AuctionPaymentResult.Completed);
        var auction = book.Start(parcel, seller, Guid.Empty, "<script>Parcel</script>", 256, 1, DateTime.UtcNow.AddHours(1));
        var portal = new LandAuctionPortal(book, region, "Fixture region", actor => connected.Contains(actor), actor => actor == seller,
            (_, _) => throw new InvalidOperationException("Not used"), _ => Array.Empty<AuctionPortalParcel>(), 24, 1);
        var moduleType = typeof(LandAuctionModule); var module = RuntimeHelpers.GetUninitializedObject(moduleType);
        foreach (var pair in new (string, object)[] { ("m_gate", new object()), ("m_webSecret", secret), ("m_webProxies", new HashSet<IPAddress>()), ("m_portal", portal) })
            moduleType.GetField(pair.Item1, Private).SetValue(module, pair.Item2);
        var handler = moduleType.GetMethod("PortalRequest", Private);
        // Actual bridge handler rejects unauthenticated, oversized and spoofed-transport requests.
        var req = DispatchProxy.Create<IOSHttpRequest, HttpProxy>(); var values = ((HttpProxy)(object)req).Values;
        values["Headers"] = new NameValueCollection { ["X-Forwarded-Proto"] = "https" };
        values["Url"] = new Uri("http://grid.example/continuum/auctions/" + region); values["RemoteIPEndPoint"] = new IPEndPoint(IPAddress.Parse("203.0.113.1"), 1234);
        values["HttpMethod"] = "POST"; values["ContentLength64"] = 2L; values["InputStream"] = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        int Direct()
        {
            var response = DispatchProxy.Create<IOSHttpResponse, HttpProxy>(); handler.Invoke(module, new object[] { req, response });
            return (int)((HttpProxy)(object)response).Values["StatusCode"];
        }
        Check(Direct() == 403, "Auction bridge trusted a forged TLS header");
        values["IsSecured"] = true; values["InputStream"] = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        Check(Direct() == 403, "Auction bridge accepted missing private key");
        values["ContentLength64"] = 5000L; Check(Direct() == 413, "Auction bridge accepted oversized request");
        values["HttpMethod"] = "GET"; Check(Direct() == 405, "Auction bridge accepted GET action");
        var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); int port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        var server = new BaseHttpServer((uint)port); string path = "/continuum/auctions/" + region;
        server.AddSimpleStreamHandler(new SimpleStreamHandler(path, (request, response) => handler.Invoke(module, new object[] { request, response })));
        server.Start();
        try
        {
            var adapter = typeof(WebUIContentStore).Assembly.GetType("OpenSim.Continuum.WebUI.WebUIServiceIntegration", true);
            var integration = RuntimeHelpers.GetUninitializedObject(adapter);
            void Set(string key, object value) => adapter.GetField(key, Private).SetValue(integration, value);
            Set("_auctionSecret", secret); Set("_auctionEndpoints", new Dictionary<Guid, string> { [region] = "http://127.0.0.1:" + port + path });
            Set("_rpcTimeoutMs", 1000); Set("_adminLevel", 200);
            var buyerAccount = new UserAccount { PrincipalID = new UUID(buyer.ToString()), FirstName = "Auction", LastName = "Buyer" };
            var sellerAccount = new UserAccount { PrincipalID = new UUID(seller.ToString()), FirstName = "Auction", LastName = "Seller" };
            var siteType = typeof(WebUIContentStore).Assembly.GetType("OpenSim.Continuum.WebUI.WhiteCoreSite", true);
            var site = RuntimeHelpers.GetUninitializedObject(siteType);
            siteType.GetField("_mountPath", Private).SetValue(site, "/portal"); siteType.GetField("_systemName", Private).SetValue(site, "Fixture grid");
            siteType.GetField("_integration", Private).SetValue(site, integration);
            siteType.GetField("_attemptsLock", Private).SetValue(site, new object());
            siteType.GetField("_attempts", Private).SetValue(site, new Dictionary<string, Queue<DateTime>>());
            var add = adapter.GetMethod("AddAuctions", Private);
            var defaults = siteType.GetMethod("DefaultVariables", Private); var render = siteType.GetMethod("Render", Private);
            var resource = siteType.GetMethod("ReadResource", BindingFlags.Static | BindingFlags.NonPublic);
            string Render(UserAccount user)
            {
                var vars = (Dictionary<string, object>)defaults.Invoke(site, new object[] { user });
                add.Invoke(integration, new object[] { new Dictionary<string, string>(), user, vars });
                return (string)render.Invoke(site, new object[] { "auctions.html", Encoding.UTF8.GetString((byte[])resource.Invoke(null, new object[] { "auctions.html" })), vars, user != null, false, 0 });
            }
            string html = Render(buyerAccount);
            Check(html.Contains("auction-bid-") && html.Contains("name=\"confirm\"") && html.Contains("&lt;script&gt;Parcel") && !html.Contains("<script>Parcel") && !html.Contains(secret), "Auction forms, escaping or secret isolation failed");
            Check(!Render(null).Contains("name=\"amount\"") && !Render(null).Contains("auction-cancel-"), "Public auction page offered writes");
            Check(Render(sellerAccount).Contains("auction-cancel-"), "Seller auction management missing");
            var post = adapter.GetMethod("HandlePost", Private);
            var form = new Dictionary<string, string> { ["region"] = region.ToString(), ["action"] = "bid", ["auction"] = auction.ID.ToString(), ["amount"] = "10", ["actor"] = foreign.ToString() };
            object[] args = { "auctions.html", form, buyerAccount, null }; post.Invoke(integration, args);
            Check((string)args[3] == "Confirm the full bid reservation before bidding" && reservations == 0, "Unconfirmed auction fee reservation");
            form["confirm"] = "yes"; post.Invoke(integration, args);
            Check((string)args[3] == "Auction bid reserved" && book.List().Single().High.Buyer == buyer && reservations == 1, "Grid portal trusted forged actor or lost successful bid");
            connected.Remove(buyer); form["amount"] = "20"; post.Invoke(integration, args);
            Check(!((string)args[3]).StartsWith("Auction bid reserved") && reservations == 1 && Render(buyerAccount).Contains("Visit this region"), "Grid portal bypassed live-viewer authority"); connected.Add(buyer);
            var auth = DispatchProxy.Create<IAuthenticationService, RecoveryProxy>();
            ((RecoveryProxy)(object)auth).Handler = (method, input) => method.Name == "Verify" ? true : null;
            var accounts = DispatchProxy.Create<IUserAccountService, RecoveryProxy>();
            ((RecoveryProxy)(object)accounts).Handler = (method, input) => method.Name == "GetUserAccount" ? buyerAccount : null;
            siteType.GetField("_authentication", Private).SetValue(site, auth); siteType.GetField("_accounts", Private).SetValue(site, accounts);
            var frontend = DispatchProxy.Create<IOSHttpRequest, HttpProxy>(); var fields = ((HttpProxy)(object)frontend).Values;
            var headers = new NameValueCollection { ["Origin"] = "https://grid.example" };
            fields["Headers"] = headers; fields["UriPath"] = "/portal/auctions.html"; fields["HttpMethod"] = "POST";
            fields["Url"] = new Uri("https://grid.example/portal/auctions.html"); fields["IsSecured"] = true;
            fields["QueryAsDictionary"] = new Dictionary<string, string>();
            byte[] body = Encoding.UTF8.GetBytes("region=" + region + "&action=bid&auction=" + auction.ID + "&amount=20&confirm=yes");
            Dictionary<string, object> Request()
            {
                fields["InputStream"] = new MemoryStream(body); fields["ContentLength64"] = (long)body.Length; fields["ContentEncoding"] = Encoding.UTF8;
                var response = DispatchProxy.Create<IOSHttpResponse, HttpProxy>(); siteType.GetMethod("Handle", Private).Invoke(site, new object[] { frontend, response });
                return ((HttpProxy)(object)response).Values;
            }
            Check((int)Request()["StatusCode"] == 401, "Anonymous auction POST accepted");
            headers["Cookie"] = "ContinuumWebUI=" + buyerAccount.PrincipalID + ".fixture"; headers["Origin"] = "https://attacker.example";
            Check((int)Request()["StatusCode"] == 403, "Cross-origin auction POST accepted");
            headers["Origin"] = "https://grid.example";
            var success = Request(); Check(Encoding.UTF8.GetString((byte[])success["RawBuffer"]) == "Auction bid reserved" && reservations == 2, "Auction HTTP success marked as failure");
            fields["IsSecured"] = false; fields["Url"] = new Uri("http://grid.example/portal/auctions.html"); fields["RemoteIPEndPoint"] = new IPEndPoint(IPAddress.Parse("203.0.113.3"), 1234);
            headers["X-Forwarded-Proto"] = "https";
            Check((int)Request()["StatusCode"] == 403, "Auction-enabled account transport accepted plaintext/spoofed TLS");
            fields["IsSecured"] = true; fields["Url"] = new Uri("https://grid.example/portal/auctions.html");
            body = Encoding.UTF8.GetBytes("action=unsupported");
            for (int i = 0; i < 30; i++)
            {
                fields["UriPath"] = i % 2 == 0 ? "/portal/AUCTIONS.HTML" : "/portal/auctions.html";
                Check((int)Request()["StatusCode"] == (i < 29 ? 200 : 429), "Auction account rate bound/casing failed");
            }
            var config = new Nini.Config.IniConfigSource(); var section = config.AddConfig("ContinuumWebUIAuctions");
            section.Set(region.ToString(), "http://public.example" + path);
            bool rejected = false; try { adapter.GetMethod("ConfigureAuctionEndpoints", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { config, secret, Array.Empty<string>() }); }
            catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { rejected = true; }
            Check(rejected, "Public HTTP auction endpoint accepted");
            Set("_auctionSecret", new string('x', 48));
            Check(Render(buyerAccount).Contains("auction service is unavailable") && !Render(buyerAccount).Contains("name=\"amount\""), "Bridge outage/denial offered new financial controls");
            Console.WriteLine("PASS: actual auction bridge/private transport, grid WebUI actor binding, native authority, forms, escaping, origin/TLS denial and rate bounds.");
        }
        finally { server.Stop(); }
    }
}
