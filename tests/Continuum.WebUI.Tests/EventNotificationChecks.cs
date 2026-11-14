using System.Collections;
using System.Data.Common;
using System.Data.SQLite;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Services.LLLoginService;
using OpenSim.Region.ClientStack.Linden;
using Nwc.XmlRpc;

static class EventNotificationChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run(string root)
    {
        OpenSim.Framework.MainConsole.Instance ??= new OpenSim.Framework.Console.LocalConsole("Reminder tests");
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "ContinuumSearch.Service.dll"));
        var storeType = assembly.GetType("ContinuumSearch.Service.SearchStore", true);
        try { storeType.GetMethod("ConfigureSQLiteNativeLibrary", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { /* an earlier native provider fixture registered the resolver */ }
        string connectionString = "Data Source=" + Path.Combine(root, "notifications.sqlite") + ";Version=3;";
        Func<DbConnection> factory = () => new SQLiteConnection(connectionString);
        var store = Activator.CreateInstance(storeType, Private, null, new object[] { "SQLite", connectionString, factory }, null);
        storeType.GetMethod("EnsureSchema", Private).Invoke(store, null);
        long timestamp = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds();
        using (DbConnection connection = factory())
        {
            connection.Open(); using var sql = connection.CreateCommand();
            sql.CommandText = "INSERT INTO search_events(owneruuid,name,eventid,creatoruuid,category,description,dateUTC,duration,covercharge,coveramount,simname,parcelUUID,globalPos,eventflags) VALUES('owner','Reminder',1,'creator',18,'Description',@date,60,0,0,'Region','00000000-0000-0000-0000-000000000000','256100,256120,30',0)";
            var parameter = sql.CreateParameter(); parameter.ParameterName = "@date"; parameter.Value = timestamp; sql.Parameters.Add(parameter); sql.ExecuteNonQuery();
        }
        string secret = new('n', 48); var rpcType = assembly.GetType("ContinuumSearch.Service.SearchRpc", true);
        var rpc = Activator.CreateInstance(rpcType, Private, null, new object[] { store, "", null, 50, secret }, null);
        var method = rpcType.GetMethod("Notifications", Private);
        var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); int port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        var server = new BaseHttpServer((uint)port);
        server.AddXmlRPCHandler("continuum_event_notifications", (request, remote) => (XmlRpcResponse)method.Invoke(rpc, new object[] { request, remote })); server.Start();
        try
        {
            string url = "http://127.0.0.1:" + port + "/";
            var client = new EventNotificationClient(url, secret, 10000); var actor = UUID.Random();
            Check(!new EventNotificationClient(url, new string('x', 48), 10000).Change(actor, 1, true), "Reminder endpoint accepted wrong key");
            Check(client.Change(actor, 1, true) && client.Change(actor, 1, true), "Idempotent viewer subscriptions failed");
            var rows = client.Get(actor);
            Check(rows.Count == 1 && client.Get(UUID.Random()).Count == 0, "Reminder persistence or subscriber isolation failed");
            var row = (Hashtable)rows[0];
            Check((int)row["event_date_ut"] == timestamp && row["event_date"].ToString().EndsWith("Z") && (int)row["event_id"] == 1, "Firestorm reminder date/id contract mismatch");
            var response = new LLLoginResponse { EventNotifications = rows };
            foreach (var field in typeof(LLLoginResponse).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (field.FieldType == typeof(ArrayList) && field.GetValue(response) == null) field.SetValue(response, new ArrayList());
            var xml = response.ToHashtable();
            Check(xml["event_notifications"] is ArrayList subscriptions && subscriptions.Count == 1, "XML-RPC login dropped reminders");
            var llsd = response.ToOSDMap() as OSDMap;
            Check(llsd != null && llsd["event_notifications"] is OSDArray values && values.Count == 1
                && ((OSDMap)values[0])["event_date_ut"].AsInteger() == timestamp, "LLSD login dropped/mistyped reminders");
            var secondStore = Activator.CreateInstance(storeType, Private, null, new object[] { "SQLite", connectionString, factory }, null);
            var persisted = (ArrayList)storeType.GetMethod("EventNotifications", Private).Invoke(secondStore, new object[] { new Hashtable { ["actor"] = actor.ToString(), ["action"] = "list" } });
            Check(persisted.Count == 1, "Reminders lost across service instance restart"); ((IDisposable)secondStore).Dispose();
            Check(client.Change(actor, 1, false) && client.Change(actor, 1, false) && client.Get(actor).Count == 0, "Idempotent reminder removal failed");
            Check(!client.Change(actor, 9999, true), "Missing event reminder accepted");
            Check(client.Change(actor, 1, true), "Cannot re-add reminder");
            using (DbConnection connection = factory())
            { connection.Open(); using var sql = connection.CreateCommand(); sql.CommandText = "DELETE FROM search_events WHERE eventid=1"; sql.ExecuteNonQuery(); }
            Check(client.Get(actor).Count == 0, "Deleted-event reminder leaked into login");
            bool invalid = false; try { new EventNotificationClient("http://public.example/", secret); } catch (ArgumentException) { invalid = true; }
            Check(invalid, "Public plaintext reminder endpoint accepted");
            var featuresType = typeof(OpenSim.Region.ClientStack.Linden.SimulatorFeaturesModule);
            var features = Activator.CreateInstance(featuresType);
            featuresType.GetMethod("AddDefaultFeatures", Private).Invoke(features, null);
            var valuesMap = (OSDMap)featuresType.GetMethod("GetFeatures").Invoke(features, null);
            Check(valuesMap["AvatarHoverHeightEnabled"].AsBoolean(), "Native hover-height support not advertised to viewer: keys=" + string.Join(",", valuesMap.Keys) + " direct=" + ((OSDMap)featuresType.GetField("m_features", Private).GetValue(features))["AvatarHoverHeightEnabled"].AsBoolean());
            var extras = (OSDMap)valuesMap["OpenSimExtras"];
            extras["fixture_mutation"] = true;
            var untouched = (OSDMap)featuresType.GetMethod("GetFeatures").Invoke(features, null);
            Check(untouched["MeshRezEnabled"].AsBoolean() && !((OSDMap)untouched["OpenSimExtras"]).ContainsKey("fixture_mutation"), "Feature snapshot exposed mutable native state");
            Console.WriteLine("PASS: real event subscription HTTP/SQLite, restart/removal/privacy, Firestorm timestamps, XML-RPC/LLSD login restoration and native hover advertisement.");
        }
        finally { server.Stop(); ((IDisposable)store).Dispose(); }
    }
}
