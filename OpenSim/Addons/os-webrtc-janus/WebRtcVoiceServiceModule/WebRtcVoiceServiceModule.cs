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
using System.Threading.Tasks;

using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Server.Base;

using OpenMetaverse;
using OpenMetaverse.StructuredData;

using Mono.Addins;

using log4net;
using Nini.Config;
using System.Linq;

[assembly: Addin("WebRtcVoiceServiceModule", "1.0")]
[assembly: AddinDependency("OpenSim.Region.Framework", OpenSim.VersionInfo.VersionNumber)]

namespace osWebRtcVoice
{
    /// <summary>
    /// Interface for the WebRtcVoiceService.
    /// An instance of this is registered as the IWebRtcVoiceService for this region.
    /// The function here is to direct the capability requests to the appropriate voice service.
    /// For the moment, there are separate voice services for spatial and non-spatial voice
    /// with the idea that a region could have a pre-region spatial voice service while
    /// the grid could have a non-spatial voice service for group chat, etc.
    /// Fancier configurations are possible.
    /// </summary>
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "WebRtcVoiceServiceModule")]
    public class WebRtcVoiceServiceModule : IWebRtcVoiceService
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static string LogHeader = "[WEBRTC VOICE SERVICE MODULE]";

        private bool m_Enabled = false;
        private IConfigSource m_Config;

        private IWebRtcVoiceService m_spatialVoiceService;
        private IWebRtcVoiceService m_nonSpatialVoiceService;

        // =====================================================================

        public WebRtcVoiceServiceModule(IConfigSource pConfig)
        {
            m_Config = pConfig;
            IConfig moduleConfig = m_Config.Configs["WebRtcVoice"];

            if (moduleConfig is not null)
            {
                m_Enabled = moduleConfig.GetBoolean("Enabled", false);
                if (m_Enabled)
                {
                    // Get the DLLs for the two voice services
                    // TODO: spacial/nonspacial names are wrong
                    // spacial here means service for region parcels, that can be spacial or not
                    // non spacial means for other uses like IMs, that just happen to be non spacial
                    // in fact this needs more consideration than just this 2 options

                    string spatialDllName = moduleConfig.GetString("SpatialVoiceService", string.Empty);
                    string nonSpatialDllName = moduleConfig.GetString("NonSpatialVoiceService", string.Empty);
                    if (string.IsNullOrEmpty(spatialDllName) && string.IsNullOrEmpty(nonSpatialDllName))
                    {
                        m_log.Error($"{LogHeader} No VoiceService specified in configuration");
                        m_Enabled = false;
                        return;
                    }

                    // Default non-spatial to spatial if not specified
                    if (string.IsNullOrEmpty(nonSpatialDllName))
                    {
                        m_log.Debug($"{LogHeader} nonSpatialDllName not specified. Defaulting to spatialDllName");
                        nonSpatialDllName = spatialDllName;
                    }

                    // Load the two voice services
                    m_log.Debug($"{LogHeader} Loading SpatialVoiceService from {spatialDllName}");
                    m_spatialVoiceService = ServerUtils.LoadPlugin<IWebRtcVoiceService>(spatialDllName, [m_Config]);
                    if (m_spatialVoiceService is null)
                    {
                        m_log.Error($"{LogHeader} Could not load SpatialVoiceService from {spatialDllName}, module disabled");
                        m_Enabled = false;
                        return;
                    }

                    m_log.Debug($"{LogHeader} Loading NonSpatialVoiceService from {nonSpatialDllName}");
                    if (spatialDllName != nonSpatialDllName)
                    {
                        m_nonSpatialVoiceService = ServerUtils.LoadPlugin<IWebRtcVoiceService>(nonSpatialDllName, [ m_Config ]);
                        if (m_nonSpatialVoiceService is null)
                        {
                            m_log.Error($"{LogHeader} Could not load NonSpatialVoiceService from {nonSpatialDllName}");
                            m_Enabled = false;
                        }
                    }
                    else
                        m_nonSpatialVoiceService = m_spatialVoiceService;

                    if (m_Enabled)
                    {
                        m_log.Info($"{LogHeader} WebRtcVoiceService enabled");
                    }
                }
            }
        }


         private static void CleanupDuplicateSessions(UUID pAgentID, UUID pSceneID, string pKeepViewerSessionId)
        {
            if(VoiceViewerSession.TryGetViewerSessionsByAgentAndRegion(pAgentID, pSceneID, out IEnumerable<KeyValuePair<string, IVoiceViewerSession>> candidates))
            {
                foreach (KeyValuePair<string, IVoiceViewerSession> candidate in candidates)
                {
                    if (!string.IsNullOrEmpty(pKeepViewerSessionId) && candidate.Key == pKeepViewerSessionId)
                        continue;

                    m_log.Warn(
                        $"{LogHeader} CleanupDuplicateSessions: removing stale viewer_session {candidate.Key} for agent {pAgentID}, scene {pSceneID}");

                    VoiceViewerSession.RemoveViewerSession(candidate.Key);
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await candidate.Value.Shutdown();
                        }
                        catch (Exception ex)
                        {
                            m_log.Debug(
                                $"{LogHeader} CleanupDuplicateSessions: shutdown failed for viewer_session {candidate.Key}: {ex.Message}");
                        }
                    });
                }
            }
        }

        // =====================================================================
        // IWebRtcVoiceService

        // IWebRtcVoiceService.ProvisionVoiceAccountRequest
        public OSDMap ProvisionVoiceAccountRequest(OSDMap pRequest, UUID pUserID, UUID pSceneID)
        {
            if (!m_Enabled || pRequest == null || pUserID == UUID.Zero || pSceneID == UUID.Zero)
                return ErrorResponse("Voice service is unavailable");

            IVoiceViewerSession vSession = null;
            if (pRequest.TryGetString("viewer_session", out string viewerSessionId))
            {
                if(pRequest.TryGetBool("logout", out bool islog) && islog)
                {
                    if(UUID.ZeroString.Equals(viewerSessionId, StringComparison.OrdinalIgnoreCase))
                    {
                        if (VoiceViewerSession.TryGetViewerSessionsByAgentId(pUserID, out IEnumerable<KeyValuePair<string, IVoiceViewerSession>> vSessions))
                        {
                            m_log.Info(
                                $"{LogHeader} ProvisionVoiceAccountRequest: doing logout for {vSessions.Count()} stall sessions");

                            OSDMap vreq = new() {{ "logout" , true} };

                            foreach(KeyValuePair<string, IVoiceViewerSession> kvp in vSessions)
                            {
                                IVoiceViewerSession v = kvp.Value;
                                if(v is null)
                                    continue;
                                vreq["viewer_session"] = v.ViewerSessionID;

                                VoiceViewerSession.RemoveViewerSession(v.ViewerSessionID);
                                try
                                {
                                    v.VoiceService?.ProvisionVoiceAccountRequest(v, vreq, pUserID, v.RegionId);
                                }
                                catch (Exception ex)
                                {
                                    m_log.Warn($"{LogHeader} Voice logout failed: {ex.Message}");
                                }
                            }
                        }

                        return new OSDMap {{ "response", "closed" }};
                    }

                    OSDMap resp = null;
                    if (VoiceViewerSession.TryGetViewerSession(viewerSessionId, out vSession))
                    {
                        if (!SessionMatches(vSession, pUserID, pSceneID))
                        {
                            m_log.Warn($"{LogHeader} Rejected logout for a viewer session owned by another agent or region");
                            return ErrorResponse("Logout session not found");
                        }
                        VoiceViewerSession.RemoveViewerSession(viewerSessionId);
                        try
                        {
                            resp = vSession.VoiceService?.ProvisionVoiceAccountRequest(
                                vSession, pRequest, pUserID, pSceneID);
                        }
                        catch (Exception ex)
                        {
                            m_log.Warn($"{LogHeader} Voice logout failed: {ex.Message}");
                        }
                    }
                    return resp ?? new OSDMap() {
                        { "response", "error" },
                        { "message", "Logout session not found" } };
                }

                // request has a viewer session. Use that to find the voice service
                if (VoiceViewerSession.TryGetViewerSession(viewerSessionId, out vSession))
                {
                    if (SessionMatches(vSession, pUserID, pSceneID))
                        CleanupDuplicateSessions(pUserID, pSceneID, viewerSessionId);
                    else
                    {
                        m_log.Warn($"{LogHeader} Rejected provisioning for a viewer session owned by another agent or region");
                        vSession = null;
                    }
                }
            }
            else
            {
                // the request does not have a viewer session. See if it's an initial request
                if (pRequest.TryGetString("channel_type", out string channelType))
                {
                    // Ensure stale sessions are cleared before creating a new one.
                    CleanupDuplicateSessions(pUserID, pSceneID, null);
                    if (channelType == "local")
                    {
                        // TODO: check if this userId is making a new session (case that user is reconnecting)
                        vSession = CreateViewerSessionSafely(m_spatialVoiceService, pRequest, pUserID, pSceneID);
                        if(vSession != null)
                        {
                            if(pRequest.TryGetInt("parcel_local_id", out int parcelID))
                                vSession.Flags = IVoiceViewerSession.VFlags.IsParcel;
                            else
                                vSession.Flags = IVoiceViewerSession.VFlags.IsEstate;
                            VoiceViewerSession.AddViewerSession(vSession);
                        }
                    }
                    else
                    {
                        // TODO: check if this userId is making a new session (case that user is reconnecting)
                        vSession = CreateViewerSessionSafely(m_nonSpatialVoiceService, pRequest, pUserID, pSceneID);
                        if(vSession != null) VoiceViewerSession.AddViewerSession(vSession);
                    }
                }
            }

            OSDMap response = null;

            if (vSession is not null)
            {
                try
                {
                    response = vSession.VoiceService?.ProvisionVoiceAccountRequest(vSession, pRequest, pUserID, pSceneID);
                }
                catch (Exception ex)
                {
                    m_log.Warn($"{LogHeader} Voice provisioning failed: {ex.Message}");
                }
            }

            if (response is null)
            {
                return new OSDMap
                {
                    { "response", "error" },
                    { "message", "Unable to provision voice session (missing viewer_session/channel_type or session not found)" }
                };
             }

            return response;
        }

        // IWebRtcVoiceService.VoiceSignalingRequest
        public OSDMap VoiceSignalingRequest(OSDMap pRequest, UUID pUserID, UUID pSceneID)
        {
            if (!m_Enabled || pRequest == null || pUserID == UUID.Zero || pSceneID == UUID.Zero)
                return ErrorResponse("Voice service is unavailable");

            OSDMap response = null;
            IVoiceViewerSession vSession = null;
            if (pRequest.TryGetString("viewer_session", out string viewerSessionId))
            {
                // request has a viewer session. Use that to find the voice service
                if (VoiceViewerSession.TryGetViewerSession(viewerSessionId, out vSession))
                {
                    if (SessionMatches(vSession, pUserID, pSceneID))
                    {
                        try
                        {
                            response = vSession.VoiceService?.VoiceSignalingRequest(vSession, pRequest, pUserID, pSceneID);
                        }
                        catch (Exception ex)
                        {
                            m_log.Warn($"{LogHeader} Voice signaling failed: {ex.Message}");
                        }
                    }
                    else
                        m_log.Warn($"{LogHeader} Rejected signaling for a viewer session owned by another agent or region");
                }
                else
                {
                    m_log.Error($"{LogHeader} VoiceSignalingRequest: viewer session {viewerSessionId} not found");
                }
            }
            else
            {
                m_log.Error($"{LogHeader} VoiceSignalingRequest: no viewer_session in request");
            }
            return response ?? ErrorResponse("Voice session not found or signaling failed");
        }

        private static IVoiceViewerSession CreateViewerSessionSafely(
            IWebRtcVoiceService service, OSDMap request, UUID userID, UUID sceneID)
        {
            if (service == null)
                return null;

            try
            {
                return service.CreateViewerSession(request, userID, sceneID);
            }
            catch (Exception ex)
            {
                m_log.Warn($"{LogHeader} Voice session creation failed: {ex.Message}");
                return null;
            }
        }

        private static bool SessionMatches(IVoiceViewerSession session, UUID userID, UUID sceneID)
        {
            return session != null && session.AgentId == userID && session.RegionId == sceneID;
        }

        private static OSDMap ErrorResponse(string message)
        {
            return new OSDMap
            {
                { "response", "error" },
                { "message", message }
            };
        }

        // Routing modules do not own backend viewer sessions. Fail closed if a
        // misconfigured caller invokes a backend-only signature.
        public OSDMap ProvisionVoiceAccountRequest(IVoiceViewerSession pVSession, OSDMap pRequest, UUID pUserID, UUID pSceneID)
        {
            return ErrorResponse("Invalid voice service route");
        }

        // This module should never be called with this signature
        public OSDMap VoiceSignalingRequest(IVoiceViewerSession pVSession, OSDMap pRequest, UUID pUserID, UUID pSceneID)
        {
            return ErrorResponse("Invalid voice service route");
        }

        public IVoiceViewerSession CreateViewerSession(OSDMap pRequest, UUID pUserID, UUID pSceneID)
        {
            return null;
        }
    }
}
