using System.Reflection;
using System.Runtime.CompilerServices;
using Nini.Config;
using Nwc.XmlRpc;
using OpenMetaverse;
using OpenSim.Continuum.WebUI;
using OpenSim.Services.Interfaces;
using OpenSim.Server.Handlers.Grid;

static class GridConfigurationChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run(string root)
    {
        string path = Path.Combine(root, "native-grid.ini");
        string original = "; Retain this comment\n[GridInfoService]\nDisableStatsEndpoint=true\ngridname=Original\ngridnick=original\nlogin=https://grid.example/\n[LoginService]\nWelcomeMessage=Welcome\nMessageUrl=https://messages.example/welcome\n[ContinuumWebUI]\nSystemName=Original\n[PrivateService]\nSecret=private-test-secret\n";
        File.WriteAllText(path, original);
        var store = new WebUIGridConfigurationStore(path); var before = store.Read();
        store.Save(before.Revision, "Our ; grid", "our-grid", "Welcome <USERNAME>! \\nEnjoy your stay.");
        var saved = store.Read();
        Check(saved.GridName == "Our ; grid" && saved.GridNick == "our-grid" && saved.RemoteWelcome
            && saved.WelcomeMessage.Contains("<USERNAME>"), "Native grid configuration did not round-trip");
        Check(File.ReadAllText(path).Contains("; Retain this comment") && File.ReadAllText(path).Contains("Secret=private-test-secret")
            && Directory.GetFiles(root, "native-grid.ini.continuum-*.bak").Select(File.ReadAllText).Single() == original, "Grid edit changed unrelated settings or lost exact backup");
        var source = new IniConfigSource(path); var native = new GridInfoHandlers(source);
        var info = (System.Collections.Hashtable)native.XmlRpcGridInfoMethod(new XmlRpcRequest(), null).Value;
        Check((string)info["gridname"] == saved.GridName && (string)info["gridnick"] == saved.GridNick
            && source.Configs["ContinuumWebUI"].GetString("SystemName") == saved.GridName, "Saved identity was not consumed by native grid-info or portal configuration");
        bool stale = false; try { store.Save(before.Revision, "Stale", "stale", "Stale"); } catch (InvalidOperationException) { stale = true; }
        Check(stale && store.Read().GridName == saved.GridName, "Stale grid editor overwrote configuration");
        foreach (string name in new[] { "Bad\n[PrivateService]", "${PrivateService|Secret}", "Bad\"name" })
        {
            bool rejected = false; try { store.Save(saved.Revision, name, "valid", "Welcome"); } catch (ArgumentException) { rejected = true; }
            Check(rejected && store.Read().Revision == saved.Revision, "Grid editor accepted INI injection");
        }
        using (var lease = new FileStream(path + ".continuum.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            bool busy = false; try { store.Save(saved.Revision, "Busy", "busy", "Welcome"); } catch (IOException) { busy = true; }
            Check(busy, "Grid editor ignored writer lease");
        }
        var adapter = typeof(WebUIContentStore).Assembly.GetType("OpenSim.Continuum.WebUI.WebUIServiceIntegration", true);
        var integration = RuntimeHelpers.GetUninitializedObject(adapter); adapter.GetField("_gridConfiguration", Private).SetValue(integration, store);
        adapter.GetField("_adminLevel", Private).SetValue(integration, 200);
        var admin = new UserAccount { PrincipalID = UUID.Random(), UserLevel = 200 }; var resident = new UserAccount { PrincipalID = UUID.Random() };
        var form = new Dictionary<string, string> { ["action"] = "save", ["revision"] = saved.Revision, ["gridName"] = "Final grid", ["gridNick"] = "final-grid", ["welcome"] = "Welcome again" };
        var post = adapter.GetMethod("HandlePost", Private);
        string Change(UserAccount actor)
        { object[] args = { "admin/gridsettings_manager.html", form, actor, null }; post.Invoke(integration, args); return (string)args[3]; }
        Check(Change(resident) == "Administrator access required" && Change(null) == "Administrator access required", "Unauthorized native grid edit accepted");
        Check(Change(admin).Contains("Confirm") && store.Read().Revision == saved.Revision, "Unconfirmed grid edit accepted");
        form["confirm"] = "true"; Check(Change(admin) == "Grid configuration saved; ROBUST restart required" && store.Read().GridName == "Final grid", "Grid editor failed or claimed running services were updated");
        Console.WriteLine("PASS: native grid INI round-trip and grid-info consumption, exact backups, unrelated settings, stale edits, injection/lease bounds and administrator confirmation.");
    }
}
