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
    }
}
