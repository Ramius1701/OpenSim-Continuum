/* Copyright (c) OpenSimulator contributors. BSD-3-Clause; see LICENSE.txt. */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Services.Interfaces;

namespace OpenSim.Continuum.WebUI
{
    internal sealed partial class WebUIServiceIntegration
    {
        private readonly string _auctionSecret;
        private readonly Dictionary<Guid, string> _auctionEndpoints;
        private static Dictionary<Guid, string> ConfigureAuctionEndpoints(IConfigSource config, string secret, params string[] otherSecrets)
        {
            var result = new Dictionary<Guid, string>();
            var section = config.Configs["ContinuumWebUIAuctions"];
            if (section == null || section.GetKeys().Length == 0) return result;
            if (section.GetKeys().Length > 256 || secret.Length < 32 || secret.Length > 256
                || secret.StartsWith("CHANGE", StringComparison.OrdinalIgnoreCase) || otherSecrets.Any(x => x.Length > 0 && x == secret))
                throw new InvalidOperationException("Auction endpoints require a distinct 32–256 character AuctionBridgeSecret and at most 256 regions");
            foreach (string key in section.GetKeys())
            {
                string value = section.GetString(key, "").Trim();
                if (!Guid.TryParse(key, out Guid region) || region == Guid.Empty || result.ContainsKey(region) || value.Length > 2048
                    || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
                    || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
                    || uri.AbsolutePath != "/continuum/auctions/" + region)
                    throw new InvalidOperationException("Auction endpoints require unique region UUIDs and HTTPS URLs (HTTP only for loopback) at /continuum/auctions/<region UUID>");
                result.Add(region, uri.AbsoluteUri);
            }
            return result;
        }
        private AuctionPortalResponse AuctionRequest(Guid region, AuctionPortalRequest value)
        {
            if (_auctionEndpoints == null || !_auctionEndpoints.TryGetValue(region, out string endpoint)) return null;
            value.Secret = _auctionSecret;
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(endpoint);
                request.Method = "POST"; request.ContentType = "application/json";
                request.Timeout = _rpcTimeoutMs; request.ReadWriteTimeout = _rpcTimeoutMs; request.AllowAutoRedirect = false;
                byte[] body = JsonSerializer.SerializeToUtf8Bytes(value); request.ContentLength = body.Length;
                using (var output = request.GetRequestStream()) output.Write(body);
                using var response = (HttpWebResponse)request.GetResponse();
                if (response.StatusCode != HttpStatusCode.OK || response.ContentLength > 65536) return null;
                using var input = response.GetResponseStream(); using var buffer = new MemoryStream();
                byte[] chunk = new byte[4096]; int count;
                while ((count = input.Read(chunk, 0, chunk.Length)) != 0)
                { if (buffer.Length + count > 65536) return null; buffer.Write(chunk, 0, count); }
                var result = JsonSerializer.Deserialize<AuctionPortalResponse>(buffer.ToArray(), new JsonSerializerOptions { MaxDepth = 12 });
                if (result == null || result.Region != region || result.Auctions == null || result.Auctions.Length > 20
                    || result.Parcels == null || result.Parcels.Length > 100) return null;
                return result;
            }
            catch (WebException error) { error.Response?.Dispose(); Log.WarnFormat("[CONTINUUM WEBUI]: Auction endpoint for region {0} did not confirm an outcome", region); return null; }
            catch (Exception) { Log.WarnFormat("[CONTINUUM WEBUI]: Auction endpoint for region {0} did not confirm an outcome", region); return null; }
        }
        private void AddAuctions(IReadOnlyDictionary<string, string> parameters, UserAccount user, Dictionary<string, object> vars)
        {
            bool configured = _auctionEndpoints?.Count > 0;
            vars["AuctionsConfigured"] = configured; vars["AuctionsDisabled"] = !configured;
            vars["AuctionRegions"] = new List<Dictionary<string, object>>();
            vars["AuctionHasPrevious"] = false; vars["AuctionHasNext"] = false;
            vars["AuctionLoggedOut"] = user == null;
            if (!configured) return;
            Guid actor = user == null ? Guid.Empty : Guid.Parse(user.PrincipalID.ToString());
            int offset = 0, itemOffset = 0;
            if (parameters.TryGetValue("auctionoffset", out string raw) && (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out offset) || offset > 256))
                throw new ArgumentException("Invalid auction region page");
            if (parameters.TryGetValue("auctionitems", out raw) && (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out itemOffset) || itemOffset > 10000))
                throw new ArgumentException("Invalid auction listing page");
            var regions = _auctionEndpoints.Keys.OrderBy(x => x).ToArray();
            if (parameters.TryGetValue("auctionregion", out raw))
            {
                if (!Guid.TryParse(raw, out Guid selected) || !_auctionEndpoints.ContainsKey(selected)) throw new ArgumentException("Unknown auction region");
                regions = new[] { selected }; offset = 0;
            }
            vars["AuctionHasPrevious"] = offset > 0; vars["AuctionHasNext"] = offset + 4 < regions.Length;
            vars["AuctionPrevious"] = Math.Max(0, offset - 4); vars["AuctionNext"] = offset + 4;
            // Four configured endpoints per page; bounded parallel fan-out, with per-endpoint timeouts.
            var responses = Task.WhenAll(regions.Skip(offset).Take(4).Select(region => Task.Run(() =>
                (Region: region, Reply: AuctionRequest(region, new AuctionPortalRequest { Actor = actor, Offset = itemOffset }))))).GetAwaiter().GetResult();
            vars["AuctionRegions"] = responses.Select(entry =>
            {
                var reply = entry.Reply;
                var rows = (reply?.Auctions ?? Array.Empty<AuctionPortalRow>()).Select(a => new Dictionary<string, object>
                {
                    ["AuctionID"] = a.ID, ["AuctionRegionID"] = entry.Region, ["AuctionName"] = H(a.Name), ["AuctionState"] = H(a.State),
                    ["AuctionArea"] = a.Area, ["AuctionEnds"] = H(a.EndsUtc.ToString("u")), ["AuctionHigh"] = a.HighBid,
                    ["AuctionNextBid"] = a.NextBid, ["AuctionPendingAmount"] = a.PendingAmount, ["AuctionHasPending"] = a.PendingAmount > 0,
                    ["AuctionCanBid"] = user != null && a.CanBid, ["AuctionCanCancel"] = user != null && a.CanCancel,
                    ["AuctionOwnHigh"] = user != null && a.OwnHighBid
                }).ToList();
                return new Dictionary<string, object>
                {
                    ["AuctionRegionID"] = entry.Region, ["AuctionRegionName"] = H(reply?.RegionName ?? entry.Region.ToString()),
                    ["AuctionUnavailable"] = reply == null || !reply.Success, ["AuctionRegionEmpty"] = reply != null && reply.Success && rows.Count == 0,
                    ["AuctionNeedsViewer"] = user != null && reply != null && !reply.Connected,
                    ["AuctionCanStart"] = user != null && reply?.CanStart == true && reply.Parcels.Length > 0,
                    ["AuctionHours"] = reply?.DurationHours ?? 0, ["AuctionMinimum"] = reply?.MinimumBid ?? 0,
                    ["AuctionMoreItems"] = reply?.HasMore == true, ["AuctionNextItems"] = reply?.NextOffset ?? 0,
                    ["AuctionRows"] = rows, ["AuctionParcels"] = (reply?.Parcels ?? Array.Empty<AuctionPortalParcel>()).Select(p =>
                        new Dictionary<string, object> { ["AuctionParcelID"] = p.ID, ["AuctionParcelName"] = H(p.Name) }).ToList()
                };
            }).ToList();
        }
        private bool ChangeAuction(IReadOnlyDictionary<string, string> form, UserAccount user, out string message)
        {
            message = "Log in to manage auctions";
            if (user == null) return true;
            string Value(string key) => form.TryGetValue(key, out var value) ? value : "";
            if (!Guid.TryParse(Value("region"), out Guid region) || _auctionEndpoints == null || !_auctionEndpoints.ContainsKey(region))
            { message = "Choose a configured auction region"; return true; }
            string action = Value("action");
            var request = new AuctionPortalRequest { Actor = Guid.Parse(user.PrincipalID.ToString()), Action = action };
            if (action == "start")
            {
                if (!Guid.TryParse(Value("parcel"), out Guid parcel) || parcel == Guid.Empty) { message = "Choose a parcel"; return true; }
                request.Parcel = parcel;
            }
            else if (action is "bid" or "cancel")
            {
                if (!uint.TryParse(Value("auction"), NumberStyles.None, CultureInfo.InvariantCulture, out uint id) || id == 0)
                { message = "Choose an auction"; return true; }
                request.ID = id;
                if (action == "bid")
                {
                    if (Value("confirm") != "yes") { message = "Confirm the full bid reservation before bidding"; return true; }
                    if (!int.TryParse(Value("amount"), NumberStyles.None, CultureInfo.InvariantCulture, out int amount) || amount < 1)
                    { message = "Enter a positive whole-number bid"; return true; }
                    request.Amount = amount;
                }
            }
            else { message = "Unknown auction operation"; return true; }
            var response = AuctionRequest(region, request);
            if (response == null) { message = "Auction outcome unconfirmed. Reload before retrying the same operation"; return true; }
            message = response.Success ? action switch { "start" => "Auction opened", "bid" => "Auction bid reserved", _ => "Auction cancellation requested" }
                : "Auction request was not completed. " + (response.Message ?? "Reload the listing");
            return true;
        }
    }
}
