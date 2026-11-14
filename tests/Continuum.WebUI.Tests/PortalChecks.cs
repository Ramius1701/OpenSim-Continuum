using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Collections.Specialized;
using OpenSim.Continuum.WebUI;
using OpenSim.Services.Interfaces;
using OpenSim.Framework.Servers.HttpServer;
using OpenMetaverse;

static class PortalChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run(string root)
    {
        string path = Path.Combine(root, "portal.json");
        var store = new WebUIPortalSettingsStore(path);
        Check(store.Read().Revision == 0, "Missing portal file did not use defaults");
        var settings = store.Read();
        settings.Heading = "<script>bad()</script>"; settings.Introduction = "A resident community";
        settings.NoticeEnabled = true; settings.NoticeTitle = "Service announcement"; settings.NoticeText = "<img src=x onerror=bad()>";
        settings.SupportText = "Contact the help team\nDo not share passwords"; settings.HiddenNavigation = new[] { "news" };
        store.Save(settings, 0);
        var other = new WebUIPortalSettingsStore(path);
        Check(other.Read().Heading == settings.Heading && other.Read().Revision == 1, "Portal restart lost content");
        bool stale = false; try { other.Save(other.Read(), 0); } catch (InvalidOperationException) { stale = true; }
        Check(stale && store.Read().Revision == 1, "Stale editor overwrote portal settings");
        var invalid = store.Read(); invalid.HiddenNavigation = new[] { "administration" };
        bool rejected = false; try { store.Save(invalid, 1); } catch (ArgumentException) { rejected = true; }
        Check(rejected && store.Read().HiddenNavigation.Single() == "news", "Protected navigation could be hidden");
        using (var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            bool busy = false; try { other.Save(other.Read(), 1); } catch (IOException) { busy = true; }
            Check(busy, "Portal writer ignored the process lease");
        }
        var assembly = typeof(WebUIContentStore).Assembly;
        var adapter = assembly.GetType("OpenSim.Continuum.WebUI.WebUIServiceIntegration", true);
        var integration = RuntimeHelpers.GetUninitializedObject(adapter);
        adapter.GetField("_portalStore", Private).SetValue(integration, store);
        adapter.GetField("_adminLevel", Private).SetValue(integration, 200);
        adapter.GetField("_allowRegistration", Private).SetValue(integration, true);
        var admin = new UserAccount { PrincipalID = UUID.Random(), UserLevel = 200 };
        var resident = new UserAccount { PrincipalID = UUID.Random() };
        var post = adapter.GetMethod("HandlePost", Private);
        var add = adapter.GetMethod("AddPortal", Private);
        var form = new Dictionary<string, string> { ["revision"] = "1", ["supportText"] = "New support instructions" };
        foreach (var actor in new UserAccount[] { resident, null })
        {
            object[] args = { "admin/settings_manager.html", form, actor, null }; post.Invoke(integration, args);
            Check((string)args[3] == "Administrator access required" && store.Read().Revision == 1, "Unauthorized portal mutation");
        }
        var siteType = assembly.GetType("OpenSim.Continuum.WebUI.WhiteCoreSite", true);
        var site = RuntimeHelpers.GetUninitializedObject(siteType);
        siteType.GetField("_mountPath", Private).SetValue(site, "/portal");
        siteType.GetField("_systemName", Private).SetValue(site, "Fixture grid");
        siteType.GetField("_integration", Private).SetValue(site, integration);
        var defaults = siteType.GetMethod("DefaultVariables", Private);
        var render = siteType.GetMethod("Render", Private);
        var resource = siteType.GetMethod("ReadResource", BindingFlags.Static | BindingFlags.NonPublic);
        string Render(string page, UserAccount user)
        {
            var values = (Dictionary<string, object>)defaults.Invoke(site, new object[] { user });
            add.Invoke(integration, new object[] { page, user, values });
            byte[] bytes = (byte[])resource.Invoke(null, new object[] { page });
            Check(bytes != null, "Missing embedded portal asset " + page);
            return (string)render.Invoke(site, new object[] { page, Encoding.UTF8.GetString(bytes), values, user != null, user?.UserLevel >= 200, 0 });
        }
        foreach (string page in new[] { "admin/welcomescreen_manager.html", "admin/settings_manager.html", "admin/page_manager.html" })
        {
            string html = Render(page, admin);
            Check(html.Contains("id=\"portal_form\"") && html.Contains("name=\"revision\" value=\"1\"") && !html.Contains("<script>bad()"), "Portal editor render failed " + page);
            if (page.Contains("page_manager")) Check(html.Contains("nav_news") && html.Contains("nav_region_search"), "Navigation options vanished");
        }
        string home = Render("home.html", null);
        Check(home.Contains("container-fluid blog"), "Enabled home gallery missing");
        Check(home.Contains("&lt;script&gt;") && home.Contains("&lt;img") && home.Contains("Create your account"), "Welcome content escaping or registration failed");
        string splash = Render("welcomescreen/info_box.html", null);
        Check(splash.Contains("Service announcement") && !splash.Contains("<img src=x"), "Viewer welcome announcement failed");
        Check(Render("user/support.html", resident).Contains("Contact the help team"), "Support instructions missing");
        var menuValues = (Dictionary<string, object>)defaults.Invoke(site, new object[] { admin });
        add.Invoke(integration, new object[] { "index.html", admin, menuValues });
        var menus = (List<Dictionary<string, object>>)menuValues["MenuItems"];
        Check(!(bool)menus.Single(x => (string)x["MenuItemID"] == "news")["ShowInMenu"]
            && (bool)menus.Single(x => (string)x["MenuItemID"] == "administration")["ShowInMenu"], "Navigation hid protected menu or ignored saved choice");
        // Exercise actual HTTP authorization/origin checks and success-message contract.
        var auth = DispatchProxy.Create<IAuthenticationService, RecoveryProxy>();
        ((RecoveryProxy)(object)auth).Handler = (method, args) => method.Name == "Verify" ? true : null;
        var accounts = DispatchProxy.Create<IUserAccountService, RecoveryProxy>();
        UserAccount active = resident;
        ((RecoveryProxy)(object)accounts).Handler = (method, args) => method.Name == "GetUserAccount" ? active : null;
        siteType.GetField("_authentication", Private).SetValue(site, auth);
        siteType.GetField("_accounts", Private).SetValue(site, accounts);
        var req = DispatchProxy.Create<IOSHttpRequest, HttpProxy>();
        var fields = ((HttpProxy)(object)req).Values;
        var headers = new NameValueCollection { ["Cookie"] = "ContinuumWebUI=" + resident.PrincipalID + ".fixture", ["Origin"] = "https://grid.example" };
        fields["UriPath"] = "/portal/admin/settings_manager.html"; fields["HttpMethod"] = "POST";
        fields["Url"] = new Uri("https://grid.example/portal/admin/settings_manager.html"); fields["IsSecured"] = true;
        fields["Headers"] = headers; fields["QueryAsDictionary"] = new Dictionary<string, string>();
        byte[] body = Encoding.UTF8.GetBytes("revision=1&supportText=Updated+help");
        fields["ContentLength64"] = (long)body.Length; fields["ContentEncoding"] = Encoding.UTF8;
        Dictionary<string, object> Request()
        {
            fields["InputStream"] = new MemoryStream(body);
            var response = DispatchProxy.Create<IOSHttpResponse, HttpProxy>();
            siteType.GetMethod("Handle", Private).Invoke(site, new object[] { req, response });
            return ((HttpProxy)(object)response).Values;
        }
        Check((int)Request()["StatusCode"] == 403 && store.Read().Revision == 1, "Resident reached portal HTTP mutation");
        active = admin; headers["Cookie"] = "ContinuumWebUI=" + admin.PrincipalID + ".fixture";
        headers["Origin"] = "https://attacker.example";
        Check((int)Request()["StatusCode"] == 403 && store.Read().Revision == 1, "Cross-site portal mutation accepted");
        headers["Origin"] = "https://grid.example";
        var success = Request();
        Check((int)success["StatusCode"] == 200 && Encoding.UTF8.GetString((byte[])success["RawBuffer"]) == "Successfully saved portal settings"
            && store.Read().Revision == 2 && store.Read().Heading == settings.Heading, "Portal success contract or section preservation failed");
        Check(Encoding.UTF8.GetString((byte[])Request()["RawBuffer"]).Contains("Reload before saving"), "Repeated stale HTTP submission overwrote settings");
        Check(!Render("home.html", null).Contains("container-fluid blog"), "Disabled slideshow remained active");
        settings = store.Read(); settings.NoticeEnabled = false; store.Save(settings, 2);
        Check(!Render("welcomescreen/info_box.html", null).Contains("Service announcement"), "Disabled welcome announcement leaked");
        adapter.GetField("_allowRegistration", Private).SetValue(integration, false);
        Check(!Render("home.html", null).Contains("Create your account"), "Home ignored native registration policy");
        var welcomeForm = new Dictionary<string, string> { ["revision"] = "3", ["heading"] = "A new welcome", ["introduction"] = "Our community", ["noticeTitle"] = "News", ["noticeText"] = "Reopening", ["noticeEnabled"] = "true" };
        object[] update = { "admin/welcomescreen_manager.html", welcomeForm, admin, null }; post.Invoke(integration, update);
        Check((string)update[3] == "Successfully saved portal settings" && store.Read().Heading == "A new welcome"
            && store.Read().SupportText == "Updated help", "Welcome write failed or overwrote support settings");
        var navigationForm = WebUIPortalSettingsStore.Navigation.ToDictionary(id => "nav_" + id, _ => "true");
        navigationForm["revision"] = "4";
        update = new object[] { "admin/page_manager.html", navigationForm, admin, null }; post.Invoke(integration, update);
        Check((string)update[3] == "Successfully saved portal settings" && store.Read().HiddenNavigation.Length == 0
            && store.Read().NoticeText == "Reopening", "Navigation write failed or overwrote welcome content");
        fields["HttpMethod"] = "GET";
        fields["QueryAsDictionary"] = new Dictionary<string, string> { ["revision"] = "5", ["supportText"] = "GET overwrite" };
        Check((int)Request()["StatusCode"] == 200 && store.Read().Revision == 5 && store.Read().SupportText == "Updated help", "GET query mutated portal settings");
        settings = store.Read(); settings.Introduction = new string('x', 2001);
        bool bounded = false; try { store.Save(settings, 5); } catch (ArgumentException) { bounded = true; }
        Check(bounded && store.Read().Revision == 5, "Overlong welcome text persisted");
        // Resets share the same revision/lease boundary and cannot overwrite unrelated settings.
        settings = store.Read(); settings.HiddenNavigation = new[] { "news" }; store.Save(settings, 5);
        var reset = new Dictionary<string, string> { ["revision"] = "6", ["action"] = "navigation" };
        object[] resetArgs = { "admin/factory_reset.html", reset, admin, null }; post.Invoke(integration, resetArgs);
        Check(store.Read().Revision == 6 && ((string)resetArgs[3]).Contains("Confirm"), "Unconfirmed portal reset persisted");
        reset["confirm"] = "true";
        resetArgs[2] = resident; post.Invoke(integration, resetArgs);
        Check(store.Read().Revision == 6 && (string)resetArgs[3] == "Administrator access required", "Resident reset portal defaults");
        resetArgs[2] = admin; post.Invoke(integration, resetArgs);
        Check(store.Read().Revision == 7 && store.Read().HiddenNavigation.Length == 0
            && store.Read().Heading == "A new welcome", "Navigation reset changed presentation or failed");
        post.Invoke(integration, resetArgs);
        Check(store.Read().Revision == 7 && ((string)resetArgs[3]).Contains("Reload before saving"), "Stale reset overwrote portal");
        settings = store.Read(); settings.HiddenNavigation = new[] { "events" }; store.Save(settings, 7);
        reset["revision"] = "8"; reset["action"] = "presentation"; post.Invoke(integration, resetArgs);
        Check(store.Read().Revision == 9 && store.Read().Heading == new WebUIPortalSettings().Heading
            && store.Read().HiddenNavigation.SequenceEqual(new[] { "events" }), "Presentation reset changed navigation or failed");
        string resetHtml = Render("admin/factory_reset.html", admin);
        Check(resetHtml.Contains("portal_reset_form") && resetHtml.Contains("value=\"9\"")
            && resetHtml.Contains("name=\"confirm\""), "Embedded reset form missing revision/confirmation");
        File.WriteAllText(path, "broken");
        bool corrupt = false; try { new WebUIPortalSettingsStore(path); } catch (InvalidDataException) { corrupt = true; }
        Check(corrupt && File.ReadAllText(path) == "broken", "Corrupt portal settings silently reset");
        Console.WriteLine("PASS: portal persistence, stale edits, writer lease, bounded navigation, admin/origin authority, embedded welcome/support forms and escaping.");
    }
}
