using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using Caps = OpenSim.Framework.Capabilities.Caps;
using System.Text;
using System.Linq;
using System.Collections.Specialized;
using System.Web;
using OpenSim.Framework;
using OpenSim.Services.Interfaces;
using OpenSim.Framework.Client;
using System.Net;

namespace OpenSim.Region.ClientStack.LindenCaps
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "ExperienceModule")]
    public class ExperienceModule : IExperienceModule, INonSharedRegionModule
    {
        private const int MaxExperienceCapsRequestBytes = 256 * 1024;
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        // Dictionary of Agent IDs, with a dictionary of experience permissions and their bools
        private Dictionary<UUID, Dictionary<UUID, bool>> m_ExperiencePermissions = new Dictionary<UUID, Dictionary<UUID, bool>>();
        private readonly object m_ExperiencePermissionsLock = new object();

        private ExpiringCache<UUID, ExperienceInfo> m_ExperienceInfoCache = new ExpiringCache<UUID, ExperienceInfo>();

        private IExperienceService m_ExperienceService = null;

        private IScriptModule[] m_ScriptModules = null;

        protected Scene m_scene = null;

        private bool m_Enabled = false;

        private int CacheTimeout = 1 * 60;

        public void Initialise(IConfigSource source)
        {
            IConfig config = source.Configs["Experience"];
            if (config == null)
                return;

            m_Enabled = config.GetBoolean("Enabled", false);

            if (!m_Enabled)
                return;

            m_log.Info("[Experience] Plugin enabled!");
        }

        #region ISharedRegionModule

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_scene = scene;

        }

        private void EventManager_OnAvatarEnteringNewParcel(ScenePresence avatar, int localLandID, UUID regionID)
        {
            UpdateScriptExperiencePerms(avatar, false);
        }

        public void RemoveRegion(Scene scene)
        {
            scene.EventManager.OnRegisterCaps -= RegisterCaps;
            scene.EventManager.OnNewClient -= OnNewClient;
            scene.EventManager.OnClientClosed -= OnClientClosed;
            scene.EventManager.OnAvatarEnteringNewParcel -= EventManager_OnAvatarEnteringNewParcel;
            scene.UnregisterModuleInterface<IExperienceModule>(this);
            lock (m_ExperiencePermissionsLock)
                m_ExperiencePermissions.Clear();
            m_scene = null;
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_ExperienceService = scene.RequestModuleInterface<IExperienceService>();
            if (m_ExperienceService == null)
            {
                m_log.ErrorFormat(
                    "[EXPERIENCE]: Enabled=true in region {0}, but IExperienceService was not registered. " +
                    "Load the matching LocalExperienceServicesConnector or RemoteExperienceServicesConnector " +
                    "and its [ExperienceService] configuration; viewer Experience CAPS and tabs are unavailable.",
                    scene.RegionInfo.RegionName);
                return;
            }

            m_ScriptModules = scene.RequestModuleInterfaces<IScriptModule>();

            scene.RegisterModuleInterface<IExperienceModule>(this);

            scene.EventManager.OnRegisterCaps += RegisterCaps;
            scene.EventManager.OnNewClient += OnNewClient;
            scene.EventManager.OnClientClosed += OnClientClosed;
            scene.EventManager.OnAvatarEnteringNewParcel += EventManager_OnAvatarEnteringNewParcel;
        }

        private void OnNewClient(IClientAPI client)
        {
            Dictionary<UUID, bool> permissions = m_ExperienceService.FetchExperiencePermissions(client.AgentId)
                ?? new Dictionary<UUID, bool>();
            lock (m_ExperiencePermissionsLock)
                m_ExperiencePermissions[client.AgentId] = permissions;
        }

        private void OnClientClosed(UUID agentID, Scene scene)
        {
            lock (m_ExperiencePermissionsLock)
                m_ExperiencePermissions.Remove(agentID);
        }

        public void PostInitialise() {}

        public void Close() {}

        public string Name { get { return "ExperienceModule"; } }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        #endregion

        public void RegisterCaps(UUID agent, Caps caps)
        {
            caps.RegisterHandler("GetExperiences", new GetExperiencesGetHandler(agent, this));
            caps.RegisterHandler("GetAdminExperiences", new GetAdminExperiencesGetHandler(agent, this));
            caps.RegisterHandler("GetCreatorExperiences", new GetCreatorExperiencesGetHandler(agent, this));
            caps.RegisterHandler("AgentExperiences", new AgentExperiencesGetHandler(agent, this));
            caps.RegisterHandler("GetExperienceInfo", new GetExperienceInfoGetHandler(agent, this));
            caps.RegisterHandler("IsExperienceAdmin", new IsExperienceAdminGetHandler(agent, this));
            caps.RegisterHandler("IsExperienceContributor", new IsExperienceContributorGetHandler(agent, this));
            caps.RegisterSimpleHandler("RegionExperiences",
                new SimpleStreamHandler(string.Format("/caps/{0}", UUID.Random()), delegate (IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
                {
                    HandleRegionExperiences(httpRequest, httpResponse, agent);
                }));
            caps.RegisterHandler("UpdateExperience", new UpdateExperiencePostHandler(agent, this));
            caps.RegisterHandler("GetMetadata", new GetMetadataPostHandler(agent, this, m_scene));
            caps.RegisterHandler("GroupExperiences", new GroupExperiencesGetHandler(agent, this));
            caps.RegisterHandler("FindExperienceByName", new FindExperienceByNameGetHandler(agent, this));

            caps.RegisterSimpleHandler("ExperiencePreferences",
                new SimpleStreamHandler(string.Format("/caps/{0}", UUID.Random()), delegate (IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
                {
                    HandleExperiencePreferences(httpRequest, httpResponse, agent);
                }));

            m_log.DebugFormat(
                "[EXPERIENCE]: Registered viewer capabilities for agent {0} in region {1}",
                agent, m_scene.RegionInfo.RegionName);
        }

        private void HandleRegionExperiences(IOSHttpRequest request, IOSHttpResponse response, UUID agentID)
        {
            if (request.HttpMethod == "POST")
            {
                if (!m_scene.Permissions.IsAdministrator(agentID) && !m_scene.Permissions.IsEstateManager(agentID))
                {
                    response.StatusCode = (int)HttpStatusCode.Forbidden;
                    return;
                }

                OSDMap body;
                try
                {
                    body = OSDParser.DeserializeLLSDXml(ReadBoundedCapsBody(request)) as OSDMap;
                }
                catch (InvalidDataException)
                {
                    response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
                    return;
                }
                catch
                {
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return;
                }

                if (body == null)
                {
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return;
                }

                UUID[] allowed = ReadExperienceList(body, "allowed", (int)Constants.EstateAccessLimits.AllowedExperiences);
                UUID[] blocked = ReadExperienceList(body, "blocked", (int)Constants.EstateAccessLimits.BlockedExperiences);
                UUID[] trusted = ReadExperienceList(body, "trusted", (int)Constants.EstateAccessLimits.KeyExperiences);
                if (allowed == null || blocked == null || trusted == null)
                {
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return;
                }

                // A policy entry must have one meaning. Trusted takes precedence,
                // followed by blocked, then ordinarily allowed.
                HashSet<UUID> trustedSet = new(trusted);
                HashSet<UUID> blockedSet = new(blocked.Where(id => !trustedSet.Contains(id)));
                allowed = allowed.Where(id => !trustedSet.Contains(id) && !blockedSet.Contains(id)).ToArray();

                HashSet<UUID> enabledPolicyIDs = new(allowed);
                enabledPolicyIDs.UnionWith(trustedSet);
                if (enabledPolicyIDs.Count > 0)
                {
                    ExperienceInfo[] policyInfos = GetExperienceInfos(enabledPolicyIDs.ToArray(), true);
                    HashSet<UUID> validPolicyIDs = new(
                        policyInfos
                            .Where(info => info != null &&
                                (info.properties & (int)(ExperienceFlags.Invalid |
                                    ExperienceFlags.Disabled | ExperienceFlags.Suspended)) == 0)
                            .Select(info => info.public_id));
                    if (!enabledPolicyIDs.SetEquals(validPolicyIDs))
                    {
                        response.StatusCode = (int)HttpStatusCode.BadRequest;
                        return;
                    }
                }

                EstateSettings settings = m_scene.RegionInfo.EstateSettings;
                settings.AllowedExperiences = allowed;
                settings.BlockedExperiences = blockedSet.ToArray();
                settings.KeyExperiences = trusted;
                m_scene.EstateDataService.StoreEstateSettings(settings);
                m_scene.ForEachRootScenePresence(
                    presence => UpdateScriptExperiencePerms(presence, false));
            }
            else if (request.HttpMethod != "GET")
            {
                response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                return;
            }

            OSDMap result = new()
            {
                ["allowed"] = ToOSDArray(GetEstateAllowedExperiences()),
                ["blocked"] = ToOSDArray(GetEstateBlockedExperiences()),
                ["default"] = UUID.Zero,
                ["disabled"] = new OSDArray(),
                ["trusted"] = ToOSDArray(GetEstateKeyExperiences())
            };

            response.ContentType = "application/llsd+xml";
            response.RawBuffer = OSDParser.SerializeLLSDXmlBytes(result);
            response.StatusCode = (int)HttpStatusCode.OK;
        }

        private static UUID[] ReadExperienceList(OSDMap body, string name, int maximum)
        {
            if (!body.TryGetValue(name, out OSD value) || value is not OSDArray array || array.Count > maximum)
                return null;

            HashSet<UUID> values = new();
            foreach (OSD entry in array)
            {
                UUID id = entry.AsUUID();
                if (id.IsZero())
                    return null;
                values.Add(id);
            }
            return values.ToArray();
        }

        private static OSDArray ToOSDArray(IEnumerable<UUID> values)
        {
            OSDArray array = new();
            foreach (UUID value in values)
                array.Add(value);
            return array;
        }


        private void HandleExperiencePreferences(IOSHttpRequest request, IOSHttpResponse response, UUID agentID)
        {
            switch (request.HttpMethod)
            {
                case "PUT":
                    HandlePutExperiencePreferences(request, response, agentID);
                    return;
                case "GET":
                    HandleGetExperiencePreferences(request, response, agentID);
                    return;
                case "DELETE":
                    HandleDeleteExperiencePreferences(request, response, agentID);
                    return;
                default:
                    {
                        response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                        return;
                    }
            }
        }

        private void HandleDeleteExperiencePreferences(IOSHttpRequest request, IOSHttpResponse response, UUID agentID)
        {
            byte[] response_bytes = new byte[0];

            string[] split = request.Url.ToString().Split(new[] { request.UriPath.ToString() }, StringSplitOptions.None);

            if (split.Length == 2)
            {
                string key_str = split[1].StartsWith("?") ? split[1].Remove(0, 1) : split[1];

                if (!UUID.TryParse(key_str, out UUID experience_id))
                {
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return;
                }

                ForgetExperiencePermissions(agentID, experience_id);

                string response_str = "<llsd><map><key>blocked</key><undef /><key>experiences</key><undef /></map></llsd>";

                response_bytes = Encoding.UTF8.GetBytes(response_str);
            }

            response.RawBuffer = response_bytes;
            response.StatusCode = (int)HttpStatusCode.OK;
        }

        private void HandleGetExperiencePreferences(IOSHttpRequest request, IOSHttpResponse response, UUID agentID)
        {
            byte[] response_bytes = new byte[0];

            string[] split = request.Url.ToString().Split(new[] { request.UriPath.ToString() }, StringSplitOptions.None);

            if (split.Length == 2)
            {
                string key_str = split[1].StartsWith("?") ? split[1].Remove(0, 1) : split[1];

                if (!UUID.TryParse(key_str, out UUID experience_id))
                {
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return;
                }

                ExperiencePermission experiencePermission = GetExperiencePermission(agentID, experience_id);

                string response_str = "<llsd><map><key>blocked</key><array>" +
                    (experiencePermission == ExperiencePermission.Blocked ? string.Format("<uuid>{0}</uuid>", experience_id) : "<undef />") +
                    "</array><key>experiences</key><array>" +
                    (experiencePermission == ExperiencePermission.Allowed ? string.Format("<uuid>{0}</uuid>", experience_id) : "<undef />") +
                    "</array></map></llsd>";

                response_bytes = Encoding.UTF8.GetBytes(response_str);
            }

            response.RawBuffer = response_bytes;
            response.StatusCode = (int)HttpStatusCode.OK;
        }

        private void HandlePutExperiencePreferences(IOSHttpRequest request, IOSHttpResponse response, UUID agentID)
        {
            OSDMap map;
            try
            {
                map = OSDParser.DeserializeLLSDXml(ReadBoundedCapsBody(request)) as OSDMap;
            }
            catch (InvalidDataException)
            {
                response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
                return;
            }
            catch
            {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            if (map == null)
            {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            byte[] response_bytes = new byte[0];

            if (map.Keys.Count != 1)
            {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            string first_key = map.Keys.First();
            if (!UUID.TryParse(first_key, out UUID experience_id) || experience_id == UUID.Zero ||
                map[first_key] is not OSDMap permissionMap ||
                !permissionMap.TryGetValue("permission", out OSD permissionValue))
            {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            string permission = permissionValue.AsString();
            if (permission != "Allow" && permission != "Block")
            {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            bool allowed = permission == "Allow";
            if (!SetExperiencePermissions(agentID, experience_id, allowed))
            {
                response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                return;
            }

            string response_str = "<llsd><map><key>blocked</key><array>" +
                (!allowed ? string.Format("<uuid>{0}</uuid>", experience_id) : "<undef />") +
                "</array><key>experiences</key><array>" +
                (allowed ? string.Format("<uuid>{0}</uuid>", experience_id) : "<undef />") +
                "</array></map></llsd>";

            response_bytes = Encoding.UTF8.GetBytes(response_str);

            response.RawBuffer = response_bytes;
            response.StatusCode = (int)HttpStatusCode.OK;
        }

        private static byte[] ReadBoundedCapsBody(IOSHttpRequest request)
        {
            if (request == null || request.InputStream == null)
                throw new InvalidDataException("Experience request body is unavailable.");
            if (request.ContentLength64 > MaxExperienceCapsRequestBytes)
                throw new InvalidDataException("Experience request body is too large.");

            return ReadBaseStreamHandler.ReadFully(request.InputStream, MaxExperienceCapsRequestBytes);
        }

        #region IExperienceModule

        public ExperiencePermission GetExperiencePermission(UUID avatar_id, UUID experience_id)
        {
            lock (m_ExperiencePermissionsLock)
            {
                if (m_ExperiencePermissions.TryGetValue(avatar_id, out Dictionary<UUID, bool> permissions) &&
                    permissions.TryGetValue(experience_id, out bool allowed))
                    return allowed ? ExperiencePermission.Allowed : ExperiencePermission.Blocked;
            }
            return ExperiencePermission.None;
        }

        public bool SetExperiencePermissions(UUID avatar_id, UUID experience_id, bool allow)
        {
            bool updated = m_ExperienceService.UpdateExperiencePermissions(avatar_id, experience_id, allow ? ExperiencePermission.Allowed : ExperiencePermission.Blocked);
            if(updated)
            {
                lock (m_ExperiencePermissionsLock)
                {
                    if (!m_ExperiencePermissions.TryGetValue(avatar_id, out Dictionary<UUID, bool> permissions))
                    {
                        permissions = new Dictionary<UUID, bool>();
                        m_ExperiencePermissions[avatar_id] = permissions;
                    }
                    permissions[experience_id] = allow;
                }

                if (!allow)
                {
                    ScenePresence scenePresence;
                    if (m_scene.TryGetScenePresence(avatar_id, out scenePresence))
                    {
                        UpdateScriptExperiencePerms(scenePresence, true);
                    }
                }
            }
            return updated;
        }

        public bool ForgetExperiencePermissions(UUID avatar_id, UUID experience_id)
        {
            bool updated = m_ExperienceService.UpdateExperiencePermissions(
                avatar_id, experience_id, ExperiencePermission.None);
            if (updated)
            {
                lock (m_ExperiencePermissionsLock)
                {
                    if (m_ExperiencePermissions.TryGetValue(avatar_id, out Dictionary<UUID, bool> permissions))
                        permissions.Remove(experience_id);
                }
            }
            return updated;
        }

        public UUID[] GetAllowedExperiences(UUID avatar_id)
        {
            lock (m_ExperiencePermissionsLock)
            {
                if (m_ExperiencePermissions.TryGetValue(avatar_id, out Dictionary<UUID, bool> permissions))
                    return permissions.Where(x => x.Value).Select(x => x.Key).ToArray();
            }
            return Array.Empty<UUID>();
        }

        public UUID[] GetBlockedExperiences(UUID avatar_id)
        {
            lock (m_ExperiencePermissionsLock)
            {
                if (m_ExperiencePermissions.TryGetValue(avatar_id, out Dictionary<UUID, bool> permissions))
                    return permissions.Where(x => !x.Value).Select(x => x.Key).ToArray();
            }
            return Array.Empty<UUID>();
        }

        public UUID[] GetAgentExperiences(UUID agent_id)
        {
            return m_ExperienceService.GetAgentExperiences(agent_id);
        }

        public ExperienceInfo GetExperienceInfo(UUID experience_id, bool fetch)
        {
            if (!fetch && m_ExperienceInfoCache.Contains(experience_id))
            {
                return (ExperienceInfo)m_ExperienceInfoCache[experience_id];
            }

            ExperienceInfo[] infos = m_ExperienceService.GetExperienceInfos(new UUID[] { experience_id });
            if (infos.Length == 1)
            {
                m_ExperienceInfoCache.AddOrUpdate(experience_id, infos[0], CacheTimeout);
                return infos[0];
            }
            else return null;
        }

        public ExperienceInfo[] GetExperienceInfos(UUID[] experience_ids, bool fetch)
        {
            List<ExperienceInfo> infos = new List<ExperienceInfo>();
            List<UUID> missing = new List<UUID>();

            if (!fetch)
            {
                foreach (var key in experience_ids)
                {
                    ExperienceInfo info;
                    if (m_ExperienceInfoCache.TryGetValue(key, out info))
                    {
                        infos.Add(info);
                    }
                    else
                    {
                        missing.Add(key);
                    }
                }
            }
            else missing.AddRange(experience_ids);

            ExperienceInfo[] retrieved = m_ExperienceService.GetExperienceInfos(missing.ToArray());

            foreach(var info in retrieved)
            {
                m_ExperienceInfoCache.AddOrUpdate(info.public_id, info, CacheTimeout);
            }

            infos.AddRange(retrieved);

            return infos.ToArray();
        }

        public bool SetExperiencePermission(UUID avatar_id, UUID experience_id, ExperiencePermission perm)
        {
            if (perm == ExperiencePermission.None)
                return ForgetExperiencePermissions(avatar_id, experience_id);

            return SetExperiencePermissions(
                avatar_id,
                experience_id,
                perm == ExperiencePermission.Allowed);
        }

        public ExperienceInfo[] FindExperiencesByName(string query)
        {
            return m_ExperienceService.FindExperiencesByName(query);
        }

        public UUID[] GetGroupExperiences(UUID group_id)
        {
            return m_ExperienceService.GetGroupExperiences(group_id);
        }

        public ExperienceInfo UpdateExperienceInfo(ExperienceInfo info)
        {
            ExperienceInfo updated = m_ExperienceService.UpdateExperienceInfo(info);
            if(updated != null)
            {
                m_ExperienceInfoCache.AddOrUpdate(updated.public_id, updated, CacheTimeout);
            }
            return updated;
        }

        public UUID[] GetAdminExperiences(UUID agent_id)
        {
            var experiences = new List<UUID>();

            experiences.AddRange(GetAgentExperiences(agent_id));

            List<UUID> groups = new List<UUID>();

            var presence = m_scene.GetScenePresence(agent_id);
            if(presence != null)
            {
                var powers = presence.ControllingClient.GetGroupPowers();
                foreach(var pair in powers)
                {
                    if((pair.Value & (ulong)GroupPowers.ExperienceAdmin) != 0)
                    {
                        groups.Add(pair.Key);
                    }
                }
            }

            if (groups.Count == 0)
                return experiences.ToArray();

            var fetched_groups = m_ExperienceService.GetExperiencesForGroups(groups.ToArray());
            return experiences.Union(fetched_groups).ToArray();
        }

        public UUID[] GetConributorExperiences(UUID agent_id)
        {
            var experiences = new List<UUID>();

            experiences.AddRange(GetAgentExperiences(agent_id));

            List<UUID> groups = new List<UUID>();

            var presence = m_scene.GetScenePresence(agent_id);
            if (presence != null)
            {
                var powers = presence.ControllingClient.GetGroupPowers();
                foreach (var pair in powers)
                {
                    if ((pair.Value & (ulong)GroupPowers.ExperienceCreator) != 0)
                    {
                        groups.Add(pair.Key);
                    }
                }
            }

            if (groups.Count == 0)
                return experiences.ToArray();

            var fetched_groups = m_ExperienceService.GetExperiencesForGroups(groups.ToArray());
            return experiences.Union(fetched_groups).ToArray();
        }

        public bool IsExperienceAdmin(UUID agent_id, UUID experience_id)
        {
            ExperienceInfo info = GetExperienceInfo(experience_id, true);
            if (info == null)
                return false;
            if (info.owner_id == agent_id)
                return true;

            if(info.group_id != UUID.Zero)
            {
                var presence = m_scene.GetScenePresence(agent_id);
                if (presence != null)
                {
                    var powers = presence.ControllingClient.GetGroupPowers(info.group_id);
                    if ((powers & (ulong)GroupPowers.ExperienceAdmin) != 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public bool IsExperienceContributor(UUID agent_id, UUID experience_id)
        {
            ExperienceInfo info = GetExperienceInfo(experience_id, true);
            if (info == null)
                return false;
            if (info.owner_id == agent_id)
                return true;

            if (info.group_id != UUID.Zero)
            {
                var presence = m_scene.GetScenePresence(agent_id);
                if (presence != null)
                {
                    var powers = presence.ControllingClient.GetGroupPowers(info.group_id);
                    if ((powers & (ulong)GroupPowers.ExperienceCreator) != 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public UUID[] GetEstateAllowedExperiences()
        {
            return m_scene.RegionInfo.EstateSettings.AllowedExperiences ?? Array.Empty<UUID>();
        }

        public UUID[] GetEstateKeyExperiences()
        {
            return m_scene.RegionInfo.EstateSettings.KeyExperiences ?? Array.Empty<UUID>();
        }

        public UUID[] GetEstateBlockedExperiences()
        {
            return m_scene.RegionInfo.EstateSettings.BlockedExperiences ?? Array.Empty<UUID>();
        }


        // These need to be added to the existing AccessList enum!
        public const int ACCESS_LIST_ALLOWED = 8;
        public const int ACCESS_LIST_BLOCKED = 0x10;

        private void UpdateScriptExperiencePerms(ScenePresence avatar, bool via_agent)
        {
            var land = m_scene.LandChannel.GetLandObject(avatar.AbsolutePosition);
            if (land == null)
            {
                m_log.WarnFormat(
                    "[EXPERIENCE]: Unable to evaluate parcel Experience policy for avatar {0} at {1}",
                    avatar.UUID,
                    avatar.AbsolutePosition);
                return;
            }

            UUID[] estateAllowed = m_scene.RegionInfo.EstateSettings.AllowedExperiences ?? Array.Empty<UUID>();
            UUID[] estateKey = m_scene.RegionInfo.EstateSettings.KeyExperiences ?? Array.Empty<UUID>();
            UUID[] estateBlocked = m_scene.RegionInfo.EstateSettings.BlockedExperiences ?? Array.Empty<UUID>();
            HashSet<UUID> estateExperiences = new(estateAllowed);
            estateExperiences.UnionWith(estateKey);

            HashSet<UUID> parcelExperiences = new(
                land.LandData.ParcelAccessList
                    .Where(x => (int)x.Flags == ACCESS_LIST_ALLOWED)
                    .Select(x => x.AgentID));

            HashSet<UUID> blockedExperiences = new(estateBlocked);
            blockedExperiences.UnionWith(
                land.LandData.ParcelAccessList
                    .Where(x => (int)x.Flags == ACCESS_LIST_BLOCKED)
                    .Select(x => x.AgentID));

            HashSet<UUID> agentAllowed = new(GetAllowedExperiences(avatar.UUID));

            HashSet<UUID> allowed = new(estateExperiences);
            allowed.UnionWith(parcelExperiences);
            allowed.IntersectWith(agentAllowed);
            allowed.ExceptWith(blockedExperiences);

            m_scene.ForEachSOG(sog =>
            {
                sog.ForEachPart(part =>
                {
                    foreach (TaskInventoryItem item in part.Inventory.GetInventoryItems())
                    {
                        // Todo: fix the enum and make a constant for the perm mask
                        if (item.PermsMask == 408628 && item.PermsGranter == avatar.UUID)
                        {
                            if (!allowed.Contains(item.ExperienceID))
                            {
                                item.PermsGranter = UUID.Zero;
                                item.PermsMask = 0;

                                foreach (var e in m_ScriptModules)
                                {
                                    e.PostScriptEvent(item.ItemID, "experience_permissions_denied", new Object[] {
                                        avatar.UUID.ToString(),
                                        // I've decided to just hard code the ints rather than include Shared.Api.Runtime in LindenCaps
                                        via_agent ? 4 /*ScriptBaseClass.XP_ERROR_NOT_PERMITTED*/ : 17 /*ScriptBaseClass.XP_ERROR_NOT_PERMITTED_LAND*/
                                    });
                                }
                            }
                        }
                    }
                });


                if (sog.IsAttachment && sog.AttachedAvatar == avatar.UUID &&
                    sog.AttachedExperienceID != UUID.Zero)
                {
                    if (!allowed.Contains(sog.AttachedExperienceID))
                    {
                        m_scene.AttachmentsModule.DetachSingleAttachmentToInv(avatar, sog);
                    }
                }
            });
        }

        public bool IsExperienceEnabled(UUID experience_id)
        {
            ExperienceInfo info = GetExperienceInfo(experience_id, false);
            if(info != null)
            {
                return (info.properties & (int)(ExperienceFlags.Disabled | ExperienceFlags.Suspended)) == 0;
            }
            return false;
        }

        public string GetKeyValue(UUID experience, string key)
        {
            return m_ExperienceService.GetKeyValue(experience, key);
        }

        public string CreateKeyValue(UUID experience, string key, string value)
        {
            return m_ExperienceService.CreateKeyValue(experience, key, value);
        }

        public string UpdateKeyValue(UUID experience, string key, string val, bool check, string original)
        {
            return m_ExperienceService.UpdateKeyValue(experience, key, val, check, original);
        }

        public string DeleteKey(UUID experience, string key)
        {
            return m_ExperienceService.DeleteKey(experience, key);
        }

        public int GetKeyCount(UUID experience)
        {
            return m_ExperienceService.GetKeyCount(experience);
        }

        public string[] GetKeys(UUID experience, int start, int count)
        {
            return m_ExperienceService.GetKeys(experience, start, count);
        }

        public int GetSize(UUID experience)
        {
            return m_ExperienceService.GetSize(experience);
        }

        #endregion
    }

    #region Cap HTTP Handlers

    internal static class ExperienceCapsResponse
    {
        public static OSDMap ToExperienceMap(ExperienceInfo info)
        {
            OSDMap metadata = new()
            {
                ["logo"] = info.logo,
                ["marketplace"] = info.marketplace ?? string.Empty
            };

            return new OSDMap
            {
                ["public_id"] = info.public_id,
                ["description"] = info.description ?? string.Empty,
                ["name"] = info.name ?? string.Empty,
                ["quota"] = info.quota,
                ["slurl"] = info.slurl ?? string.Empty,
                ["maturity"] = info.maturity,
                ["expiration"] = 600,
                ["extended_metadata"] = OSDParser.SerializeLLSDXmlString(metadata),
                ["group_id"] = info.group_id,
                ["properties"] = info.properties,
                ["agent_id"] = info.owner_id
            };
        }

        public static byte[] SerializeExperiences(IEnumerable<ExperienceInfo> infos)
        {
            OSDArray experiences = new();
            foreach (ExperienceInfo info in infos)
                experiences.Add(ToExperienceMap(info));

            return OSDParser.SerializeLLSDXmlBytes(new OSDMap
            {
                ["experience_keys"] = experiences
            });
        }
    }

    public class FindExperienceByNameGetHandler : BaseStreamHandler
    {
        //private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private UUID m_AgentID = UUID.Zero;
        private IExperienceModule m_ExperienceModule = null;

        public FindExperienceByNameGetHandler(UUID agent_id, IExperienceModule experienceModule)
            : this(string.Format("/caps/{0}", UUID.Random()), agent_id, experienceModule)
        {
        }

        public FindExperienceByNameGetHandler(string path, UUID agent_id, IExperienceModule experienceModule)
            : base("GET", path, null, null)
        {
            m_AgentID = agent_id;
            m_ExperienceModule = experienceModule;
        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            //m_log.InfoFormat("[EXPERIENCE] FindExperienceByName path = {0}", path);

            NameValueCollection query = HttpUtility.ParseQueryString(httpRequest.Url.Query);

            string page = query.Get("page");
            string page_size = query.Get("page_size");
            string query_str = query.Get("query");

            int pageNumber = 1;
            int pageSize = 30;
            if ((!string.IsNullOrEmpty(page) && !int.TryParse(page, out pageNumber)) ||
                (!string.IsNullOrEmpty(page_size) && !int.TryParse(page_size, out pageSize)) ||
                pageNumber < 1 || pageSize < 1 || pageSize > 100)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return ExperienceCapsResponse.SerializeExperiences(Array.Empty<ExperienceInfo>());
            }

            ExperienceInfo[] results = m_ExperienceModule.FindExperiencesByName(query_str);
            long offset = ((long)pageNumber - 1) * pageSize;
            if (offset >= results.Length)
                return ExperienceCapsResponse.SerializeExperiences(Array.Empty<ExperienceInfo>());

            return ExperienceCapsResponse.SerializeExperiences(results.Skip((int)offset).Take(pageSize));
        }
    }

    public class GroupExperiencesGetHandler : BaseStreamHandler
    {
        //private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private UUID m_AgentID = UUID.Zero;
        private IExperienceModule m_ExperienceModule = null;

        public GroupExperiencesGetHandler(UUID agent_id, IExperienceModule experienceModule)
            : this(string.Format("/caps/{0}", UUID.Random()), agent_id, experienceModule)
        {
        }

        public GroupExperiencesGetHandler(string path, UUID agent_id, IExperienceModule experienceModule)
            : base("GET", path, null, null)
        {
            m_AgentID = agent_id;
            m_ExperienceModule = experienceModule;
        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            string response_str = "<llsd><map><key>experience_ids</key><array>";

            if (httpRequest.Query.ContainsKey("") && UUID.TryParse(httpRequest.Query[""].ToString(), out UUID group_id))
            {
                UUID[] experiences = m_ExperienceModule.GetGroupExperiences(group_id);

                foreach(UUID id in experiences)
                {
                    response_str += string.Format("<uuid>{0}</uuid>", id);
                }
            }

            response_str += "</array></map></llsd>";

            return Encoding.UTF8.GetBytes(response_str);
        }
    }

    public class GetMetadataPostHandler : ReadBaseStreamHandler
    {
        //private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private UUID m_AgentID = UUID.Zero;
        private IExperienceModule m_ExperienceModule = null;
        private Scene m_Scene = null;

        public GetMetadataPostHandler(UUID agent_id, IExperienceModule experienceModule, Scene scene)
            : this(string.Format("/caps/{0}", UUID.Random()),agent_id, experienceModule, scene)
        {

        }

        public GetMetadataPostHandler(string path, UUID agent_id, IExperienceModule experienceModule, Scene scene)
            : base("POST", path)
        {
            m_AgentID = agent_id;
            m_ExperienceModule = experienceModule;
            m_Scene = scene;
        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            byte[] data;
            try
            {
                data = ReadFully(request, httpRequest);
            }
            catch (InvalidDataException)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
                return Encoding.UTF8.GetBytes("<llsd><undef/></llsd>");
            }

            //m_log.InfoFormat("[EXPERIENCE] GetMetadata == {0}", Encoding.UTF8.GetString(data));

            OSDMap map;
            try
            {
                map = OSDParser.DeserializeLLSDXml(data) as OSDMap;
            }
            catch
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return Encoding.UTF8.GetBytes("<llsd><undef/></llsd>");
            }
            if (map == null)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return Encoding.UTF8.GetBytes("<llsd><undef/></llsd>");
            }

            UUID object_id = UUID.Zero;
            UUID item_id = UUID.Zero;

            OSD object_id_osd = null;
            if (map.TryGetValue("object-id", out object_id_osd))
            {
                object_id = object_id_osd.AsUUID();
            }

            OSD item_id_osd = null;
            if(map.TryGetValue("item-id", out item_id_osd))
            {
                item_id = item_id_osd.AsUUID();
            }

            if (object_id == UUID.Zero || item_id == UUID.Zero)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return Encoding.UTF8.GetBytes("<llsd><undef/></llsd>");
            }

            SceneObjectPart scene_object = m_Scene.GetSceneObjectPart(object_id);

            if(scene_object != null)
            {
                if (!m_Scene.Permissions.CanEditObject(scene_object.ParentGroup.UUID, m_AgentID))
                {
                    httpResponse.StatusCode = (int)HttpStatusCode.Forbidden;
                    return Encoding.UTF8.GetBytes("<llsd><undef/></llsd>");
                }

                TaskInventoryItem inv_item = scene_object.Inventory.GetInventoryItem(item_id);

                if(inv_item != null)
                {
                    string response_str = "<llsd><map>";

                    // todo: iterate over fields and add the requested ones
                    if (inv_item.ExperienceID != UUID.Zero)
                    {
                        response_str += string.Format("<key>experience</key><uuid>{0}</uuid>", inv_item.ExperienceID);
                    }
                    response_str += "</map></llsd>";

                    return Encoding.UTF8.GetBytes(response_str);
                }
            }

            return Encoding.UTF8.GetBytes("<llsd><undef/></llsd>");
        }
    }

    public class UpdateExperiencePostHandler : ReadBaseStreamHandler
    {
        //private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private UUID m_AgentID = UUID.Zero;
        private IExperienceModule m_ExperienceModule = null;
        
        public UpdateExperiencePostHandler(UUID agent_id, IExperienceModule experienceModule)
            : this(string.Format("/caps/{0}", UUID.Random()), agent_id, experienceModule)
        {

        }

        public UpdateExperiencePostHandler(string path, UUID agent_id, IExperienceModule experienceModule)
            : base("POST", path)
        {
            m_AgentID = agent_id;
            m_ExperienceModule = experienceModule;
        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            byte[] read;
            OSDMap experience;
            try
            {
                read = ReadFully(request, httpRequest);
                experience = OSDParser.Deserialize(read) as OSDMap;
            }
            catch (InvalidDataException)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
                return Encoding.UTF8.GetBytes("<llsd><undef/></llsd>");
            }
            catch
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return Encoding.UTF8.GetBytes("<llsd><undef/></llsd>");
            }

            if (experience == null || !experience.ContainsKey("public_id") ||
                !experience.ContainsKey("group_id") || !experience.ContainsKey("name") ||
                !experience.ContainsKey("description") || !experience.ContainsKey("slurl") ||
                !experience.ContainsKey("extended_metadata") || !experience.ContainsKey("maturity") ||
                !experience.ContainsKey("properties"))
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return Encoding.UTF8.GetBytes("<llsd><undef/></llsd>");
            }

            UUID public_id = experience["public_id"].AsUUID();
            UUID group_id = experience["group_id"].AsUUID();
            string name = experience["name"].AsString();
            string desc = experience["description"].AsString();
            string slurl = experience["slurl"].AsString();
            string metadata = experience["extended_metadata"].AsString();
            int maturity = experience["maturity"].AsInteger();
            int properties = experience["properties"].AsInteger();

            if (name.Length > 42 || desc.Length > 128 || slurl.Length > 256 ||
                metadata.Length > 16 * 1024)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return Encoding.UTF8.GetBytes("<llsd><undef/></llsd>");
            }

            // 42 = adult, 21 = mature, 13 = general
            if (maturity != 42 && maturity != 13)
                maturity = 21;

            string decoded_meta = HttpUtility.HtmlDecode(metadata);

            OSDMap extended;
            try
            {
                extended = OSDParser.Deserialize(decoded_meta) as OSDMap;
            }
            catch
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return Encoding.UTF8.GetBytes("<llsd><undef/></llsd>");
            }
            if (extended == null || !extended.ContainsKey("logo") || !extended.ContainsKey("marketplace"))
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return Encoding.UTF8.GetBytes("<llsd><undef/></llsd>");
            }

            UUID logo = extended["logo"].AsUUID();
            string marketplace = extended["marketplace"].AsString();
            if (marketplace.Length > 256)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return Encoding.UTF8.GetBytes("<llsd><undef/></llsd>");
            }

            ExperienceInfo currentInfo = m_ExperienceModule.GetExperienceInfo(public_id);

            if (currentInfo == null)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                return Encoding.UTF8.GetBytes("<llsd><undef/></llsd>");
            }

            if (!m_ExperienceModule.IsExperienceAdmin(m_AgentID, public_id) ||
                group_id != currentInfo.group_id)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.Forbidden;
                return Encoding.UTF8.GetBytes("<llsd><undef/></llsd>");
            }

            int updatedProperties = currentInfo.properties;
            if ((properties & (int)ExperienceFlags.Disabled) != 0)
                updatedProperties |= (int)ExperienceFlags.Disabled;
            else
                updatedProperties &= ~(int)ExperienceFlags.Disabled;

            ExperienceInfo requestedInfo = new()
            {
                public_id = currentInfo.public_id,
                owner_id = currentInfo.owner_id,
                group_id = currentInfo.group_id,
                name = name,
                description = desc,
                slurl = slurl == "last" ? currentInfo.slurl : slurl,
                marketplace = marketplace,
                logo = logo,
                maturity = maturity,
                properties = updatedProperties,
                quota = currentInfo.quota
            };

            ExperienceInfo updatedInfo = m_ExperienceModule.UpdateExperienceInfo(requestedInfo);
            if (updatedInfo == null)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                return Encoding.UTF8.GetBytes("<llsd><undef/></llsd>");
            }

            return ExperienceCapsResponse.SerializeExperiences(new[] { updatedInfo });
        }
    }

    public class IsExperienceContributorGetHandler : BaseStreamHandler
    {
        //private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private UUID m_AgentID = UUID.Zero;
        private IExperienceModule m_ExperienceModule = null;
        
        public IsExperienceContributorGetHandler(UUID agent_id, IExperienceModule experienceModule)
            : this(string.Format("/caps/{0}", UUID.Random()), agent_id, experienceModule)
        {
        }

        public IsExperienceContributorGetHandler(string path, UUID agent_id, IExperienceModule experienceModule)
            : base("GET", path, null, null)
        {
            m_AgentID = agent_id;
            m_ExperienceModule = experienceModule;
        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            bool is_contributor = false;
            
            if (httpRequest.Query.ContainsKey("experience_id"))
            {
                UUID experience_id;
                if(UUID.TryParse(httpRequest.Query["experience_id"].ToString(), out experience_id))
                {
                    is_contributor = m_ExperienceModule.IsExperienceContributor(m_AgentID, experience_id);
                }
            }

            string response_str = "<?xml version=\"1.0\" ?><llsd><map><key>status</key><boolean>" + (is_contributor ? "true" : "false") + "</boolean></map></llsd>";

            return Encoding.UTF8.GetBytes(response_str);
        }
    }

    public class IsExperienceAdminGetHandler : BaseStreamHandler
    {
        private UUID m_AgentID = UUID.Zero;
        private IExperienceModule m_ExperienceModule = null;

        public IsExperienceAdminGetHandler(UUID agent_id, IExperienceModule experienceModule)
            : this(string.Format("/caps/{0}", UUID.Random()), agent_id, experienceModule)
        {
        }

        public IsExperienceAdminGetHandler(string path, UUID agent_id, IExperienceModule experienceModule)
            : base("GET", path, null, null)
        {
            m_AgentID = agent_id;
            m_ExperienceModule = experienceModule;
        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            bool is_admin = false;

            if (httpRequest.Query.ContainsKey("experience_id"))
            {
                UUID experience_id;
                if (UUID.TryParse(httpRequest.Query["experience_id"].ToString(), out experience_id))
                {
                    is_admin = m_ExperienceModule.IsExperienceAdmin(m_AgentID, experience_id);
                }
            }

            string response_str = "<?xml version=\"1.0\" ?><llsd><map><key>status</key><boolean>" + (is_admin ? "true" : "false") + "</boolean></map></llsd>";

            return Encoding.UTF8.GetBytes(response_str);
        }
    }

    public class RegionExperiencesGetHandler : BaseStreamHandler
    {
        //private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private UUID m_AgentID = UUID.Zero;
        private IExperienceModule m_ExperienceModule = null;

        public RegionExperiencesGetHandler(UUID agent_id, IExperienceModule experienceModule)
            : this(string.Format("/caps/{0}", UUID.Random()), agent_id, experienceModule)
        {
        }

        public RegionExperiencesGetHandler(string path, UUID agent_id, IExperienceModule experienceModule)
            : base("GET", path, null, null)
        {
            m_AgentID = agent_id;
            m_ExperienceModule = experienceModule;
        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            //m_log.InfoFormat("[EXPERIENCE] RegionExperiences request on {0}", path);

            UUID[] allowed = m_ExperienceModule.GetEstateAllowedExperiences();
            UUID[] key = m_ExperienceModule.GetEstateKeyExperiences();
            UUID[] blocked = m_ExperienceModule.GetEstateBlockedExperiences();

            string response_str = "<llsd><map><key>allowed</key>";
            if (allowed.Length > 0)
            {
                response_str += "<array>";
                foreach (UUID id in allowed)
                {
                    response_str += string.Format("<uuid>{0}</uuid>", id);
                }
                response_str += "</array>";
            }
            else response_str += "<undef />";

            response_str += "<key>blocked</key>";
            if (blocked.Length > 0)
            {
                response_str += "<array>";
                foreach (UUID id in blocked)
                    response_str += string.Format("<uuid>{0}</uuid>", id);
                response_str += "</array>";
            }
            else response_str += "<array />";

            response_str += "<key>default</key><uuid /><key>disabled</key><array /><key>trusted</key>";

            if (key.Length > 0)
            {
                response_str += "<array>";
                foreach (UUID id in key)
                {
                    response_str += string.Format("<uuid>{0}</uuid>", id);
                }
                response_str += "</array>";
            }
            else response_str += "<undef />";

            response_str += "</map></llsd>";

            return Encoding.UTF8.GetBytes(response_str);
        }
    }

    public class GetExperienceInfoGetHandler : BaseStreamHandler
    {
        //private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private UUID m_AgentID = UUID.Zero;
        private IExperienceModule m_ExperienceModule = null;
        public GetExperienceInfoGetHandler(UUID agent_id, IExperienceModule experienceModule)
            : this(string.Format("/caps/{0}", UUID.Random()), agent_id, experienceModule)
        {
        }

        public GetExperienceInfoGetHandler(string path, UUID agent_id, IExperienceModule experienceModule)
            : base("GET", path, null, null)
        {
            m_AgentID = agent_id;
            m_ExperienceModule = experienceModule;
        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            //m_log.InfoFormat("[EXPERIENCE] GetExperienceInfo request on {0}", path);

            NameValueCollection query = HttpUtility.ParseQueryString(httpRequest.Url.Query);
            string[] ids = query.GetValues("public_id");
            //m_log.InfoFormat("[EXPERIENCE] GetExperienceInfo public_ids = {0}", string.Join(", ", ids));

            if (ids == null)
                return ExperienceCapsResponse.SerializeExperiences(Array.Empty<ExperienceInfo>());
            if (ids.Length > 1000)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return ExperienceCapsResponse.SerializeExperiences(Array.Empty<ExperienceInfo>());
            }

            HashSet<UUID> experienceIDs = new();

            foreach (string id in ids)
            {
                if (UUID.TryParse(id, out UUID experienceID) && experienceID != UUID.Zero)
                    experienceIDs.Add(experienceID);
            }

            ExperienceInfo[] infos = m_ExperienceModule.GetExperienceInfos(experienceIDs.ToArray());
            return ExperienceCapsResponse.SerializeExperiences(infos);
        }
    }

    public class GetCreatorExperiencesGetHandler : BaseStreamHandler
    {
        //private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private UUID m_AgentID = UUID.Zero;
        private IExperienceModule m_ExperienceModule = null;

        public GetCreatorExperiencesGetHandler(UUID agent_id, IExperienceModule experienceModule)
            : this(string.Format("/caps/{0}", UUID.Random()), agent_id, experienceModule)
        {
        }

        public GetCreatorExperiencesGetHandler(string path, UUID agent_id, IExperienceModule experienceModule)
            : base("GET", path, null, null)
        {
            m_AgentID = agent_id;
            m_ExperienceModule = experienceModule;
        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            //m_log.InfoFormat("[EXPERIENCE] GetCreatorExperiences request on {0}", path);

            string response_str = "<llsd><map><key>experience_ids</key>";

            UUID[] agent_experiences = m_ExperienceModule.GetConributorExperiences(m_AgentID);

            response_str += "<array>";

            foreach (UUID id in agent_experiences)
                response_str += string.Format("<uuid>{0}</uuid>", id);

            response_str += "</array>";

            response_str += "</map></llsd>";

            return Encoding.UTF8.GetBytes(response_str);
        }
    }

    public class GetAdminExperiencesGetHandler : BaseStreamHandler
    {
        //private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private UUID m_AgentID = UUID.Zero;
        private IExperienceModule m_ExperienceModule = null;

        public GetAdminExperiencesGetHandler(UUID agent_id, IExperienceModule experienceModule)
            : this(string.Format("/caps/{0}", UUID.Random()), agent_id, experienceModule)
        {
        }

        public GetAdminExperiencesGetHandler(string path, UUID agent_id, IExperienceModule experienceModule)
            : base("GET", path, null, null)
        {
            m_AgentID = agent_id;
            m_ExperienceModule = experienceModule;
        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            //m_log.InfoFormat("[EXPERIENCE] GetAdminExperiences request on {0}", path);

            string response_str = "<llsd><map><key>experience_ids</key>";

            UUID[] agent_experiences = m_ExperienceModule.GetAdminExperiences(m_AgentID);

            response_str += "<array>";

            foreach (UUID id in agent_experiences)
                response_str += string.Format("<uuid>{0}</uuid>", id);

            response_str += "</array>";

            response_str += "</map></llsd>";

            return Encoding.UTF8.GetBytes(response_str);
        }
    }

    public class AgentExperiencesGetHandler : BaseStreamHandler
    {
        //private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private UUID m_AgentID = UUID.Zero;
        private IExperienceModule m_ExperienceModule = null;

        public AgentExperiencesGetHandler(UUID agent_id, IExperienceModule experienceModule)
            : this(string.Format("/caps/{0}", UUID.Random()), agent_id, experienceModule)
        {
        }

        public AgentExperiencesGetHandler(string path, UUID agent_id, IExperienceModule experienceModule)
            : base("GET", path, null, null)
        {
            m_AgentID = agent_id;
            m_ExperienceModule = experienceModule;
        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            //m_log.InfoFormat("[EXPERIENCE] AgentExperiences request on {0}", path);

            string response_str = "<llsd><map><key>experience_ids</key>";

            UUID[] agent_experiences = m_ExperienceModule.GetAgentExperiences(m_AgentID);

            response_str += "<array>";

            foreach (UUID id in agent_experiences)
                response_str += string.Format("<uuid>{0}</uuid>", id);

            response_str += "</array>";

            response_str += "</map></llsd>";

            return Encoding.UTF8.GetBytes(response_str);
        }
    }

    public class GetExperiencesGetHandler : BaseStreamHandler
    {
        //private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private UUID m_AgentID = UUID.Zero;
        private IExperienceModule m_ExperienceModule = null;

        public GetExperiencesGetHandler(UUID agent_id, IExperienceModule experienceModule)
            : this(string.Format("/caps/{0}", UUID.Random()), agent_id, experienceModule)
        {
        }

        public GetExperiencesGetHandler(string path, UUID agent_id, IExperienceModule experienceModule)
            : base("GET", path, null, null)
        {
            m_AgentID = agent_id;
            m_ExperienceModule = experienceModule;
        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            //m_log.InfoFormat("[EXPERIENCE] GetExperiences request on {0}", path);

            string response_str = "<llsd><map><key>blocked</key>";

            UUID[] allowed = m_ExperienceModule.GetAllowedExperiences(m_AgentID);
            UUID[] blocked = m_ExperienceModule.GetBlockedExperiences(m_AgentID);

            response_str += "<array>";

            foreach (UUID id in blocked)
                response_str += string.Format("<uuid>{0}</uuid>", id);

            response_str += "</array>";

            response_str += "<key>experiences</key>";

            response_str += "<array>";

            foreach (UUID id in allowed)
                response_str += string.Format("<uuid>{0}</uuid>", id);

            response_str += "</array>";

            response_str += "</map></llsd>";

            return Encoding.UTF8.GetBytes(response_str);
        }
    }

    #endregion

    public class ReadBaseStreamHandler : BaseStreamHandler
    {
        public ReadBaseStreamHandler(string method, string url) : base(method, url, null, null)
        {
        }

        internal static byte[] ReadFully(Stream stream, int maximumBytes)
        {
            byte[] buffer = new byte[1024];
            using (MemoryStream ms = new MemoryStream(1024 * 256))
            {
                while (true)
                {
                    int read = stream.Read(buffer, 0, buffer.Length);

                    if (read <= 0)
                    {
                        return ms.ToArray();
                    }

                    if (ms.Length + read > maximumBytes)
                        throw new InvalidDataException("Experience request body is too large.");

                    ms.Write(buffer, 0, read);
                }
            }
        }

        protected static byte[] ReadFully(Stream stream, IOSHttpRequest request)
        {
            const int maximumBytes = 256 * 1024;
            if (request != null && request.ContentLength64 > maximumBytes)
                throw new InvalidDataException("Experience request body is too large.");
            return ReadFully(stream, maximumBytes);
        }
    }
}
