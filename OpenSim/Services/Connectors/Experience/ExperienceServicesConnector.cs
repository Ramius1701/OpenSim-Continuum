using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using Nini.Config;
using OpenSim.Framework;

using OpenSim.Framework.ServiceAuth;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using OpenSim.Server.Base;
using OpenMetaverse;
using System.Security.AccessControl;
using OpenSim.Data;
using System.Linq;

namespace OpenSim.Services.Connectors
{
    public class ExperienceServicesConnector : BaseServiceConnector, IExperienceService
    {
        private const int MaxCollectionResults = 1000;
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private string m_ServerURI = String.Empty;

        public ExperienceServicesConnector()
        {
        }

        public ExperienceServicesConnector(string serverURI)
        {
            m_ServerURI = serverURI.TrimEnd('/') + "/experience";
        }

        public ExperienceServicesConnector(IConfigSource source)
        {
            Initialise(source);
        }

        public virtual void Initialise(IConfigSource source)
        {
            IConfig gridConfig = source.Configs["ExperienceService"];
            if (gridConfig == null)
            {
                m_log.Error("[EXPERIENCE CONNECTOR]: ExperienceService missing from configuration");
                throw new Exception("Experience connector init error");
            }

            string serviceURI = gridConfig.GetString("ExperienceServerURI",
                    String.Empty);

            if (serviceURI == String.Empty)
            {
                m_log.Error("[EXPERIENCE CONNECTOR]: ExperienceServerURI is missing from [ExperienceService]");
                throw new Exception("Experience connector init error");
            }
            m_ServerURI = serviceURI.TrimEnd('/') + "/experience";
            base.Initialise(source, "ExperienceService");
        }

        #region IExperienceService
        
        public Dictionary<UUID, bool> FetchExperiencePermissions(UUID agent_id)
        {
            //m_log.InfoFormat("[ExperienceServiceConnector]: FetchExperiencePermissions for {0}", agent_id);

            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["METHOD"] = "getpermissions";
            sendData["agent_id"] = agent_id.ToString();

            string request_str = ServerUtils.BuildQueryString(sendData);

            Dictionary<UUID, bool> experiences = new Dictionary<UUID, bool>();

            string reply = MakeRequest(request_str);
            if (reply == string.Empty)
                throw new WebException("Experience permission service returned no response");

            Dictionary<string, object> replyData = ParseReply(reply);
            if (replyData == null)
                throw new InvalidDataException("Experience permission service returned a malformed response");

            for (int iter = 0; iter < MaxCollectionResults; iter++)
            {
                string key = string.Format("uuid_{0}", iter);
                string perm = string.Format("perm_{0}", iter);

                if (replyData.ContainsKey(key) && replyData.ContainsKey(perm))
                {
                    if (UUID.TryParse(replyData[key]?.ToString(), out UUID experienceID) &&
                        experienceID != UUID.Zero &&
                        bool.TryParse(replyData[perm]?.ToString(), out bool allow))
                    {
                        experiences[experienceID] = allow;

                        //m_log.InfoFormat("[EXPERIENCE SERVICE CONNECTOR]: {0} = {1}", experienceID, allow);
                    }
                }
                else break;
            }

            return experiences;
        }

        public bool UpdateExperiencePermissions(UUID agent_id, UUID experience, ExperiencePermission perm)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["METHOD"] = "updatepermission";
            sendData["agent_id"] = agent_id.ToString();
            sendData["experience"] = experience.ToString();
            sendData["permission"] = perm == ExperiencePermission.None ? "forget" : perm == ExperiencePermission.Allowed ? "allow" : "block";

            string request_str = ServerUtils.BuildQueryString(sendData);

            return doSimplePost(request_str, "updatepermission");
        }

        public ExperienceInfo[] GetExperienceInfos(UUID[] experiences)
        {
            if (experiences == null || experiences.Length == 0)
                return Array.Empty<ExperienceInfo>();

            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["METHOD"] = "getexperienceinfos";
            int i = 0;
            foreach (UUID id in experiences.Where(id => id != UUID.Zero).Distinct().Take(MaxCollectionResults))
            {
                sendData[string.Format("id_{0}", i)] = id.ToString();
                i++;
            }
            if (i == 0)
                return Array.Empty<ExperienceInfo>();

            string request_str = ServerUtils.BuildQueryString(sendData);

            List<ExperienceInfo> infos = new List<ExperienceInfo>();

            string reply = MakeRequest(request_str);

            //m_log.InfoFormat("[EXPERIENCE SERVICE CONNECTOR]: Reply: {0}", reply);

            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = ParseReply(reply);
                if (replyData == null)
                    return Array.Empty<ExperienceInfo>();

                Dictionary<string, object>.ValueCollection experienceList = replyData.Values;

                foreach (object ex in experienceList)
                {
                    if (infos.Count >= MaxCollectionResults)
                        break;
                    if (ex is Dictionary<string, object> experience)
                    {
                        try
                        {
                            infos.Add(new ExperienceInfo(experience));
                        }
                        catch (Exception e)
                        {
                            m_log.WarnFormat(
                                "[EXPERIENCE CONNECTOR]: Ignoring malformed Experience info: {0}",
                                e.Message);
                        }
                    }
                }
            }

            return infos.ToArray();
        }

        #endregion IExperienceService

        private bool doSimplePost(string reqString, string meth)
        {
            try
            {
                string reply = SynchronousRestFormsRequester.MakeRequest("POST", m_ServerURI, reqString, m_Auth);
                if (reply != string.Empty)
                {
                    Dictionary<string, object> replyData = ParseReply(reply);

                    if (replyData != null && replyData.ContainsKey("result"))
                    {
                        if (replyData["result"].ToString().ToLower() == "success")
                            return true;
                        else
                            return false;
                    }
                    else
                        m_log.DebugFormat("[EXPERIENCE CONNECTOR]: {0} reply data does not contain result field", meth);
                }
                else
                    m_log.DebugFormat("[EXPERIENCE CONNECTOR]: {0} received empty reply", meth);
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[EXPERIENCE CONNECTOR]: Exception when contacting server at {0}: {1}", m_ServerURI, e.Message);
            }

            return false;
        }

        public UUID[] GetAgentExperiences(UUID agent_id)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["METHOD"] = "getagentexperiences";
            sendData["AGENT"] = agent_id.ToString();

            string request_str = ServerUtils.BuildQueryString(sendData);

            List<ExperienceInfo> infos = new List<ExperienceInfo>();

            string reply = MakeRequest(request_str);

            //m_log.InfoFormat("[EXPERIENCE SERVICE CONNECTOR]: Reply: {0}", reply);

            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = ParseReply(reply);
                if(replyData != null)
                {
                    Dictionary<string, object>.ValueCollection experienceList = replyData.Values;

                    return ParseExperienceIDs(experienceList);
                }
            }

            return new UUID[0];
        }

        public ExperienceInfo UpdateExperienceInfo(ExperienceInfo info)
        {
            // let's just pray they never add a parameter named "method"
            Dictionary<string, object> sendData = info.ToDictionary();
            sendData["METHOD"] = "updateexperienceinfo";

            string request_str = ServerUtils.BuildQueryString(sendData);

            string reply = MakeRequest(request_str);

            //m_log.InfoFormat("[EXPERIENCE SERVICE CONNECTOR]: UpdateExperienceInfo Reply: {0}", reply);

            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = ParseReply(reply);
                if (replyData != null)
                {
                    try
                    {
                        return new ExperienceInfo(replyData);
                    }
                    catch (Exception e)
                    {
                        m_log.WarnFormat(
                            "[EXPERIENCE CONNECTOR]: Ignoring malformed updated Experience info: {0}",
                            e.Message);
                    }
                }
            }

            return null;
        }

        public ExperienceInfo[] FindExperiencesByName(string search)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["METHOD"] = "findexperiences";
            sendData["SEARCH"] = search;

            string request_str = ServerUtils.BuildQueryString(sendData);

            List<ExperienceInfo> infos = new List<ExperienceInfo>();

            string reply = MakeRequest(request_str);
            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = ParseReply(reply);

                if (replyData == null)
                    return Array.Empty<ExperienceInfo>();

                Dictionary<string, object>.ValueCollection experienceList = replyData.Values;

                foreach (object ex in experienceList)
                {
                    if (infos.Count >= MaxCollectionResults)
                        break;
                    if (ex is Dictionary<string, object> experience)
                    {
                        try
                        {
                            infos.Add(new ExperienceInfo(experience));
                        }
                        catch (Exception e)
                        {
                            m_log.WarnFormat(
                                "[EXPERIENCE CONNECTOR]: Ignoring malformed Experience search result: {0}",
                                e.Message);
                        }
                    }
                }
            }

            return infos.ToArray();
        }

        public UUID[] GetGroupExperiences(UUID group_id)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["METHOD"] = "getgroupexperiences";
            sendData["GROUP"] = group_id.ToString();

            string request_str = ServerUtils.BuildQueryString(sendData);

            List<ExperienceInfo> infos = new List<ExperienceInfo>();

            string reply = MakeRequest(request_str);
            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = ParseReply(reply);
                if (replyData != null)
                {
                    Dictionary<string, object>.ValueCollection experienceList = replyData.Values;

                    return ParseExperienceIDs(experienceList);
                }
            }

            return new UUID[0];
        }

        public UUID[] GetExperiencesForGroups(UUID[] groups)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["METHOD"] = "getexperiencesforgroups";

            int i = 0;
            if (groups == null || groups.Length == 0)
                return Array.Empty<UUID>();

            foreach (UUID id in groups.Where(id => id != UUID.Zero).Distinct().Take(MaxCollectionResults))
            {
                sendData["id_" + i] = id.ToString();
                i++;
            }

            string request_str = ServerUtils.BuildQueryString(sendData);

            string reply = MakeRequest(request_str);
            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = ParseReply(reply);
                if (replyData != null)
                {
                    Dictionary<string, object>.ValueCollection experienceList = replyData.Values;

                    return ParseExperienceIDs(experienceList);
                }
            }

            return new UUID[0];
        }

        private static UUID[] ParseExperienceIDs(IEnumerable<object> values)
        {
            HashSet<UUID> ids = new HashSet<UUID>();
            foreach (object value in values)
            {
                if (ids.Count >= MaxCollectionResults)
                    break;
                if (value != null && UUID.TryParse(value.ToString(), out UUID id) && id != UUID.Zero)
                    ids.Add(id);
            }
            return ids.ToArray();
        }

        private string MakeRequest(string requestData)
        {
            try
            {
                return SynchronousRestFormsRequester.MakeRequest(
                    "POST",
                    m_ServerURI,
                    requestData,
                    m_Auth) ?? string.Empty;
            }
            catch (Exception e)
            {
                m_log.WarnFormat(
                    "[EXPERIENCE CONNECTOR]: Request to {0} failed: {1}",
                    m_ServerURI,
                    e.Message);
                return string.Empty;
            }
        }

        private Dictionary<string, object> ParseReply(string reply)
        {
            try
            {
                return ServerUtils.ParseXmlResponse(reply);
            }
            catch (Exception e)
            {
                m_log.WarnFormat(
                    "[EXPERIENCE CONNECTOR]: Ignoring malformed reply from {0}: {1}",
                    m_ServerURI,
                    e.Message);
                return null;
            }
        }

        public string GetKeyValue(UUID experience, string key)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["METHOD"] = "accesskvdatabase";
            sendData["ACTION"] = "GET";
            sendData["EXPERIENCE"] = experience.ToString();
            sendData["KEY"] = key;

            string request_str = ServerUtils.BuildQueryString(sendData);

            string reply = MakeRequest(request_str);
            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = ParseReply(reply);
                if (replyData != null)
                {
                    if(replyData.ContainsKey("result"))
                    {
                        if(replyData["result"].ToString() == "success")
                        {
                            if (replyData.ContainsKey("value"))
                            {
                                return replyData["value"].ToString();
                            }
                        }
                    }
                }
            }

            return null;
        }

        public string CreateKeyValue(UUID experience, string key, string value)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["METHOD"] = "accesskvdatabase";
            sendData["ACTION"] = "CREATE";
            sendData["EXPERIENCE"] = experience.ToString();
            sendData["KEY"] = key;
            sendData["VALUE"] = value;

            string request_str = ServerUtils.BuildQueryString(sendData);

            string reply = MakeRequest(request_str);
            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = ParseReply(reply);
                if (replyData != null)
                {
                    if (replyData.ContainsKey("result"))
                    {
                        return replyData["result"].ToString();
                    }
                }
            }

            return "error";
        }

        public string UpdateKeyValue(UUID experience, string key, string val, bool check, string original)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["METHOD"] = "accesskvdatabase";
            sendData["ACTION"] = "UPDATE";
            sendData["EXPERIENCE"] = experience.ToString();
            sendData["KEY"] = key;
            sendData["VALUE"] = val;
            sendData["CHECK"] = check ? "TRUE" : "FALSE";
            sendData["ORIGINAL"] = check ? original : string.Empty;

            string request_str = ServerUtils.BuildQueryString(sendData);

            string reply = MakeRequest(request_str);
            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = ParseReply(reply);
                if (replyData != null)
                {
                    if (replyData.ContainsKey("result"))
                    {
                        return replyData["result"].ToString();
                    }
                }
            }

            return "error";
        }

        public string DeleteKey(UUID experience, string key)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["METHOD"] = "accesskvdatabase";
            sendData["ACTION"] = "DELETE";
            sendData["EXPERIENCE"] = experience.ToString();
            sendData["KEY"] = key;

            string request_str = ServerUtils.BuildQueryString(sendData);

            string reply = MakeRequest(request_str);
            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = ParseReply(reply);
                if (replyData != null)
                {
                    if (replyData.ContainsKey("result"))
                    {
                        return replyData["result"].ToString();
                    }
                }
            }

            return "error";
        }

        public int GetKeyCount(UUID experience)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["METHOD"] = "accesskvdatabase";
            sendData["ACTION"] = "COUNT";
            sendData["EXPERIENCE"] = experience.ToString();

            string request_str = ServerUtils.BuildQueryString(sendData);

            string reply = MakeRequest(request_str);
            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = ParseReply(reply);
                if (replyData != null)
                {
                    if (replyData.ContainsKey("result"))
                    {
                        if(replyData["result"].ToString() == "success")
                        {
                            if (replyData.ContainsKey("count") &&
                                int.TryParse(replyData["count"]?.ToString(), out int count) && count >= 0)
                            {
                                return count;
                            }
                        }
                    }
                }
            }

            return 0;
        }

        public string[] GetKeys(UUID experience, int start, int count)
        {
            if (experience == UUID.Zero || start < 0 || count < 1 || count > MaxCollectionResults)
                return Array.Empty<string>();

            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["METHOD"] = "accesskvdatabase";
            sendData["ACTION"] = "GETKEYS";
            sendData["EXPERIENCE"] = experience.ToString();
            sendData["START"] = start.ToString();
            sendData["COUNT"] = count.ToString();

            string request_str = ServerUtils.BuildQueryString(sendData);

            string reply = MakeRequest(request_str);

            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = ParseReply(reply);
                if (replyData != null)
                {
                    List<string> keys = new List<string>();
                    for (int i = 0; i < count; i++)
                    {
                        if (!replyData.TryGetValue("key_" + i, out object value))
                            break;
                        if (value != null)
                            keys.Add(value.ToString());
                    }
                    return keys.ToArray();
                }
            }

            return new string[0];
        }

        public int GetSize(UUID experience)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["METHOD"] = "accesskvdatabase";
            sendData["ACTION"] = "SIZE";
            sendData["EXPERIENCE"] = experience.ToString();

            string request_str = ServerUtils.BuildQueryString(sendData);

            string reply = MakeRequest(request_str);
            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = ParseReply(reply);
                if (replyData != null)
                {
                    if (replyData.ContainsKey("result"))
                    {
                        if (replyData["result"].ToString() == "success")
                        {
                            if (replyData.ContainsKey("count") &&
                                int.TryParse(replyData["count"]?.ToString(), out int size) && size >= 0)
                            {
                                return size;
                            }
                        }
                    }
                }
            }

            return 0;
        }
    }
}
