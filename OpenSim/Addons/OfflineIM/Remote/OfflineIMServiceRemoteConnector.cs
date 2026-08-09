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
using System.Linq;
using System.Reflection;
using System.Text;

using OpenSim.Framework;
using OpenSim.Framework.ServiceAuth;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;

using OpenMetaverse;
using log4net;
using Nini.Config;

namespace OpenSim.OfflineIM
{
    public class OfflineIMServiceRemoteConnector : IOfflineIMService
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private string m_ServerURI = string.Empty;
        private IServiceAuth m_Auth;
        private readonly object m_Lock = new object();
        private int m_MaxReturnedMessages = 1000;

        public OfflineIMServiceRemoteConnector(string url)
        {
            m_ServerURI = ValidateServerUri(url);
            m_log.DebugFormat("[OfflineIM.V2.RemoteConnector]: Offline IM server at {0}", m_ServerURI);
        }

        public OfflineIMServiceRemoteConnector(IConfigSource config)
        {
            IConfig cnf = config.Configs["Messaging"];
            if (cnf == null)
            {
                m_log.WarnFormat("[OfflineIM.V2.RemoteConnector]: Missing Messaging configuration");
                return;
            }

            m_ServerURI = ValidateServerUri(cnf.GetString("OfflineMessageURL", string.Empty));
            m_MaxReturnedMessages = Math.Clamp(
                cnf.GetInt("MaxOfflineIMs", 25),
                0,
                1000);

            /// This is from BaseServiceConnector
            string authType = Util.GetConfigVarFromSections<string>(config, "AuthType", new string[] { "Network", "Messaging" }, "None");

            switch (authType)
            {
                case "BasicHttpAuthentication":
                    m_Auth = new BasicHttpAuthentication(config, "Messaging");
                    break;
            }
            ///
            m_log.DebugFormat("[OfflineIM.V2.RemoteConnector]: Offline IM server at {0} with auth {1}",
                m_ServerURI, (m_Auth == null ? "None" : m_Auth.GetType().ToString()));
        }

        #region IOfflineIMService
        public List<GridInstantMessage> GetMessages(UUID principalID)
        {
            List<GridInstantMessage> ims = new List<GridInstantMessage>();

            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["PrincipalID"] = principalID;

            Dictionary<string, object> ret = MakeRequest("GET", sendData);
            if (ret == null)
                return ims;

            if (!ret.TryGetValue("RESULT", out object resultobj))
                return ims;

            if(resultobj is string result)
            {
                if (result == "NULL" || result.Equals("false", StringComparison.InvariantCultureIgnoreCase))
                {
                    if (ret.TryGetValue("REASON", out object rso))
                        m_log.Debug($"[OfflineIM.V2.RemoteConnector]: GetMessages for {principalID} failed: {rso}");
                    else
                        m_log.Debug($"[OfflineIM.V2.RemoteConnector]: GetMessages for {principalID} failed: Unknown error");
                    return ims;
                }
            }
            else if(resultobj is Dictionary<string, object> resultdic)
            {
                foreach (object v in resultdic.Values)
                {
                    if (ims.Count >= m_MaxReturnedMessages)
                        break;

                    if (v is Dictionary<string, object> vdic)
                    {
                        try
                        {
                            GridInstantMessage m = OfflineIMDataUtils.GridInstantMessage(vdic);
                            if (m.toAgentID == principalID.Guid)
                                ims.Add(m);
                            else
                                m_log.WarnFormat(
                                    "[OfflineIM.V2.RemoteConnector]: Ignoring message returned for the wrong recipient");
                        }
                        catch (Exception ex)
                        {
                            m_log.WarnFormat(
                                "[OfflineIM.V2.RemoteConnector]: Ignoring malformed message for {0}: {1}",
                                principalID,
                                ex.Message);
                        }
                    }
                }
            }
            return ims;
        }

        public bool StoreMessage(GridInstantMessage im, out string reason)
        {
            Dictionary<string, object> sendData = OfflineIMDataUtils.GridInstantMessage(im);
            Dictionary<string, object> ret = MakeRequest("STORE", sendData);

            if (ret == null)
            {
                reason = "Bad response from server";
                return false;
            }

            if (!ret.TryGetValue("RESULT", out object o)
                || !bool.TryParse(o?.ToString(), out bool stored)
                || !stored)
            {
                if(ret.TryGetValue("REASON", out object ro))
                    reason = ro.ToString();
                else
                    reason = "Offline message server did not confirm storage";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public void DeleteMessages(UUID userID)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["UserID"] = userID;

            MakeRequest("DELETE", sendData);
        }

        #endregion


        #region Make Request

        private Dictionary<string, object> MakeRequest(string method, Dictionary<string, object> sendData)
        {
            sendData["METHOD"] = method;

            try
            {
                string reply;
                lock (m_Lock)
                    reply = SynchronousRestFormsRequester.MakeRequest("POST",
                             m_ServerURI + "/offlineim",
                             ServerUtils.BuildQueryString(sendData),
                             m_Auth);

                return ServerUtils.ParseXmlResponse(reply);
            }
            catch (Exception ex)
            {
                m_log.WarnFormat("[OfflineIM.V2.RemoteConnector]: {0} request failed: {1}", method, ex.Message);
                return null;
            }
        }

        private static string ValidateServerUri(string value)
        {
            string candidate = (value ?? string.Empty).Trim().TrimEnd('/');
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || string.IsNullOrEmpty(uri.Host))
            {
                throw new ArgumentException("OfflineMessageURL must be an absolute HTTP or HTTPS URL");
            }

            return candidate;
        }
        #endregion

    }
}
