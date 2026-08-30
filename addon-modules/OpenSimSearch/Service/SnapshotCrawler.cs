using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Nini.Config;
using OpenSim.Framework.Servers.HttpServer;

namespace ContinuumSearch.Service
{
    internal sealed class SnapshotCrawler : IDisposable
    {
        private readonly SearchStore m_store;
        private readonly HttpClient m_http;
        private readonly Timer m_timer;
        private readonly int m_batchSize;
        private readonly int m_defaultInterval;
        private readonly bool m_allowPrivateHosts;
        private int m_running;

        internal SnapshotCrawler(SearchStore store, IConfig config)
        {
            m_store = store;
            m_batchSize = Math.Clamp(config.GetInt("SnapshotBatchSize", 10), 1, 100);
            m_defaultInterval = Math.Clamp(config.GetInt("SnapshotIntervalSeconds", 600), 60, 86400);
            m_allowPrivateHosts = config.GetBoolean("AllowPrivateSnapshotHosts", false);
            m_http = new HttpClient(new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(Math.Clamp(config.GetInt("ConnectTimeoutSeconds", 10), 2, 60))
            }) { Timeout = TimeSpan.FromSeconds(Math.Clamp(config.GetInt("RequestTimeoutSeconds", 30), 5, 120)) };
            m_timer = new Timer(_ => _ = Poll(), null, Timeout.Infinite, Timeout.Infinite);
        }

        internal void Start() => m_timer.Change(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(15));
        internal void Stop() => m_timer.Change(Timeout.Infinite, Timeout.Infinite);

        internal void Register(IOSHttpRequest request, IOSHttpResponse response)
        {
            response.ContentType = "text/plain";
            if (!String.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            { response.StatusCode = (int)HttpStatusCode.MethodNotAllowed; return; }

            string service = request.QueryString["service"] ?? String.Empty;
            string host = (request.QueryString["host"] ?? String.Empty).Trim();
            string secret = (request.QueryString["secret"] ?? String.Empty).Trim();
            if (!Int32.TryParse(request.QueryString["port"], NumberStyles.None, CultureInfo.InvariantCulture, out int port) ||
                port < 1 || port > 65535 || !Guid.TryParse(secret, out Guid parsedSecret) || parsedSecret == Guid.Empty ||
                (service != "online" && service != "offline") || !ValidHost(host))
            { response.StatusCode = (int)HttpStatusCode.BadRequest; return; }

            try
            {
                Uri validation = new UriBuilder(Uri.UriSchemeHttp, host, port, "/")
                { Query = "method=validate&secret=" + Uri.EscapeDataString(secret) }.Uri;
                using HttpRequestMessage message = new(HttpMethod.Get, validation);
                using HttpResponseMessage result = m_http.Send(message, HttpCompletionOption.ResponseHeadersRead);
                if (result.StatusCode != HttpStatusCode.OK)
                { response.StatusCode = (int)HttpStatusCode.Forbidden; return; }
                m_store.RegisterHost(host, port, secret, service == "online");
                response.RawBuffer = System.Text.Encoding.UTF8.GetBytes("OK\n");
                response.StatusCode = (int)HttpStatusCode.OK;
            }
            catch
            { response.StatusCode = (int)HttpStatusCode.BadGateway; }
        }

        private async Task Poll()
        {
            if (Interlocked.Exchange(ref m_running, 1) != 0) return;
            try
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                foreach (SearchHost host in m_store.DueHosts(now, m_batchSize))
                    await PollHost(host, now).ConfigureAwait(false);
            }
            catch (Exception e) { Console.Error.WriteLine("ContinuumSearch snapshot pass failed: {0}", e.Message); }
            finally { Volatile.Write(ref m_running, 0); }
        }

        private async Task PollHost(SearchHost host, long now)
        {
            int interval = m_defaultInterval;
            bool success = false;
            try
            {
                if (!ValidHost(host.Host))
                    throw new InvalidDataException("Snapshot host no longer resolves to an allowed address");
                Uri collector = new UriBuilder(Uri.UriSchemeHttp, host.Host, host.Port, "/") { Query = "method=collector" }.Uri;
                using HttpResponseMessage response = await m_http.GetAsync(collector, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > 8 * 1024 * 1024)
                    throw new InvalidDataException("Snapshot exceeds 8 MiB");
                await using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                XmlDocument document = new() { XmlResolver = null };
                using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024
                });
                document.Load(reader);
                if (Int32.TryParse(Node(document.DocumentElement, "expire"), out int requested))
                    interval = Math.Clamp(requested, 60, 86400);
                List<XmlElement> regions = document.SelectNodes("/regiondata/region").OfType<XmlElement>().ToList();
                if (regions.Count > 4096) throw new InvalidDataException("Snapshot contains too many regions");
                foreach (XmlElement region in regions)
                    m_store.ReplaceRegion(ParseRegion(region));
                success = true;
            }
            catch (Exception e) { Console.Error.WriteLine("Search snapshot {0}:{1} failed: {2}", host.Host, host.Port, e.Message); }
            finally { m_store.HostChecked(host, now + (success ? interval : Math.Min(3600, 60 * (host.Failures + 1))), success); }
        }

        internal static SearchRegion ParseRegion(XmlElement element)
        {
            XmlElement info = element.SelectSingleNode("info") as XmlElement ?? throw new InvalidDataException("Region info is missing");
            XmlElement data = element.SelectSingleNode("data") as XmlElement ?? throw new InvalidDataException("Region data is missing");
            SearchRegion region = new()
            {
                ID = Required(info, "uuid"), Name = Limited(Node(info, "name"), 255),
                Handle = Limited(Node(info, "handle"), 32), Url = Limited(Node(info, "url"), 1024),
                Maturity = Maturity(element.GetAttribute("category"))
            };
            XmlElement estate = data.SelectSingleNode("estate") as XmlElement;
            region.OwnerID = OptionalUuid(Node(estate, "uuid")); region.OwnerName = Limited(Node(estate, "name"), 255);
            int parentEstate = NonNegativeInteger(Node(estate, "id"));

            List<XmlElement> parcels = data.SelectNodes("parcel").OfType<XmlElement>().ToList();
            if (parcels.Count > 8192) throw new InvalidDataException("Region snapshot contains too many parcels");
            foreach (XmlElement item in parcels)
            {
                SearchParcel parcel = new()
                {
                    ID = Required(item, "uuid"), InfoID = OptionalUuid(Node(item, "infouuid")),
                    Name = Limited(Node(item, "name"), 255), Description = Limited(Node(item, "description"), 16000),
                    Landing = Limited(Node(item, "location"), 255), SnapshotID = OptionalUuid(Node(item, "image")),
                    Category = NonNegativeInteger(item.GetAttribute("category")), Area = NonNegativeInteger(Node(item, "area")),
                    SalePrice = NonNegativeInteger(item.GetAttribute("salesprice")), ParentEstate = parentEstate,
                    Dwell = NonNegativeReal(Node(item, "dwell")),
                    ForSale = Boolean(item.GetAttribute("forsale")), ShowInSearch = Boolean(item.GetAttribute("showinsearch"))
                };
                if (String.IsNullOrEmpty(parcel.InfoID)) parcel.InfoID = parcel.ID;
                region.Parcels.Add(parcel);
            }
            List<XmlElement> objects = data.SelectNodes("object").OfType<XmlElement>().ToList();
            if (objects.Count > 50000) throw new InvalidDataException("Region snapshot contains too many objects");
            foreach (XmlElement item in objects)
                region.Objects.Add(new SearchObject
                {
                    ID = Required(item, "uuid"), ParcelID = OptionalUuid(Node(item, "parceluuid")),
                    Name = Limited(Node(item, "title"), 255), Description = Limited(Node(item, "description"), 16000),
                    Location = Limited(Node(item, "location"), 255)
                });
            return region;
        }

        private bool ValidHost(string host)
        {
            if (String.IsNullOrWhiteSpace(host) || host.Length > 255 || host.Contains('/') || host.Contains('\\')) return false;
            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses(host);
                return addresses.Length > 0 && (m_allowPrivateHosts || addresses.All(IsPublic));
            }
            catch { return false; }
        }
        internal static bool IsPublic(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            if (IPAddress.IsLoopback(address)) return false;
            byte[] b = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetwork)
                return !(b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224
                    || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                    || (b[0] == 169 && b[1] == 254)
                    || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                    || (b[0] == 192 && b[1] == 168));
            return !(address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None)
                || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
                || (b.Length == 16 && (b[0] & 0xfe) == 0xfc));
        }
        private static string Node(XmlElement parent, string name) => parent?.SelectSingleNode(name)?.InnerText?.Trim() ?? String.Empty;
        private static string Required(XmlElement parent, string name) { string value = Node(parent, name); if (!Guid.TryParse(value, out Guid id) || id == Guid.Empty) throw new InvalidDataException(name + " UUID is invalid"); return id.ToString(); }
        private static string OptionalUuid(string value) => Guid.TryParse(value, out Guid id) ? id.ToString() : String.Empty;
        private static string Limited(string value, int maximum) => value.Length <= maximum ? value : value.Substring(0, maximum);
        private static int NonNegativeInteger(string value) => Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? Math.Max(0, parsed) : 0;
        private static double NonNegativeReal(string value) => Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && Double.IsFinite(parsed) ? Math.Max(0, parsed) : 0;
        private static bool Boolean(string value) => bool.TryParse(value, out bool parsed) && parsed;
        private static int Maturity(string value) => value.Equals("Adult", StringComparison.OrdinalIgnoreCase) ? 2 : value.Equals("Mature", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        public void Dispose() { Stop(); m_timer.Dispose(); m_http.Dispose(); }
    }
}
