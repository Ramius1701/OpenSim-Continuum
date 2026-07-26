/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Security.Cryptography;
using System.Text;
using System.Reflection;
using System.Threading;
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.Region.OptionalModules.Avatar.GroupAutoInvite
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "GroupAutoInviteModule")]
    public class GroupAutoInviteModule : INonSharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Scene m_scene;
        private IGroupsModule m_groupsModule;
        private bool m_enabled;
        private UUID m_groupID = UUID.Zero;
        private string m_groupName = string.Empty;
        private UUID m_inviterID = UUID.Zero;
        private UUID m_roleID = UUID.Zero;
        private int m_inviteDelaySeconds = 10;
        private bool m_inviteOncePerSession = true;
        private string m_inviteMessage = "Welcome to {RegionName}, {AvatarName}! Join the {GroupName} group for news and updates.";

        public string Name { get { return "Group Auto Invite Module"; } }

        public Type ReplaceableInterface { get { return null; } }

        public void Initialise(IConfigSource source)
        {
            IConfig config = source.Configs["GroupAutoInvite"];
            if (config == null)
                return;

            m_enabled = config.GetBoolean("Enabled", false);
            UUID.TryParse(config.GetString("GroupID", string.Empty), out m_groupID);
            m_groupName = config.GetString("GroupName", string.Empty).Trim();
            UUID.TryParse(config.GetString("InviterID", string.Empty), out m_inviterID);
            UUID.TryParse(config.GetString("RoleID", string.Empty), out m_roleID);
            m_inviteDelaySeconds = Math.Max(0, config.GetInt("InviteDelaySeconds", 10));
            m_inviteOncePerSession = config.GetBoolean("InviteOncePerSession", true);
            m_inviteMessage = config.GetString("InviteMessage", m_inviteMessage).Trim();
        }

        public void AddRegion(Scene scene)
        {
            if (!m_enabled)
                return;

            m_scene = scene;
            m_scene.EventManager.OnMakeRootAgent += OnMakeRootAgent;
        }

        public void RemoveRegion(Scene scene)
        {
            if (!m_enabled || m_scene == null)
                return;

            m_scene.EventManager.OnMakeRootAgent -= OnMakeRootAgent;
            m_groupsModule = null;
            m_scene = null;
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_enabled)
                return;

            m_groupsModule = scene.RequestModuleInterface<IGroupsModule>();
            if (m_groupsModule == null)
                m_log.WarnFormat("[GROUP AUTO INVITE]: Groups module is not available in {0}.", scene.RegionInfo.RegionName);
        }

        public void Close()
        {
            m_groupsModule = null;
            m_scene = null;
        }

        private void OnMakeRootAgent(ScenePresence sp)
        {
            if (!m_enabled || sp == null || sp.IsDeleted || sp.IsNPC || sp.IsChildAgent || sp.ControllingClient == null)
                return;

            UUID sessionID = sp.ControllingClient.SessionId;
            if (sessionID.IsZero())
                return;

            Util.FireAndForget(
                o =>
                {
                    if (m_inviteDelaySeconds > 0)
                        Thread.Sleep(m_inviteDelaySeconds * 1000);

                    TryInvite(sp.UUID, sessionID);
                },
                null,
                "GroupAutoInvite",
                false);
        }

        private void TryInvite(UUID agentID, UUID sessionID)
        {
            Scene scene = m_scene;
            if (scene == null)
                return;

            ScenePresence sp = scene.GetScenePresence(agentID);
            if (sp == null || sp.IsDeleted || sp.IsNPC || sp.IsChildAgent || sp.ControllingClient == null)
                return;

            // The delayed task must still belong to the same viewer login.
            if (sp.ControllingClient.SessionId != sessionID)
                return;

            IGroupsModule groups = m_groupsModule ?? scene.RequestModuleInterface<IGroupsModule>();
            if (groups == null)
                return;

            try
            {
                GroupRecord group = ResolveGroup(groups);
                if (group == null || group.GroupID.IsZero())
                {
                    m_log.WarnFormat("[GROUP AUTO INVITE]: Target group not found in {0}.", scene.RegionInfo.RegionName);
                    return;
                }

                if (groups.GetMembershipData(group.GroupID, agentID) != null)
                    return;

                UUID inviterID = m_inviterID.IsZero() ? group.FounderID : m_inviterID;
                if (inviterID.IsZero())
                {
                    m_log.WarnFormat(
                        "[GROUP AUTO INVITE]: Cannot invite {0} to {1}; no InviterID configured and group founder is unknown.",
                        sp.Name, group.GroupName);
                    return;
                }

                string inviteMessage = FormatInviteMessage(sp, group);
                UUID inviteID = m_inviteOncePerSession
                    ? CreateSessionInviteID(agentID, group.GroupID, sessionID)
                    : UUID.Random();

                if (!groups.InviteGroup(
                    null,
                    inviterID,
                    group.GroupID,
                    agentID,
                    m_roleID,
                    inviteMessage,
                    inviteID))
                {
                    return;
                }

                m_log.InfoFormat(
                    "[GROUP AUTO INVITE]: Invited {0} to group {1} ({2}) in {3} with message '{4}'.",
                    sp.Name, group.GroupName, group.GroupID, scene.RegionInfo.RegionName, inviteMessage);
            }
            catch (Exception e)
            {
                m_log.WarnFormat(
                    "[GROUP AUTO INVITE]: Failed to invite {0} in {1}: {2}",
                    sp.Name, scene.RegionInfo.RegionName, e.Message);
            }
        }

        private static UUID CreateSessionInviteID(UUID agentID, UUID groupID, UUID sessionID)
        {
            string seed = string.Concat(
                agentID.ToString(), "|",
                groupID.ToString(), "|",
                sessionID.ToString());

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
            string hex = Convert.ToHexString(hash).ToLowerInvariant();

            // a17a0001 identifies a GroupAutoInvite per-login marker.
            return new UUID(string.Format(
                "a17a0001-{0}-{1}-{2}-{3}",
                hex.Substring(0, 4),
                hex.Substring(4, 4),
                hex.Substring(8, 4),
                hex.Substring(12, 12)));
        }
        private GroupRecord ResolveGroup(IGroupsModule groups)
        {
            if (!m_groupID.IsZero())
                return groups.GetGroupRecord(m_groupID);

            if (!string.IsNullOrEmpty(m_groupName))
                return groups.GetGroupRecord(m_groupName);

            return null;
        }

        private string FormatInviteMessage(ScenePresence sp, GroupRecord group)
        {
            if (string.IsNullOrEmpty(m_inviteMessage))
                return null;

            string regionName = m_scene == null ? string.Empty : m_scene.RegionInfo.RegionName;
            return m_inviteMessage
                .Replace("{AvatarName}", sp.Name)
                .Replace("{GroupName}", group.GroupName)
                .Replace("{RegionName}", regionName);
        }
    }
}
