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
            Console.WriteLine("PASS: DataSnapshot parse and idempotent region replacement");
            Console.WriteLine("PASS: places, popular, parcel details, region parcels, land, events, owner events and event-details query contracts");
            Console.WriteLine("ContinuumSearch {0} acceptance self-test passed.", store.Provider);
            return 0;
        }

        private static Hashtable Request(string text, int flags) => new()
        {
            ["text"] = text, ["flags"] = flags.ToString(), ["category"] = "0", ["query_start"] = "0"
        };
        private static void Require(bool value, string operation)
        { if (!value) throw new InvalidOperationException("Search acceptance failed: " + operation); }
    }
}
