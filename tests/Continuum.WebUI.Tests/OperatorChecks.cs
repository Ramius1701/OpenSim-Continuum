using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Continuum.WebUI;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

static class OperatorChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run(string root)
    {
        var assembly = typeof(WebUIContentStore).Assembly; var adapter = assembly.GetType("OpenSim.Continuum.WebUI.WebUIServiceIntegration", true);
        var integration = RuntimeHelpers.GetUninitializedObject(adapter);
        void Set(string field, object value) => adapter.GetField(field, Private).SetValue(integration, value);
        Set("_adminLevel", 200);
        var owner = new UserAccount { PrincipalID = UUID.Random() }; var admin = new UserAccount { PrincipalID = UUID.Random(), UserLevel = 200 };
        var stranger = new UserAccount { PrincipalID = UUID.Random() };
        var region = new GridRegion { RegionID = UUID.Random(), RegionName = "<Region>", EstateOwner = stranger.PrincipalID, ServerURI = "https://sim.example/" };
        var estate = new EstateSettings { EstateID = 101, EstateOwner = owner.PrincipalID };
        var grid = DispatchProxy.Create<IGridService, RecoveryProxy>();
        ((RecoveryProxy)(object)grid).Handler = (method, args) => method.Name == "GetRegionByUUID" && (UUID)args[1] == region.RegionID ? region : null;
        var estates = DispatchProxy.Create<IEstateDataService, RecoveryProxy>();
        ((RecoveryProxy)(object)estates).Handler = (method, args) => method.Name == "LoadEstateSettings" ? estate : null;
        Set("_grid", grid); Set("_estates", estates);
        Set("_regionControlRooms", new Dictionary<UUID, string> { [region.RegionID] = "https://sim.example/regionweb/admin" });
        var config = new IniConfigSource(); var consoles = config.AddConfig("ContinuumWebUIConsoles"); consoles.Set("Simulator <West>", "https://console.example/native/");
        var configure = adapter.GetMethod("ConfigureConsoles", BindingFlags.Static | BindingFlags.NonPublic);
        var endpoints = (Dictionary<string, string>)configure.Invoke(null, new object[] { config }); Set("_consoleEndpoints", endpoints);
        foreach (string invalid in new[] { "http://remote.example/", "https://user:pass@host.example/", "https://host.example/?secret=bad", "https://host.example/#bad" })
        {
            consoles.Set("Simulator <West>", invalid); bool rejected = false;
            try { configure.Invoke(null, new object[] { config }); } catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { rejected = true; }
            Check(rejected, "Unsafe native console URL accepted");
        }
        Check(((string)adapter.GetProperty("ContentSecurityPolicy", Private).GetValue(integration)).Contains("connect-src 'self' https://console.example"), "Configured console origin missing from CSP");
        var addRegion = adapter.GetMethod("AddRegionManagement", Private);
        var parameters = new Dictionary<string, string> { ["regionid"] = region.RegionID.ToString(), ["url"] = "https://attacker.example/" };
        var values = new Dictionary<string, object>(); addRegion.Invoke(integration, new object[] { "user/region_edit.html", parameters, owner, values });
        Check((bool)values["RegionEditorControlRoom"] && (string)values["RegionEditorControlRoomURL"] == "https://sim.example/regionweb/admin"
            && !(bool)values["RegionEditorConsole"] && (string)values["RegionEditorName"] == "&lt;Region&gt;", "Native region delegation ignored ownership, escaped output or configured URL authority");
        foreach (var request in new[] { ("user/region_edit.html", stranger), ("admin/region_edit.html", owner) })
        {
            bool rejected = false; try { addRegion.Invoke(integration, new object[] { request.Item1, parameters, request.Item2, new Dictionary<string, object>() }); }
            catch (TargetInvocationException e) when (e.InnerException is ArgumentException) { rejected = true; }
            Check(rejected, "Region management trusted stale grid ownership or resident administrator route");
        }
        values.Clear(); addRegion.Invoke(integration, new object[] { "admin/region_edit.html", parameters, admin, values });
        Check((bool)values["RegionEditorConsole"], "Administrator native console link missing");
        var siteType = assembly.GetType("OpenSim.Continuum.WebUI.WhiteCoreSite", true); var site = RuntimeHelpers.GetUninitializedObject(siteType);
        siteType.GetField("_mountPath", Private).SetValue(site, "/portal"); siteType.GetField("_systemName", Private).SetValue(site, "Fixture"); siteType.GetField("_integration", Private).SetValue(site, integration);
        string Render(string page, UserAccount actor)
        {
            var vars = (Dictionary<string, object>)siteType.GetMethod("DefaultVariables", Private).Invoke(site, new object[] { actor });
            if (page == "admin/sim_console.html") adapter.GetMethod("AddConsole", Private).Invoke(integration, new object[] { actor, vars });
            else addRegion.Invoke(integration, new object[] { page, parameters, actor, vars });
            var bytes = (byte[])siteType.GetMethod("ReadResource", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { page });
            return (string)siteType.GetMethod("Render", Private).Invoke(site, new object[] { page, Encoding.UTF8.GetString(bytes), vars, true, actor.UserLevel == 200, 0 });
        }
        Check(Render("user/region_edit.html", owner).Contains("Open configured simulator control room") && Render("admin/region_edit.html", admin).Contains("admin-console"), "Embedded native region workflow missing");
        string html = Render("admin/sim_console.html", admin);
        Check(html.Contains("Simulator &lt;West&gt;") && html.Contains("https://console.example/native/") && html.Contains("continuum-console.js"), "Native console selection was not rendered safely");
        NativeConsole();
        Console.WriteLine("PASS: fresh estate ownership, fixed native control-room delegation, administrator console routing, URL/CSP bounds and actual RemoteConsole authentication/command/output/cleanup protocol.");
    }
    static void NativeConsole()
    {
        // Production server startup registers quit before the remote console publishes its help tree.
        MainConsole.Instance.Commands.AddCommand("Fixture", false, "quit", "quit", "Inert fixture command", (_, _) => { });
        var console = new RemoteConsole("Fixture"); var server = DispatchProxy.Create<IHttpServer, RecoveryProxy>();
        var handlers = new Dictionary<string, GenericHTTPMethod>(); var polls = new HashSet<string>();
        ((RecoveryProxy)(object)server).Handler = (method, args) =>
        {
            if (method.Name == "AddHTTPHandler") { handlers[(string)args[0]] = (GenericHTTPMethod)args[1]; return true; }
            if (method.Name == "AddPollServiceHTTPHandler") { polls.Add(((PollServiceEventArgs)args.Last()).Url); return true; }
            if (method.Name == "RemovePollServiceHTTPHandler") { polls.Remove((string)args.Last()); return null; }
            return null;
        };
        try
        {
            var config = new IniConfigSource(); var network = config.AddConfig("Network"); network.Set("ConsoleUser", "operator"); network.Set("ConsolePass", "fixture-password"); network.Set("ConsoleAllowedOrigin", "https://grid.example");
            console.ReadConfig(config); console.SetServer(server);
            Hashtable Call(string path, string body) => handlers[path](new Hashtable { ["body"] = body });
            Check((int)Call("/StartSession", "USER=operator&PASS=bad")["int_response_code"] == 401, "Native console accepted wrong credentials");
            // Concurrent native help publication must retain the live help/quit tree.
            System.Threading.Tasks.Parallel.For(0, 20, _ => MainConsole.Instance.Commands.GetXml(new XmlDocument()));
            var started = Call("/StartSession", "USER=operator&PASS=fixture-password"); var xml = new XmlDocument(); xml.LoadXml((string)started["str_response_string"]);
            string id = xml.SelectSingleNode("/ConsoleSession/SessionID").InnerText;
            Check((int)started["int_response_code"] == 200 && polls.Contains("/ReadResponses/" + id) && (string)started["access_control_allow_origin"] == "https://grid.example", "Native console handshake/CORS contract mismatch");
            Check((int)Call("/SessionCommand", "ID=" + UUID.Random() + "&COMMAND=show+info")["int_response_code"] == 404, "Unknown native console session accepted");
            Check((int)Call("/SessionCommand", "ID=" + id + "&COMMAND=show+info")["int_response_code"] == 200, "Native console command was not queued");
            var queued = (List<string>)typeof(RemoteConsole).GetField("m_InputData", Private).GetValue(console);
            Check(queued.SequenceEqual(new[] { "show info" }), "Native command body contract mismatch"); // No command is executed by this fixture.
            console.Output("<script>fixture</script>");
            var output = (Hashtable)typeof(RemoteConsole).GetMethod("GetEvents", Private).Invoke(console, new object[] { UUID.Random(), new UUID(id) });
            Check(((string)output["str_response_string"]).Contains("&lt;script&gt;"), "Native output XML did not escape console text");
            Call("/CloseSession", "ID=" + id); Check(polls.Count == 0, "Closed native console session leaked its registered poll capability");
            Check((int)Call("/SessionCommand", "ID=" + id + "&COMMAND=show+info")["int_response_code"] == 404, "Closed native console session retained command authority");
        }
        finally
        {
            ((System.Timers.Timer)typeof(RemoteConsole).GetField("m_expireTimer", Private).GetValue(console)).Dispose();
            ((ManualResetEvent)typeof(RemoteConsole).GetField("m_DataEvent", Private).GetValue(console)).Dispose();
        }
    }
}
