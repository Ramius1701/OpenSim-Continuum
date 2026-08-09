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
    public class UserAccountServicesConnector : BaseServiceConnector, IUserAccountService
    {
        private const int MaxAccountResults = 1000;
        private const int MaxQueryLength = 256;
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        private string m_ServerURI = String.Empty;

        public UserAccountServicesConnector()
        {
        }

        public UserAccountServicesConnector(string serverURI)
        {
            m_ServerURI = serverURI.TrimEnd('/');
        }

        public UserAccountServicesConnector(IConfigSource source)
        {
            Initialise(source);
        }

        public virtual void Initialise(IConfigSource source)
        {
            IConfig assetConfig = source.Configs["UserAccountService"];
            if (assetConfig == null)
            {
                m_log.Error("[ACCOUNT CONNECTOR]: UserAccountService missing from OpenSim.ini");
                throw new Exception("User account connector init error");
            }

            string serviceURI = assetConfig.GetString("UserAccountServerURI", string.Empty);

            if (string.IsNullOrWhiteSpace(serviceURI))
            {
                m_log.Error("[ACCOUNT CONNECTOR]: UserAccountServerURI not found in section UserAccountService");
                throw new Exception("User account connector init error");
            }

            OSHHTPHost tmp = new OSHHTPHost(serviceURI, true);
            if (!tmp.IsResolvedHost)
            {
                m_log.ErrorFormat("[ACCOUNT CONNECTOR]: {0}", tmp.IsValidHost ? "Could not resolve UserAccountServerURI" : "UserAccountServerURI is a invalid host");
                throw new Exception("User account connector init error");
            }

            m_ServerURI = tmp.URI;

            base.Initialise(source, "UserAccountService");
        }

        public virtual UserAccount GetUserAccount(UUID scopeID, string firstName, string lastName)
        {
            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
                return null;

            Dictionary<string, object> sendData = new Dictionary<string, object>();
            //sendData["SCOPEID"] = scopeID.ToString();
            sendData["VERSIONMIN"] = ProtocolVersions.ClientProtocolVersionMin.ToString();
            sendData["VERSIONMAX"] = ProtocolVersions.ClientProtocolVersionMax.ToString();
            sendData["METHOD"] = "getaccount";

            sendData["ScopeID"] = scopeID;
            sendData["FirstName"] = firstName;
            sendData["LastName"] = lastName;

            return SendAndGetReply(sendData);
        }

        public virtual UserAccount GetUserAccount(UUID scopeID, string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return null;

            Dictionary<string, object> sendData = new Dictionary<string, object>();
            //sendData["SCOPEID"] = scopeID.ToString();
            sendData["VERSIONMIN"] = ProtocolVersions.ClientProtocolVersionMin.ToString();
            sendData["VERSIONMAX"] = ProtocolVersions.ClientProtocolVersionMax.ToString();
            sendData["METHOD"] = "getaccount";

            sendData["ScopeID"] = scopeID;
            sendData["Email"] = email;

            return SendAndGetReply(sendData);
        }

        public virtual UserAccount GetUserAccount(UUID scopeID, UUID userID)
        {
            if (userID == UUID.Zero)
                return null;

            //m_log.DebugFormat("[ACCOUNTS CONNECTOR]: GetUserAccount {0}", userID);
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            //sendData["SCOPEID"] = scopeID.ToString();
            sendData["VERSIONMIN"] = ProtocolVersions.ClientProtocolVersionMin.ToString();
            sendData["VERSIONMAX"] = ProtocolVersions.ClientProtocolVersionMax.ToString();
            sendData["METHOD"] = "getaccount";

            sendData["ScopeID"] = scopeID;
            sendData["UserID"] = userID.ToString();

            return SendAndGetReply(sendData);
        }

        public List<UserAccount> GetUserAccounts(UUID scopeID, string query)
        {
            query = query?.Trim();
            if (string.IsNullOrEmpty(query) || query.Length > MaxQueryLength)
                return new List<UserAccount>();

            Dictionary<string, object> sendData = new Dictionary<string, object>();
            //sendData["SCOPEID"] = scopeID.ToString();
            sendData["VERSIONMIN"] = ProtocolVersions.ClientProtocolVersionMin.ToString();
            sendData["VERSIONMAX"] = ProtocolVersions.ClientProtocolVersionMax.ToString();
            sendData["METHOD"] = "getaccounts";

            sendData["ScopeID"] = scopeID.ToString();
            sendData["query"] = query;

            string reqString = ServerUtils.BuildQueryString(sendData);
            List<UserAccount> accounts = new List<UserAccount>();
            Dictionary<string, object> replyData = DoPost(reqString, "GetUserAccounts");

            if (replyData != null)
            {
                if (replyData.TryGetValue("result", out object result) &&
                    string.Equals(result?.ToString(), "null", StringComparison.OrdinalIgnoreCase))
                {
                    return accounts;
                }

                Dictionary<string, object>.ValueCollection accountList = replyData.Values;
                //m_log.DebugFormat("[ACCOUNTS CONNECTOR]: GetAgents returned {0} elements", pinfosList.Count);
                foreach (object acc in accountList)
                {
                    if (accounts.Count >= MaxAccountResults)
                        break;
                    if (acc is Dictionary<string, object>)
                    {
                        UserAccount account = ParseAccount((Dictionary<string, object>)acc);
                        if (account != null)
                            accounts.Add(account);
                    }
                    else
                        m_log.DebugFormat("[ACCOUNT CONNECTOR]: GetUserAccounts received invalid response type {0}",
                            acc?.GetType());
                }
            }
            else
                m_log.DebugFormat("[ACCOUNTS CONNECTOR]: GetUserAccounts received null response");

            return accounts;
        }

        public virtual List<UserAccount> GetUserAccounts(UUID scopeID, List<string> IDs)
        {
            if (IDs == null || IDs.Count == 0 || IDs.Count > MaxAccountResults)
                return new List<UserAccount>();

            List<UserAccount> accs = new List<UserAccount>();
            bool multisuported = true;
            accs = doGetMultiUserAccounts(scopeID, IDs, out multisuported);
            if(multisuported)
                return accs;

            // service does not do multi accounts so need to do it one by one

            UUID uuid = UUID.Zero;
            foreach(string id in IDs)
            {
                if(UUID.TryParse(id, out uuid) && !uuid.IsZero())
                {
                    UserAccount account = GetUserAccount(scopeID, uuid);
                    if (account != null)
                        accs.Add(account);
                }
            }

            return accs;
        }

        private List<UserAccount> doGetMultiUserAccounts(UUID scopeID, List<string> IDs, out bool suported)
        {
            suported = true;
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            //sendData["SCOPEID"] = scopeID.ToString();
            sendData["VERSIONMIN"] = ProtocolVersions.ClientProtocolVersionMin.ToString();
            sendData["VERSIONMAX"] = ProtocolVersions.ClientProtocolVersionMax.ToString();
            sendData["METHOD"] = "getmultiaccounts";

            sendData["ScopeID"] = scopeID.ToString();
            sendData["IDS"] = new List<string>(IDs);

            string reqString = ServerUtils.BuildQueryString(sendData);
            List<UserAccount> accounts = new List<UserAccount>();
            Dictionary<string, object> replyData = DoPost(reqString, "GetMultiUserAccounts");

            if (replyData != null)
            {
                if (replyData.TryGetValue("result", out object result))
                {
                    if (string.Equals(result?.ToString(), "null", StringComparison.OrdinalIgnoreCase))
                        return accounts;

                    if (string.Equals(result?.ToString(), "Failure", StringComparison.OrdinalIgnoreCase))
                    {
                        suported = false;
                        return accounts;
                    }
                }

                Dictionary<string, object>.ValueCollection accountList = replyData.Values;
                //m_log.DebugFormat("[ACCOUNTS CONNECTOR]: GetAgents returned {0} elements", pinfosList.Count);
                foreach (object acc in accountList)
                {
                    if (accounts.Count >= MaxAccountResults)
                        break;
                    if (acc is Dictionary<string, object>)
                    {
                        UserAccount account = ParseAccount((Dictionary<string, object>)acc);
                        if (account != null)
                            accounts.Add(account);
                    }
                    else
                        m_log.DebugFormat("[ACCOUNT CONNECTOR]: GetMultiUserAccounts received invalid response type {0}",
                            acc?.GetType());
                }
            }
            else
                m_log.DebugFormat("[ACCOUNTS CONNECTOR]: GetMultiUserAccounts received null response");

            return accounts;
        }


        public virtual void InvalidateCache(UUID userID)
        {
        }

        public virtual bool SetDisplayName(UUID agentID, string displayName)
        {
            if (agentID == UUID.Zero)
                return false;

            //m_log.DebugFormat("[ACCOUNTS CONNECTOR]: SetDisplayName {0}", agentID);
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["VERSIONMIN"] = ProtocolVersions.ClientProtocolVersionMin.ToString();
            sendData["VERSIONMAX"] = ProtocolVersions.ClientProtocolVersionMax.ToString();
            sendData["METHOD"] = "setdisplayname";

            sendData["PrincipalID"] = agentID.ToString();
            sendData["DisplayName"] = displayName;

            return SendAndGetBoolReply(sendData);
        }

        public List<UserAccount> GetUserAccountsWhere(UUID scopeID, string where)
        {
            return null; // Not implemented for regions
        }

        public virtual bool StoreUserAccount(UserAccount data)
        {
            if (data == null || data.PrincipalID == UUID.Zero)
                return false;

            Dictionary<string, object> sendData = new Dictionary<string, object>();
            //sendData["SCOPEID"] = scopeID.ToString();
            sendData["VERSIONMIN"] = ProtocolVersions.ClientProtocolVersionMin.ToString();
            sendData["VERSIONMAX"] = ProtocolVersions.ClientProtocolVersionMax.ToString();
            sendData["METHOD"] = "setaccount";

            Dictionary<string, object> structData = data.ToKeyValuePairs();

            foreach (KeyValuePair<string, object> kvp in structData)
            {
                if (kvp.Value == null)
                {
                    m_log.DebugFormat("[ACCOUNTS CONNECTOR]: Null value for {0}", kvp.Key);
                    continue;
                }
                sendData[kvp.Key] = kvp.Value.ToString();
            }

            if (SendAndGetReply(sendData) != null)
                return true;
            else
                return false;
        }

        /// <summary>
        /// Create user remotely. Note this this is not part of the IUserAccountsService
        /// </summary>
        /// <param name="first"></param>
        /// <param name="last"></param>
        /// <param name="password"></param>
        /// <param name="email"></param>
        /// <param name="scopeID"></param>
        /// <returns></returns>
        public virtual UserAccount CreateUser(string first, string last, string password, string email, UUID scopeID)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            //sendData["SCOPEID"] = scopeID.ToString();
            sendData["VERSIONMIN"] = ProtocolVersions.ClientProtocolVersionMin.ToString();
            sendData["VERSIONMAX"] = ProtocolVersions.ClientProtocolVersionMax.ToString();
            sendData["METHOD"] = "createuser";

            sendData["FirstName"] = first;
            sendData["LastName"] = last;
            sendData["Password"] = password;
            if (!string.IsNullOrEmpty(email))
                sendData["Email"] = email;
            sendData["ScopeID"] = scopeID.ToString();

            return SendAndGetReply(sendData);
        }

        private UserAccount SendAndGetReply(Dictionary<string, object> sendData)
        {
            string reqString = ServerUtils.BuildQueryString(sendData);
            Dictionary<string, object> replyData = DoPost(reqString, "GetUserAccount");
            UserAccount account = null;

            if ((replyData != null) && replyData.ContainsKey("result") && (replyData["result"] != null))
            {
                if (replyData["result"] is Dictionary<string, object>)
                {
                    account = ParseAccount((Dictionary<string, object>)replyData["result"]);
                }
            }

            return account;

        }

        private bool SendAndGetBoolReply(Dictionary<string, object> sendData)
        {
            string reqString = ServerUtils.BuildQueryString(sendData);
            Dictionary<string, object> replyData = DoPost(reqString, "SetUserAccount");
            if (replyData != null && replyData.TryGetValue("result", out object result))
            {
                return string.Equals(
                    result?.ToString(), "success", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private Dictionary<string, object> DoPost(string requestString, string method)
        {
            string uri = m_ServerURI + "/accounts";
            try
            {
                string reply = SynchronousRestFormsRequester.MakeRequest(
                    "POST", uri, requestString, m_Auth);
                if (string.IsNullOrEmpty(reply))
                    return null;
                return ServerUtils.ParseXmlResponse(reply);
            }
            catch (Exception e)
            {
                m_log.WarnFormat(
                    "[ACCOUNT CONNECTOR]: {0} failed contacting {1}: {2}",
                    method, uri, e.Message);
                return null;
            }
        }

        private static UserAccount ParseAccount(Dictionary<string, object> data)
        {
            try
            {
                return new UserAccount(data);
            }
            catch
            {
                return null;
            }
        }
    }
}
