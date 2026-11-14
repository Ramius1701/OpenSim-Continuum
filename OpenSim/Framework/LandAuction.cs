/* Copyright (c) OpenSimulator contributors. BSD-3-Clause; see LICENSE.txt.
 * Continuum auction lifecycle. WhiteCore is behavioral evidence, not copied code.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenMetaverse;

namespace OpenSim.Framework
{
    public enum AuctionPaymentResult { Completed, Rejected, Unknown }

    // Optional: do not emulate escrow with an ordinary, non-idempotent transfer.
    public interface IAuctionMoneyModule
    {
        AuctionPaymentResult ReserveAuction(UUID purchase, UUID buyer, UUID seller, int amount,
            UUID region, UUID parcel);
        AuctionPaymentResult CompleteAuction(UUID purchase, UUID buyer, bool capture);
    }

    public interface IAuctionClient
    {
        event Action<IClientAPI, int, UUID> OnStartAuction;
    }

    public sealed class AuctionBid
    {
        public Guid Purchase { get; set; }
        public Guid Buyer { get; set; }
        public int Amount { get; set; }
    }

    public sealed class LandAuction
    {
        public uint ID { get; set; }
        public Guid Parcel { get; set; }
        public Guid Seller { get; set; }
        public Guid Snapshot { get; set; }
        public int Area { get; set; }
        public string Name { get; set; }
        public string GeometryHash { get; set; } = "";
        public DateTime EndsUtc { get; set; }
        public int Minimum { get; set; }
        public string State { get; set; } = "Open";
        public AuctionBid High { get; set; }
        public AuctionBid Pending { get; set; }
        public AuctionBid Release { get; set; }
    }

    /// <summary>
    /// Single-region durable saga. All external operations use persisted purchase IDs.
    /// A failed/ambiguous write faults the instance; restart reads the last atomic file.
    /// Cross-service capture and land transfer are explicitly separate recovery steps.
    /// </summary>
    public sealed class LandAuctionBook : IDisposable
    {
        private readonly object m_gate = new();
        private readonly string m_path;
        private readonly FileStream m_lease;
        private readonly Action<LandAuction> m_validate;
        private readonly Action<LandAuction> m_deliver;
        private readonly Func<AuctionBid, LandAuction, AuctionPaymentResult> m_reserve;
        private readonly Func<AuctionBid, bool, AuctionPaymentResult> m_complete;
        private readonly Func<DateTime> m_clock;
        private List<LandAuction> m_rows;
        private bool m_faulted, m_closed;
        private int m_cursor;

        public LandAuctionBook(string path, Action<LandAuction> validate, Action<LandAuction> deliver,
            Func<AuctionBid, LandAuction, AuctionPaymentResult> reserve,
            Func<AuctionBid, bool, AuctionPaymentResult> complete, Func<DateTime> clock = null)
        {
            m_path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(m_path));
            m_lease = new FileStream(m_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try
            {
                if (File.Exists(m_path) && new FileInfo(m_path).Length > 16 * 1024 * 1024)
                    throw new InvalidDataException("Auction journal is too large");
                m_rows = File.Exists(m_path) ? JsonSerializer.Deserialize<List<LandAuction>>(File.ReadAllText(m_path))
                    ?? throw new InvalidDataException("Invalid auction journal") : new();
                var states = new HashSet<string> { "Open", "Cancelling", "Capturing", "Captured", "Review", "Completed", "Cancelled" };
                bool InvalidBid(AuctionBid bid) => bid != null && (bid.Purchase == Guid.Empty
                    || bid.Buyer == Guid.Empty || bid.Amount < 1);
                if (m_rows.Count > 10000 || m_rows.Select(x => x.ID).Distinct().Count() != m_rows.Count
                    || m_rows.Any(x => x == null || x.ID == 0 || x.Parcel == Guid.Empty || x.Seller == Guid.Empty
                        || !states.Contains(x.State) || x.Area < 1 || x.Minimum < 1 || x.Name == null || x.Name.Length > 255
                        || x.EndsUtc.Kind != DateTimeKind.Utc || (x.GeometryHash?.Length ?? 0) > 64
                        || InvalidBid(x.High) || InvalidBid(x.Pending) || InvalidBid(x.Release)
                        || ((x.State == "Capturing" || x.State == "Captured" || x.State == "Completed" || x.State == "Review") && x.High == null)))
                    throw new InvalidDataException("Invalid auction journal records");
            }
            catch { m_lease.Dispose(); throw; }
            m_validate = validate; m_deliver = deliver; m_reserve = reserve; m_complete = complete;
            m_clock = clock ?? (() => DateTime.UtcNow);
        }

        private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value));
        public LandAuction[] List()
        { lock (m_gate) { Ready(); return Copy(m_rows.ToArray()); } }

        public LandAuction Start(Guid parcel, Guid seller, Guid snapshot, string name, int area,
            int minimum, DateTime endsUtc, string geometryHash = "")
        {
            lock (m_gate)
            {
                Ready();
                if (parcel == Guid.Empty || seller == Guid.Empty || minimum < 1 || area < 1
                    || endsUtc.Kind != DateTimeKind.Utc || endsUtc <= m_clock()
                    || endsUtc > m_clock().AddDays(30) || String.IsNullOrWhiteSpace(name) || name.Length > 255
                    || (geometryHash?.Length ?? 0) > 64)
                    throw new ArgumentException("Invalid auction terms");
                if (m_rows.Count >= 10000 || m_rows.Any(a => a.Parcel == parcel && Active(a)))
                    throw new InvalidOperationException("Parcel already has an auction or journal limit reached");
                uint next = m_rows.Count == 0 ? 1 : checked(m_rows.Max(a => a.ID) + 1);
                var auction = new LandAuction { ID = next, Parcel = parcel, Seller = seller,
                    Snapshot = snapshot, Name = name, Area = area, Minimum = minimum, EndsUtc = endsUtc,
                    GeometryHash = geometryHash ?? "" };
                m_validate(auction);
                m_rows.Add(auction); Save();
                return Copy(auction);
            }
        }

        public void Bid(uint id, Guid buyer, int amount)
        {
            lock (m_gate)
            {
                Ready(); var a = Find(id);
                if (a.State != "Open" || m_clock() >= a.EndsUtc || buyer == Guid.Empty || buyer == a.Seller
                    || amount < a.Minimum || (a.High != null && amount <= a.High.Amount))
                    throw new InvalidOperationException("Auction is closed or bid is not eligible");
                m_validate(a);
                Release(a);
                if (a.Release != null) throw new InvalidOperationException("Previous bid refund is pending");
                if (a.Pending != null && (a.Pending.Buyer != buyer || a.Pending.Amount != amount))
                    throw new InvalidOperationException("A bid authorization is pending; retry that bid first");
                if (a.Pending == null)
                {
                    a.Pending = new AuctionBid { Purchase = Guid.NewGuid(), Buyer = buyer, Amount = amount };
                    Save(); // before the first possible remote debit reservation
                }
                AuctionPaymentResult result = m_reserve(a.Pending, a);
                if (result == AuctionPaymentResult.Unknown)
                    throw new InvalidOperationException("Bid outcome is pending; retry the same bid");
                if (result == AuctionPaymentResult.Rejected)
                { a.Pending = null; Save(); throw new InvalidOperationException("Bid reservation rejected"); }
                a.Release = a.High; a.High = a.Pending; a.Pending = null; Save();
                Release(a);
            }
        }

        public void Cancel(uint id)
        {
            lock (m_gate)
            {
                Ready(); var a = Find(id);
                if (a.State == "Cancelled") return;
                if (a.State != "Open" && a.State != "Cancelling" && a.State != "Review")
                    throw new InvalidOperationException("Settlement has begun; cancellation is unavailable");
                a.State = "Cancelling"; Save(); ContinueCancel(a);
            }
        }

        // Recovery is bounded by the caller's timer; no bidding or debit by a timer.
        public void Tick()
        {
            lock (m_gate)
            {
                Ready();
                var active = m_rows.Where(Active).ToArray();
                if (active.Length == 0) return;
                var batch = Enumerable.Range(0, Math.Min(4, active.Length))
                    .Select(i => active[(m_cursor + i) % active.Length]).ToArray();
                m_cursor = (m_cursor + batch.Length) % active.Length;
                foreach (var a in batch)
                {
                    try
                    {
                        Release(a);
                        if (a.State == "Cancelling") { ContinueCancel(a); continue; }
                        if (a.State == "Open" && m_clock() < a.EndsUtc) continue;
                        if (a.Pending != null)
                        { a.State = "Cancelling"; Save(); ContinueCancel(a); continue; }
                        if (a.Release != null) continue;
                        if (a.State == "Open")
                        {
                            if (a.High == null) { a.State = "Cancelling"; Save(); ContinueCancel(a); continue; }
                            try { m_validate(a); }
                            catch { a.State = "Cancelling"; Save(); ContinueCancel(a); continue; }
                            a.State = "Capturing"; Save();
                        }
                        if (a.State == "Capturing")
                        {
                            // Always replay capture once intent is durable, even after a crash.
                            var result = m_complete(a.High, true);
                            if (result == AuctionPaymentResult.Unknown) continue;
                            if (result == AuctionPaymentResult.Rejected)
                            { a.State = "Review"; Save(); continue; }
                            a.State = "Captured"; Save();
                        }
                        if (a.State == "Captured")
                        {
                            m_deliver(a); // must recognize already-delivered parcels after restart
                            a.State = "Completed"; Save();
                        }
                    }
                    catch (Exception) { if (m_faulted) throw; /* leave persisted intent for retry */ }
                }
            }
        }

        private void ContinueCancel(LandAuction a)
        {
            if (a.Pending != null)
            {
                if (m_complete(a.Pending, false) != AuctionPaymentResult.Completed) return;
                a.Pending = null; Save();
            }
            Release(a); if (a.Release != null) return;
            if (a.High != null)
            {
                if (m_complete(a.High, false) != AuctionPaymentResult.Completed) return;
                a.High = null; Save();
            }
            a.State = "Cancelled"; Save();
        }
        private void Release(LandAuction a)
        {
            if (a.Release != null && m_complete(a.Release, false) == AuctionPaymentResult.Completed)
            { a.Release = null; Save(); }
        }
        public static bool Active(LandAuction a) => a.State != "Completed" && a.State != "Cancelled";
        private LandAuction Find(uint id) => m_rows.FirstOrDefault(a => a.ID == id)
            ?? throw new ArgumentException("Auction not found");
        private void Ready()
        { if (m_closed || m_faulted) throw new InvalidOperationException("Auction journal is unavailable"); }
        private void Save()
        {
            try
            {
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(m_rows);
                using (var stream = new FileStream(m_path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
                { stream.Write(json); stream.Flush(true); }
                File.Move(m_path + ".tmp", m_path, true);
            }
            catch { m_faulted = true; throw; }
        }
        public void Dispose() { lock (m_gate) { m_closed = true; m_lease.Dispose(); } }
    }

    public sealed class AuctionPortalRequest
    {
        public string Secret { get; set; } = "";
        public Guid Actor { get; set; }
        public string Action { get; set; } = "list";
        public int Offset { get; set; }
        public uint ID { get; set; }
        public int Amount { get; set; }
        public Guid Parcel { get; set; }
    }
    public sealed class AuctionPortalRow
    {
        public uint ID { get; set; }
        public Guid Parcel { get; set; }
        public string Name { get; set; }
        public string State { get; set; }
        public int Area { get; set; }
        public DateTime EndsUtc { get; set; }
        public int HighBid { get; set; }
        public long NextBid { get; set; }
        public int PendingAmount { get; set; }
        public bool OwnHighBid { get; set; }
        public bool CanBid { get; set; }
        public bool CanCancel { get; set; }
    }
    public sealed class AuctionPortalParcel
    {
        public Guid ID { get; set; }
        public string Name { get; set; }
    }
    public sealed class AuctionPortalResponse
    {
        public Guid Region { get; set; }
        public string RegionName { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool HasMore { get; set; }
        public int NextOffset { get; set; }
        public bool Connected { get; set; }
        public bool CanStart { get; set; }
        public int DurationHours { get; set; }
        public int MinimumBid { get; set; }
        public AuctionPortalRow[] Auctions { get; set; } = Array.Empty<AuctionPortalRow>();
        public AuctionPortalParcel[] Parcels { get; set; } = Array.Empty<AuctionPortalParcel>();
    }

    /// <summary>Trusted portal adapter. Mutations retain native live-viewer and seller authority.</summary>
    public sealed class LandAuctionPortal
    {
        private readonly LandAuctionBook _book;
        private readonly Guid _region;
        private readonly string _name;
        private readonly Func<Guid, bool> _connected, _manager;
        private readonly Func<Guid, Guid, LandAuction> _start;
        private readonly Func<Guid, AuctionPortalParcel[]> _parcels;
        private readonly int _hours, _minimum;
        public LandAuctionPortal(LandAuctionBook book, Guid region, string name, Func<Guid, bool> connected,
            Func<Guid, bool> manager, Func<Guid, Guid, LandAuction> start, Func<Guid, AuctionPortalParcel[]> parcels,
            int hours, int minimum)
        { _book = book; _region = region; _name = name; _connected = connected; _manager = manager;
            _start = start; _parcels = parcels; _hours = hours; _minimum = minimum; }
        public AuctionPortalResponse Handle(AuctionPortalRequest request)
        {
            if (request == null || request.Offset < 0 || request.Offset > 10000) throw new ArgumentException("Invalid auction request or offset");
            bool connected = request.Actor != Guid.Empty && _connected(request.Actor);
            bool manager = request.Actor != Guid.Empty && _manager(request.Actor);
            var response = new AuctionPortalResponse { Region = _region, RegionName = _name, Connected = connected,
                CanStart = connected && manager, DurationHours = _hours, MinimumBid = _minimum };
            try
            {
                if (request.Action != "list")
                {
                    if (!connected) throw new InvalidOperationException("Log in with your viewer and teleport to this region before managing bids");
                    if (request.Action == "start")
                    {
                        if (!manager) throw new InvalidOperationException("Estate authority is required to start an auction");
                        _start(request.Actor, request.Parcel); response.Message = "Auction opened";
                    }
                    else if (request.Action == "bid")
                    { _book.Bid(request.ID, request.Actor, request.Amount); response.Message = "Auction bid reserved"; }
                    else if (request.Action == "cancel")
                    {
                        var auction = _book.List().FirstOrDefault(x => x.ID == request.ID);
                        if (!manager || auction == null || auction.Seller != request.Actor)
                            throw new InvalidOperationException("Seller estate authority is required to cancel");
                        _book.Cancel(request.ID); response.Message = "Auction cancellation requested";
                    }
                    else throw new ArgumentException("Unknown auction operation");
                }
                response.Success = true;
            }
            catch (ArgumentException e) { response.Message = e.Message; }
            catch (InvalidOperationException e) { response.Message = e.Message; }
            // Do not expose bidder identities, pending purchase IDs, private journal paths or service credentials.
            var visible = _book.List().Where(a => a.State == "Open" || (request.Actor != Guid.Empty
                && (manager || a.Seller == request.Actor || a.High?.Buyer == request.Actor)))
                .OrderByDescending(a => a.ID).ToArray();
            response.HasMore = request.Offset + 20 < visible.Length; response.NextOffset = request.Offset + 20;
            response.Auctions = visible.Skip(request.Offset).Take(20).Select(a =>
                {
                    long next = Math.Max(a.Minimum, (long)(a.High?.Amount ?? 0) + 1);
                    bool pending = a.Pending?.Buyer == request.Actor;
                    return new AuctionPortalRow { ID = a.ID, Parcel = a.Parcel, Name = a.Name, State = a.State,
                        Area = a.Area, EndsUtc = a.EndsUtc, HighBid = a.High?.Amount ?? 0, OwnHighBid = a.High?.Buyer == request.Actor,
                        NextBid = pending ? a.Pending.Amount : next, PendingAmount = pending ? a.Pending.Amount : 0,
                        CanBid = connected && a.State == "Open" && a.EndsUtc > DateTime.UtcNow && a.Seller != request.Actor
                            && (pending || (a.Pending == null && next <= int.MaxValue)),
                        CanCancel = connected && manager && a.Seller == request.Actor && (a.State is "Open" or "Review" or "Cancelling") };
                }).ToArray();
            if (response.CanStart) response.Parcels = _parcels(request.Actor).Take(100).ToArray();
            return response;
        }
    }
}
