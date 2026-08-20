/*
 * Mobius Display Names lineage: 924deef165b94d463079903ec78962f5d2c11f1e
 * Adapted for bounded requests, current service authentication and no DNS rewriting.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.ServiceAuth;
using OpenSim.Server.Base;

namespace OpenSim.Services.Connectors.Hypergrid
{
    public sealed class RemoteDisplayNameData
    {
        public string DisplayName = String.Empty;
        public DateTime NameChanged = DateTime.MinValue;
    }

    public sealed class DisplayNameServiceConnector
    {
        private const int MaxResponseBytes = 64 * 1024;
        private readonly Uri m_endpoint;
        private readonly IServiceAuth m_auth;
        private readonly int m_timeoutSeconds;

        public DisplayNameServiceConnector(string homeUri, IConfigSource config)
        {
            if (!Uri.TryCreate(homeUri?.TrimEnd('/') + "/get_display_names", UriKind.Absolute, out m_endpoint) ||
                (m_endpoint.Scheme != Uri.UriSchemeHttp && m_endpoint.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("A valid HTTP(S) home-grid URI is required", nameof(homeUri));
            if (m_endpoint.Scheme != Uri.UriSchemeHttps &&
                !(config.Configs["HGDisplayNames"]?.GetBoolean("AllowInsecureHttp", false) ?? false))
                throw new InvalidOperationException("HG display-name credentials require HTTPS unless insecure HTTP is explicitly allowed for testing");
            m_auth = new BasicHttpAuthentication(config, "HGDisplayNames");
            m_timeoutSeconds = Math.Clamp(config.Configs["HGDisplayNames"]?.GetInt("TimeoutSeconds", 5) ?? 5, 1, 30);
        }

        public Dictionary<UUID, RemoteDisplayNameData> GetDisplayNames(IReadOnlyCollection<UUID> userIDs)
        {
            Dictionary<UUID, RemoteDisplayNameData> result = new();
            if (userIDs == null || userIDs.Count == 0 || userIDs.Count > 100)
                return result;
            List<string> ids = new(userIDs.Count);
            foreach (UUID id in userIDs)
                if (!id.IsZero()) ids.Add(id.ToString());
            if (ids.Count == 0)
                return result;

            string requestBody = ServerUtils.BuildQueryString(new Dictionary<string, object>
                { ["AgentIDs"] = ids });
            using HttpClient client = WebUtil.GetNewGlobalHttpClient(m_timeoutSeconds * 1000);
            using HttpRequestMessage request = new(HttpMethod.Post, m_endpoint);
            m_auth.AddAuthorization(request.Headers);
            request.Content = new StringContent(requestBody, Util.UTF8, "application/x-www-form-urlencoded");
            using HttpResponseMessage response = client.Send(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length > MaxResponseBytes)
                throw new InvalidDataException("Home-grid display-name response exceeds the configured bound");
            using Stream stream = response.Content.ReadAsStream();
            using MemoryStream bounded = new();
            byte[] buffer = new byte[4096];
            int total = 0;
            for (int read; (read = stream.Read(buffer, 0, buffer.Length)) > 0; )
            {
                total += read;
                if (total > MaxResponseBytes)
                    throw new InvalidDataException("Home-grid display-name response exceeds the configured bound");
                bounded.Write(buffer, 0, read);
            }
            string reply = Util.UTF8.GetString(bounded.ToArray());
            Dictionary<string, object> data = ServerUtils.ParseXmlResponse(reply);
            if (data == null || !data.TryGetValue("success", out object success) ||
                !String.Equals(Convert.ToString(success), "true", StringComparison.OrdinalIgnoreCase))
                return result;
            HashSet<UUID> requested = new(userIDs);
            for (int i = 0; i < 100; ++i)
            {
                if (!data.TryGetValue("uuid" + i, out object idValue) ||
                    !data.TryGetValue("name" + i, out object nameValue))
                    break;
                if (!UUID.TryParse(Convert.ToString(idValue), out UUID id) || id.IsZero() || !requested.Contains(id))
                    continue;
                long.TryParse(data.TryGetValue("changed" + i, out object changed) ? Convert.ToString(changed) : "0", out long unixChanged);
                result[id] = new RemoteDisplayNameData
                {
                    DisplayName = (Convert.ToString(nameValue) ?? String.Empty).Trim(),
                    NameChanged = unixChanged > 0 && unixChanged <= UInt32.MaxValue
                        ? Utils.UnixTimeToDateTime((uint)unixChanged) : DateTime.MinValue
                };
            }
            return result;
        }
    }
}
