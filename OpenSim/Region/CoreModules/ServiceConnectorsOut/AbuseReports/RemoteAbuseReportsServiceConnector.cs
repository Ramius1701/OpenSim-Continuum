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
using OpenSim.Services.Connectors;
using OpenSim.Services.Interfaces;

namespace OpenSim.Region.CoreModules.ServiceConnectorsOut.AbuseReports
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "RemoteAbuseReportsServicesConnector")]
    public class RemoteAbuseReportsServicesConnector : ISharedRegionModule, IAbuseReportsService
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly List<Scene> m_Scenes = new List<Scene>();
        private IAbuseReportsService m_RemoteConnector;
        private bool m_Enabled;

        public Type ReplaceableInterface => null;
        public string Name => "RemoteAbuseReportsServicesConnector";

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

            try
            {
                m_RemoteConnector = new AbuseReportsServicesConnector(source);
                m_Enabled = true;
                m_log.Info("[ABUSE REPORTS REMOTE CONNECTOR]: Enabled");
            }
            catch (Exception e)
            {
                m_log.ErrorFormat(
                    "[ABUSE REPORTS REMOTE CONNECTOR]: Failed to initialize: {0}",
                    e);
            }
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
            m_log.InfoFormat(
                "[ABUSE REPORTS REMOTE CONNECTOR]: Enabled for region {0}",
                scene.RegionInfo.RegionName);
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
            m_RemoteConnector = null;
        }

        public bool ReportAbuse(AbuseReportData report)
        {
            return m_Enabled &&
                m_RemoteConnector != null &&
                m_RemoteConnector.ReportAbuse(report);
        }

        public AbuseReportData GetReport(int reportID, bool includeImage)
        {
            return m_Enabled && m_RemoteConnector != null
                ? m_RemoteConnector.GetReport(reportID, includeImage)
                : null;
        }

        public AbuseReportData[] GetReports(int start, int count, string status)
        {
            return m_Enabled && m_RemoteConnector != null
                ? m_RemoteConnector.GetReports(start, count, status)
                : Array.Empty<AbuseReportData>();
        }

        public bool UpdateReport(int reportID, string status, string notes,
            UUID moderatorID, string moderatorName)
        {
            return m_Enabled && m_RemoteConnector != null &&
                m_RemoteConnector.UpdateReport(reportID, status, notes, moderatorID, moderatorName);
        }
    }
}
