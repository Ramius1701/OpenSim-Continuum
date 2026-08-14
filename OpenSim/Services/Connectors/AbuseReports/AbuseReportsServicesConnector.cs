using System;
using System.Collections.Generic;
using System.Reflection;
using log4net;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Framework.ServiceAuth;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;
using OpenMetaverse;

namespace OpenSim.Services.Connectors
{
    public class AbuseReportsServicesConnector : BaseServiceConnector, IAbuseReportsService
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private string m_ServerURI = string.Empty;
        private int m_MaxScreenshotBytes = 5 * 1024 * 1024;

        public AbuseReportsServicesConnector()
        {
        }

        public AbuseReportsServicesConnector(string serverURI)
        {
            SetServerURI(serverURI);
        }

        public AbuseReportsServicesConnector(IConfigSource source)
        {
            Initialise(source);
        }

        public virtual void Initialise(IConfigSource source)
        {
            IConfig serviceConfig = source.Configs["AbuseReportsService"];
            if (serviceConfig == null)
                throw new Exception("[ABUSE REPORTS CONNECTOR]: Missing [AbuseReportsService] configuration");

            string serviceURI = serviceConfig.GetString("AbuseReportsServerURI", string.Empty);
            if (string.IsNullOrWhiteSpace(serviceURI))
                throw new Exception("[ABUSE REPORTS CONNECTOR]: AbuseReportsServerURI is not configured");

            IConfig abuseConfig = source.Configs["AbuseReports"];
            if (abuseConfig != null)
            {
                m_MaxScreenshotBytes = Math.Clamp(
                    abuseConfig.GetInt("MaxScreenshotBytes", m_MaxScreenshotBytes),
                    0,
                    20 * 1024 * 1024);
            }

            SetServerURI(serviceURI);
            base.Initialise(source, "AbuseReportsService");
        }

        public bool ReportAbuse(AbuseReportData report)
        {
            if (report == null || string.IsNullOrEmpty(m_ServerURI)
                || (report.ImageData?.Length ?? 0) > m_MaxScreenshotBytes)
                return false;

            Dictionary<string, object> sendData = new Dictionary<string, object>
            {
                ["METHOD"] = "report",
                ["reporter"] = report.SenderID.ToString(),
                ["reporter-name"] = report.SenderName ?? string.Empty,
                ["abuser"] = report.AbuserID.ToString(),
                ["abuser-name"] = report.AbuserName ?? string.Empty,
                ["summary"] = report.Summary ?? string.Empty,
                ["check-flags"] = report.CheckFlags.ToString(),
                ["region-id"] = report.AbuseRegionID.ToString(),
                ["region-name"] = report.AbuseRegionName ?? string.Empty,
                ["category"] = report.Category ?? string.Empty,
                ["version"] = report.Version ?? string.Empty,
                ["details"] = report.Details ?? string.Empty,
                ["object-id"] = report.ObjectID.ToString(),
                ["position"] = report.Position ?? string.Empty,
                ["report-type"] = report.ReportType.ToString(),
                ["image-data"] = Convert.ToBase64String(report.ImageData ?? Array.Empty<byte>())
            };

            return DoSimplePost(ServerUtils.BuildQueryString(sendData), "report");
        }

        public AbuseReportData GetReport(int reportID, bool includeImage)
        {
            if (reportID < 1)
                return null;

            Dictionary<string, object> request = new Dictionary<string, object>
            {
                ["METHOD"] = "get",
                ["report-id"] = reportID.ToString(),
                ["include-image"] = includeImage ? "1" : "0"
            };
            Dictionary<string, object> reply = DoPost(ServerUtils.BuildQueryString(request), "get");
            return IsSuccess(reply) ? ParseReport(reply, "report-") : null;
        }

        public AbuseReportData[] GetReports(int start, int count, string status)
        {
            if (start < 0 || count < 1)
                return Array.Empty<AbuseReportData>();

            count = Math.Min(count, 200);

            Dictionary<string, object> request = new Dictionary<string, object>
            {
                ["METHOD"] = "list",
                ["start"] = start.ToString(),
                ["count"] = count.ToString(),
                ["status"] = status ?? string.Empty
            };
            Dictionary<string, object> reply = DoPost(ServerUtils.BuildQueryString(request), "list");
            if (!IsSuccess(reply) || !TryGetInt(reply, "report-count", out int reportCount))
                return Array.Empty<AbuseReportData>();

            reportCount = Math.Clamp(reportCount, 0, 200);
            List<AbuseReportData> reports = new List<AbuseReportData>(reportCount);
            for (int i = 0; i < reportCount; i++)
            {
                AbuseReportData report = ParseReport(reply, $"report-{i}-");
                if (report != null)
                    reports.Add(report);
                else
                    m_log.WarnFormat(
                        "[ABUSE REPORTS CONNECTOR]: Ignored malformed report entry {0} in list reply.",
                        i);
            }
            return reports.ToArray();
        }

        public bool UpdateReport(int reportID, string status, string notes,
            UUID moderatorID, string moderatorName)
        {
            if (reportID < 1)
                return false;

            Dictionary<string, object> request = new Dictionary<string, object>
            {
                ["METHOD"] = "update",
                ["report-id"] = reportID.ToString(),
                ["status"] = status ?? string.Empty,
                ["notes"] = notes ?? string.Empty,
                ["moderator-id"] = moderatorID.ToString(),
                ["moderator-name"] = moderatorName ?? string.Empty
            };
            return IsSuccess(DoPost(ServerUtils.BuildQueryString(request), "update"));
        }

        private void SetServerURI(string serverURI)
        {
            if (string.IsNullOrWhiteSpace(serverURI))
                throw new ArgumentException("Abuse reports server URI cannot be empty", nameof(serverURI));

            string candidate = serverURI.Trim().TrimEnd('/');
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || string.IsNullOrEmpty(uri.Host))
            {
                throw new ArgumentException(
                    "Abuse reports server URI must be an absolute HTTP or HTTPS URL",
                    nameof(serverURI));
            }

            m_ServerURI = candidate + "/abuse";
        }

        private bool DoSimplePost(string requestString, string method)
        {
            return IsSuccess(DoPost(requestString, method));
        }

        private Dictionary<string, object> DoPost(string requestString, string method)
        {
            try
            {
                string reply = SynchronousRestFormsRequester.MakeRequest(
                    "POST",
                    m_ServerURI,
                    requestString,
                    m_Auth);

                if (string.IsNullOrEmpty(reply))
                {
                    m_log.WarnFormat(
                        "[ABUSE REPORTS CONNECTOR]: {0} received an empty reply from {1}",
                        method,
                        m_ServerURI);
                    return null;
                }

                Dictionary<string, object> replyData = ServerUtils.ParseXmlResponse(reply);
                if (replyData == null || !replyData.ContainsKey("result"))
                {
                    m_log.WarnFormat(
                        "[ABUSE REPORTS CONNECTOR]: {0} reply did not contain a result field",
                        method);
                    return null;
                }
                return replyData;
            }
            catch (Exception e)
            {
                m_log.WarnFormat(
                    "[ABUSE REPORTS CONNECTOR]: Exception contacting {0}: {1}",
                    m_ServerURI,
                    e);
                return null;
            }
        }

        private static bool IsSuccess(Dictionary<string, object> reply)
        {
            return reply != null && reply.TryGetValue("result", out object result) &&
                string.Equals(result?.ToString(), "success", StringComparison.OrdinalIgnoreCase);
        }

        private AbuseReportData ParseReport(Dictionary<string, object> data, string prefix)
        {
            if (!TryGetInt(data, prefix + "id", out int reportID) || reportID < 1 ||
                !UUID.TryParse(GetString(data, prefix + "reporter"), out UUID senderID) ||
                senderID == UUID.Zero ||
                !UUID.TryParse(GetString(data, prefix + "region-id"), out UUID regionID) ||
                regionID == UUID.Zero)
            {
                return null;
            }

            AbuseReportData report = new AbuseReportData
            {
                ReportID = reportID,
                SenderID = senderID,
                AbuseRegionID = regionID
            };
            TryGetInt(data, prefix + "time", out report.Time);
            TryGetInt(data, prefix + "check-flags", out report.CheckFlags);
            TryGetInt(data, prefix + "report-type", out report.ReportType);
            TryGetInt(data, prefix + "last-updated", out report.LastUpdated);
            UUID.TryParse(GetString(data, prefix + "abuser"), out report.AbuserID);
            UUID.TryParse(GetString(data, prefix + "object-id"), out report.ObjectID);
            UUID.TryParse(GetString(data, prefix + "moderator-id"), out report.ModeratorID);
            report.SenderName = GetString(data, prefix + "reporter-name");
            report.AbuserName = GetString(data, prefix + "abuser-name");
            report.AbuseRegionName = GetString(data, prefix + "region-name");
            report.Category = GetString(data, prefix + "category");
            report.Details = GetString(data, prefix + "details");
            report.Position = GetString(data, prefix + "position");
            report.Summary = GetString(data, prefix + "summary");
            report.Version = GetString(data, prefix + "version");
            report.Status = GetString(data, prefix + "status");
            report.ModeratorNotes = GetString(data, prefix + "notes");
            report.ModeratorName = GetString(data, prefix + "moderator-name");
            string image = GetString(data, prefix + "image-data");
            report.ImageData = TryDecodeImage(image);
            return report;
        }

        private byte[] TryDecodeImage(string image)
        {
            if (string.IsNullOrEmpty(image))
                return Array.Empty<byte>();

            // Base64 expands three bytes into four characters. Reject before
            // allocating the decoded array, allowing a small padding margin.
            long maximumEncodedLength = ((long)m_MaxScreenshotBytes + 2L) / 3L * 4L;
            if (image.Length > maximumEncodedLength)
                return Array.Empty<byte>();

            try
            {
                byte[] decoded = Convert.FromBase64String(image);
                return decoded.Length <= m_MaxScreenshotBytes ? decoded : Array.Empty<byte>();
            }
            catch (FormatException)
            {
                return Array.Empty<byte>();
            }
        }

        private static bool TryGetInt(Dictionary<string, object> data, string key, out int value)
        {
            value = 0;
            return data != null && data.TryGetValue(key, out object raw) &&
                int.TryParse(raw?.ToString(), out value);
        }

        private static string GetString(Dictionary<string, object> data, string key)
        {
            return data != null && data.TryGetValue(key, out object value)
                ? value?.ToString() ?? string.Empty
                : string.Empty;
        }
    }
}
