using System;
using Nini.Config;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Handlers.Base;

namespace OpenSim.Server.Handlers.Hypergrid
{
    /// <summary>
    /// Registers the /get_display_names endpoint on Robust. Ported from
    /// Mobius. Reuses the grid's own UserAccountService (via
    /// [UserAccountService] LocalServiceModule), same as Mobius did,
    /// rather than a dedicated config section.
    /// </summary>
    public class HGGetDisplayNames : ServiceConnector
    {
        private string m_ConfigName = "UserAccountService";

        private IUserAccountService m_UserAccountService = null;

        // Called from Robust
        public HGGetDisplayNames(IConfigSource config, IHttpServer server, string configName)
        {
            IConfig serverConfig = config.Configs[m_ConfigName];
            if (serverConfig is null)
                throw new Exception(string.Format("No section {0} in config file", m_ConfigName));

            string service = serverConfig.GetString("LocalServiceModule", string.Empty);
            if (service.Length == 0)
                throw new Exception("No LocalServiceModule in config file");

            object[] args = new object[] { config };
            m_UserAccountService = ServerUtils.LoadPlugin<IUserAccountService>(service, args);

            server.AddStreamHandler(new HGGetDisplayNamesPostHandler(m_UserAccountService));
        }
    }
}
