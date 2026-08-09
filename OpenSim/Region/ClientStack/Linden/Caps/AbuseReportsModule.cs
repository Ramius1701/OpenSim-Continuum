using System;
using System.Reflection;
using System.Text;
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using Caps = OpenSim.Framework.Capabilities.Caps;

namespace OpenSim.Region.ClientStack.Linden
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "AbuseReports")]
    public class AbuseReportsModule : INonSharedRegionModule
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private bool m_Enabled;
        private bool m_CapsEventRegistered;
        private Scene m_Scene;
        private IAbuseReportsService m_Connector;
        private IUserManagement m_UserManager;
        private int m_MaxScreenshotBytes = 5 * 1024 * 1024;
        private int m_MaxReportRequestBytes = 128 * 1024;

        public string Name => "AbuseReportsModule";
        public Type ReplaceableInterface => null;

        public void Initialise(IConfigSource source)
        {
            IConfig config = source.Configs["AbuseReports"];
            m_Enabled = config != null && config.GetBoolean("Enabled", false);
            if (config != null)
            {
                m_MaxScreenshotBytes = Math.Clamp(
                    config.GetInt("MaxScreenshotBytes", m_MaxScreenshotBytes),
                    0,
                    20 * 1024 * 1024);
                m_MaxReportRequestBytes = Math.Clamp(
                    config.GetInt("MaxReportRequestBytes", m_MaxReportRequestBytes),
                    1024,
                    1024 * 1024);
            }

            if (m_Enabled)
                m_log.Info("[ABUSE REPORTS]: Viewer abuse report CAPS enabled");
        }

        public void AddRegion(Scene scene)
        {
            if (m_Enabled)
                m_Scene = scene;
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_Scene = scene;
            m_UserManager = scene.RequestModuleInterface<IUserManagement>();
            if (m_UserManager == null)
            {
                m_log.ErrorFormat(
                    "[ABUSE REPORTS]: IUserManagement is unavailable in region {0}; module disabled",
                    scene.RegionInfo.RegionName);
                m_Enabled = false;
                return;
            }

            m_Connector = scene.RequestModuleInterface<IAbuseReportsService>();
            if (m_Connector == null)
            {
                m_log.ErrorFormat(
                    "[ABUSE REPORTS]: IAbuseReportsService is unavailable in region {0}; module disabled",
                    scene.RegionInfo.RegionName);
                m_Enabled = false;
                return;
            }

            scene.EventManager.OnRegisterCaps += RegisterCaps;
            m_CapsEventRegistered = true;

            m_log.InfoFormat(
                "[ABUSE REPORTS]: Enabled in region {0}",
                scene.RegionInfo.RegionName);
        }

        public void RemoveRegion(Scene scene)
        {
            if (m_CapsEventRegistered)
            {
                scene.EventManager.OnRegisterCaps -= RegisterCaps;
                m_CapsEventRegistered = false;
            }

            if (ReferenceEquals(m_Scene, scene))
            {
                m_Scene = null;
                m_Connector = null;
                m_UserManager = null;
            }
        }

        public void Close()
        {
            Scene scene = m_Scene;
            if (scene != null)
                RemoveRegion(scene);
        }

        private void RegisterCaps(UUID agentID, Caps caps)
        {
            IRequestHandler sendUserReportHandler = new RestStreamHandler(
                "POST",
                "/CAPS/" + UUID.Random(),
                (request, path, param, httpRequest, httpResponse) =>
                    SendUserReport(request, path, param, httpRequest, httpResponse, caps),
                "SendUserReportHandler",
                null);

            caps.RegisterHandler("SendUserReport", sendUserReportHandler);

            IRequestHandler sendUserReportWithScreenshotHandler = new RestStreamHandler(
                "POST",
                "/CAPS/" + UUID.Random(),
                (request, path, param, httpRequest, httpResponse) =>
                    SendUserReportWithScreenshot(request, path, param, httpRequest, httpResponse, caps),
                "SendUserReportWithScreenshot",
                null);

            caps.RegisterHandler(
                "SendUserReportWithScreenshot",
                sendUserReportWithScreenshotHandler);
        }

        private AbuseReportData AbuseReportDataFromOSD(OSDMap map)
        {
            AbuseReportData report = new AbuseReportData();

            if (map.ContainsKey("abuser-id"))
                report.AbuserID = map["abuser-id"].AsUUID();

            if (map.ContainsKey("category"))
                report.Category = map["category"].ToString();

            if (map.ContainsKey("check-flags"))
                report.CheckFlags = map["check-flags"].AsInteger();

            if (map.ContainsKey("details"))
                report.Details = map["details"].ToString();

            if (map.ContainsKey("object-id"))
                report.ObjectID = map["object-id"].AsUUID();

            if (map.ContainsKey("position"))
                report.Position = map["position"].AsVector3().ToString();

            if (map.ContainsKey("report-type"))
                report.ReportType = map["report-type"].AsInteger();

            if (map.ContainsKey("summary"))
                report.Summary = map["summary"].ToString();

            if (map.ContainsKey("version-string"))
                report.Version = map["version-string"].ToString();

            return report;
        }

        public string SendUserReport(
            string request,
            string path,
            string param,
            IOSHttpRequest httpRequest,
            IOSHttpResponse httpResponse,
            Caps caps)
        {
            SetLLSDResponse(httpResponse);

            try
            {
                if (!IsReportRequestSizeValid(request))
                    return SerializeState("failed");

                OSDMap map = DeserializeMap(request);
                AbuseReportData report = AbuseReportDataFromOSD(map);
                PopulateRegionContext(report, caps.AgentID);

                if (m_Connector.ReportAbuse(report))
                {
                    m_log.InfoFormat(
                        "[ABUSE REPORTS]: {0} reported {1} in {2}",
                        report.SenderName,
                        report.AbuserName,
                        report.AbuseRegionName);
                    return SerializeState("complete");
                }

                return SerializeState("failed");
            }
            catch (Exception e)
            {
                m_log.WarnFormat("[ABUSE REPORTS]: Failed to process report: {0}", e);
                return SerializeState("failed");
            }
        }

        public string SendUserReportWithScreenshot(
            string request,
            string path,
            string param,
            IOSHttpRequest httpRequest,
            IOSHttpResponse httpResponse,
            Caps caps)
        {
            SetLLSDResponse(httpResponse);

            try
            {
                if (!IsReportRequestSizeValid(request))
                    return SerializeState("failed");

                OSDMap map = DeserializeMap(request);
                AbuseReportData report = AbuseReportDataFromOSD(map);
                PopulateRegionContext(report, caps.AgentID);

                UUID screenshotID = map.ContainsKey("screenshot-id")
                    ? map["screenshot-id"].AsUUID()
                    : UUID.Zero;

                BinaryStreamHandler uploader = null;
                uploader = new BinaryStreamHandler(
                    "POST",
                    "/CAPS/" + UUID.Random(),
                    (data, uploadPath, uploadParam) =>
                    {
                        caps.HttpListener.RemoveStreamHandler("POST", uploadPath);

                        report.ImageData = data ?? Array.Empty<byte>();

                        OSDMap uploadResponse = new OSDMap();
                        if (report.ImageData.Length > m_MaxScreenshotBytes)
                        {
                            m_log.WarnFormat(
                                "[ABUSE REPORTS]: Rejected {0}-byte screenshot from {1}; limit is {2} bytes",
                                report.ImageData.Length,
                                report.SenderName,
                                m_MaxScreenshotBytes);
                            uploadResponse["state"] = "failed";
                        }
                        else if (m_Connector.ReportAbuse(report))
                        {
                            m_log.InfoFormat(
                                "[ABUSE REPORTS]: {0} reported {1} with screenshot in {2}",
                                report.SenderName,
                                report.AbuserName,
                                report.AbuseRegionName);

                            uploadResponse["state"] = "complete";
                            uploadResponse["new_asset"] = screenshotID;
                        }
                        else
                        {
                            uploadResponse["state"] = "failed";
                        }

                        return OSDParser.SerializeLLSDXmlString(uploadResponse);
                    },
                    "AbuseReportScreenshotUploader",
                    null);

                caps.HttpListener.AddStreamHandler(uploader);

                OSDMap response = new OSDMap
                {
                    ["state"] = "upload",
                    ["uploader"] = (caps.SSLCaps ? "https://" : "http://") +
                        (caps.SSLCaps ? caps.SSLCommonName : caps.HostName) + ":" + caps.Port + uploader.Path
                };

                return OSDParser.SerializeLLSDXmlString(response);
            }
            catch (Exception e)
            {
                m_log.WarnFormat(
                    "[ABUSE REPORTS]: Failed to prepare screenshot report: {0}",
                    e);
                return SerializeState("failed");
            }
        }

        private void PopulateRegionContext(AbuseReportData report, UUID senderID)
        {
            Scene scene = m_Scene;
            if (scene == null || m_Connector == null || m_UserManager == null)
                throw new InvalidOperationException("Abuse reports module is not fully initialized");

            report.SenderID = senderID;
            report.SenderName = m_UserManager.GetUserName(senderID) ?? string.Empty;
            report.AbuseRegionID = scene.RegionInfo.RegionID;
            report.AbuseRegionName = scene.RegionInfo.RegionName ?? string.Empty;
            report.AbuserName = m_UserManager.GetUserName(report.AbuserID) ?? string.Empty;
        }

        private static OSDMap DeserializeMap(string request)
        {
            OSD data = OSDParser.DeserializeLLSDXml(request);
            if (data is not OSDMap map)
                throw new FormatException("Abuse report request was not an LLSD map");

            return map;
        }

        private bool IsReportRequestSizeValid(string request)
        {
            int bytes = Encoding.UTF8.GetByteCount(request ?? string.Empty);
            if (bytes <= m_MaxReportRequestBytes)
                return true;

            m_log.WarnFormat(
                "[ABUSE REPORTS]: Rejected {0}-byte report request; limit is {1} bytes",
                bytes,
                m_MaxReportRequestBytes);
            return false;
        }

        private static void SetLLSDResponse(IOSHttpResponse response)
        {
            response.StatusCode = 200;
            response.ContentType = "application/llsd+xml";
        }

        private static string SerializeState(string state)
        {
            OSDMap response = new OSDMap
            {
                ["state"] = state
            };

            return OSDParser.SerializeLLSDXmlString(response);
        }
    }
}
