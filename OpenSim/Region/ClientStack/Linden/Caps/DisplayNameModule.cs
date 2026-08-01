using System;
using System.Reflection;
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using Caps = OpenSim.Framework.Capabilities.Caps;
using OpenSim.Framework;
using System.Net;

namespace OpenSim.Region.ClientStack.LindenCaps
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "DisplayNameModule")]
    public class DisplayNameModule : IDisplayNameModule, INonSharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private IEventQueue m_EventQueue = null;

        protected Scene m_Scene = null;

        private bool m_Enabled = false;

        #region ISharedRegionModule

        public void Initialise(IConfigSource source)
        {
            IConfig config = source.Configs["ClientStack.LindenCaps"];
            if (config == null)
                return;

            string url = config.GetString("Cap_SetDisplayName", string.Empty);
            if (url == "localhost")
                m_Enabled = true;

            if (!m_Enabled)
                return;

            m_log.Info("[DISPLAY NAMES] Plugin enabled!");
        }

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_Scene = scene;

            scene.RegisterModuleInterface<IDisplayNameModule>(this);
        }

        public void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            scene.EventManager.OnRegisterCaps -= OnRegisterCaps;
            scene.UnregisterModuleInterface<IDisplayNameModule>(this);
            m_Scene = null;
            m_EventQueue = null;
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_EventQueue = scene.RequestModuleInterface<IEventQueue>();
            if (m_EventQueue is null)
            {
                m_log.Info("[DISPLAY NAMES]: Module disabled becuase IEventQueue was not found!");
                return;
            }

            scene.EventManager.OnRegisterCaps += OnRegisterCaps;
        }

        public void PostInitialise() { }

        public void Close() { }

        public string Name { get { return "DisplayNamesModule"; } }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        #endregion

        #region IDisplayNameModule

        public string GetDisplayName(UUID avatar)
        {
            var user = m_Scene.UserManagementModule.GetUserData(avatar);

            if (user is not null)
            {
                return user.ViewerDisplayName;
            }

            return string.Empty;
        }

        #endregion

        private void OnRegisterCaps(UUID agentID, Caps caps)
        {
            if (m_Scene.UserManagementModule.IsLocalGridUser(agentID))
            {
                caps.RegisterSimpleHandler("SetDisplayName", new SimpleStreamHandler($"/{UUID.Random()}", (req, resp) => SetDisplayName(agentID, req, resp)));
                return;
            }
        }

        private void SetDisplayName(UUID agent_id, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            if (httpRequest.HttpMethod != "POST")
            {
                httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            ScenePresence sp = m_Scene.GetScenePresence(agent_id);
            if (sp == null || sp.IsDeleted)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.Gone;
                return;
            }

            if (sp.IsInTransit && !sp.IsInLocalTransit)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                httpResponse.AddHeader("Retry-After", "30");
                return;
            }

            UserData userData = m_Scene.UserManagementModule.GetUserData(agent_id);
            if (userData is null)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            if (userData.NameChanged.AddDays(7) > DateTime.UtcNow)
            {
                sp.ControllingClient.SendAlertMessage("You can only change your display name once a week!");
                httpResponse.StatusCode = (int)HttpStatusCode.TooManyRequests;
                return;
            }

            OSDMap req;
            try
            {
                req = OSDParser.DeserializeLLSDXml(httpRequest.InputStream) as OSDMap;
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[DISPLAY NAMES]: Invalid SetDisplayName request for {0}: {1}", agent_id, e.Message);
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            if (req is null || !req.TryGetValue("display_name", out OSD displayNameValue) ||
                displayNameValue is not OSDArray name || name.Count < 2)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            string oldName = userData.ViewerDisplayName;
            string newName = name[1].AsString().Trim();
            bool resetting = string.IsNullOrWhiteSpace(newName);
            if (resetting)
                newName = string.Empty;

            if (newName.Length > 31 || newName.IndexOfAny(['\r', '\n', '\t']) >= 0)
            {
                sp.ControllingClient.SendAlertMessage("Display names must be 31 characters or fewer and cannot contain control characters.");
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            if (!m_Scene.UserManagementModule.SetDisplayName(agent_id, newName))
            {
                sp.ControllingClient.SendAlertMessage("Failed to update display name.");
                httpResponse.StatusCode = (int)HttpStatusCode.InternalServerError;
                return;
            }

            userData.DisplayName = newName;
            userData.NameChanged = DateTime.UtcNow;

            if (resetting)
                m_log.InfoFormat("[DISPLAY NAMES] {0} {1} reset their display name", userData.FirstName, userData.LastName);
            else
                m_log.InfoFormat("[DISPLAY NAMES] {0} {1} changed their display name to {2}", userData.FirstName, userData.LastName, userData.DisplayName);

            DateTime next_update = DateTime.UtcNow.AddDays(7);
            OSD update = FormatDisplayNameUpdate(oldName, userData, next_update);
            m_Scene.ForEachClient(x => m_EventQueue.Enqueue(update, x.AgentId));
            SendSetDisplayNameReply(newName, oldName, userData, next_update);

            httpResponse.ContentType = "application/llsd+xml";
            httpResponse.RawBuffer = Utils.StringToBytes("<llsd><undef/></llsd>");
            httpResponse.StatusCode = (int)HttpStatusCode.OK;
        }

        public OSD FormatDisplayNameUpdate(string oldName, UserData userData, DateTime nextUpdate)
        {
            var agentData = new OSDMap();
            agentData["display_name"] = OSD.FromString(userData.ViewerDisplayName);
            agentData["id"] = OSD.FromUUID(userData.Id);
            agentData["is_display_name_default"] = OSD.FromBoolean(userData.IsNameDefault);
            agentData["legacy_first_name"] = OSD.FromString(userData.FirstName);
            agentData["legacy_last_name"] = OSD.FromString(userData.LastName);
            agentData["username"] = OSD.FromString(userData.Username);
            agentData["display_name_next_update"] = OSD.FromDate(nextUpdate);

            var body = new OSDMap();
            body["agent"] = agentData;
            body["agent_id"] = OSD.FromString(userData.Id.ToString());
            body["old_display_name"] = OSD.FromString(oldName);

            var nameReply = new OSDMap();
            nameReply["body"] = body;
            nameReply["message"] = OSD.FromString("DisplayNameUpdate");
            return nameReply;
        }

        public void SendSetDisplayNameReply(string newDisplayName, string oldDisplayName, UserData nameInfo, DateTime nextUpdate)
        {
            var content = new OSDMap();
            content["display_name"] = OSD.FromString(nameInfo.ViewerDisplayName);
            content["display_name_next_update"] = OSD.FromDate(nextUpdate);
            content["id"] = OSD.FromUUID(nameInfo.Id);
            content["is_display_name_default"] = OSD.FromBoolean(nameInfo.IsNameDefault);
            content["legacy_first_name"] = OSD.FromString(nameInfo.FirstName);
            content["legacy_last_name"] = OSD.FromString(nameInfo.LastName);
            content["username"] = OSD.FromString(nameInfo.LowerUsername);

            var body = new OSDMap();
            body["content"] = content;
            body["reason"] = OSD.FromString("OK");
            body["status"] = OSD.FromInteger(200);

            var nameReply = new OSDMap();
            nameReply["body"] = body;
            nameReply["message"] = OSD.FromString("SetDisplayNameReply");

            m_EventQueue.Enqueue((OSD)nameReply, nameInfo.Id);
        }
    }
}
