using System;
using Nini.Config;
using OpenSim.Framework.ServiceAuth;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Base;
using OpenSim.Server.Handlers.Base;
using OpenSim.Services.Interfaces;

namespace OpenSim.Server.Handlers.AbuseReports
{
    public class AbuseReportsServiceConnector : ServiceConnector
    {
        public AbuseReportsServiceConnector(
            IConfigSource config,
            IHttpServer server,
            string configName)
            : base(config, server, configName)
        {
            string sectionName = string.IsNullOrWhiteSpace(configName)
                ? "AbuseReportsService"
                : configName;

            IConfig serverConfig = config.Configs[sectionName];
            if (serverConfig == null)
                throw new Exception("No [" + sectionName + "] section in configuration");

            string service = serverConfig.GetString("LocalServiceModule", string.Empty);
            if (string.IsNullOrWhiteSpace(service))
                throw new Exception("LocalServiceModule is not configured in [" + sectionName + "]");

            IAbuseReportsService abuseReportsService =
                ServerUtils.LoadPlugin<IAbuseReportsService>(
                    service,
                    new object[] { config });

            if (abuseReportsService == null)
                throw new Exception("Could not load IAbuseReportsService from " + service);

            IServiceAuth auth = ServiceAuth.Create(config, sectionName);
            int maxScreenshotBytes = Math.Clamp(
                serverConfig.GetInt("MaxScreenshotBytes", 5 * 1024 * 1024),
                0,
                20 * 1024 * 1024);
            // The form-encoded base64 screenshot is larger than its binary
            // source. Scale from the established 8 MiB transport allowance
            // for a 5 MiB screenshot and keep the handler strictly bounded.
            int maxRequestBodyBytes = Math.Max(
                8 * 1024 * 1024,
                (int)Math.Min(32L * 1024 * 1024,
                    ((long)maxScreenshotBytes * 8 + 4) / 5));
            server.AddStreamHandler(
                new AbuseReportsServerPostHandler(
                    abuseReportsService,
                    auth,
                    maxRequestBodyBytes));
        }
    }
}
