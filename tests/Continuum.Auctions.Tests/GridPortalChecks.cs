using System.Text.Json;
using OpenSim.Framework;

static class GridPortalChecks
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string root)
    {
        Guid region = Guid.NewGuid(), seller = Guid.NewGuid(), buyer = Guid.NewGuid(), foreign = Guid.NewGuid(), parcel = Guid.NewGuid();
        var connected = new HashSet<Guid> { seller, buyer, foreign };
        bool unknown = false; int reservations = 0;
        using var book = new LandAuctionBook(Path.Combine(root, "grid-portal.json"), _ => { }, _ => { },
            (_, _) => { reservations++; return unknown ? AuctionPaymentResult.Unknown : AuctionPaymentResult.Completed; },
            (_, _) => AuctionPaymentResult.Completed);
        var portal = new LandAuctionPortal(book, region, "<script>Region</script>", actor => connected.Contains(actor), actor => actor == seller,
            (actor, selected) => { if (selected != parcel || actor != seller) throw new InvalidOperationException("Foreign parcel"); return book.Start(parcel, seller, Guid.Empty, "Test parcel", 16, 1, DateTime.UtcNow.AddHours(1)); },
            _ => new[] { new AuctionPortalParcel { ID = parcel, Name = "Test parcel" } }, 24, 1);
        Check(!portal.Handle(new AuctionPortalRequest { Actor = foreign, Action = "start", Parcel = parcel }).Success && book.List().Length == 0, "Foreign estate start accepted");
        Check(portal.Handle(new AuctionPortalRequest { Actor = seller, Action = "start", Parcel = parcel }).Success, "Seller cannot start through grid portal");
        uint id = book.List().Single().ID;
        connected.Remove(buyer);
        Check(!portal.Handle(new AuctionPortalRequest { Actor = buyer, Action = "bid", ID = id, Amount = 10 }).Success && reservations == 0, "Offline grid bid bypassed viewer authority");
        connected.Add(buyer); unknown = true;
        Check(!portal.Handle(new AuctionPortalRequest { Actor = buyer, Action = "bid", ID = id, Amount = 10 }).Success, "Ambiguous reservation reported success");
        var pending = portal.Handle(new AuctionPortalRequest { Actor = buyer });
        Check(pending.Auctions.Single().PendingAmount == 10 && pending.Auctions.Single().NextBid == 10, "Pending bidder cannot resume exact amount");
        var publicRows = portal.Handle(new AuctionPortalRequest());
        Check(publicRows.Success && publicRows.Auctions.Single().PendingAmount == 0 && !publicRows.Auctions.Single().CanBid
            && !publicRows.Auctions.Single().CanCancel, "Public catalog exposed bidder state or write authority");
        string wire = JsonSerializer.Serialize(publicRows);
        Check(!wire.Contains(buyer.ToString()) && !wire.Contains(book.List().Single().Pending.Purchase.ToString()), "Catalog leaked pending buyer/purchase identity");
        unknown = false;
        Check(portal.Handle(new AuctionPortalRequest { Actor = buyer, Action = "bid", ID = id, Amount = 10 }).Success, "Same pending grid bid could not resume");
        Check(!portal.Handle(new AuctionPortalRequest { Actor = foreign, Action = "cancel", ID = id }).Success, "Foreign cancellation accepted");
        connected.Remove(seller);
        Check(!portal.Handle(new AuctionPortalRequest { Actor = seller, Action = "cancel", ID = id }).Success, "Disconnected seller cancellation accepted");
        connected.Add(seller);
        Check(portal.Handle(new AuctionPortalRequest { Actor = seller, Action = "cancel", ID = id }).Success && book.List().Single().State == "Cancelled", "Seller grid cancellation failed");
        Check(portal.Handle(new AuctionPortalRequest()).Auctions.Length == 0 && portal.Handle(new AuctionPortalRequest { Actor = seller }).Auctions.Single().State == "Cancelled", "Closed journal privacy/owner history failed");
        for (int i = 0; i < 25; i++) book.Start(Guid.NewGuid(), seller, Guid.Empty, "Paged parcel " + i, 16, 1, DateTime.UtcNow.AddHours(1));
        var page = portal.Handle(new AuctionPortalRequest());
        Check(page.Auctions.Length == 20 && page.HasMore && portal.Handle(new AuctionPortalRequest { Offset = page.NextOffset }).Auctions.Length == 5, "Grid auction pagination dropped active auctions");
        Console.WriteLine("PASS: grid auction portal authority, pending replay, privacy, start/cancel and pagination.");
    }
}
