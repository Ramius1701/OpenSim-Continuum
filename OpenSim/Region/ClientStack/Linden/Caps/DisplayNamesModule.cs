using log4net;
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Timers;

namespace OpenSim.Region.ClientStack.Linden
{
    // Display Names region module, ported from Mobius (treated as a design
    // reference - see the "Display Names" commit messages). Caches NameInfo
    // per agent and backs GetDisplayNames/SetDisplayName for BunchOfCaps.
    // HG visitors get their display name federated from their home grid via
    // GetUserDatas -> GridUserService -> DisplayNameServiceConnector,
    // refreshed periodically (see [GridUserService] FetchDisplayNames /
    // DisplayNamesCacheExpirationInHours).
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "DisplayNames")]
    public class DisplayNamesModule : ISharedRegionModule, IDisplayNamesModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private bool enabled = false;

        private IUserManagement m_UserManager = null;
        private IUserAccountService m_UserAccountService = null;

        private readonly Dictionary<UUID, NameInfo> m_DisplayNameCache = new Dictionary<UUID, NameInfo>();

        private Timer mCacheTimer = null;

        private Scene m_Scene = null;

        #region IRegionModuleBase implementation

        public void Initialise(IConfigSource config)
        {
            IConfig cnf = config.Configs["DisplayNames"];
            if (cnf is null || cnf.GetString("Enabled", "false") != "true")
            {
                enabled = false;
                return;
            }

            enabled = true;
            m_log.Info("[DisplayNames]: Plugin enabled!");

            mCacheTimer = new Timer
            {
                AutoReset = true,
                Interval = 5 * 60 * 1000
            };
            mCacheTimer.Elapsed += MCacheTimer_Elapsed;
        }

        private void MCacheTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            List<UUID> expired = new List<UUID>();

            foreach (KeyValuePair<UUID, NameInfo> pair in m_DisplayNameCache)
            {
                if (m_Scene.GetScenePresence(pair.Key) is not null)
                    continue;
                if (pair.Value.TimeCached.AddMinutes(10) < DateTime.Now)
                    expired.Add(pair.Key);
            }

            foreach (UUID key in expired)
                m_DisplayNameCache.Remove(key);
        }

        public void AddRegion(Scene scene)
        {
            if (!enabled)
                return;

            scene.EventManager.OnNewClient += (client) =>
            {
                ScenePresence sp = scene.GetScenePresence(client.AgentId);
                if (sp is not null && sp.PresenceType != PresenceType.Npc)
                {
                    m_UserManager.RemoveUser(client.AgentId);
                    m_DisplayNameCache.Remove(client.AgentId);
                }
            };
        }

        public void RegionLoaded(Scene scene)
        {
            if (!enabled)
                return;

            m_Scene = scene;

            m_UserManager = scene.RequestModuleInterface<IUserManagement>();
            if (m_UserManager is null)
                return;

            m_UserAccountService = scene.RequestModuleInterface<IUserAccountService>();
            if (m_UserAccountService is null)
                return;

            scene.RegisterModuleInterface<IDisplayNamesModule>(this);
        }

        public void RemoveRegion(Scene scene)
        {
            if (!enabled)
                return;

            scene.UnregisterModuleInterface<IDisplayNamesModule>(this);
        }

        public void PostInitialise()
        {
            if (enabled)
                mCacheTimer.Start();
        }

        public string Name
        {
            get { return "DisplayNamesModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void Close()
        {
        }

        #endregion

        public Dictionary<UUID, NameInfo> GetCachedDisplayNames(ref string[] ids)
        {
            Dictionary<UUID, NameInfo> result = new Dictionary<UUID, NameInfo>();
            List<string> id_list = new List<string>(ids);

            foreach (string key in ids)
            {
                if (UUID.TryParse(key, out UUID uuid) && m_DisplayNameCache.TryGetValue(uuid, out NameInfo info))
                {
                    result[uuid] = info;
                    id_list.Remove(key);
                }
            }

            ids = id_list.ToArray();
            return result;
        }

        public Dictionary<UUID, NameInfo> GetDisplayNames(string[] ids)
        {
            Dictionary<UUID, NameInfo> result = GetCachedDisplayNames(ref ids);
            if (ids.Length == 0)
                return result;

            Dictionary<UUID, UserData> names = m_UserManager.GetUserDatas(ids, UUID.Zero, true);
            if (names.Count == 0)
                return result;

            foreach (KeyValuePair<UUID, UserData> kvp in names)
            {
                if (kvp.Value is null || kvp.Key.IsZero() || kvp.Value.IsUnknownUser)
                    continue;

                if (m_DisplayNameCache.TryGetValue(kvp.Key, out NameInfo cached))
                {
                    result[kvp.Key] = cached;
                    continue;
                }

                UserData userdata = kvp.Value;
                NameInfo nameInfo = new NameInfo
                {
                    FirstName = userdata.FirstName,
                    LastName = userdata.LastName,
                    DisplayName = userdata.DisplayName,
                    HomeURI = userdata.HomeURL,
                    NameChanged = userdata.NameChanged
                };

                result[kvp.Key] = nameInfo;
                m_DisplayNameCache[kvp.Key] = nameInfo;
            }

            return result;
        }

        public bool SetDisplayName(UUID agentID, string displayName, out NameInfo nameInfo)
        {
            Dictionary<UUID, NameInfo> names = GetDisplayNames(new string[] { agentID.ToString() });

            if (names.TryGetValue(agentID, out NameInfo name_info))
            {
                if (m_UserAccountService.SetDisplayName(agentID, displayName))
                {
                    name_info.DisplayName = displayName;
                    name_info.NameChanged = DateTime.UtcNow;

                    m_UserManager.RemoveUser(agentID);
                    m_DisplayNameCache[agentID] = name_info;

                    nameInfo = name_info;
                    return true;
                }
            }

            nameInfo = null;
            return false;
        }

        public string GetCachedDisplayName(string id)
        {
            if (UUID.TryParse(id, out UUID agentID) && m_DisplayNameCache.TryGetValue(agentID, out NameInfo nameInfo))
                return nameInfo.IsDefault ? nameInfo.Name : nameInfo.DisplayName;
            return string.Empty;
        }

        public string GetDisplayName(string id)
        {
            if (!UUID.TryParse(id, out UUID agentID))
                return string.Empty;

            var res = GetDisplayNames(new string[] { id });
            if (res.TryGetValue(agentID, out NameInfo nameInfo))
                return nameInfo.IsDefault ? nameInfo.Name : nameInfo.DisplayName;

            return string.Empty;
        }
    }
}
