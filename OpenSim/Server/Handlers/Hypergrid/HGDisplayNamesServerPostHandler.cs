/* Mobius 924deef165 lineage, bounded and authenticated for current Robust. */
using System;
using System.Collections.Generic;
using System.IO;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.ServiceAuth;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;

namespace OpenSim.Server.Handlers.Hypergrid
{
    public sealed class HGDisplayNamesServerPostHandler : BaseStreamHandler
    {
        private const int MaxRequestBytes = 32 * 1024;
        private readonly IUserAccountService m_accounts;

        public HGDisplayNamesServerPostHandler(IUserAccountService accounts, IServiceAuth auth) :
            base("POST", "/get_display_names", auth)
        {
            m_accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        }

        protected override byte[] ProcessRequest(string path, Stream requestData,
            IOSHttpRequest request, IOSHttpResponse response)
        {
            response.ContentType = "text/xml";
            if (request.ContentLength64 > MaxRequestBytes)
            {
                response.StatusCode = 413;
                return Array.Empty<byte>();
            }
            string body;
            using (MemoryStream bounded = new())
            {
                byte[] buffer = new byte[4096]; int total = 0;
                for (int read; (read = requestData.Read(buffer, 0, buffer.Length)) > 0; )
                {
                    total += read;
                    if (total > MaxRequestBytes) { response.StatusCode = 413; return Array.Empty<byte>(); }
                    bounded.Write(buffer, 0, read);
                }
                body = Util.UTF8.GetString(bounded.ToArray());
            }
            Dictionary<string, object> parsed;
            try { parsed = ServerUtils.ParseQueryString(body); }
            catch { response.StatusCode = 400; return Array.Empty<byte>(); }
            if (parsed == null || !parsed.TryGetValue("AgentIDs", out object raw) || raw is not List<string> values ||
                values.Count == 0 || values.Count > 100)
            {
                response.StatusCode = 400;
                return Array.Empty<byte>();
            }
            HashSet<UUID> distinct = new(); List<string> ids = new(values.Count);
            foreach (string value in values)
                if (UUID.TryParse(value, out UUID id) && !id.IsZero() && distinct.Add(id)) ids.Add(id.ToString());
            if (ids.Count == 0) { response.StatusCode = 400; return Array.Empty<byte>(); }

            List<UserAccount> accounts = m_accounts.GetUserAccounts(UUID.Zero, ids);
            Dictionary<string, object> result = new() { ["success"] = "true" };
            int index = 0;
            foreach (UserAccount account in accounts)
            {
                if (account == null || !distinct.Contains(account.PrincipalID) || index >= 100)
                    continue;
                result["uuid" + index] = account.PrincipalID.ToString();
                result["name" + index] = account.DisplayName ?? String.Empty;
                result["changed" + index] = account.NameChanged.ToString();
                ++index;
            }
            response.StatusCode = 200;
            return Util.UTF8NoBomEncoding.GetBytes(ServerUtils.BuildXmlResponse(result));
        }
    }
}
