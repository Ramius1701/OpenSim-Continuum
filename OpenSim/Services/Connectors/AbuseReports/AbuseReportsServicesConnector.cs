using System;
using System.Collections.Generic;
using System.Reflection;
using log4net;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Framework.ServiceAuth;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;

namespace OpenSim.Services.Connectors
{
    public class AbuseReportsServicesConnector : BaseServiceConnector, IAbuseReportsService
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private string m_ServerURI = string.Empty;

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

            SetServerURI(serviceURI);
            base.Initialise(source, "AbuseReportsService");
        }

        public bool ReportAbuse(AbuseReportData report)
        {
            if (report == null || string.IsNullOrEmpty(m_ServerURI))
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

        private void SetServerURI(string serverURI)
        {
            if (string.IsNullOrWhiteSpace(serverURI))
                throw new ArgumentException("Abuse reports server URI cannot be empty", nameof(serverURI));

            m_ServerURI = serverURI.TrimEnd('/') + "/abuse";
        }

        private bool DoSimplePost(string requestString, string method)
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
                    return false;
                }

                Dictionary<string, object> replyData = ServerUtils.ParseXmlResponse(reply);
                if (!replyData.TryGetValue("result", out object result))
                {
                    m_log.WarnFormat(
                        "[ABUSE REPORTS CONNECTOR]: {0} reply did not contain a result field",
                        method);
                    return false;
                }

                return string.Equals(
                    result.ToString(),
                    "success",
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception e)
            {
                m_log.WarnFormat(
                    "[ABUSE REPORTS CONNECTOR]: Exception contacting {0}: {1}",
                    m_ServerURI,
                    e);
                return false;
            }
        }
    }
}
