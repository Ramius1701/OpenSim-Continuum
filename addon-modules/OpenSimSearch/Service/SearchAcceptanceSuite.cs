using System;
using System.Collections;
using System.Xml;

namespace ContinuumSearch.Service
{
    internal static class SearchAcceptanceSuite
    {
        internal static int Run(string databasePath)
        {
            if (String.IsNullOrWhiteSpace(databasePath) ||
                databasePath.IndexOf("test", StringComparison.OrdinalIgnoreCase) < 0)
            {
                Console.Error.WriteLine("Search self-test requires a SQLite filename containing 'test'.");
                return 2;
            }

            using SearchStore store = SearchStore.Open("SQLite", "Data Source=" + databasePath + ";Version=3;");
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

            ArrayList places = store.FindPlaces(Request("Welcome", 0x07000000));
            Require(places.Count == 1 && ((Hashtable)places[0])["name"].ToString() == "Continuum Plaza", "places query");
            ArrayList popular = store.FindPopular(Request(String.Empty, 0x07000000));
            Require(popular.Count == 1, "popular query");
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
            Require(events.Count == 1 && Convert.ToInt32(((Hashtable)events[0])["event_id"]) == 900001, "events query");
            Require(store.GetEvent(new Hashtable { ["eventID"] = "900001" }).Count == 1, "event details query");
            Console.WriteLine("PASS: DataSnapshot parse and idempotent region replacement");
            Console.WriteLine("PASS: places, popular, land, events and event-details viewer query contracts");
            Console.WriteLine("ContinuumSearch SQLite acceptance self-test passed.");
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
