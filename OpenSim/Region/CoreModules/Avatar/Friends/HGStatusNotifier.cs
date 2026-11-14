using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Services.Interfaces;
using OpenSim.Services.Connectors.Hypergrid;
using FriendInfo = OpenSim.Services.Interfaces.FriendInfo;

using OpenMetaverse;

using log4net;

namespace OpenSim.Region.CoreModules.Avatar.Friends
{
    public class HGStatusNotifier
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private HGFriendsModule m_FriendsModule;

        public HGStatusNotifier(HGFriendsModule friendsModule)
        {
            m_FriendsModule = friendsModule;
        }

        public void Notify(UUID userID, Dictionary<string, List<FriendInfo>> friendsPerDomain, bool online)
        {
            if(m_FriendsModule is null)
                return;

            // lickx: statusnotification takes UUIDs, not universal user identifiers.
            // Resolve every endpoint rather than assuming all friends share the first one's URL.
            Dictionary<string, List<string>> targets = GetNotificationTargets(
                m_FriendsModule.UserManagementModule, friendsPerDomain.Values.SelectMany(f => f));
            foreach (KeyValuePair<string, List<string>> target in targets)
            {
                try
                {
                    HGFriendsServicesConnector connector = new(target.Key);
                    List<UUID> friendsOnline = connector.StatusNotification(target.Value, userID, online);
                    friendsOnline = friendsOnline.Where(id => target.Value.Contains(id.ToString())).Distinct().ToList();
                    if (friendsOnline.Count > 0)
                    {
                        IClientAPI client = m_FriendsModule.LocateClientObject(userID);
                        if (client != null)
                        {
                            m_FriendsModule.CacheFriendsOnline(userID, friendsOnline, online);
                            if (online)
                                client.SendAgentOnline(friendsOnline.ToArray());
                            else
                                client.SendAgentOffline(friendsOnline.ToArray());
                        }
                    }
                }
                catch (Exception e)
                {
                    m_log.WarnFormat("[HG STATUS NOTIFIER]: Notification failed: {0}", e.Message);
                }
            }
        }

        private static Dictionary<string, List<string>> GetNotificationTargets(
            IUserManagement users, IEnumerable<FriendInfo> friends)
        {
            Dictionary<string, List<string>> targets = new();
            foreach (FriendInfo friend in friends)
            {
                if (friend == null || !Util.ParseUniversalUserIdentifier(friend.Friend, out UUID friendID)
                    || friendID.IsZero())
                    continue;
                string endpoint;
                try { endpoint = users.GetUserServerURL(friendID, "FriendsServerURI"); }
                catch { continue; }
                if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    continue;
                string key = uri.AbsoluteUri.TrimEnd('/');
                if (!targets.TryGetValue(key, out List<string> ids))
                    targets[key] = ids = new List<string>();
                if (!ids.Contains(friendID.ToString()))
                    ids.Add(friendID.ToString());
            }
            return targets;
        }
    }
}
