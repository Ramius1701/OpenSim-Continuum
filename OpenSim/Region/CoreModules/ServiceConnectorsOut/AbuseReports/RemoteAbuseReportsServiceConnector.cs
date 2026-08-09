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

        private readonly object m_ScenesLock = new object();
        private readonly HashSet<Scene> m_Scenes = new HashSet<Scene>();
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
            if (!m_Enabled || scene == null)
                return;

            lock (m_ScenesLock)
            {
                if (!m_Enabled || !m_Scenes.Add(scene))
                    return;

                scene.RegisterModuleInterface<IAbuseReportsService>(this);
            }

            m_log.InfoFormat(
                "[ABUSE REPORTS REMOTE CONNECTOR]: Enabled for region {0}",
                scene.RegionInfo.RegionName);
        }

        public void RemoveRegion(Scene scene)
        {
            if (scene == null)
                return;

            lock (m_ScenesLock)
            {
                if (!m_Scenes.Remove(scene))
                    return;
            }

            scene.UnregisterModuleInterface<IAbuseReportsService>(this);
        }

        public void RegionLoaded(Scene scene)
        {
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
            if (!m_Enabled)
                return;

            Scene[] scenes;
            lock (m_ScenesLock)
            {
                scenes = new Scene[m_Scenes.Count];
                m_Scenes.CopyTo(scenes);
                m_Scenes.Clear();
                m_Enabled = false;
            }

            foreach (Scene scene in scenes)
                scene.UnregisterModuleInterface<IAbuseReportsService>(this);
        }

        public bool ReportAbuse(AbuseReportData report)
        {
            IAbuseReportsService connector = m_RemoteConnector;
            return m_Enabled && connector != null && connector.ReportAbuse(report);
        }

        public AbuseReportData GetReport(int reportID, bool includeImage)
        {
            IAbuseReportsService connector = m_RemoteConnector;
            return m_Enabled && connector != null
                ? connector.GetReport(reportID, includeImage)
                : null;
        }

        public AbuseReportData[] GetReports(int start, int count, string status)
        {
            IAbuseReportsService connector = m_RemoteConnector;
            return m_Enabled && connector != null
                ? connector.GetReports(start, count, status)
                : Array.Empty<AbuseReportData>();
        }

        public bool UpdateReport(int reportID, string status, string notes,
            UUID moderatorID, string moderatorName)
        {
            IAbuseReportsService connector = m_RemoteConnector;
            return m_Enabled && connector != null &&
                connector.UpdateReport(reportID, status, notes, moderatorID, moderatorName);
        }
    }
}
