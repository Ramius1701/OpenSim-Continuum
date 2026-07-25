using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using OpenSim.Framework;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;
using OpenMetaverse;
using log4net;

namespace OpenSim.Services.Connectors.Hypergrid
{
    /// <summary>
    /// HTTP client for a foreign grid's get_display_names endpoint (see
    /// HGGetDisplayNamesPostHandler on the server side). Ported from
    /// Mobius essentially verbatim - self-contained, nothing here for it
    /// to conflict with in current core.
    /// </summary>
    public class DisplayNameServiceConnector
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(
            MethodBase.GetCurrentMethod().DeclaringType);

        private string m_ServerURLHost;
        private string m_ServerURL;

        public DisplayNameServiceConnector(string url) : this(url, true)
        {
        }

        public DisplayNameServiceConnector(string url, bool dnsLookup)
        {
            m_ServerURL = m_ServerURLHost = url;

            if (dnsLookup)
            {
                try
                {
                    Uri m_Uri = new Uri(m_ServerURL);
                    IPAddress ip = Util.GetHostFromDNS(m_Uri.Host);
                    if (ip is not null)
                    {
                        m_ServerURL = m_ServerURL.Replace(m_Uri.Host, ip.ToString());
                        if (!m_ServerURL.EndsWith("/"))
                            m_ServerURL += "/";
                    }
                    else
                        m_log.DebugFormat("[DISPLAY NAME CONNECTOR]: Failed to resolve address of {0}", url);
                }
                catch (Exception e)
                {
                    m_log.DebugFormat("[DISPLAY NAME CONNECTOR]: Malformed Uri {0}: {1}", url, e.Message);
                }
            }
        }

        public Dictionary<UUID, string> GetDisplayNames(UUID[] userIDs)
        {
            string uri = m_ServerURL + "get_display_names";

            List<string> str_userIDs = new List<string>();
            foreach (UUID id in userIDs)
                str_userIDs.Add(id.ToString());

            Dictionary<string, object> sendData = new Dictionary<string, object>
            {
                ["AgentIDs"] = str_userIDs
            };

            string reqString = ServerUtils.BuildQueryString(sendData);

            Dictionary<UUID, string> data = new Dictionary<UUID, string>();
            try
            {
                string reply = SynchronousRestFormsRequester.MakeRequest("POST",
                        uri,
                        reqString, 5, null, false);
                if (!string.IsNullOrEmpty(reply))
                {
                    Dictionary<string, object> replyData = ServerUtils.ParseXmlResponse(reply);

                    if (replyData is not null && replyData.TryGetValue("success", out object successObj)
                            && successObj?.ToString() == "true")
                    {
                        int i = 0;
                        while (true)
                        {
                            if (replyData.ContainsKey("uuid" + i) && replyData.ContainsKey("name" + i))
                            {
                                string str_uuid = replyData["uuid" + i].ToString();
                                string name = replyData["name" + i].ToString();

                                if (UUID.TryParse(str_uuid, out UUID uuid))
                                    data[uuid] = name;
                                i++;
                            }
                            else
                                break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                // Target grid is offline, unreachable, or didn't send back
                // the expected result - not fatal, callers fall back to
                // the visitor's base name.
                m_log.DebugFormat("[DISPLAY NAME CONNECTOR]: Exception when contacting {0}: {1}", uri, e.Message);
            }

            return data;
        }
    }
}
