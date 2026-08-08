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
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Client;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;

namespace OpenSim.OfflineIM
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "OfflineIMConnectorModule")]
    public class OfflineIMRegionModule : ISharedRegionModule, IOfflineIMService
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private bool m_Enabled = false;
        private readonly List<Scene> m_SceneList = new List<Scene>();
        private readonly object m_SceneLock = new object();
        IMessageTransferModule m_TransferModule = null;
        private bool m_TransferSubscribed;
        private bool m_ForwardOfflineGroupMessages = true;

        private IOfflineIMService m_OfflineIMService;

        public void Initialise(IConfigSource config)
        {
            IConfig cnf = config.Configs["Messaging"];
            if (cnf == null)
                return;
            if (cnf != null && cnf.GetString("OfflineMessageModule", string.Empty) != Name)
                return;

            m_Enabled = true;

            string serviceLocation = cnf.GetString("OfflineMessageURL", string.Empty);
            if (serviceLocation.Length == 0)
                m_OfflineIMService = new OfflineIMService(config);
            else
                m_OfflineIMService = new OfflineIMServiceRemoteConnector(config);

            m_ForwardOfflineGroupMessages = cnf.GetBoolean("ForwardOfflineGroupMessages", m_ForwardOfflineGroupMessages);
            m_log.DebugFormat("[OfflineIM.V2]: Offline messages enabled by {0}", Name);
        }

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            scene.RegisterModuleInterface<IOfflineIMService>(this);
            lock (m_SceneLock)
            {
                if (!m_SceneList.Contains(scene))
                    m_SceneList.Add(scene);
            }
            scene.EventManager.OnNewClient += OnNewClient;
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;

            if (m_TransferModule == null)
            {
                m_TransferModule = scene.RequestModuleInterface<IMessageTransferModule>();
                if (m_TransferModule == null)
                {
                    m_log.Error("[OfflineIM.V2]: No message transfer module is enabled. Disabling offline messages");
                    return;
                }
            }

            if (!m_TransferSubscribed)
            {
                m_TransferModule.OnUndeliveredMessage += UndeliveredMessage;
                m_TransferSubscribed = true;
            }
        }

        public void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            lock (m_SceneLock)
                m_SceneList.Remove(scene);

            scene.EventManager.OnNewClient -= OnNewClient;
            scene.UnregisterModuleInterface<IOfflineIMService>(this);

            scene.ForEachClient(delegate(IClientAPI client)
            {
                client.OnRetrieveInstantMessages -= RetrieveInstantMessages;
            });

            lock (m_SceneLock)
            {
                if (m_SceneList.Count == 0 && m_TransferSubscribed)
                {
                    m_TransferModule.OnUndeliveredMessage -= UndeliveredMessage;
                    m_TransferSubscribed = false;
                    m_TransferModule = null;
                }
            }
        }

        public void PostInitialise()
        {
        }

        public string Name
        {
            get { return "Offline Message Module V2"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void Close()
        {
            Scene[] scenes;
            lock (m_SceneLock)
                scenes = m_SceneList.ToArray();

            foreach (Scene scene in scenes)
                RemoveRegion(scene);
        }

        private Scene FindScene(UUID agentID)
        {
            Scene[] scenes;
            lock (m_SceneLock)
                scenes = m_SceneList.ToArray();

            foreach (Scene s in scenes)
            {
                ScenePresence presence = s.GetScenePresence(agentID);
                if (presence != null && !presence.IsChildAgent)
                    return s;
            }
            return null;
        }

        private IClientAPI FindClient(UUID agentID)
        {
            Scene[] scenes;
            lock (m_SceneLock)
                scenes = m_SceneList.ToArray();

            foreach (Scene s in scenes)
            {
                ScenePresence presence = s.GetScenePresence(agentID);
                if (presence != null && !presence.IsChildAgent)
                    return presence.ControllingClient;
            }
            return null;
        }

        private void OnNewClient(IClientAPI client)
        {
            client.OnRetrieveInstantMessages += RetrieveInstantMessages;
        }

        private void RetrieveInstantMessages(IClientAPI client)
        {
            m_log.DebugFormat("[OfflineIM.V2]: Retrieving stored messages for {0}", client.AgentId);

            List<GridInstantMessage> msglist;
            try
            {
                msglist = m_OfflineIMService.GetMessages(client.AgentId);
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[OfflineIM.V2]: Failed retrieving messages for {0}: {1}", client.AgentId, ex.Message);
                return;
            }

            if (msglist == null)
            {
                m_log.DebugFormat("[OfflineIM.V2]: WARNING null message list.");
                return;
            }

            foreach (GridInstantMessage im in msglist)
            {
                if (im.dialog == (byte)InstantMessageDialog.InventoryOffered)
                    // send it directly or else the item will be given twice
                    client.SendInstantMessage(im);
                else
                {
                    // Send through scene event manager so all modules get a chance
                    // to look at this message before it gets delivered.
                    //
                    // Needed for proper state management for stored group
                    // invitations
                    //
                    Scene s = client.Scene as Scene;
                    if (s != null)
                    {
                        im.offline = 1;
                        s.EventManager.TriggerIncomingInstantMessage(im);
                    }
                }
            }
        }

        private void UndeliveredMessage(GridInstantMessage im)
        {
            if (im.dialog != (byte)InstantMessageDialog.MessageFromObject &&
                im.dialog != (byte)InstantMessageDialog.MessageFromAgent &&
                im.dialog != (byte)InstantMessageDialog.GroupNotice &&
                im.dialog != (byte)InstantMessageDialog.GroupInvitation &&
                im.dialog != (byte)InstantMessageDialog.InventoryOffered)
            {
                return;
            }

            if (!m_ForwardOfflineGroupMessages)
            {
                if (im.dialog == (byte)InstantMessageDialog.GroupNotice ||
                    im.dialog == (byte)InstantMessageDialog.GroupInvitation)
                    return;
            }

            string reason = string.Empty;
            bool success;
            try
            {
                success = m_OfflineIMService.StoreMessage(im, out reason);
            }
            catch (Exception ex)
            {
                success = false;
                reason = "Offline message service unavailable";
                m_log.ErrorFormat("[OfflineIM.V2]: Failed storing message for {0}: {1}", new UUID(im.toAgentID), ex.Message);
            }

            if (im.dialog == (byte)InstantMessageDialog.MessageFromAgent)
            {
                IClientAPI client = FindClient(new UUID(im.fromAgentID));
                if (client == null)
                    return;

                client.SendInstantMessage(new GridInstantMessage(
                        null, new UUID(im.toAgentID),
                        "System", new UUID(im.fromAgentID),
                        (byte)InstantMessageDialog.MessageFromAgent,
                        "User is not logged in. " +
                        (success ? "Message saved." : "Message not saved: " + reason),
                        false, new Vector3()));
            }
        }

        #region IOfflineIM

        public List<GridInstantMessage> GetMessages(UUID principalID)
        {
            return m_OfflineIMService.GetMessages(principalID);
        }

        public bool StoreMessage(GridInstantMessage im, out string reason)
        {
            return m_OfflineIMService.StoreMessage(im, out reason);
        }

        public void DeleteMessages(UUID userID)
        {
            m_OfflineIMService.DeleteMessages(userID);
        }

        #endregion
    }
}
