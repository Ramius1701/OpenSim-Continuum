using System;
using System.Collections.Generic;
using System.Reflection;
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;

namespace OpenSim.Region.CoreModules.ServiceConnectorsOut.AbuseReports
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "LocalAbuseReportsServicesConnector")]
    public class LocalAbuseReportsServicesConnector : ISharedRegionModule, IAbuseReportsService
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly List<Scene> m_Scenes = new List<Scene>();
        private IAbuseReportsService m_Service;
        private bool m_Enabled;

        public Type ReplaceableInterface => null;
        public string Name => "LocalAbuseReportsServicesConnector";

        public void Initialise(IConfigSource source)
        {
            IConfig modulesConfig = source.Configs["Modules"];
            if (modulesConfig == null ||
                !string.Equals(
                    modulesConfig.GetString("AbuseReportsService", string.Empty),
                    Name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            IConfig serviceConfig = source.Configs["AbuseReportsService"];
            if (serviceConfig == null)
            {
                m_log.Error("[ABUSE REPORTS LOCAL CONNECTOR]: Missing [AbuseReportsService] configuration");
                return;
            }

            string serviceDll = serviceConfig.GetString("LocalServiceModule", string.Empty);
            if (string.IsNullOrWhiteSpace(serviceDll))
            {
                m_log.Error("[ABUSE REPORTS LOCAL CONNECTOR]: LocalServiceModule is not configured");
                return;
            }

            try
            {
                m_Service = ServerUtils.LoadPlugin<IAbuseReportsService>(
                    serviceDll,
                    new object[] { source });
            }
            catch (Exception e)
            {
                m_log.ErrorFormat(
                    "[ABUSE REPORTS LOCAL CONNECTOR]: Failed to load service {0}: {1}",
                    serviceDll,
                    e);
                return;
            }

            if (m_Service == null)
            {
                m_log.ErrorFormat(
                    "[ABUSE REPORTS LOCAL CONNECTOR]: Could not load IAbuseReportsService from {0}",
                    serviceDll);
                return;
            }

            m_Enabled = true;
            m_log.Info("[ABUSE REPORTS LOCAL CONNECTOR]: Enabled");
        }

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            lock (m_Scenes)
            {
                if (!m_Scenes.Contains(scene))
                    m_Scenes.Add(scene);
            }

            scene.RegisterModuleInterface<IAbuseReportsService>(this);
        }

        public void RemoveRegion(Scene scene)
        {
            scene.UnregisterModuleInterface<IAbuseReportsService>(this);

            lock (m_Scenes)
                m_Scenes.Remove(scene);
        }

        public void RegionLoaded(Scene scene)
        {
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
            lock (m_Scenes)
            {
                foreach (Scene scene in m_Scenes)
                    scene.UnregisterModuleInterface<IAbuseReportsService>(this);

                m_Scenes.Clear();
            }

            m_Enabled = false;
            m_Service = null;
        }

        public bool ReportAbuse(AbuseReportData report)
        {
            return m_Enabled && m_Service != null && m_Service.ReportAbuse(report);
        }

        public AbuseReportData GetReport(int reportID, bool includeImage)
        {
            return m_Enabled && m_Service != null
                ? m_Service.GetReport(reportID, includeImage)
                : null;
        }

        public AbuseReportData[] GetReports(int start, int count, string status)
        {
            return m_Enabled && m_Service != null
                ? m_Service.GetReports(start, count, status)
                : Array.Empty<AbuseReportData>();
        }

        public bool UpdateReport(int reportID, string status, string notes,
            UUID moderatorID, string moderatorName)
        {
            return m_Enabled && m_Service != null &&
                m_Service.UpdateReport(reportID, status, notes, moderatorID, moderatorName);
        }
    }
}
