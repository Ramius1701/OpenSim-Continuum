using System;
using System.Reflection;
using log4net;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Services.Interfaces;

namespace OpenSim.Services.AbuseReportsService
{
    public class AbuseReportsService : AbuseReportsServiceBase, IAbuseReportsService
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public AbuseReportsService(IConfigSource config)
            : base(config)
        {
            m_log.Debug("[ABUSE REPORTS SERVICE]: Starting abuse reports service");
        }

        public bool ReportAbuse(AbuseReportData report)
        {
            if (report == null)
                return false;

            Normalize(report);

            try
            {
                return m_Database.Store(report);
            }
            catch (Exception e)
            {
                m_log.ErrorFormat(
                    "[ABUSE REPORTS SERVICE]: Failed to store report from {0} ({1}): {2}",
                    report.SenderName,
                    report.SenderID,
                    e);
                return false;
            }
        }

        private static void Normalize(AbuseReportData report)
        {
            if (report.Time <= 0)
                report.Time = Util.UnixTimeSinceEpoch();

            report.SenderName ??= string.Empty;
            report.AbuseRegionName ??= string.Empty;
            report.AbuserName ??= string.Empty;
            report.Category ??= string.Empty;
            report.Details ??= string.Empty;
            report.Position ??= string.Empty;
            report.Summary ??= string.Empty;
            report.Version ??= string.Empty;
            report.ImageData ??= Array.Empty<byte>();
        }
    }
}
