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

using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Nini.Config;
using OpenSim.Framework;

using OpenSim.Framework.ServiceAuth;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;
using OpenMetaverse;

namespace OpenSim.Services.Connectors
{
    public class UserAliasServicesConnector : BaseServiceConnector, IUserAliasService
    {
        private const int MaxAliasResults = 1000;
        private const int MaxDescriptionLength = 80;
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        private string m_ServerURI = String.Empty;

        public UserAliasServicesConnector()
        {
        }

        public UserAliasServicesConnector(string serverURI)
        {
            m_ServerURI = serverURI.TrimEnd('/');
        }

        public UserAliasServicesConnector(IConfigSource source)
        {
            Initialise(source);
        }

        public virtual void Initialise(IConfigSource source)
        {
            IConfig aliasConfig = source.Configs["UserAliasService"];
            if (aliasConfig == null)
            {
                m_log.Error("[ALIAS CONNECTOR]: UserAliasService missing from OpenSim.ini");
                throw new Exception("User Alias connector init error");
            }

            string serviceURI = aliasConfig.GetString("UserAliasServerURI", string.Empty);

            if (string.IsNullOrWhiteSpace(serviceURI))
            {
                m_log.Error("[ACCOUNT CONNECTOR]: UserAliasServerURI not found in section UserAliasService");
                throw new Exception("User Alias connector init error");
            }

            OSHHTPHost tmp = new OSHHTPHost(serviceURI, true);
            if (!tmp.IsResolvedHost)
            {
                m_log.ErrorFormat("[ALIAS CONNECTOR]: {0}", tmp.IsValidHost ? "Could not resolve UserAliasServerURI" : "UserAliasServerURI is a invalid host");
                throw new Exception("User Alias connector init error");
            }

            m_ServerURI = tmp.URI;

            base.Initialise(source, "UserAliasService");
        }

        public UserAlias GetUserForAlias(UUID aliasID)
        {
            if (aliasID == UUID.Zero)
                return null;

            Dictionary<string, object> sendData = new Dictionary<string, object>();

            sendData["VERSIONMIN"] = ProtocolVersions.ClientProtocolVersionMin.ToString();
            sendData["VERSIONMAX"] = ProtocolVersions.ClientProtocolVersionMax.ToString();
            sendData["METHOD"] = "getuserforalias";
            sendData["AliasID"] = aliasID.ToString();

            string reqString = ServerUtils.BuildQueryString(sendData);
            Dictionary<string, object> replyData = DoPost(reqString, "GetUserForAlias");

            if ((replyData != null) && replyData.ContainsKey("result") && (replyData["result"] != null))
            {
                if (replyData["result"] is Dictionary<string, object>)
                {
                    return ParseAlias((Dictionary<string, object>)replyData["result"]);
                }
            }

            return null;
        }

        public List<UserAlias> GetUserAliases(UUID userID)
        {
            if (userID == UUID.Zero)
                return new List<UserAlias>();

            Dictionary<string, object> sendData = new Dictionary<string, object>();

            sendData["VERSIONMIN"] = ProtocolVersions.ClientProtocolVersionMin.ToString();
            sendData["VERSIONMAX"] = ProtocolVersions.ClientProtocolVersionMax.ToString();
            sendData["METHOD"] = "getuseraliases";
            sendData["UserID"] = userID.ToString();

            string reqString = ServerUtils.BuildQueryString(sendData);
            Dictionary<string, object> replyData = DoPost(reqString, "GetUserAliases");

            if ((replyData == null) || 
                (replyData.ContainsKey("result") && replyData["result"].ToString() == "null"))
            {
                return new List<UserAlias>();
            }

            Dictionary<string, object>.ValueCollection aliasList = replyData.Values;
            List<UserAlias> userAliases = new List<UserAlias>();

            foreach (object elements in aliasList)
            {
                if (userAliases.Count >= MaxAliasResults)
                    break;
                if (elements is Dictionary<string, object>)
                {
                    UserAlias alias = ParseAlias((Dictionary<string, object>)elements);
                    if (alias != null)
                        userAliases.Add(alias);
                }
                else
                {
                    m_log.DebugFormat(
                        "[USER ALIAS CONNECTOR]: GetUserAliases received invalid response type {0}",
                        elements?.GetType());
                }
            }

            return userAliases;
        }

        public UserAlias CreateAlias(UUID AliasID, UUID UserID, string Description)
        {
            if (AliasID == UUID.Zero || UserID == UUID.Zero ||
                (Description?.Length ?? 0) > MaxDescriptionLength)
                return null;

            Dictionary<string, object> sendData = new Dictionary<string, object>();

            sendData["VERSIONMIN"] = ProtocolVersions.ClientProtocolVersionMin.ToString();
            sendData["VERSIONMAX"] = ProtocolVersions.ClientProtocolVersionMax.ToString();
            sendData["METHOD"] = "createalias";
            sendData["AliasID"] = AliasID.ToString();
            sendData["UserID"] = UserID.ToString();
            sendData["Description"] = Description ?? string.Empty;

            string reqString = ServerUtils.BuildQueryString(sendData);
            Dictionary<string, object> replyData = DoPost(reqString, "CreateAlias");

            if ((replyData != null) && replyData.ContainsKey("result") && (replyData["result"] != null))
            {
                if (replyData["result"] is Dictionary<string, object>)
                {
                    return ParseAlias((Dictionary<string, object>)replyData["result"]);
                }
            }

            return null;
        }

        public bool DeleteAlias(UUID aliasID)
        {
            if (aliasID == UUID.Zero)
                return false;

            Dictionary<string, object> sendData = new Dictionary<string, object>();

            sendData["VERSIONMIN"] = ProtocolVersions.ClientProtocolVersionMin.ToString();
            sendData["VERSIONMAX"] = ProtocolVersions.ClientProtocolVersionMax.ToString();
            sendData["METHOD"] = "deletealias";
            sendData["AliasID"] = aliasID.ToString();

            string reqString = ServerUtils.BuildQueryString(sendData);
            Dictionary<string, object> replyData = DoPost(reqString, "DeleteAlias");
            if ((replyData != null) && replyData.ContainsKey("result") && (replyData["result"] != null))
            {
                return bool.TryParse(replyData["result"].ToString(), out bool result) && result;
            }

            return false;
        }

        private Dictionary<string, object> DoPost(string requestString, string method)
        {
            string uri = m_ServerURI + "/useralias";
            try
            {
                string reply = SynchronousRestFormsRequester.MakeRequest(
                    "POST", uri, requestString, m_Auth);
                if (string.IsNullOrEmpty(reply))
                {
                    m_log.DebugFormat("[USER ALIAS CONNECTOR]: {0} received an empty reply", method);
                    return null;
                }

                return ServerUtils.ParseXmlResponse(reply);
            }
            catch (Exception e)
            {
                m_log.WarnFormat(
                    "[USER ALIAS CONNECTOR]: {0} failed contacting {1}: {2}",
                    method, uri, e.Message);
                return null;
            }
        }

        private static UserAlias ParseAlias(Dictionary<string, object> data)
        {
            try
            {
                return new UserAlias(data);
            }
            catch
            {
                return null;
            }
        }
    }
}
