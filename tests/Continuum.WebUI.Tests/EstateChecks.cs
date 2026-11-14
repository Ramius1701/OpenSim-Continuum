using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Data.SQLite;
using System.Collections.Specialized;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Continuum.WebUI;
using OpenSim.Data.SQLite;
using OpenSim.Services.EstateService;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using OpenSim.Framework.Servers.HttpServer;

static class EstateChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run(string root)
    {
        OpenSim.Framework.MainConsole.Instance ??= new OpenSim.Framework.Console.LocalConsole("Estate tests");
        string path = Path.Combine(root, "estate.sqlite");
        var store = new SQLiteEstateStore("Data Source=" + path + ";Version=3");
        SQLiteConnection Connection(SQLiteEstateStore source) => (SQLiteConnection)typeof(SQLiteEstateStore).GetField("m_connection", Private).GetValue(source);
        SQLiteEstateStore reopened = null;
        try
        {
            var owner = new UserAccount { PrincipalID = UUID.Random(), FirstName = "Estate", LastName = "Owner", UserLevel = 0 };
            var stranger = new UserAccount { PrincipalID = UUID.Random(), FirstName = "Other", LastName = "Owner" };
            var admin = new UserAccount { PrincipalID = UUID.Random(), FirstName = "Grid", LastName = "Admin", UserLevel = 200 };
            var estate = store.CreateNewEstate(101);
            estate.EstateName = "Original estate"; estate.EstateOwner = owner.PrincipalID;
            estate.PricePerMeter = 10; estate.PublicAccess = false; estate.AllowVoice = true; estate.TaxFree = true;
            estate.DenyAnonymous = true; estate.DenyMinors = true; estate.AllowEnvironmentOverride = true;
            UUID manager = UUID.Random(), experience = UUID.Random(), access = UUID.Random(), ban = UUID.Random(), region = UUID.Random();
            estate.EstateManagers = new[] { manager }; estate.EstateAccess = new[] { access }; estate.AllowedExperiences = new[] { experience };
            estate.EstateBans = new[] { new EstateBan { EstateID = 101, BannedUserID = ban, BanningUserID = owner.PrincipalID, BanTime = 1234 } };
            store.StoreEstateSettings(estate); store.LinkRegion(region, 101);
            var second = store.CreateNewEstate(102); second.EstateOwner = stranger.PrincipalID; second.EstateName = "Other estate"; store.StoreEstateSettings(second);
            Check(store.GetRegions(101).SequenceEqual(new[] { region }) && store.GetRegions(102).Count == 0, "SQLite estate-region lookup incomplete");
            EstateSettings Read() => store.LoadEstateSettings(101);
            EstateSettings Updated(EstateSettings original, string name) => new EstateSettings
            { EstateID = original.EstateID, EstateOwner = original.EstateOwner, EstateName = name, PricePerMeter = 20, PublicAccess = true, AllowVoice = false, AllowDirectTeleport = true };
            var snapshot = Read();
            var service = (EstateDataService)RuntimeHelpers.GetUninitializedObject(typeof(EstateDataService));
            typeof(EstateDataService).GetField("m_database", Private).SetValue(service, store);
            Check(service.SupportsEstateSettingsUpdate && service.TryUpdateEstateSettings(snapshot, Updated(snapshot, "Updated estate")), "Native service rejected conditional update");
            var saved = Read();
            var explicitBan = saved.EstateBans.Single(); saved.AddBan(explicitBan);
            Check(saved.EstateBans.Length == 1, "Duplicate explicit estate ban accepted");
            saved.AddBan(new EstateBan { BannedUserID = UUID.Random() });
            Check(saved.EstateBans.Length == 2, "Payment/age denial suppressed a new explicit estate ban");
            saved = Read();
            Check(saved.EstateName == "Updated estate" && saved.PricePerMeter == 20 && saved.PublicAccess && !saved.AllowVoice && saved.AllowDirectTeleport, "Estate update values did not persist");
            Check(saved.EstateOwner == owner.PrincipalID && saved.TaxFree && saved.DenyAnonymous && saved.AllowEnvironmentOverride
                && saved.EstateManagers.SequenceEqual(new[] { manager }) && saved.EstateAccess.SequenceEqual(new[] { access })
                && saved.AllowedExperiences.SequenceEqual(new[] { experience }) && saved.EstateBans.Single().BannedUserID == ban
                && store.GetRegions(101).Single() == region && store.LoadEstateSettings(102).EstateName == "Other estate", "Narrow estate update altered unrelated policy, membership or another estate");
            Check(!service.TryUpdateEstateSettings(snapshot, Updated(snapshot, "Stale overwrite")), "Stale native update overwrote newer settings");
            reopened = new SQLiteEstateStore("Data Source=" + path + ";Version=3");
            Check(reopened.LoadEstateSettings(101).EstateName == "Updated estate", "Estate settings lost on reopen");
            snapshot = Read();
            var one = Updated(snapshot, "Concurrent one"); var two = Updated(snapshot, "Concurrent two");
            var results = Task.WhenAll(Task.Run(() => store.TryUpdateEstateSettings(snapshot, one)), Task.Run(() => reopened.TryUpdateEstateSettings(snapshot, two))).GetAwaiter().GetResult();
            Check(results.Count(x => x) == 1, "Concurrent estate editors did not produce exactly one winner");
            snapshot = Read();
            using (var command = Connection(store).CreateCommand())
            { command.CommandText = "UPDATE estate_settings SET EstateOwner=@owner WHERE EstateID=101"; command.Parameters.AddWithValue("@owner", stranger.PrincipalID.ToString()); command.ExecuteNonQuery(); }
            Check(!service.TryUpdateEstateSettings(snapshot, Updated(snapshot, "Old owner overwrite")), "Ownership race bypassed native conditional guard");
            using (var command = Connection(store).CreateCommand())
            { command.CommandText = "UPDATE estate_settings SET EstateOwner=@owner WHERE EstateID=101"; command.Parameters.AddWithValue("@owner", owner.PrincipalID.ToString()); command.ExecuteNonQuery(); }
            snapshot = Read();
            using (var command = Connection(store).CreateCommand())
            { command.CommandText = "UPDATE estate_settings SET DenyAnonymous=0 WHERE EstateID=101"; command.ExecuteNonQuery(); }
            Check(service.TryUpdateEstateSettings(snapshot, Updated(snapshot, "Unrelated policy preserved")) && !Read().DenyAnonymous,
                "Conditional patch lost an unrelated concurrent policy change");
            snapshot = Read();
            using (var command = Connection(store).CreateCommand())
            { command.CommandText = "UPDATE estate_settings SET EstateName=lower(EstateName) WHERE EstateID=101"; command.ExecuteNonQuery(); }
            Check(!service.TryUpdateEstateSettings(snapshot, Updated(snapshot, "Case-race overwrite")), "Case-only estate name race bypassed conditional update");
            snapshot = Read(); var transfer = Updated(snapshot, "Invalid transfer"); transfer.EstateOwner = stranger.PrincipalID;
            bool rejected = false; try { service.TryUpdateEstateSettings(snapshot, transfer); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Conditional editing allowed an ownership transfer");
            var adapter = typeof(WebUIContentStore).Assembly.GetType("OpenSim.Continuum.WebUI.WebUIServiceIntegration", true);
            var integration = RuntimeHelpers.GetUninitializedObject(adapter);
            adapter.GetField("_hopBase", Private).SetValue(integration, "hop://grid.example");
            adapter.GetField("_estates", Private).SetValue(integration, service); adapter.GetField("_adminLevel", Private).SetValue(integration, 200);
            var accounts = DispatchProxy.Create<IUserAccountService, RecoveryProxy>();
            UserAccount active = owner;
            ((RecoveryProxy)(object)accounts).Handler = (method, args) => method.Name == "GetUserAccount" ? active : null;
            adapter.GetField("_accounts", Private).SetValue(integration, accounts);
            var post = adapter.GetMethod("HandlePost", Private);
            Dictionary<string, string> Form() => new() { ["estateid"] = "101", ["editToken"] = EstateSettingsEdit.Token(Read()), ["action"] = "save", ["name"] = "<script>Estate</script>", ["price"] = "25", ["voice"] = "true", ["EstateOwner"] = stranger.PrincipalID.ToString(), ["TaxFree"] = "false" };
            var form = Form();
            foreach (var actor in new UserAccount[] { null, stranger })
            {
                object[] args = { "user/estate_edit.html", form, actor, null }; post.Invoke(integration, args);
                Check(!((string)args[3]).StartsWith("Estate settings saved") && Read().PricePerMeter == 20, "Anonymous/foreign estate mutation accepted");
            }
            object[] save = { "user/estate_edit.html", form, owner, null }; post.Invoke(integration, save);
            Check(((string)save[3]).StartsWith("Estate settings saved") && Read().PricePerMeter == 25 && Read().TaxFree && Read().EstateOwner == owner.PrincipalID, "Owner update failed or trusted forged immutable fields");
            post.Invoke(integration, save); Check(((string)save[3]).Contains("Reload before saving"), "Stale estate form accepted");
            var invalid = Form(); invalid["price"] = "-1";
            save = new object[] { "user/estate_edit.html", invalid, owner, null }; post.Invoke(integration, save);
            Check(Read().PricePerMeter == 25 && !((string)save[3]).StartsWith("Estate settings saved"), "Negative estate reference price accepted");
            invalid = Form(); invalid["action"] = "delete";
            save = new object[] { "user/estate_edit.html", invalid, owner, null }; post.Invoke(integration, save);
            Check((string)save[3] == "Unknown estate operation" && Read().EstateID == 101, "Estate delete accepted through editor");
            var siteType = typeof(WebUIContentStore).Assembly.GetType("OpenSim.Continuum.WebUI.WhiteCoreSite", true);
            var site = RuntimeHelpers.GetUninitializedObject(siteType);
            siteType.GetField("_mountPath", Private).SetValue(site, "/portal"); siteType.GetField("_systemName", Private).SetValue(site, "Fixture grid");
            siteType.GetField("_integration", Private).SetValue(site, integration); siteType.GetField("_accounts", Private).SetValue(site, accounts);
            var auth = DispatchProxy.Create<IAuthenticationService, RecoveryProxy>();
            ((RecoveryProxy)(object)auth).Handler = (method, args) => method.Name == "Verify" ? true : null;
            siteType.GetField("_authentication", Private).SetValue(site, auth);
            var resource = siteType.GetMethod("ReadResource", BindingFlags.Static | BindingFlags.NonPublic);
            var render = siteType.GetMethod("Render", Private); var defaults = siteType.GetMethod("DefaultVariables", Private);
            string Render(string page, UserAccount user)
            {
                var vars = (Dictionary<string, object>)defaults.Invoke(site, new object[] { user });
                adapter.GetMethod("Populate", Private).Invoke(integration, new object[] { page, new Dictionary<string, string> { ["estateid"] = "101" }, user, vars });
                return (string)render.Invoke(site, new object[] { page, Encoding.UTF8.GetString((byte[])resource.Invoke(null, new object[] { page })), vars, user != null, user?.UserLevel >= 200, 0 });
            }
            foreach (string page in new[] { "user/estate_edit.html", "admin/estate_edit.html" })
            {
                string html = Render(page, page.StartsWith("admin/") ? admin : owner);
                Check(html.Contains("id=\"estate-form\"") && html.Contains("name=\"editToken\"") && html.Contains("&lt;script&gt;Estate") && !html.Contains("<script>Estate"), "Embedded estate form missing or unsafe");
                Check(html.Contains("must reload estate data") && !html.Contains("name=\"EstateOwner\""), "Estate editor misrepresented runtime state or ownership controls");
            }
            Check(Render("user/estate_manager.html", owner).Contains("user-estate-edit") && Render("admin/estate_manager.html", admin).Contains("admin-estate-edit"), "Estate list lacks edit navigation");
            var req = DispatchProxy.Create<IOSHttpRequest, HttpProxy>(); var fields = ((HttpProxy)(object)req).Values;
            var headers = new NameValueCollection { ["Cookie"] = "ContinuumWebUI=" + owner.PrincipalID + ".fixture", ["Origin"] = "https://grid.example" };
            fields["UriPath"] = "/portal/user/estate_edit.html"; fields["HttpMethod"] = "POST";
            fields["Url"] = new Uri("https://grid.example/portal/user/estate_edit.html"); fields["IsSecured"] = true;
            fields["Headers"] = headers; fields["QueryAsDictionary"] = new Dictionary<string, string>();
            byte[] body = Array.Empty<byte>();
            void Body(Dictionary<string, string> data) { body = Encoding.UTF8.GetBytes(string.Join("&", data.Select(x => System.Net.WebUtility.UrlEncode(x.Key) + "=" + System.Net.WebUtility.UrlEncode(x.Value)))); }
            Dictionary<string, object> Request()
            {
                fields["InputStream"] = new MemoryStream(body); fields["ContentLength64"] = (long)body.Length; fields["ContentEncoding"] = Encoding.UTF8;
                var response = DispatchProxy.Create<IOSHttpResponse, HttpProxy>(); siteType.GetMethod("Handle", Private).Invoke(site, new object[] { req, response });
                return ((HttpProxy)(object)response).Values;
            }
            Body(Form()); headers.Remove("Cookie"); Check((int)Request()["StatusCode"] == 401, "Anonymous HTTP estate editor accepted");
            headers["Cookie"] = "ContinuumWebUI=" + owner.PrincipalID + ".fixture"; headers["Origin"] = "https://attacker.example";
            Check((int)Request()["StatusCode"] == 403, "Cross-origin estate mutation accepted"); headers["Origin"] = "https://grid.example";
            fields["UriPath"] = "/portal/admin/estate_edit.html";
            Check((int)Request()["StatusCode"] == 403, "Resident reached admin estate route");
            active = admin; headers["Cookie"] = "ContinuumWebUI=" + admin.PrincipalID + ".fixture";
            var change = Form(); change["name"] = "Admin edit"; Body(change);
            var responseValues = Request();
            Check((int)responseValues["StatusCode"] == 200 && Encoding.UTF8.GetString((byte[])responseValues["RawBuffer"]).StartsWith("Estate settings saved;")
                && Read().EstateName == "Admin edit", "Administrator save failed or success response was marked as an error");
            fields["HttpMethod"] = "GET"; fields["QueryAsDictionary"] = new Dictionary<string, string> { ["estateid"] = "101", ["name"] = "GET overwrite", ["action"] = "save" };
            Check((int)Request()["StatusCode"] == 200 && Read().EstateName == "Admin edit", "GET query altered estate settings");
            var controlConfig = new Nini.Config.IniConfigSource();
            var controlSection = controlConfig.AddConfig("ContinuumWebUIRegionControlRooms");
            controlSection.Set(region.ToString(), "https://sim.example/regionweb/admin");
            var configure = adapter.GetMethod("ConfigureRegionControlRooms", BindingFlags.Static | BindingFlags.NonPublic);
            var rooms = (Dictionary<UUID, string>)configure.Invoke(null, new object[] { controlConfig });
            adapter.GetField("_regionControlRooms", Private).SetValue(integration, rooms);
            var grid = DispatchProxy.Create<IGridService, ClassifiedGrid>();
            ((ClassifiedGrid)(object)grid).Region = new GridRegion { RegionID = region, EstateOwner = owner.PrincipalID, RegionName = "Estate region", ExternalHostName = "untrusted-directory.example", HttpPort = 9000 };
            adapter.GetField("_grid", Private).SetValue(integration, grid);
            Check(Render("user/region_manager.html", owner).Contains("https://sim.example/regionweb/admin")
                && Render("admin/region_manager.html", admin).Contains("Simulator control room")
                && !Render("user/region_manager.html", stranger).Contains("https://sim.example/regionweb/admin"), "Configured simulator control room missing or leaked to non-owner");
            controlSection.Set(region.ToString(), "http://sim.example/regionweb/admin");
            bool invalidControl = false; try { configure.Invoke(null, new object[] { controlConfig }); }
            catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { invalidControl = true; }
            Check(invalidControl, "Non-HTTPS simulator control-room link accepted");
            rooms.Clear();
            Check(!Render("user/region_manager.html", owner).Contains("Simulator control room"), "Unconfigured control-room link displayed");
            var unsupported = DispatchProxy.Create<IEstateDataService, RecoveryProxy>();
            ((RecoveryProxy)(object)unsupported).Handler = (method, args) => method.Name == "LoadEstateSettings" ? Read() : null;
            adapter.GetField("_estates", Private).SetValue(integration, unsupported);
            Check(!Render("user/estate_edit.html", owner).Contains("id=\"estate-form\""), "Unsupported estate service offered writes");
            Console.WriteLine("PASS: native SQLite estate conditional updates, ownership races, concurrent writers, preserved policy/lists, region lookup, owner/admin HTTP authority and embedded forms.");
        }
        finally
        {
            Connection(reopened ?? store).Dispose();
            if (reopened != null) Connection(store).Dispose();
        }
    }
}
