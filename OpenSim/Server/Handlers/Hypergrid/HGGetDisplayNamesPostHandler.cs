using Nini.Config;
using log4net;
using System;
using System.Reflection;
using System.IO;
using System.Collections.Generic;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenMetaverse;

namespace OpenSim.Server.Handlers.Hypergrid
{
    /// <summary>
    /// Robust-side endpoint any grid exposes at /get_display_names so that
    /// foreign regions can batch-resolve this grid's users' display names
    /// for their HG visitors. Ported from Mobius; called by
    /// DisplayNameServiceConnector on the requesting side.
    /// </summary>
    public class HGGetDisplayNamesPostHandler : BaseStreamHandler
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private IUserAccountService m_UserAccountService;

        public HGGetDisplayNamesPostHandler(IUserAccountService userAccountService) :
                base("POST", "/get_display_names")
        {
            m_UserAccountService = userAccountService;

            if (m_UserAccountService is null)
                m_log.Error("[HGGetDisplayNames Handler]: UserAccountService is null!");
        }

        protected override byte[] ProcessRequest(string path, Stream requestData,
                IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            string body;
            using (StreamReader sr = new StreamReader(requestData))
                body = sr.ReadToEnd();
            body = body.Trim();

            Dictionary<string, object> request = ServerUtils.ParseQueryString(body);

            if (!request.TryGetValue("AgentIDs", out object idsObj) || idsObj is not List<string> userIDs)
            {
                m_log.Debug("[HGGetDisplayNames Handler]: get_display_names called without a valid AgentIDs argument");
                return Array.Empty<byte>();
            }

            List<UserAccount> userAccounts = m_UserAccountService.GetUserAccounts(UUID.Zero, userIDs);

            Dictionary<string, object> result = new Dictionary<string, object>();

            int i = 0;
            foreach (UserAccount user in userAccounts)
            {
                result["uuid" + i] = user.PrincipalID;
                result["name" + i] = user.DisplayName;
                i++;
            }

            result["success"] = "true";

            string xmlString = ServerUtils.BuildXmlResponse(result);
            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }
    }
}
