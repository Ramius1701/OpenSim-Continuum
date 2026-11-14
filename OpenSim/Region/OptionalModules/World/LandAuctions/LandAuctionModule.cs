/* Copyright (c) OpenSimulator contributors. BSD-3-Clause; see LICENSE.txt. */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using System.Web;
using System.Text.Json;
using OpenSim.Framework.Servers;
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using Caps = OpenSim.Framework.Capabilities.Caps;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.Region.OptionalModules.World.LandAuctions
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "LandAuctionModule")]
    public sealed class LandAuctionModule : INonSharedRegionModule
    {
        private readonly object m_gate = new();
        private readonly Dictionary<UUID, IClientAPI> m_clients = new();
        private readonly Dictionary<UUID, string> m_urls = new();
        private string m_webSecret = "";
        private uint m_webPort;
        private HashSet<IPAddress> m_webProxies = new();
        private IHttpServer m_webServer;
        private ISimpleStreamHandler m_webHandler;
        private LandAuctionPortal m_portal;
        private Scene m_scene;
        private LandAuctionBook m_book;
        private Timer m_timer;
        private bool m_enabled, m_closed;
        private string m_directory;
        private int m_hours, m_minimum, m_channel;
        public string Name => "LandAuctionModule";
        public Type ReplaceableInterface => null;

        public void Initialise(IConfigSource source)
        {
            var config = source.Configs["LandAuctions"];
            m_enabled = config?.GetBoolean("Enabled", false) ?? false;
            if (!m_enabled) return;
            m_webSecret = config.GetString("WebUIBridgeSecret", "");
            if (m_webSecret.Length > 0 && (m_webSecret.Length < 32 || m_webSecret.Length > 256 || m_webSecret.StartsWith("CHANGE", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("LandAuctions WebUIBridgeSecret requires a unique 32–256 character secret");
            m_webPort = checked((uint)config.GetInt("WebUIPort", 0));
            foreach (string entry in config.GetString("WebUITrustedProxyAddresses", "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!IPAddress.TryParse(entry.Trim(), out var address)) throw new ArgumentException("Invalid auction trusted proxy IP address");
                m_webProxies.Add(address.MapToIPv6());
            }
            m_directory = config.GetString("JournalDirectory", "LandAuctions");
            m_hours = Math.Clamp(config.GetInt("DurationHours", 24), 1, 720);
            m_minimum = Math.Max(1, config.GetInt("MinimumBid", 1));
            m_channel = config.GetInt("ChatChannel", 93);
            if (m_channel == 0) throw new ArgumentException("Auction chat channel must be nonzero");
        }
        public void AddRegion(Scene scene) { if (m_enabled) m_scene = scene; }
        public void RegionLoaded(Scene scene)
        {
            if (!m_enabled || m_closed) return;
            if (scene.RequestModuleInterface<IAuctionMoneyModule>() == null)
                throw new InvalidOperationException("LandAuctions requires a transactional IAuctionMoneyModule");
            m_book = new LandAuctionBook(Path.Combine(m_directory, scene.RegionInfo.RegionID + ".json"),
                Validate, Deliver,
                (bid, auction) => Money.ReserveAuction(Id(bid.Purchase), Id(bid.Buyer), Id(auction.Seller),
                    bid.Amount, scene.RegionInfo.RegionID, Id(auction.Parcel)),
                (bid, capture) => Money.CompleteAuction(Id(bid.Purchase), Id(bid.Buyer), capture));
            foreach (var a in m_book.List().Where(a => a.State == "Open"))
            { try { Mark(a); } catch { m_book.Cancel(a.ID); } }
            m_book.Tick();
            scene.EventManager.OnNewClient += NewClient;
            scene.EventManager.OnClientClosed += ClientClosed;
            scene.EventManager.OnRegisterCaps += RegisterCaps;
            scene.ForEachClient(NewClient);
            if (m_webSecret.Length != 0)
            {
                m_portal = new LandAuctionPortal(m_book, Id(scene.RegionInfo.RegionID), scene.RegionInfo.RegionName,
                    PortalConnected, actor => scene.RegionInfo.EstateSettings.IsEstateManagerOrOwner(Id(actor)) || scene.Permissions.IsGod(Id(actor)),
                    PortalStart, actor => scene.LandChannel.AllParcels().Where(p => p.LandData.OwnerID == Id(actor)
                        && !p.LandData.IsGroupOwned && p.LandData.AuctionID == 0)
                        .Select(p => new AuctionPortalParcel { ID = Id(p.LandData.GlobalID), Name = p.LandData.Name }).ToArray(), m_hours, m_minimum);
                m_webServer = m_webPort == 0 ? MainServer.Instance : MainServer.GetHttpServer(m_webPort);
                m_webHandler = new SimpleStreamHandler("/continuum/auctions/" + scene.RegionInfo.RegionID, PortalRequest);
                if (!m_webServer.TryAddSimpleStreamHandler(m_webHandler))
                { m_webHandler = null; Close(); throw new InvalidOperationException("Auction portal endpoint is already owned"); }
            }
            m_timer = new Timer(_ => Tick(), null, 5000, 5000);
        }
        private bool PortalConnected(Guid actor)
        {
            if (!m_clients.TryGetValue(Id(actor), out var client)) return false;
            try { Local(client); return true; } catch (InvalidOperationException) { return false; }
        }
        private LandAuction PortalStart(Guid actor, Guid parcel)
        {
            var land = m_scene.LandChannel.GetLandObject(Id(parcel));
            if (land == null || land.LandData.OwnerID != Id(actor) || land.LandData.IsGroupOwned || land.LandData.AuctionID != 0)
                throw new InvalidOperationException("Choose your own eligible individually owned parcel");
            var auction = m_book.Start(parcel, actor, Guid.Empty, land.LandData.Name, land.LandData.Area,
                m_minimum, DateTime.UtcNow.AddHours(m_hours), Geometry(land.LandData));
            Mark(auction); return auction;
        }
        private void PortalRequest(IOSHttpRequest request, IOSHttpResponse response)
        {
            response.AddHeader("Cache-Control", "no-store");
            response.ContentType = "application/json; charset=utf-8";
            try
            {
                IPAddress address = request.RemoteIPEndPoint?.Address;
                bool trusted = address != null && m_webProxies.Contains(address.MapToIPv6());
                bool secure = request.IsSecured || (trusted && request.Headers["X-Forwarded-Proto"] == "https")
                    || (!trusted && address != null && IPAddress.IsLoopback(address) && request.Url?.IsLoopback == true);
                if (!secure) { response.StatusCode = 403; return; }
                if (request.HttpMethod != "POST") { response.StatusCode = 405; return; }
                if (request.ContentLength64 < 0 || request.ContentLength64 > 4096) { response.StatusCode = 413; return; }
                using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                char[] chars = new char[4097]; int count = reader.ReadBlock(chars, 0, chars.Length);
                if (count > 4096) { response.StatusCode = 413; return; }
                var value = JsonSerializer.Deserialize<AuctionPortalRequest>(new string(chars, 0, count), new JsonSerializerOptions { MaxDepth = 8 });
                if (value?.Secret == null || value.Secret.Length > 256 || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(value.Secret), Encoding.UTF8.GetBytes(m_webSecret))) { response.StatusCode = 403; return; }
                lock (m_gate)
                {
                    if (m_closed || m_portal == null) { response.StatusCode = 503; return; }
                    response.RawBuffer = JsonSerializer.SerializeToUtf8Bytes(m_portal.Handle(value));
                    response.StatusCode = 200;
                }
            }
            catch (JsonException) { response.StatusCode = 400; }
            catch (Exception)
            {
                response.StatusCode = 503;
                response.RawBuffer = JsonSerializer.SerializeToUtf8Bytes(new AuctionPortalResponse
                    { Success = false, Message = "Auction outcome unconfirmed. Reload before retrying the same operation" });
            }
        }
        private IAuctionMoneyModule Money => m_scene.RequestModuleInterface<IAuctionMoneyModule>()
            ?? throw new InvalidOperationException("Auction economy is unavailable");
        private static UUID Id(Guid value) => new UUID(value.ToString());
        private static Guid Id(UUID value) => Guid.Parse(value.ToString());
        private static string H(object value) => WebUtility.HtmlEncode(Convert.ToString(value, CultureInfo.InvariantCulture));
        private bool Manager(IClientAPI client) => m_scene.Permissions.IsGod(client.AgentId)
            || m_scene.RegionInfo.EstateSettings.IsEstateManagerOrOwner(client.AgentId);
        private void Local(IClientAPI client)
        {
            var account = m_scene.UserAccountService.GetUserAccount(m_scene.RegionInfo.ScopeID, client.AgentId);
            if (m_closed || !m_scene.TryGetScenePresence(client.AgentId, out ScenePresence presence)
                || presence.IsChildAgent || presence.IsDeleted || presence.IsInTransit
                || presence.ControllingClient != client
                || account == null || !account.LocalToGrid || account.UserLevel < 0)
                throw new InvalidOperationException("Auctions require a connected local-grid resident in this region");
        }
        private void NewClient(IClientAPI client)
        {
            lock (m_gate)
            {
                if (m_closed || m_clients.ContainsKey(client.AgentId)) return;
                m_clients.Add(client.AgentId, client);
                client.OnChatFromClient += Chat;
                if (client is IAuctionClient auctionClient) auctionClient.OnStartAuction += Start;
            }
        }
        private void ClosingClient(IClientAPI client)
        {
            lock (m_gate)
            {
                client.OnChatFromClient -= Chat;
                if (client is IAuctionClient auctionClient) auctionClient.OnStartAuction -= Start;
                if (m_clients.TryGetValue(client.AgentId, out var current) && ReferenceEquals(current, client))
                { m_clients.Remove(client.AgentId); m_urls.Remove(client.AgentId); }
            }
        }
        private void ClientClosed(UUID agent, Scene scene)
        {
            lock (m_gate)
                if (m_clients.TryGetValue(agent, out var client)) ClosingClient(client);
        }
        private void Chat(object sender, OSChatMessage message)
        {
            if (message.Channel != m_channel || !String.Equals(message.Message?.Trim(), "auctions", StringComparison.OrdinalIgnoreCase)) return;
            if (sender is IClientAPI client) Open(client);
        }
        private void Open(IClientAPI client)
        {
            lock (m_gate)
            {
                try
                {
                    Local(client);
                    if (m_urls.TryGetValue(client.AgentId, out string url))
                        client.SendLoadURL("Land Auctions", UUID.Zero, UUID.Zero, false,
                            "Browse and bid on this region's land auctions", url);
                    else client.SendAgentAlertMessage("Auction page is not ready; relog to refresh capabilities.", false);
                }
                catch (Exception e) { client.SendAgentAlertMessage(e.Message, false); }
            }
        }
        private void Start(IClientAPI client, int localID, UUID snapshot)
        {
            lock (m_gate)
            {
                try
                {
                    Local(client);
                    if (!Manager(client)) throw new InvalidOperationException("Estate authority is required to start an auction");
                    var land = m_scene.LandChannel.GetLandObject(localID);
                    if (land == null || land.LandData.IsGroupOwned || land.LandData.AuctionID != 0)
                        throw new InvalidOperationException("Parcel is unavailable or group owned");
                    if (land.LandData.OwnerID != client.AgentId)
                        throw new InvalidOperationException("Auction starter must own the parcel");
                    var a = m_book.Start(Id(land.LandData.GlobalID), Id(client.AgentId), Id(snapshot),
                        land.LandData.Name, land.LandData.Area, m_minimum, DateTime.UtcNow.AddHours(m_hours),
                        Geometry(land.LandData));
                    Mark(a);
                    client.SendAgentAlertMessage("Auction " + a.ID + " opened. Use /" + m_channel + " auctions to manage bids.", false);
                    Open(client);
                }
                catch (Exception e) { client.SendAgentAlertMessage(e.Message, false); }
            }
        }
        private void Validate(LandAuction a)
        {
            var land = m_scene.LandChannel.GetLandObject(Id(a.Parcel));
            if (land == null || Id(land.LandData.OwnerID) != a.Seller || land.LandData.IsGroupOwned
                || land.LandData.Area != a.Area || (land.LandData.AuctionID != 0 && land.LandData.AuctionID != a.ID))
                throw new InvalidOperationException("Auction parcel ownership or geometry changed");
            if (!String.Equals(a.GeometryHash, Geometry(land.LandData), StringComparison.Ordinal))
                throw new InvalidOperationException("Auction parcel shape changed");
        }
        private static string Geometry(LandData data) => Convert.ToHexString(SHA256.HashData(data.Bitmap));
        private void Mark(LandAuction a)
        {
            Validate(a);
            var land = m_scene.LandChannel.GetLandObject(Id(a.Parcel));
            var data = land.LandData.Copy();
            data.AuctionID = a.ID;
            data.Flags &= ~(uint)(ParcelFlags.ForSale | ParcelFlags.ForSaleObjects | ParcelFlags.SellParcelObjects);
            data.SalePrice = 0; data.AuthBuyerID = UUID.Zero;
            m_scene.LandChannel.UpdateLandObject(data.LocalID, data);
            m_scene.SimulationDataService.StoreLandObject(land);
            land.SendLandUpdateToAvatars();
        }
        private void Deliver(LandAuction a)
        {
            var land = m_scene.LandChannel.GetLandObject(Id(a.Parcel));
            if (land == null) throw new InvalidOperationException("Settlement parcel is missing");
            if (Id(land.LandData.OwnerID) != a.High.Buyer)
            {
                Validate(a);
                land.UpdateLandSold(Id(a.High.Buyer), UUID.Zero, false, a.ID, a.High.Amount, a.Area);
            }
            var data = land.LandData.Copy(); data.AuctionID = 0;
            m_scene.LandChannel.UpdateLandObject(data.LocalID, data);
            m_scene.SimulationDataService.StoreLandObject(land);
            land.SendLandUpdateToAvatars();
            m_scene.ForEachClient(client => m_scene.LandChannel.SendParcelsOverlay(client));
        }
        private void Tick()
        {
            if (!Monitor.TryEnter(m_gate)) return;
            try
            {
                if (m_closed) return;
                m_book.Tick();
                foreach (var a in m_book.List())
                {
                    var land = m_scene.LandChannel.GetLandObject(Id(a.Parcel));
                    if (LandAuctionBook.Active(a))
                    { if (a.State == "Open" && land != null && land.LandData.AuctionID == 0) Mark(a); }
                    else if (land != null && land.LandData.AuctionID == a.ID)
                    {
                        var data = land.LandData.Copy(); data.AuctionID = 0;
                        m_scene.LandChannel.UpdateLandObject(data.LocalID, data);
                        m_scene.SimulationDataService.StoreLandObject(land);
                        land.SendLandUpdateToAvatars();
                    }
                }
            }
            catch (Exception e) { System.Diagnostics.Trace.TraceError("Auction recovery requires attention: " + e.Message); }
            finally { Monitor.Exit(m_gate); }
        }
        private void RegisterCaps(UUID agent, Caps caps)
        {
            string path = "/" + UUID.Random(); string token = UUID.Random().ToString();
            string url = (caps.SSLCaps ? "https://" : "http://") + caps.HostName + ":" + caps.Port + path;
            lock (m_gate) { if (m_closed) return; m_urls[agent] = url; }
            caps.RegisterSimpleHandler("ParcelAuctions", new SimpleStreamHandler(path, (request, response) =>
            {
                lock (m_gate)
                {
                    response.AddHeader("Cache-Control", "no-store");
                    response.AddHeader("Referrer-Policy", "no-referrer");
                    response.AddHeader("X-Frame-Options", "DENY");
                    response.AddHeader("Content-Security-Policy", "default-src 'none'; form-action 'self'; frame-ancestors 'none'");
                    try
                    {
                        if (!m_clients.TryGetValue(agent, out var client) || !m_urls.TryGetValue(agent, out var current) || current != url)
                            throw new InvalidOperationException("Auction session expired");
                        Local(client);
                        if (request.HttpMethod == "POST")
                        {
                            if (request.ContentLength > 4096) throw new ArgumentException("Request is too large");
                            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                            char[] chars = new char[4097]; int count = reader.ReadBlock(chars, 0, chars.Length);
                            if (count > 4096) throw new ArgumentException("Request is too large");
                            var values = HttpUtility.ParseQueryString(new string(chars, 0, count));
                            if (values["token"] != token) throw new InvalidOperationException("Invalid form token");
                            if (values["action"] == "start")
                            {
                                Start(client, Int32.Parse(values["localID"], CultureInfo.InvariantCulture), UUID.Zero);
                            }
                            else if (values["action"] == "cancel")
                            {
                                uint id = UInt32.Parse(values["id"], CultureInfo.InvariantCulture);
                                var a = m_book.List().First(x => x.ID == id);
                                if (!Manager(client) || a.Seller != Id(agent)) throw new InvalidOperationException("Seller authority required");
                                m_book.Cancel(id);
                            }
                            else if (values["action"] == "bid")
                                m_book.Bid(UInt32.Parse(values["id"], CultureInfo.InvariantCulture), Id(agent),
                                    Int32.Parse(values["amount"], CultureInfo.InvariantCulture));
                            else throw new ArgumentException("Unknown auction action");
                        }
                        else if (request.HttpMethod != "GET") { response.StatusCode = 405; return; }
                        var html = new StringBuilder("<!doctype html><html><head><meta charset=utf-8><title>Land Auctions</title></head><body><h1>Land Auctions</h1><p>Bids reserve the full amount. Outbid reservations are released. Keep this page private.</p>");
                        if (Manager(client))
                            html.Append("<h2>Start an auction</h2><p>Only your individually owned parcels are eligible. Default duration: ")
                                .Append(m_hours).Append(" hours; minimum bid: ").Append(m_minimum)
                                .Append(".</p><form method=post><input type=hidden name=token value='").Append(token)
                                .Append("'><label>Parcel local ID <input type=number name=localID min=1 required></label>")
                                .Append("<button name=action value=start>Start auction</button></form>");
                        foreach (var a in m_book.List().OrderByDescending(x => x.ID).Take(100))
                        {
                            html.Append("<section><h2>").Append(H(a.Name)).Append(" #").Append(a.ID).Append("</h2><p>")
                                .Append(H(a.State)).Append("; ends ").Append(H(a.EndsUtc.ToString("u")))
                                .Append("; minimum ").Append(a.Minimum).Append("; high bid ").Append(a.High?.Amount ?? 0).Append("</p>");
                            if (a.Pending != null)
                                html.Append("<p>Authorization pending for ").Append(a.Pending.Amount)
                                    .Append(". The same bidder must retry that amount, or the seller can cancel.</p>");
                            if (a.State == "Open" && DateTime.UtcNow < a.EndsUtc)
                            {
                                html.Append("<form method=post><input type=hidden name=token value='").Append(token)
                                    .Append("'><input type=hidden name=id value='").Append(a.ID)
                                    .Append("'><input type=number name=amount min=1 max=2147483647 required><button name=action value=bid>Bid</button></form>");
                            }
                            if ((a.State == "Open" || a.State == "Review" || a.State == "Cancelling") && Manager(client) && a.Seller == Id(agent))
                                html.Append("<form method=post><input type=hidden name=token value='").Append(token)
                                    .Append("'><input type=hidden name=id value='").Append(a.ID)
                                    .Append("'><button name=action value=cancel>Cancel and release bids</button></form>");
                            html.Append("</section>");
                        }
                        html.Append("</body></html>");
                        response.ContentType = "text/html; charset=utf-8";
                        response.RawBuffer = Encoding.UTF8.GetBytes(html.ToString());
                        response.StatusCode = 200;
                    }
                    catch (Exception e)
                    {
                        response.StatusCode = 400; response.ContentType = "text/plain; charset=utf-8";
                        response.RawBuffer = Encoding.UTF8.GetBytes("Auction request failed: " + e.Message);
                    }
                }
            }));
        }
        public void RemoveRegion(Scene scene) { if (scene == m_scene) Close(); }
        public void Close()
        {
            lock (m_gate)
            {
                if (m_closed) return;
                m_closed = true; m_timer?.Dispose();
                if (m_webHandler != null) m_webServer?.TryRemoveSimpleStreamHandler(m_webHandler);
                m_webHandler = null;
                if (m_scene != null)
                {
                    m_scene.EventManager.OnNewClient -= NewClient;
                    m_scene.EventManager.OnClientClosed -= ClientClosed;
                    m_scene.EventManager.OnRegisterCaps -= RegisterCaps;
                    foreach (var client in m_clients.Values.ToArray()) ClosingClient(client);
                }
                m_urls.Clear(); m_book?.Dispose();
            }
        }
    }
}
