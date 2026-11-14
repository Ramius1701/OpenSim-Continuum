using System;
using System.Collections;
using System.Xml;

namespace ContinuumSearch.Service
{
    internal static class SearchAcceptanceSuite
    {
        internal static int Run(string provider, string connectionString)
        {
            if (String.IsNullOrWhiteSpace(provider) || String.IsNullOrWhiteSpace(connectionString) ||
                connectionString.IndexOf("test", StringComparison.OrdinalIgnoreCase) < 0)
            {
                Console.Error.WriteLine("Search self-test requires provider/connection environment variables naming a dedicated test database.");
                return 2;
            }

            using SearchStore store = SearchStore.Open(provider, connectionString);
            store.EnsureSchema();
            XmlDocument document = new() { XmlResolver = null };
            document.LoadXml("<region category='PG'><info>" +
                "<uuid>11111111-1111-1111-1111-111111111111</uuid><name>Welcome Test</name>" +
                "<handle>1099511628032000</handle><url>http://127.0.0.1:18011/</url></info><data>" +
                "<estate><name>Grid Owner</name><uuid>22222222-2222-2222-2222-222222222222</uuid><id>2</id></estate>" +
                "<parcel category='1' salesprice='250' forsale='true' showinsearch='true'>" +
                "<name>Continuum Plaza</name><uuid>33333333-3333-3333-3333-333333333333</uuid>" +
                "<infouuid>44444444-4444-4444-4444-444444444444</infouuid><location>128,128,25</location>" +
                "<description>Welcome shopping and events</description><area>4096</area><dwell>42.5</dwell>" +
                "<image>55555555-5555-5555-5555-555555555555</image></parcel>" +
                "<object><uuid>66666666-6666-6666-6666-666666666666</uuid>" +
                "<parceluuid>33333333-3333-3333-3333-333333333333</parceluuid><location>128,128,25</location>" +
                "<title>Searchable Vendor</title><description>Test object</description></object></data></region>");

            SearchRegion region = SnapshotCrawler.ParseRegion(document.DocumentElement);
            store.ReplaceRegion(region);
            store.ReplaceRegion(region); // repeat collection must be idempotent
            XmlDocument hostileDocument = (XmlDocument)document.Clone();
            hostileDocument.SelectSingleNode("/region/info/name").InnerText = new String('R', 300);
            hostileDocument.SelectSingleNode("/region/data/parcel/infouuid").InnerText = "not-a-uuid";
            hostileDocument.SelectSingleNode("/region/data/parcel/description").InnerText = new String('D', 20000);
            hostileDocument.SelectSingleNode("/region/data/parcel/area").InnerText = "-1";
            hostileDocument.SelectSingleNode("/region/data/parcel/dwell").InnerText = "NaN";
            SearchRegion normalized = SnapshotCrawler.ParseRegion(hostileDocument.DocumentElement);
            Require(normalized.Name.Length == 255 && normalized.Parcels[0].InfoID == normalized.Parcels[0].ID
                && normalized.Parcels[0].Description.Length == 16000 && normalized.Parcels[0].Area == 0
                && normalized.Parcels[0].Dwell == 0, "snapshot field normalization");
            Require(!SnapshotCrawler.IsPublic(System.Net.IPAddress.Parse("127.0.0.1"))
                && !SnapshotCrawler.IsPublic(System.Net.IPAddress.Parse("10.0.0.1"))
                && !SnapshotCrawler.IsPublic(System.Net.IPAddress.Parse("fc00::1"))
                && !SnapshotCrawler.IsPublic(System.Net.IPAddress.Parse("::ffff:192.168.1.10"))
                && SnapshotCrawler.IsPublic(System.Net.IPAddress.Parse("2001:4860:4860::8888")),
                "snapshot public-address policy");

            ArrayList places = store.FindPlaces(Request("Welcome", 0x07000000));
            Require(places.Count == 1 && ((Hashtable)places[0])["name"].ToString() == "Continuum Plaza" &&
                ((Hashtable)places[0])["region_name"].ToString() == "Welcome Test" &&
                ((Hashtable)places[0])["description"].ToString() == "Welcome shopping and events",
                "enriched places query");
            ArrayList popular = store.FindPopular(Request(String.Empty, 0x07000000));
            Require(popular.Count == 1 && ((Hashtable)popular[0])["region_name"].ToString() == "Welcome Test",
                "enriched popular query");
            ArrayList parcel = store.GetParcel(new Hashtable { ["parcel_id"] = "44444444-4444-4444-4444-444444444444" });
            Require(parcel.Count == 1 && ((Hashtable)parcel[0])["region_name"].ToString() == "Welcome Test" &&
                ((Hashtable)parcel[0])["snapshot_id"].ToString() == "55555555-5555-5555-5555-555555555555",
                "public parcel details query");
            Require(store.GetParcel(new Hashtable { ["parcel_id"] = "not-a-uuid" }).Count == 0,
                "invalid parcel details query");
            ArrayList regionParcels = store.GetRegionParcels(new Hashtable
                { ["region_id"] = "11111111-1111-1111-1111-111111111111" });
            Require(regionParcels.Count == 1 && ((Hashtable)regionParcels[0])["name"].ToString() == "Continuum Plaza",
                "region parcels query");
            Hashtable landRequest = Request(String.Empty, 0x07000000);
            landRequest["type"] = UInt32.MaxValue.ToString();
            ArrayList land = store.FindLand(landRequest);
            Require(land.Count == 1 && Convert.ToInt32(((Hashtable)land[0])["sale_price"]) == 250, "land query");
            store.SaveEvent(new SearchEvent
            {
                ID = 900001, OwnerID = "22222222-2222-2222-2222-222222222222",
                CreatorID = "22222222-2222-2222-2222-222222222222", Name = "Continuum Test Event",
                Category = 1, Description = "Search acceptance event", DateUtc = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
                Duration = 60, SimName = "Welcome Test", ParcelID = "33333333-3333-3333-3333-333333333333",
                GlobalPosition = "128,128,25", Flags = 0
            });
            Hashtable eventRequest = Request("u|0|Continuum", 0x07000020);
            ArrayList events = store.FindEvents(eventRequest);
            Require(events.Count == 1 && Convert.ToInt32(((Hashtable)events[0])["event_id"]) == 900001 &&
                ((Hashtable)events[0])["description"].ToString() == "Search acceptance event" &&
                ((Hashtable)events[0])["simname"].ToString() == "Welcome Test", "enriched events query");
            ArrayList ownerEvents = store.FindOwnerEvents(new Hashtable
                { ["creatoruuid"] = "22222222-2222-2222-2222-222222222222", ["query_start"] = "0" });
            Require(ownerEvents.Count == 1 && Convert.ToInt32(((Hashtable)ownerEvents[0])["event_id"]) == 900001,
                "owner events query");
            Require(store.FindOwnerEvents(new Hashtable { ["creatoruuid"] = "not-a-uuid" }).Count == 0,
                "invalid owner events query");
            Require(store.GetEvent(new Hashtable { ["eventID"] = "900001" }).Count == 1, "event details query");
            CheckEventManagement(store);
            store.CheckClassifiedManagement();
            CheckEventNotifications(store);
            Console.WriteLine("PASS: DataSnapshot parse and idempotent region replacement");
            Console.WriteLine("PASS: places, popular, parcel details, region parcels, land, events, owner events and event-details query contracts");
            Console.WriteLine("ContinuumSearch {0} acceptance self-test passed.", store.Provider);
            return 0;
        }

        private static Hashtable Request(string text, int flags) => new()
        {
            ["text"] = text, ["flags"] = flags.ToString(), ["category"] = "0", ["query_start"] = "0"
        };
        private static void CheckEventNotifications(SearchStore store)
        {
            Guid actor = Guid.NewGuid(), other = Guid.NewGuid();
            long future = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds();
            store.SaveEvent(new SearchEvent { ID = 900101, OwnerID = actor.ToString(), CreatorID = actor.ToString(), Name = "Reminder event",
                Description = "Persistent reminder", DateUtc = future, Duration = 60, Category = 18, SimName = "Welcome Test", ParcelID = Guid.Empty.ToString(), GlobalPosition = "256100,256120,30" });
            var request = new Hashtable { ["actor"] = actor.ToString(), ["action"] = "add", ["event"] = "900101" };
            Require(store.EventNotifications(request).Count == 1 && store.EventNotifications(request).Count == 1, "idempotent notification subscription");
            request["actor"] = other.ToString(); request["action"] = "list";
            Require(store.EventNotifications(request).Count == 0, "notification subscriber isolation");
            string secret = new('n', 48);
            var rpc = new SearchRpc(store, notificationSecret: secret);
            var wire = new Nwc.XmlRpc.XmlRpcRequest("continuum_event_notifications", new ArrayList { request });
            Require(!Convert.ToBoolean(((Hashtable)rpc.Notifications(wire, null).Value)["success"]), "notification private authentication");
            request["notificationSecret"] = secret; request["actor"] = actor.ToString();
            var reply = (Hashtable)rpc.Notifications(wire, null).Value;
            Require(Convert.ToBoolean(reply["success"]) && ((ArrayList)reply["data"]).Count == 1, "authenticated notification read");
            var row = (Hashtable)((ArrayList)reply["data"])[0];
            Require(row["event_date_ut"].ToString() == future.ToString() && row["event_name"].ToString() == "Reminder event", "viewer reminder fields");
            request["action"] = "remove";
            Require(store.EventNotifications(request).Count == 0 && store.EventNotifications(request).Count == 0, "idempotent notification removal");
            request["action"] = "add"; request["event"] = "2147483647";
            bool rejected = false; try { store.EventNotifications(request); } catch (ArgumentException) { rejected = true; }
            Require(rejected, "missing event notification rejected");
            var capped = Guid.NewGuid();
            request = new Hashtable { ["actor"] = capped.ToString(), ["action"] = "add" };
            for (int i = 0; i <= 100; i++)
            {
                store.SaveEvent(new SearchEvent { ID = 900200 + i, OwnerID = actor.ToString(), CreatorID = actor.ToString(), Name = "Limit event " + i,
                    Description = "Notification limit fixture", DateUtc = future, Duration = 60, Category = 18, SimName = "Welcome Test", ParcelID = Guid.Empty.ToString(), GlobalPosition = "256100,256120,30" });
                request["event"] = (900200 + i).ToString();
                if (i < 100) store.EventNotifications(request);
            }
            bool full = false; try { store.EventNotifications(request); } catch (ArgumentException) { full = true; }
            request["event"] = "900200";
            Require(full && store.EventNotifications(request).Count == 100, "notification cap and duplicate-at-cap behavior");
            Console.WriteLine("PASS: durable event reminder subscriptions, idempotence, subscriber isolation, authenticated service and viewer timestamp fields");
        }

        private static void CheckEventManagement(SearchStore store)
        {
            string owner = "22222222-2222-2222-2222-222222222222";
            Hashtable form = new()
            {
                ["actor"] = owner, ["action"] = "create", ["requestID"] = Guid.NewGuid().ToString(),
                ["regionID"] = "11111111-1111-1111-1111-111111111111", ["name"] = "Resident event",
                ["description"] = "<script>plain text</script>", ["category"] = "18", ["duration"] = "60",
                ["dateUTC"] = DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeSeconds().ToString(),
                ["eventflags"] = "0", ["x"] = "100", ["y"] = "120", ["z"] = "30"
            };
            int id = store.ChangeEvent(form);
            Require(id > 0 && store.ChangeEvent(form) == id, "event duplicate submission idempotency");
            Hashtable detail = (Hashtable)store.GetEvent(new Hashtable { ["eventID"] = id.ToString() })[0];
            Require(detail["creator"].ToString() == owner && Convert.ToInt32(detail["revision"]) == 1
                && detail["description"].ToString() == "<script>plain text</script>"
                && detail["globalposition"].ToString() == "256100,256120,30", "event persistence and global coordinates");
            void Reject(string field, object value, string test)
            {
                object previous = form[field]; form[field] = value;
                bool rejected = false;
                try { store.ChangeEvent(form); } catch (ArgumentException) { rejected = true; }
                finally { form[field] = previous; }
                Require(rejected, test);
            }
            Reject("name", "Different payload", "event reused request ID rejects changed payload");
            Reject("actor", Guid.NewGuid().ToString(), "event creation region ownership");
            Reject("x", "NaN", "event invalid coordinates");
            Reject("eventflags", "2", "event maturity cannot exceed region");
            form["action"] = "update"; form["eventID"] = id.ToString(); form["revision"] = "1";
            Reject("actor", Guid.NewGuid().ToString(), "event update ownership");
            form["name"] = "Updated event";
            Require(store.ChangeEvent(form) == id, "event update");
            Reject("revision", "1", "event stale update");
            form["action"] = "delete";
            Reject("revision", "1", "event stale delete");
            form["revision"] = "2";
            Reject("actor", Guid.NewGuid().ToString(), "event delete ownership");
            Require(store.ChangeEvent(form) == id
                && store.GetEvent(new Hashtable { ["eventID"] = id.ToString() }).Count == 0, "event deletion");
            string secret = new('k', 48);
            SearchRpc rpc = new(store, secret);
            Nwc.XmlRpc.XmlRpcRequest request = new("continuum_event_write", new ArrayList { form });
            Require(!Convert.ToBoolean(((Hashtable)rpc.EventWrite(request, null).Value)["success"]), "event private API requires secret");
            form["writeSecret"] = secret; form["action"] = "create"; form["requestID"] = Guid.NewGuid().ToString();
            Require(Convert.ToBoolean(((Hashtable)rpc.EventWrite(request, null).Value)["success"]), "event authenticated API");
            Require(!Convert.ToBoolean(((Hashtable)new SearchRpc(store).EventWrite(request, null).Value)["success"]), "event writes disabled by default");
            Console.WriteLine("PASS: event write authentication, ownership, persistence, global coordinates, duplicate submissions, revisions and deletion");
        }
        private static void Require(bool value, string operation)
        { if (!value) throw new InvalidOperationException("Search acceptance failed: " + operation); }
    }
}
