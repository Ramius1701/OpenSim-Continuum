using System.Reflection;
using System.Runtime.CompilerServices;
using OpenSim.Continuum.WebUI;
using OpenSim.Services.Interfaces;
using OpenMetaverse;
using OpenSim.Framework.Servers.HttpServer;

static class Program
{
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Main(string[] arguments)
    {
        string root = Path.Combine(Path.GetTempPath(), "continuum-webui-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            if (arguments.Contains("--event-notifications-only")) { EventNotificationChecks.Run(root); return; }
            if (arguments.Contains("--currency-credits-only")) { CurrencyCreditChecks.Run(root); return; }
            if (arguments.Contains("--auctions-only")) { AuctionChecks.Run(root); return; }
            string path = Path.Combine(root, "content.json");
            var store = new WebUIContentStore(path);
            Guid author = Guid.NewGuid();
            Guid id = store.Save(Guid.Empty, 0, author, "<b>News</b>", "<script>bad()</script>\nSecond line", false);
            Assert(new WebUIContentStore(path).Read().Single().Text.Contains("Second line"), "Restart lost content");
            var other = new WebUIContentStore(path);
            store.Save(id, 1, author, "Published", "Text", true);
            bool rejected = false;
            try { other.Save(id, 1, author, "Stale", "Overwrite", true); } catch (InvalidOperationException) { rejected = true; }
            Assert(rejected && store.Read().Single().Title == "Published", "Stale editor overwrote current article");
            store.Save(Guid.Empty, 0, author, "Private draft", "Never public", false);
            var type = typeof(WebUIContentStore).Assembly.GetType("OpenSim.Continuum.WebUI.WebUIServiceIntegration", true);
            var integration = RuntimeHelpers.GetUninitializedObject(type);
            type.GetField("_contentStore", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(integration, store);
            type.GetField("_adminLevel", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(integration, 200);
            var admin = new UserAccount { PrincipalID = UUID.Random(), UserLevel = 200 };
            var resident = new UserAccount { PrincipalID = UUID.Random(), UserLevel = 0 };
            var post = type.GetMethod("HandlePost", BindingFlags.Instance | BindingFlags.NonPublic);
            var form = new Dictionary<string, string> { ["NewsTitle"] = "<img src=x onerror=bad()>", ["NewsText"] = "<script>bad()</script>", ["published"] = "true" };
            object[] args = { "admin/news_add.html", form, resident, null };
            post.Invoke(integration, args);
            Assert((string)args[3] == "Administrator access required" && store.Read().Length == 2, "Resident created news");
            args[2] = null; post.Invoke(integration, args);
            Assert(store.Read().Length == 2, "Anonymous user created news");
            args[2] = admin; post.Invoke(integration, args);
            Assert((string)args[3] == "Successfully saved news article" && store.Read().Length == 3, "Admin create failed");
            var add = type.GetMethod("AddNews", BindingFlags.Instance | BindingFlags.NonPublic);
            var vars = new Dictionary<string, object>();
            add.Invoke(integration, new object[] { "news.html", new Dictionary<string, string>(), null, vars });
            var rows = (List<Dictionary<string, object>>)vars["NewsRows"];
            Assert(rows.Count == 2 && rows.All(x => (string)x["NewsTitle"] != "Private draft"), "Draft leaked publicly");
            Assert(rows.Any(x => ((string)x["NewsTitle"]).Contains("&lt;img")) && rows.Any(x => ((string)x["NewsText"]).Contains("&lt;script")), "News was not HTML escaped");
            var siteType = typeof(WebUIContentStore).Assembly.GetType("OpenSim.Continuum.WebUI.WhiteCoreSite", true);
            var site = RuntimeHelpers.GetUninitializedObject(siteType);
            siteType.GetField("_mountPath", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(site, "/portal");
            siteType.GetField("_systemName", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(site, "Fixture grid");
            siteType.GetField("_integration", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(site, integration);
            var defaults = siteType.GetMethod("DefaultVariables", BindingFlags.Instance | BindingFlags.NonPublic);
            var render = siteType.GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic);
            var resource = siteType.GetMethod("ReadResource", BindingFlags.Static | BindingFlags.NonPublic);
            var request = DispatchProxy.Create<IOSHttpRequest, HttpProxy>();
            var requestValues = ((HttpProxy)(object)request).Values;
            requestValues["UriPath"] = "/portal/admin/news_add.html";
            requestValues["HttpMethod"] = "POST";
            requestValues["Headers"] = new System.Collections.Specialized.NameValueCollection();
            requestValues["QueryAsDictionary"] = new Dictionary<string, string>();
            requestValues["Url"] = new Uri("https://grid.example/portal/admin/news_add.html");
            var response = DispatchProxy.Create<IOSHttpResponse, HttpProxy>();
            siteType.GetMethod("Handle", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(site, new object[] { request, response });
            Assert((int)((HttpProxy)(object)response).Values["StatusCode"] == 403 && store.Read().Length == 3,
                "Anonymous HTTP request bypassed admin route authorization");
            foreach (string page in new[] { "news.html", "news_list.html", "admin/news_add.html", "admin/news_manager.html", "admin/news_edit.html" })
            {
                var values = (Dictionary<string, object>)defaults.Invoke(site, new object[] { admin });
                add.Invoke(integration, new object[] { page, new Dictionary<string, string> { ["newsid"] = id.ToString() }, admin, values });
                var bytes = (byte[])resource.Invoke(null, new object[] { page });
                Assert(bytes != null, "Page missing from embedded asset bundle: " + page);
                string html = (string)render.Invoke(site, new object[] { page, System.Text.Encoding.UTF8.GetString(bytes), values, true, true, 0 });
                Assert(!System.Text.RegularExpressions.Regex.IsMatch(html, @"\{News[A-Za-z]*\}"), "Unresolved news placeholder in " + page);
                Assert(!html.Contains("<script>bad()"), "Stored script executed in rendered markup");
                if (page == "admin/news_manager.html")
                    Assert(html.Contains("method=\"post\"") && !html.Contains("?delete="), "Deletion is not POST-only");
            }
            var eventEditor = type.GetMethod("AddEventEditor", BindingFlags.Instance | BindingFlags.NonPublic);
            var eventValues = (Dictionary<string, object>)defaults.Invoke(site, new object[] { resident });
            eventEditor.Invoke(integration, new object[] { "events/add_event.html", new Dictionary<string, string>(), resident, eventValues });
            byte[] eventBytes = (byte[])resource.Invoke(null, new object[] { "events/add_event.html" });
            string eventHtml = (string)render.Invoke(site, new object[] { "events/add_event.html", System.Text.Encoding.UTF8.GetString(eventBytes), eventValues, true, false, 0 });
            Assert(eventHtml.Contains("Event management is not available") && !eventHtml.Contains("id=\"event-form\""), "Disabled event editor offers writes");
            type.GetField("_searchWriteSecret", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(integration, new string('x', 48));
            eventEditor.Invoke(integration, new object[] { "events/add_event.html", new Dictionary<string, string>(), resident, eventValues });
            eventHtml = (string)render.Invoke(site, new object[] { "events/add_event.html", System.Text.Encoding.UTF8.GetString(eventBytes), eventValues, true, false, 0 });
            Assert(eventHtml.Contains("id=\"event-form\"") && !eventHtml.Contains(new string('x', 48))
                && !System.Text.RegularExpressions.Regex.IsMatch(eventHtml, @"\{Event[A-Za-z]*\}"), "Event form rendering or secret isolation");
            object[] eventPost = { "events/add_event.html", new Dictionary<string, string> { ["action"] = "create", ["regionid"] = UUID.Random().ToString() }, resident, null };
            post.Invoke(integration, eventPost);
            Assert((string)eventPost[3] == "Choose a region you own", "Event writes bypass native region ownership");
            eventPost[2] = null; post.Invoke(integration, eventPost);
            Assert((string)eventPost[3] == "Log in to manage your events", "Anonymous event mutation accepted");
            Console.WriteLine("PASS: event form rendering, disabled configuration, secret isolation and native ownership checks.");
            Assert(Vector3.TryParse("256100,256120,30", out Vector3 eventPosition)
                && eventPosition.X == 256100 && eventPosition.Y == 256120 && eventPosition.Z == 30,
                "Event position cannot be decoded by the native viewer search module");
            var delete = new Dictionary<string, string> { ["newsid"] = id.ToString(), ["revision"] = "2", ["action"] = "delete" };
            args = new object[] { "admin/news_manager.html", delete, resident, null }; post.Invoke(integration, args);
            Assert(store.Read().Any(x => x.ID == id), "Resident deleted news");
            args[2] = admin; post.Invoke(integration, args);
            Assert((string)args[3] == "Successfully deleted news article" && store.Read().All(x => x.ID != id), "Admin delete failed");
            // A foreign writer cannot race an update through the process lease.
            using (var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                bool busy = false; try { other.Save(Guid.Empty, 0, author, "Busy", "No write", true); } catch (IOException) { busy = true; }
                Assert(busy, "Concurrent writer ignored lease");
            }
            File.WriteAllText(path, "not json");
            bool corrupt = false; try { new WebUIContentStore(path); } catch (System.Text.Json.JsonException) { corrupt = true; }
            Assert(corrupt && File.ReadAllText(path) == "not json", "Corruption silently reset content");
            PortalChecks.Run(root);
            EstateChecks.Run(root);
            ContactChecks.Run(root);
            AccountClosureChecks.Run(root);
            PartnershipChecks.Run(root);
            GridConfigurationChecks.Run(root);
            OperatorChecks.Run(root);
            RecoveryChecks.Run(root);
            ClassifiedChecks.Run(root);
            Console.WriteLine("PASS: WebUI news authorization, drafts, escaping, CRUD, stale edits, restart persistence, concurrent writer lease and corruption handling.");
        }
        finally { Directory.Delete(root, true); }
    }
}

public class HttpProxy : DispatchProxy
{
    public readonly Dictionary<string, object> Values = new();
    protected override object Invoke(MethodInfo target, object[] arguments)
    {
        if (target.Name == "AddHeader") { Values["Header:" + arguments[0]] = arguments[1]; return null; }
        if (target.Name == "Redirect") { Values["StatusCode"] = (int)(System.Net.HttpStatusCode)arguments[1]; Values["Header:Location"] = arguments[0]; return null; }
        if (target.Name.StartsWith("set_")) { Values[target.Name.Substring(4)] = arguments[0]; return null; }
        if (target.Name.StartsWith("get_") && Values.TryGetValue(target.Name.Substring(4), out var value)) return value;
        return target.ReturnType.IsValueType && target.ReturnType != typeof(void) ? Activator.CreateInstance(target.ReturnType) : null;
    }
}
