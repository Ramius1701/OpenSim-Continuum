/* Mobius 924deef165 lineage, adapted to current Robust service authentication. */
using System;
using Nini.Config;
using OpenSim.Framework.ServiceAuth;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Base;
using OpenSim.Server.Handlers.Base;
using OpenSim.Services.Interfaces;

namespace OpenSim.Server.Handlers.Hypergrid
{
    public sealed class HGDisplayNamesServerConnector : ServiceConnector
    {
        public HGDisplayNamesServerConnector(IConfigSource config, IHttpServer server, string configName) :
            base(config, server, configName)
        {
            IConfig section = config.Configs["HGDisplayNames"] ??
                throw new InvalidOperationException("[HGDisplayNames] is required");
            if (!section.GetBoolean("Enabled", false))
                throw new InvalidOperationException("HGDisplayNamesServerConnector was loaded while HGDisplayNames is disabled");
            if (!String.Equals(section.GetString("AuthType", String.Empty), "BasicHttpAuthentication", StringComparison.Ordinal))
                throw new InvalidOperationException("HG display-name federation requires BasicHttpAuthentication");
            string username = section.GetString("HttpAuthUsername", String.Empty);
            string password = section.GetString("HttpAuthPassword", String.Empty);
            if (String.IsNullOrWhiteSpace(username) || password.Length < 24 ||
                username.Contains("CHANGE-ME", StringComparison.OrdinalIgnoreCase) ||
                password.Contains("CHANGE-ME", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("HG display-name federation requires non-example credentials and a 24+ character password");
            IConfig accounts = config.Configs["UserAccountService"] ??
                throw new InvalidOperationException("[UserAccountService] is required");
            string module = accounts.GetString("LocalServiceModule", String.Empty);
            IUserAccountService service = ServerUtils.LoadPlugin<IUserAccountService>(module, new object[] { config }) ??
                throw new InvalidOperationException("Could not load the local user account service");
            server.AddStreamHandler(new HGDisplayNamesServerPostHandler(service,
                ServiceAuth.Create(config, "HGDisplayNames")));
        }
    }
}
