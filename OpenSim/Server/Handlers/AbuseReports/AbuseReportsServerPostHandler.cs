using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using log4net;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.ServiceAuth;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;

namespace OpenSim.Server.Handlers.AbuseReports
{
    public class AbuseReportsServerPostHandler : BaseStreamHandler
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly IAbuseReportsService m_Service;

        public AbuseReportsServerPostHandler(
            IAbuseReportsService service,
            IServiceAuth auth)
            : base("POST", "/abuse", auth)
        {
            m_Service = service ?? throw new ArgumentNullException(nameof(service));
        }

        protected override byte[] ProcessRequest(
            string path,
            Stream requestData,
            IOSHttpRequest httpRequest,
            IOSHttpResponse httpResponse)
        {
            httpResponse.ContentType = "text/xml";

            string body;
            using (StreamReader reader = new StreamReader(requestData))
                body = reader.ReadToEnd().Trim();

            string method = string.Empty;

            try
            {
                Dictionary<string, object> request = ServerUtils.ParseQueryString(body);
                if (!request.TryGetValue("METHOD", out object methodValue))
                    return FailureResult();

                method = methodValue.ToString();
                if (string.Equals(method, "report", StringComparison.OrdinalIgnoreCase))
                    return Report(request);
                if (string.Equals(method, "get", StringComparison.OrdinalIgnoreCase))
                    return GetReport(request);
                if (string.Equals(method, "list", StringComparison.OrdinalIgnoreCase))
                    return ListReports(request);
                if (string.Equals(method, "update", StringComparison.OrdinalIgnoreCase))
                    return UpdateReport(request);

                m_log.WarnFormat(
                    "[ABUSE REPORT HANDLER]: Unknown method request: {0}",
                    method);
            }
            catch (Exception e)
            {
                m_log.WarnFormat(
                    "[ABUSE REPORT HANDLER]: Exception in method {0}: {1}",
                    method,
                    e);
            }

            return FailureResult();
        }

        private byte[] GetReport(Dictionary<string, object> request)
        {
            if (!TryGetInt(request, "report-id", -1, out int reportID) || reportID <= 0)
                return FailureResult();
            bool includeImage = GetString(request, "include-image") == "1";
            AbuseReportData report = m_Service.GetReport(reportID, includeImage);
            if (report == null)
                return FailureResult();

            Dictionary<string, object> result = new Dictionary<string, object> { ["result"] = "Success" };
            AddReport(result, "report-", report, includeImage);
            return XmlResult(result);
        }

        private byte[] ListReports(Dictionary<string, object> request)
        {
            if (!TryGetInt(request, "start", 0, out int start) || start < 0 ||
                !TryGetInt(request, "count", 20, out int count) || count < 1 || count > 200)
            {
                return FailureResult();
            }

            AbuseReportData[] reports = m_Service.GetReports(start, count, GetString(request, "status"));
            Dictionary<string, object> result = new Dictionary<string, object>
            {
                ["result"] = "Success",
                ["report-count"] = reports.Length.ToString()
            };
            for (int i = 0; i < reports.Length; i++)
                AddReport(result, $"report-{i}-", reports[i], false);
            return XmlResult(result);
        }

        private byte[] UpdateReport(Dictionary<string, object> request)
        {
            if (!TryGetInt(request, "report-id", -1, out int reportID) || reportID <= 0 ||
                !TryGetUUID(request, "moderator-id", out UUID moderatorID))
            {
                return FailureResult();
            }

            return m_Service.UpdateReport(reportID, GetString(request, "status"),
                GetString(request, "notes"), moderatorID, GetString(request, "moderator-name"))
                ? SuccessResult()
                : FailureResult();
        }

        private byte[] Report(Dictionary<string, object> request)
        {
            if (!TryGetUUID(request, "reporter", out UUID reporterID) ||
                !TryGetUUID(request, "abuser", out UUID abuserID) ||
                !TryGetUUID(request, "region-id", out UUID regionID))
            {
                return FailureResult();
            }

            AbuseReportData report = new AbuseReportData
            {
                SenderID = reporterID,
                AbuserID = abuserID,
                AbuseRegionID = regionID,
                SenderName = GetString(request, "reporter-name"),
                AbuserName = GetString(request, "abuser-name"),
                AbuseRegionName = GetString(request, "region-name"),
                Summary = GetString(request, "summary"),
                Details = GetString(request, "details"),
                Version = GetString(request, "version"),
                Position = GetString(request, "position"),
                Category = GetString(request, "category"),
                Time = Util.UnixTimeSinceEpoch()
            };

            if (request.TryGetValue("object-id", out object objectIDValue) &&
                !string.IsNullOrWhiteSpace(objectIDValue?.ToString()) &&
                !UUID.TryParse(objectIDValue.ToString(), out report.ObjectID))
            {
                return FailureResult();
            }

            if (!TryGetInt(request, "check-flags", 0, out report.CheckFlags) ||
                !TryGetInt(request, "report-type", 0, out report.ReportType))
            {
                return FailureResult();
            }

            string imageData = GetString(request, "image-data");
            report.ImageData = string.IsNullOrEmpty(imageData)
                ? Array.Empty<byte>()
                : Convert.FromBase64String(imageData);

            m_log.InfoFormat(
                "[ABUSE REPORTS]: {0} reported {1} in {2}",
                report.SenderName,
                report.AbuserName,
                report.AbuseRegionName);

            return m_Service.ReportAbuse(report)
                ? SuccessResult()
                : FailureResult();
        }

        private static bool TryGetUUID(
            Dictionary<string, object> request,
            string key,
            out UUID value)
        {
            value = UUID.Zero;
            return request.TryGetValue(key, out object raw) &&
                UUID.TryParse(raw?.ToString(), out value);
        }

        private static bool TryGetInt(
            Dictionary<string, object> request,
            string key,
            int defaultValue,
            out int value)
        {
            value = defaultValue;

            if (!request.TryGetValue(key, out object raw) ||
                string.IsNullOrWhiteSpace(raw?.ToString()))
            {
                return true;
            }

            return int.TryParse(raw.ToString(), out value);
        }

        private static string GetString(
            Dictionary<string, object> request,
            string key)
        {
            return request.TryGetValue(key, out object value)
                ? value?.ToString() ?? string.Empty
                : string.Empty;
        }

        private static byte[] SuccessResult()
        {
            return Result("Success");
        }

        private static byte[] FailureResult()
        {
            return Result("Failure");
        }

        private static byte[] Result(string value)
        {
            XmlDocument document = new XmlDocument();
            XmlDeclaration declaration = document.CreateXmlDeclaration("1.0", null, null);
            document.AppendChild(declaration);

            XmlElement root = document.CreateElement("ServerResponse");
            document.AppendChild(root);

            XmlElement result = document.CreateElement("result");
            result.AppendChild(document.CreateTextNode(value));
            root.AppendChild(result);

            return Util.DocToBytes(document);
        }

        private static void AddReport(Dictionary<string, object> data, string prefix,
            AbuseReportData report, bool includeImage)
        {
            data[prefix + "id"] = report.ReportID.ToString();
            data[prefix + "reporter"] = report.SenderID.ToString();
            data[prefix + "reporter-name"] = report.SenderName ?? string.Empty;
            data[prefix + "time"] = report.Time.ToString();
            data[prefix + "region-id"] = report.AbuseRegionID.ToString();
            data[prefix + "region-name"] = report.AbuseRegionName ?? string.Empty;
            data[prefix + "abuser"] = report.AbuserID.ToString();
            data[prefix + "abuser-name"] = report.AbuserName ?? string.Empty;
            data[prefix + "category"] = report.Category ?? string.Empty;
            data[prefix + "check-flags"] = report.CheckFlags.ToString();
            data[prefix + "details"] = report.Details ?? string.Empty;
            data[prefix + "object-id"] = report.ObjectID.ToString();
            data[prefix + "position"] = report.Position ?? string.Empty;
            data[prefix + "report-type"] = report.ReportType.ToString();
            data[prefix + "summary"] = report.Summary ?? string.Empty;
            data[prefix + "version"] = report.Version ?? string.Empty;
            data[prefix + "status"] = report.Status ?? string.Empty;
            data[prefix + "notes"] = report.ModeratorNotes ?? string.Empty;
            data[prefix + "moderator-id"] = report.ModeratorID.ToString();
            data[prefix + "moderator-name"] = report.ModeratorName ?? string.Empty;
            data[prefix + "last-updated"] = report.LastUpdated.ToString();
            if (includeImage)
                data[prefix + "image-data"] = Convert.ToBase64String(report.ImageData ?? Array.Empty<byte>());
        }

        private static byte[] XmlResult(Dictionary<string, object> result)
        {
            return Util.UTF8NoBomEncoding.GetBytes(ServerUtils.BuildXmlResponse(result));
        }
    }
}
