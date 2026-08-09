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
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Client;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using Mono.Addins;

using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;

namespace OpenSim.Region.CoreModules.Avatar.InstantMessage
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "MuteListModule")]
    public class MuteListModule : ISharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        protected bool m_Enabled = false;
        protected readonly List<Scene> m_SceneList = new List<Scene>();

        public void Initialise(IConfigSource config)
        {
            IConfig cnf = config.Configs["Messaging"];
            if (cnf == null)
                return;

            if (cnf.GetString("MuteListModule", "None") != "MuteListModule")
                return;

            m_Enabled = true;
        }

        public void AddRegion(Scene scene)
        {
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;

            IXfer xfer = scene.RequestModuleInterface<IXfer>();
            if (xfer == null)
            {
                m_log.ErrorFormat("[MuteListModule]: Xfer not available in region {0}. Module Disabled", scene.Name);
                return;
            }

            IMuteListService srv = scene.RequestModuleInterface<IMuteListService>();
            if(srv == null)
            {
                m_log.ErrorFormat("[MuteListModule]: MuteListService not available in region {0}. Module Disabled", scene.Name);
                return;
            }

            bool added;
            lock (m_SceneList)
            {
                added = !m_SceneList.Contains(scene);
                if (added)
                    m_SceneList.Add(scene);
            }
            if (added)
                scene.EventManager.OnNewClient += OnNewClient;
        }

        public void RemoveRegion(Scene scene)
        {
            bool removed;
            lock (m_SceneList)
            {
                removed = m_SceneList.Remove(scene);
            }
            if (!removed)
                return;

            scene.EventManager.OnNewClient -= OnNewClient;
            scene.ForEachClient(UnsubscribeClient);
        }

        public void PostInitialise()
        {
            if (!m_Enabled)
                return;

            m_log.Debug("[MuteListModule]: enabled");
        }

        public string Name
        {
            get { return "MuteListModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void Close()
        {
            Scene[] scenes;
            lock (m_SceneList)
                scenes = m_SceneList.ToArray();
            foreach (Scene scene in scenes)
                RemoveRegion(scene);
        }

        private bool IsForeign(IClientAPI client)
        {
            IUserManagement userManagement = client.Scene.RequestModuleInterface<IUserManagement>();
            if(userManagement == null)
                return false; // we can't check

            return !userManagement.IsLocalGridUser(client.AgentId);
        }

        private void OnNewClient(IClientAPI client)
        {
            client.OnMuteListRequest += OnMuteListRequest;
            client.OnUpdateMuteListEntry += OnUpdateMuteListEntry;
            client.OnRemoveMuteListEntry += OnRemoveMuteListEntry;
        }

        private void UnsubscribeClient(IClientAPI client)
        {
            client.OnMuteListRequest -= OnMuteListRequest;
            client.OnUpdateMuteListEntry -= OnUpdateMuteListEntry;
            client.OnRemoveMuteListEntry -= OnRemoveMuteListEntry;
        }

        private void OnMuteListRequest(IClientAPI client, uint crc)
        {
            if (!m_Enabled || IsForeign(client))
            {
                if(crc == 0)
                    client.SendEmpytMuteList();
                else
                    client.SendUseCachedMuteList();
                return;
            }

            IXfer xfer = client.Scene.RequestModuleInterface<IXfer>();
            if (xfer == null)
            {
                if(crc == 0)
                    client.SendEmpytMuteList();
                else
                    client.SendUseCachedMuteList();
                return;
            }

            IMuteListService service = client.Scene.RequestModuleInterface<IMuteListService>();
            if (service == null)
            {
                SendCachedOrEmpty(client, crc);
                return;
            }

            Byte[] data;
            try
            {
                data = service.MuteListRequest(client.AgentId, crc);
            }
            catch (Exception ex)
            {
                m_log.WarnFormat("[MuteListModule]: Failed to retrieve mutes for {0}: {1}", client.AgentId, ex.Message);
                SendCachedOrEmpty(client, crc);
                return;
            }
            if (data == null)
            {
                SendCachedOrEmpty(client, crc);
                return;
            }

            if (data.Length == 0)
            {
                client.SendEmpytMuteList();
                return;
            }

            if (data.Length == 1)
            {
                SendCachedOrEmpty(client, crc);
                return;
            }

            string filename = "mutes" + client.AgentId.ToString();
            xfer.AddNewFile(filename, data);
            client.SendMuteListUpdate(filename);
        }

        private void OnUpdateMuteListEntry(IClientAPI client, UUID muteID, string muteName, int muteType, uint muteFlags)
        {
            if (!m_Enabled || IsForeign(client))
                return;

            UUID agentID = client.AgentId;
            if(muteType == 1) // agent
            {
                if(agentID == muteID)
                    return;
                if(client.Scene is Scene scene && scene.Permissions.IsAdministrator(muteID))
                {
                    OnMuteListRequest(client, 0);
                    return;
                }
            }

            MuteData mute = new MuteData();
            mute.AgentID = agentID;
            mute.MuteID = muteID;
            mute.MuteName = muteName;
            mute.MuteType = muteType;
            mute.MuteFlags = (int)muteFlags;
            mute.Stamp = Util.UnixTimeSinceEpoch();

            IMuteListService service = client.Scene.RequestModuleInterface<IMuteListService>();
            if (service == null)
                return;
            try
            {
                if (!service.UpdateMute(mute))
                    m_log.WarnFormat("[MuteListModule]: Failed to persist mute update for {0}", agentID);
            }
            catch (Exception ex)
            {
                m_log.WarnFormat("[MuteListModule]: Failed to update mutes for {0}: {1}", agentID, ex.Message);
            }
        }

        private void OnRemoveMuteListEntry(IClientAPI client, UUID muteID, string muteName)
        {
            if (!m_Enabled || IsForeign(client))
                return;
            IMuteListService service = client.Scene.RequestModuleInterface<IMuteListService>();
            if (service == null)
                return;
            try
            {
                if (!service.RemoveMute(client.AgentId, muteID, muteName))
                    m_log.WarnFormat("[MuteListModule]: Failed to persist mute removal for {0}", client.AgentId);
            }
            catch (Exception ex)
            {
                m_log.WarnFormat("[MuteListModule]: Failed to remove mute for {0}: {1}", client.AgentId, ex.Message);
            }
        }

        private static void SendCachedOrEmpty(IClientAPI client, uint crc)
        {
            if (crc == 0)
                client.SendEmpytMuteList();
            else
                client.SendUseCachedMuteList();
        }
    }
}
