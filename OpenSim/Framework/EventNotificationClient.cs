/* Copyright (c) OpenSimulator contributors. BSD-3-Clause; see LICENSE.txt. */
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using Nini.Config;
using Nwc.XmlRpc;
using OpenMetaverse;

namespace OpenSim.Framework
{
    /// <summary>Private trusted-service event subscriptions, shared by login and simulator adapters.</summary>
    public sealed class EventNotificationClient
    {
        private readonly string _url, _secret;
        private readonly int _timeout;
        public EventNotificationClient(string url, string secret, int timeout = 3000)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
                || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
                || secret == null || secret.Length < 32 || secret.Length > 256 || secret.StartsWith("CHANGE", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Event notifications require HTTPS (HTTP only for loopback) and a distinct 32–256 character secret");
            _url = uri.AbsoluteUri; _secret = secret; _timeout = Math.Clamp(timeout, 500, 10000);
        }
        public static EventNotificationClient Configure(IConfig section, string defaultUrl = "")
        {
            string secret = section?.GetString("EventNotificationSecret", "") ?? "";
            string url = section?.GetString("EventNotificationURL", defaultUrl) ?? "";
            if (secret.Length == 0) return null;
            return new EventNotificationClient(url, secret, section.GetInt("EventNotificationTimeoutMs", 3000));
        }
        private Hashtable Invoke(UUID actor, string action, uint id)
        {
            if (actor == UUID.Zero) return null;
            try
            {
                var request = new Hashtable { ["notificationSecret"] = _secret, ["actor"] = actor.ToString(), ["action"] = action, ["event"] = id.ToString(CultureInfo.InvariantCulture) };
                byte[] payload = Encoding.UTF8.GetBytes(new XmlRpcRequestSerializer().Serialize(new XmlRpcRequest("continuum_event_notifications", new ArrayList { request })));
                var transport = (HttpWebRequest)WebRequest.Create(_url);
                transport.Method = "POST"; transport.ContentType = "text/xml"; transport.AllowAutoRedirect = false;
                transport.Timeout = _timeout; transport.ReadWriteTimeout = _timeout; transport.KeepAlive = false;
                transport.ContentLength = payload.Length;
                using (var output = transport.GetRequestStream()) output.Write(payload);
                using var incoming = (HttpWebResponse)transport.GetResponse();
                if (incoming.StatusCode != HttpStatusCode.OK || incoming.ContentLength > 262144) return null;
                using var input = incoming.GetResponseStream(); using var bytes = new MemoryStream();
                byte[] chunk = new byte[4096]; int count;
                while ((count = input.Read(chunk, 0, chunk.Length)) != 0)
                { if (bytes.Length + count > 262144) return null; bytes.Write(chunk, 0, count); }
                var response = new XmlRpcResponseDeserializer().Deserialize(Encoding.UTF8.GetString(bytes.ToArray())) as XmlRpcResponse;
                return response != null && !response.IsFault && response.Value is Hashtable value && value["success"] is true ? value : null;
            }
            catch (WebException error) { error.Response?.Dispose(); return null; }
            catch (Exception) { return null; } // optional directory failure must not fail grid login
        }
        public bool Change(UUID actor, uint id, bool add) => id > 0 && id <= int.MaxValue && Invoke(actor, add ? "add" : "remove", id) != null;
        public ArrayList Get(UUID actor)
        {
            var result = new ArrayList();
            var reply = Invoke(actor, "list", 0);
            if (reply?["data"] is not ArrayList rows || rows.Count > 100) return result;
            foreach (object item in rows)
            {
                if (item is not Hashtable row || !int.TryParse(row["event_id"]?.ToString(), out int id) || id < 1
                    || !int.TryParse(row["event_date_ut"]?.ToString(), out int timestamp) || timestamp < 1) continue;
                string name = row["event_name"]?.ToString() ?? "", description = row["event_desc"]?.ToString() ?? "";
                if (name.Length > 255 || description.Length > 8192) continue;
                result.Add(new Hashtable { ["event_id"] = id, ["event_name"] = name, ["event_desc"] = description,
                    ["event_date_ut"] = timestamp, ["event_date"] = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture) });
            }
            return result;
        }
    }
}
