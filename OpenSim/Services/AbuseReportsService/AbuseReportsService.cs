using System;
using System.Reflection;
using System.Text;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Services.Interfaces;

namespace OpenSim.Services.AbuseReportsService
{
    public class AbuseReportsService : AbuseReportsServiceBase, IAbuseReportsService
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly int m_MaxScreenshotBytes;
        private readonly int m_MaxSummaryBytes;
        private readonly int m_MaxDetailsBytes;

        public AbuseReportsService(IConfigSource config)
            : base(config)
        {
            IConfig serviceConfig = config.Configs["AbuseReportsService"];
            m_MaxScreenshotBytes = Math.Max(0,
                serviceConfig?.GetInt("MaxScreenshotBytes", 5 * 1024 * 1024) ?? 5 * 1024 * 1024);
            m_MaxSummaryBytes = Math.Max(256,
                serviceConfig?.GetInt("MaxSummaryBytes", 4096) ?? 4096);
            m_MaxDetailsBytes = Math.Max(1024,
                serviceConfig?.GetInt("MaxDetailsBytes", 65536) ?? 65536);

            m_log.Debug("[ABUSE REPORTS SERVICE]: Starting abuse reports service");

            if (MainConsole.Instance != null)
            {
                MainConsole.Instance.Commands.AddCommand("Abuse Reports", false,
                    "show abuse reports",
                    "show abuse reports [open|in-review|resolved|dismissed|all] [count]",
                    "List recent abuse reports without screenshot data.", HandleShowReports);
                MainConsole.Instance.Commands.AddCommand("Abuse Reports", false,
                    "show abuse report",
                    "show abuse report <report-id>",
                    "Show one abuse report without dumping its screenshot bytes.", HandleShowReport);
                MainConsole.Instance.Commands.AddCommand("Abuse Reports", false,
                    "update abuse report",
                    "update abuse report <report-id> <open|in-review|resolved|dismissed> [notes]",
                    "Update abuse report moderation state and notes.", HandleUpdateReport);
            }
        }

        public bool ReportAbuse(AbuseReportData report)
        {
            if (report == null)
                return false;

            if (report.SenderID.IsZero() || report.AbuseRegionID.IsZero() ||
                Encoding.UTF8.GetByteCount(report.Summary ?? string.Empty) > m_MaxSummaryBytes ||
                Encoding.UTF8.GetByteCount(report.Details ?? string.Empty) > m_MaxDetailsBytes ||
                (report.ImageData?.Length ?? 0) > m_MaxScreenshotBytes)
            {
                m_log.WarnFormat(
                    "[ABUSE REPORTS SERVICE]: Rejected invalid or oversized report from {0}; " +
                    "summary limit {1}, details limit {2}, screenshot limit {3} bytes",
                    report.SenderID,
                    m_MaxSummaryBytes,
                    m_MaxDetailsBytes,
                    m_MaxScreenshotBytes);
                return false;
            }

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

        public AbuseReportData GetReport(int reportID, bool includeImage)
        {
            if (reportID <= 0)
                return null;
            try
            {
                return m_Database.Get(reportID, includeImage);
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[ABUSE REPORTS SERVICE]: Failed to retrieve report {0}: {1}", reportID, e);
                return null;
            }
        }

        public AbuseReportData[] GetReports(int start, int count, string status)
        {
            start = Math.Max(0, start);
            count = Math.Clamp(count, 1, 200);
            if (!TryNormalizeStatus(status, true, out string normalizedStatus))
                return Array.Empty<AbuseReportData>();
            try
            {
                return m_Database.Get(start, count, normalizedStatus);
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[ABUSE REPORTS SERVICE]: Failed to list reports: {0}", e);
                return Array.Empty<AbuseReportData>();
            }
        }

        public bool UpdateReport(int reportID, string status, string notes,
            UUID moderatorID, string moderatorName)
        {
            if (reportID <= 0 || !TryNormalizeStatus(status, false, out string normalizedStatus))
                return false;

            notes = (notes ?? string.Empty).Trim();
            if (notes.Length > 16384)
                return false;
            moderatorName = string.IsNullOrWhiteSpace(moderatorName)
                ? "Console"
                : moderatorName.Trim();
            if (moderatorName.Length > 128)
                return false;

            try
            {
                return m_Database.UpdateModeration(reportID, normalizedStatus, notes,
                    moderatorID, moderatorName, Util.UnixTimeSinceEpoch());
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[ABUSE REPORTS SERVICE]: Failed to update report {0}: {1}", reportID, e);
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
            report.Status = "Open";
            report.ModeratorNotes ??= string.Empty;
            report.ModeratorName ??= string.Empty;
            report.LastUpdated = report.Time;
            report.ImageData ??= Array.Empty<byte>();
        }

        private static bool TryNormalizeStatus(string status, bool allowAll, out string normalized)
        {
            normalized = string.Empty;
            string candidate = (status ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
            switch (candidate)
            {
                case "":
                case "all" when allowAll:
                    return true;
                case "open":
                    normalized = "Open";
                    return true;
                case "in-review":
                case "review":
                    normalized = "In Review";
                    return true;
                case "resolved":
                    normalized = "Resolved";
                    return true;
                case "dismissed":
                    normalized = "Dismissed";
                    return true;
                default:
                    return false;
            }
        }

        private void HandleShowReports(string module, string[] cmd)
        {
            string status = cmd.Length > 3 ? cmd[3] : "open";
            int count = 20;
            if (cmd.Length > 4 && (!int.TryParse(cmd[4], out count) || count < 1 || count > 200))
            {
                MainConsole.Instance.Output("Count must be between 1 and 200.");
                return;
            }

            AbuseReportData[] reports = GetReports(0, count, status);
            MainConsole.Instance.Output("ID     Status       Date (UTC)           Reporter -> Reported user @ Region");
            foreach (AbuseReportData report in reports)
            {
                DateTime date = Util.ToDateTime(report.Time).ToUniversalTime();
                MainConsole.Instance.Output("{0,-6} {1,-12} {2:yyyy-MM-dd HH:mm}  {3} -> {4} @ {5}",
                    report.ReportID, report.Status, date, report.SenderName,
                    report.AbuserName, report.AbuseRegionName);
            }
            MainConsole.Instance.Output("{0} report(s).", reports.Length);
        }

        private void HandleShowReport(string module, string[] cmd)
        {
            if (cmd.Length < 4 || !int.TryParse(cmd[3], out int reportID))
            {
                MainConsole.Instance.Output("Usage: show abuse report <report-id>");
                return;
            }

            AbuseReportData report = GetReport(reportID, false);
            if (report == null)
            {
                MainConsole.Instance.Output("Abuse report {0} was not found.", reportID);
                return;
            }

            MainConsole.Instance.Output("Report ID: {0}\nStatus: {1}\nTime (UTC): {2:u}\nReporter: {3} ({4})\nReported user: {5} ({6})\nRegion: {7} ({8})\nPosition: {9}\nCategory: {10}\nSummary: {11}\nDetails: {12}\nModerator: {13} ({14})\nNotes: {15}",
                report.ReportID, report.Status, Util.ToDateTime(report.Time).ToUniversalTime(),
                report.SenderName, report.SenderID, report.AbuserName, report.AbuserID,
                report.AbuseRegionName, report.AbuseRegionID, report.Position, report.Category,
                report.Summary, report.Details, report.ModeratorName, report.ModeratorID,
                report.ModeratorNotes);
        }

        private void HandleUpdateReport(string module, string[] cmd)
        {
            if (cmd.Length < 5 || !int.TryParse(cmd[3], out int reportID))
            {
                MainConsole.Instance.Output("Usage: update abuse report <report-id> <status> [notes]");
                return;
            }
            string notes = cmd.Length > 5 ? string.Join(" ", cmd, 5, cmd.Length - 5) : string.Empty;
            bool updated = UpdateReport(reportID, cmd[4], notes, UUID.Zero, "Console");
            MainConsole.Instance.Output(updated
                ? "Abuse report {0} updated."
                : "Unable to update abuse report {0}; verify the ID and status.", reportID);
        }
    }
}
