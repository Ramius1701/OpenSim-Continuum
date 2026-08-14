using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;
using Nini.Config;
using log4net;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Data;
using OpenSim.Services.Interfaces;
using OpenMetaverse;

namespace OpenSim.Services.ExperienceService
{
    public class ExperienceService : ExperienceServiceBase, IExperienceService
    {
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        private IUserAccountService m_UserService = null;

        // Second Life exposes a 128 MiB key-value quota per Experience.
        private const int MAX_QUOTA = 1024 * 1024 * 128;
        private const int MAX_NAME_LENGTH = 42;
        private const int MAX_DESCRIPTION_LENGTH = 128;
        private const int MAX_SLURL_LENGTH = 256;
        private const int MAX_MARKETPLACE_LENGTH = 256;
        private const int MAX_SEARCH_LENGTH = 256;
        private const int MAX_SEARCH_RESULTS = 1000;

        // KVP quota accounting and writes must be one operation.  Without this,
        // concurrent scripts can all pass the size check and exceed the quota.
        private readonly object m_KeyValueLock = new object();

        public ExperienceService(IConfigSource config)
            : base(config)
        {
            m_log.Debug("[EXPERIENCE SERVICE]: Starting experience service");

            IConfig userConfig = config.Configs["ExperienceService"];
            if (userConfig == null)
                throw new Exception("No ExperienceService configuration");

            string userServiceDll = userConfig.GetString("UserAccountService", string.Empty);
            if (userServiceDll != string.Empty)
                m_UserService = LoadPlugin<IUserAccountService>(userServiceDll, new Object[] { config });

            if (MainConsole.Instance != null)
            {
                MainConsole.Instance.Commands.AddCommand("Experience", false,
                        "create experience",
                        "create experience <first> <last>",
                        "Create a new experience owned by a user.", HandleCreateNewExperience);

                MainConsole.Instance.Commands.AddCommand("Experience", false,
                        "suspend experience",
                        "suspend experience <key> <true/false>",
                        "Suspend/unsuspend an experience by its key.", HandleSuspendExperience);
            }
        }

        private void HandleCreateNewExperience(string module, string[] cmdparams)
        {
            string firstName;
            string lastName;
            string experienceKey;

            if (cmdparams.Length < 3)
                firstName = MainConsole.Instance.Prompt("Experience owner first name", "Test");
            else firstName = cmdparams[2];

            if (cmdparams.Length < 4)
                lastName = MainConsole.Instance.Prompt("Experience owner last name", "Resident");
            else lastName = cmdparams[3];

            if (cmdparams.Length < 5)
                experienceKey = MainConsole.Instance.Prompt("Experience Key (leave blank for random)", "");
            else experienceKey = cmdparams[4];

            UUID newExperienceKey;

            if (experienceKey == "")
                newExperienceKey = UUID.Random();
            else
            {
                if(!UUID.TryParse(experienceKey, out newExperienceKey))
                {
                    MainConsole.Instance.Output("Invalid UUID");
                    return;
                }
            }

            UserAccount account = m_UserService.GetUserAccount(UUID.Zero, firstName, lastName);
            if (account == null)
            {
                MainConsole.Instance.Output("No such user as {0} {1}", firstName, lastName);
                return;
            }

            var existing = GetExperienceInfos(new UUID[] { newExperienceKey });
            if(existing.Length > 0)
            {
                MainConsole.Instance.Output("Experience already exists!");
                return;
            }

            ExperienceInfo new_info = new ExperienceInfo
            {
                public_id = newExperienceKey,
                owner_id = account.PrincipalID
            };

            var stored_info = UpdateExperienceInfo(new_info);

            if (stored_info == null)
                MainConsole.Instance.Output("Unable to create experience!");
            else
            {
                MainConsole.Instance.Output("Experience created!");
            }
        }

        private void HandleSuspendExperience(string module, string[] cmdparams)
        {
            string experience_key;
            string enabled_str;

            if (cmdparams.Length < 3)
                experience_key = MainConsole.Instance.Prompt("Experience Key");
            else experience_key = cmdparams[2];

            UUID experienceID;
            if(!UUID.TryParse(experience_key, out experienceID))
            {
                MainConsole.Instance.Output("Invalid key!");
                return;
            }

            if (cmdparams.Length < 4)
                enabled_str = MainConsole.Instance.Prompt("Suspended:", "false");
            else enabled_str = cmdparams[3];

            bool suspend = enabled_str == "true";

            var infos = GetExperienceInfos(new UUID[] { experienceID });
            if(infos.Length != 1)
            {
                MainConsole.Instance.Output("Experience not found!");
                return;
            }

            ExperienceInfo info = infos[0];

            bool is_suspended = (info.properties & (int)ExperienceFlags.Suspended) != 0;

            string message = "";
            bool update = false;

            if (suspend && !is_suspended)
            {
                info.properties |= (int)ExperienceFlags.Suspended;
                message = "Experience has been suspended";
                update = true;
            }
            else if(!suspend && is_suspended)
            {
                info.properties &= ~(int)ExperienceFlags.Suspended;
                message = "Experience has been unsuspended";
                update = true;
            }
            else if(suspend && is_suspended)
            {
                message = "Experience is already suspended";
            }
            else if (!suspend && !is_suspended)
            {
                message = "Experience is not suspended";
            }

            if(update)
            {
                var updated = UpdateExperienceInfo(info);
                if (updated != null)
                {
                    MainConsole.Instance.Output(message);
                }
                else
                    MainConsole.Instance.Output("Error updating experience!");
            }
            else
            {
                MainConsole.Instance.Output(message);
            }
        }

        public Dictionary<UUID, bool> FetchExperiencePermissions(UUID agent_id)
        {
            return m_Database.GetExperiencePermissions(agent_id);
        }

        public ExperienceInfo[] FindExperiencesByName(string search)
        {
            search = (search ?? string.Empty).Trim();
            if (search.Length == 0 || search.Length > MAX_SEARCH_LENGTH)
                return Array.Empty<ExperienceInfo>();

            List<ExperienceInfo> infos = new List<ExperienceInfo>();
            ExperienceInfoData[] datas = m_Database.FindExperiences(search);

            foreach (var data in datas)
            {
                ExperienceInfo info = new ExperienceInfo(data.ToDictionary());
                if ((info.properties & (int)(ExperienceFlags.Invalid | ExperienceFlags.Private |
                    ExperienceFlags.Disabled | ExperienceFlags.Suspended)) != 0)
                    continue;

                infos.Add(info);
                if (infos.Count >= MAX_SEARCH_RESULTS)
                    break;
            }

            return infos.ToArray();
        }

        public UUID[] GetAgentExperiences(UUID agent_id)
        {
            return m_Database.GetAgentExperiences(agent_id);
        }

        public ExperienceInfo[] GetExperienceInfos(UUID[] experiences)
        {
            if (experiences == null || experiences.Length == 0)
                return Array.Empty<ExperienceInfo>();
            if (experiences.Length > 1000)
                experiences = experiences[..1000];

            ExperienceInfoData[] datas = m_Database.GetExperienceInfos(experiences);

            List<ExperienceInfo> infos = new List<ExperienceInfo>();

            foreach (var data in datas)
            {
                infos.Add(new ExperienceInfo(data.ToDictionary()));
            }

            return infos.ToArray();
        }

        public UUID[] GetExperiencesForGroups(UUID[] groups)
        {
            if (groups == null || groups.Length == 0)
                return Array.Empty<UUID>();
            if (groups.Length > 1000)
                groups = groups[..1000];
            return m_Database.GetExperiencesForGroups(groups);
        }

        public UUID[] GetGroupExperiences(UUID group_id)
        {
            return m_Database.GetGroupExperiences(group_id);
        }

        public ExperienceInfo UpdateExperienceInfo(ExperienceInfo info)
        {
            if (!IsValidExperienceInfo(info))
                return null;

            ExperienceInfo[] existing = GetExperienceInfos(new UUID[] { info.public_id });
            if (existing.Length > 0)
            {
                // Ownership is an identity property, not viewer-editable profile
                // data.  Reject replacement even from a trusted region caller.
                if (existing[0].owner_id != info.owner_id)
                    return null;
            }

            ExperienceInfoData data = new ExperienceInfoData();

            data.public_id = info.public_id;
            data.owner_id = info.owner_id;
            data.name = info.name;
            data.description = info.description;
            data.group_id = info.group_id;
            data.slurl = info.slurl;
            data.logo = info.logo;
            data.marketplace = info.marketplace;
            data.maturity = info.maturity;
            data.properties = info.properties;

            if (m_Database.UpdateExperienceInfo(data))
            {
                var find = GetExperienceInfos(new UUID[] { data.public_id });
                if(find.Length == 1)
                {
                    return new ExperienceInfo(find[0].ToDictionary());
                }
            }
            return null;
        }

        public bool UpdateExperiencePermissions(UUID agent_id, UUID experience, ExperiencePermission perm)
        {
            if (agent_id == UUID.Zero || !ExperienceExists(experience))
                return false;

            if (perm == ExperiencePermission.None)
                return m_Database.ForgetExperiencePermissions(agent_id, experience);
            else return m_Database.SetExperiencePermissions(agent_id, experience, perm == ExperiencePermission.Allowed);
        }

        public string GetKeyValue(UUID experience, string key)
        {
            if (!CanUseKeyValueStore(experience) || string.IsNullOrEmpty(key))
                return null;
            return m_Database.GetKeyValue(experience, key);
        }

        public string CreateKeyValue(UUID experience, string key, string value)
        {
            if (!CanUseKeyValueStore(experience) || string.IsNullOrEmpty(key) || value == null)
                return "error";

            lock (m_KeyValueLock)
            {
                int current_size = m_Database.GetKeyValueSize(experience);
                if ((long)current_size + Utf8Size(key) + Utf8Size(value) > MAX_QUOTA)
                    return "full";

                string get = m_Database.GetKeyValue(experience, key);
                if (get == null)
                    return m_Database.SetKeyValue(experience, key, value) ? "success" : "error";
                return "exists";
            }
        }

        public string UpdateKeyValue(UUID experience, string key, string val, bool check, string original)
        {
            if (!CanUseKeyValueStore(experience) || string.IsNullOrEmpty(key) || val == null || (check && original == null))
                return "error";

            lock (m_KeyValueLock)
            {
                string get = m_Database.GetKeyValue(experience, key);
                if (get == null)
                    return "missing";

                if (check && get != original)
                    return "mismatch";

                int current_size = m_Database.GetKeyValueSize(experience);
                if ((long)current_size - Utf8Size(get) + Utf8Size(val) > MAX_QUOTA)
                    return "full";

                return m_Database.SetKeyValue(experience, key, val) ? "success" : "error";
            }
        }

        public string DeleteKey(UUID experience, string key)
        {
            if (!CanUseKeyValueStore(experience) || string.IsNullOrEmpty(key))
                return "failed";

            lock (m_KeyValueLock)
            {
                string get = m_Database.GetKeyValue(experience, key);
                if (get != null)
                {
                    return m_Database.DeleteKey(experience, key) ? "success" : "failed";
                }
                return "missing";
            }
        }

        public int GetKeyCount(UUID experience)
        {
            if (!CanUseKeyValueStore(experience))
                return 0;
            return m_Database.GetKeyCount(experience);
        }

        public string[] GetKeys(UUID experience, int start, int count)
        {
            if (!CanUseKeyValueStore(experience) || start < 0 || count < 1 || count > 1000)
                return Array.Empty<string>();
            return m_Database.GetKeys(experience, start, count);
        }

        public int GetSize(UUID experience)
        {
            if (!CanUseKeyValueStore(experience))
                return 0;
            return m_Database.GetKeyValueSize(experience);
        }

        private bool ExperienceExists(UUID experience)
        {
            return experience != UUID.Zero && GetExperienceInfos(new UUID[] { experience }).Length == 1;
        }

        private bool CanUseKeyValueStore(UUID experience)
        {
            if (experience == UUID.Zero)
                return false;

            ExperienceInfo[] infos = GetExperienceInfos(new UUID[] { experience });
            return infos.Length == 1 &&
                (infos[0].properties & (int)(ExperienceFlags.Invalid | ExperienceFlags.Disabled | ExperienceFlags.Suspended)) == 0;
        }

        private static bool IsValidExperienceInfo(ExperienceInfo info)
        {
            if (info == null || info.public_id == UUID.Zero || info.owner_id == UUID.Zero)
                return false;

            info.name = (info.name ?? string.Empty).Trim();
            info.description ??= string.Empty;
            info.slurl ??= string.Empty;
            info.marketplace ??= string.Empty;

            return info.name.Length <= MAX_NAME_LENGTH &&
                info.description.Length <= MAX_DESCRIPTION_LENGTH &&
                info.slurl.Length <= MAX_SLURL_LENGTH &&
                info.marketplace.Length <= MAX_MARKETPLACE_LENGTH &&
                !ContainsControlCharacters(info.name) &&
                !ContainsControlCharacters(info.description) &&
                !ContainsControlCharacters(info.slurl) &&
                !ContainsControlCharacters(info.marketplace);
        }

        private static bool ContainsControlCharacters(string value)
        {
            foreach (char c in value)
            {
                if (char.IsControl(c))
                    return true;
            }
            return false;
        }

        private static int Utf8Size(string value) => Encoding.UTF8.GetByteCount(value);
    }
}
