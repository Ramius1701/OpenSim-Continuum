using OpenSim.Framework;
using OpenSim.Continuum.Economy;

static class Program
{
    static void Assert(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    static void Reject(Action action)
    { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected rejection"); }
    static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "continuum-auctions-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try { Run(root); LedgerChecks(root); GridPortalChecks.Run(root); Console.WriteLine("PASS: auction reservations, outbid release, cancellation, expiry, replay, crash recovery, stale parcels, journal isolation and real SQLite escrow."); }
        finally { Directory.Delete(root, true); }
    }
    static void LedgerChecks(string root)
    {
        var backend = EconomyProviderFactory.Create("SQLite", "Data Source=" + Path.Combine(root, "escrow.db") + ";Version=3;");
        backend.Ledger.EnsureSchema();
        Guid seller = Guid.NewGuid(), buyer = Guid.NewGuid(), actor = Guid.NewGuid();
        var credited = backend.Ledger.Adjust(new LedgerAdjustmentRequest {
            OperationID = Guid.NewGuid(), AccountID = buyer, ActorID = actor,
            Amount = 100, Kind = LedgerAdjustmentKind.Credit, Reason = "Auction fixture" });
        Assert(credited.Succeeded, "Fixture funding failed");
        DateTime now = DateTime.UtcNow;
        var path = Path.Combine(root, "ledger-auction.json");
        AuctionPaymentResult Result(LedgerPurchaseResult r, LedgerPurchaseState expected) =>
            r.Succeeded && r.State == expected ? AuctionPaymentResult.Completed :
            r.State == LedgerPurchaseState.InsufficientFunds ? AuctionPaymentResult.Rejected : AuctionPaymentResult.Unknown;
        using (var book = new LandAuctionBook(path, _ => {}, _ => {},
            (bid, a) => Result(backend.Purchases.Authorize(new LedgerPurchaseRequest {
                PurchaseID = bid.Purchase, BuyerID = bid.Buyer, SellerID = a.Seller, Amount = bid.Amount,
                ObjectID = a.Parcel, Description = "Auction fixture" }), LedgerPurchaseState.Authorized),
            (bid, capture) => Result(capture ? backend.Purchases.Capture(bid.Purchase, bid.Buyer)
                : backend.Purchases.Cancel(bid.Purchase, bid.Buyer),
                capture ? LedgerPurchaseState.Captured : LedgerPurchaseState.Cancelled), () => now))
        {
            var a = book.Start(Guid.NewGuid(), seller, Guid.Empty, "Ledger land", 16, 1, now.AddHours(1));
            book.Bid(a.ID, buyer, 80);
            Assert(backend.Ledger.GetBalance(buyer) == 100 && backend.Ledger.GetAvailableBalance(buyer) == 20,
                "Bid did not reserve spendable balance");
            Assert(backend.Ledger.GetBalance(seller) == 0, "Seller credited before expiry");
            Reject(() => book.Bid(a.ID, buyer, 90)); // full replacement must be fundable before releasing old hold
            Assert(book.List()[0].High.Amount == 80, "Rejected replacement lost high bid");
            now = now.AddHours(2); book.Tick(); book.Tick();
            Assert(backend.Ledger.GetBalance(buyer) == 20 && backend.Ledger.GetBalance(seller) == 80,
                "Settlement failed to conserve funds");
            Assert(backend.Ledger.GetAvailableBalance(buyer) == 20, "Settlement left a duplicate hold");
        }
        // SQLite provider pools may retain handles; clear them before fixture cleanup.
        System.Data.SQLite.SQLiteConnection.ClearAllPools();
    }
    static void Run(string root)
    {
        var seller = Guid.NewGuid(); var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var ledger = new Dictionary<Guid, string>(); var amounts = new Dictionary<Guid, int>();
        DateTime now = DateTime.UtcNow;
        bool lostReserve = false, lostCapture = false, loseDelivery = false, stale = false;
        int captures = 0, delivered = 0, reserves = 0;
        Guid deliveredTo = Guid.Empty;
        string path = Path.Combine(root, "region.json");
        LandAuctionBook Open() => new(path,
            a => { if (stale) throw new InvalidOperationException("parcel changed"); },
            a => { if (stale) throw new InvalidOperationException("parcel changed");
                if (deliveredTo != a.High.Buyer) { deliveredTo = a.High.Buyer; delivered++; }
                if (loseDelivery) { loseDelivery = false; throw new IOException("lost delivery acknowledgement"); } },
            (bid, a) => {
                if (!ledger.ContainsKey(bid.Purchase)) { ledger[bid.Purchase] = "Held"; amounts[bid.Purchase] = bid.Amount; reserves++; }
                if (lostReserve) { lostReserve = false; return AuctionPaymentResult.Unknown; }
                return ledger[bid.Purchase] == "Held" ? AuctionPaymentResult.Completed : AuctionPaymentResult.Rejected;
            },
            (bid, capture) => {
                if (!ledger.TryGetValue(bid.Purchase, out var status)) return AuctionPaymentResult.Unknown;
                if (capture)
                {
                    if (status == "Cancelled") return AuctionPaymentResult.Rejected;
                    if (status != "Captured") { captures++; ledger[bid.Purchase] = "Captured"; }
                    if (lostCapture) { lostCapture = false; return AuctionPaymentResult.Unknown; }
                }
                else { if (status == "Captured") return AuctionPaymentResult.Rejected; ledger[bid.Purchase] = "Cancelled"; }
                return AuctionPaymentResult.Completed;
            }, () => now);
        uint id;
        using (var book = Open())
        {
            var a = book.Start(Guid.NewGuid(), seller, Guid.Empty, "Test land", 512, 10, now.AddHours(1)); id = a.ID;
            Reject(() => book.Start(a.Parcel, seller, Guid.Empty, "Duplicate", 512, 10, now.AddHours(1)));
            Reject(() => book.Bid(id, seller, 10)); Reject(() => book.Bid(id, first, 9));
            book.Bid(id, first, 10);
            Reject(() => book.Bid(id, second, 10));
            var firstPurchase = book.List()[0].High.Purchase;
            lostReserve = true;
            Reject(() => book.Bid(id, second, 20));
            Assert(book.List()[0].Pending != null && ledger[firstPurchase] == "Held", "Ambiguous authorization lost old bid");
            // A second simulator cannot operate the same journal.
            bool locked = false; try { using var duplicate = Open(); } catch (IOException) { locked = true; }
            Assert(locked, "Journal lease was not exclusive");
        }
        using (var book = Open())
        {
            book.Bid(id, second, 20);
            Assert(reserves == 2, "Retry changed reservation ID");
            Assert(ledger.Values.Count(x => x == "Cancelled") == 1, "Outbid funds not released");
            var copy = book.List(); copy[0].State = "Completed";
            Assert(book.List()[0].State == "Open", "Read result mutates journal");
            now = now.AddHours(2); lostCapture = true; book.Tick();
            Assert(book.List()[0].State == "Capturing" && captures == 1 && delivered == 0, "Capture intent lost");
        }
        using (var book = Open())
        {
            loseDelivery = true; book.Tick();
            Assert(captures == 1 && delivered == 1 && book.List()[0].State == "Captured", "Capture replay duplicated payment");
        }
        using (var book = Open())
        {
            book.Tick();
            Assert(book.List()[0].State == "Completed" && delivered == 1 && captures == 1, "Delivery recovery duplicated transfer");
            Reject(() => book.Cancel(id));
            var a = book.Start(Guid.NewGuid(), seller, Guid.Empty, "Cancel", 16, 1, now.AddHours(1));
            book.Bid(a.ID, first, 2); var purchase = book.List().Last().High.Purchase;
            book.Cancel(a.ID); book.Cancel(a.ID);
            Assert(ledger[purchase] == "Cancelled" && captures == 1, "Cancellation captured or failed to refund");
            a = book.Start(Guid.NewGuid(), seller, Guid.Empty, "Stale", 16, 1, now.AddHours(1));
            book.Bid(a.ID, first, 3); purchase = book.List().Last().High.Purchase;
            stale = true; now = now.AddHours(2); book.Tick(); stale = false;
            Assert(ledger[purchase] == "Cancelled" && captures == 1, "Stale ownership received payment");
            a = book.Start(Guid.NewGuid(), seller, Guid.Empty, "No bids", 16, 1, now.AddHours(1));
            now = now.AddHours(2); book.Tick();
            Assert(book.List().Last().State == "Cancelled", "No-bid auction failed to expire");
            a = book.Start(Guid.NewGuid(), seller, Guid.Empty, "Concurrent", 16, 1, now.AddHours(1));
            int accepted = 0;
            Parallel.For(0, 12, i => {
                try { book.Bid(a.ID, i % 2 == 0 ? first : second, 50); Interlocked.Increment(ref accepted); }
                catch (InvalidOperationException) { }
            });
            Assert(accepted == 1 && book.List().Last().High.Amount == 50, "Concurrent equal bids were both admitted");
            book.Cancel(a.ID);
        }
    }
}
