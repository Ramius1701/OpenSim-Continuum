using System.Reflection;
using System.Data;
using MySql.Data.MySqlClient;
using OpenSim.Framework;
using System.Collections.Generic;
using OpenMetaverse;
using OpenMetaverse.Packets;
using System.Linq;
using log4net;

namespace OpenSim.Data.MySQL
{
    public class MySqlExperienceData : MySqlFramework, IExperienceData
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected virtual Assembly Assembly
        {
            get { return GetType().Assembly; }
        }

        public MySqlExperienceData(string connectionString)
                : base(connectionString)
        {
            m_connectionString = connectionString;

            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();
                Migration m = new Migration(dbcon, Assembly, "Experience");
                m.Update();
                dbcon.Close();
            }
        }

        public Dictionary<UUID, bool> GetExperiencePermissions(UUID agent_id)
        {
            Dictionary<UUID, bool> experiencePermissions = new Dictionary<UUID, bool>();

            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                using (MySqlCommand cmd
                    = new MySqlCommand("select * from `experience_permissions` where avatar = ?avatar", dbcon))
                {
                    cmd.Parameters.AddWithValue("?avatar", agent_id.ToString());
                    
                    using (IDataReader result = cmd.ExecuteReader())
                    {
                        while (result.Read())
                        {
                            string uuid = result.GetString(0);
                            bool allow = result.GetBoolean(2);

                            UUID experience_key;
                            if(UUID.TryParse(uuid, out experience_key) && experience_key != UUID.Zero)
                            {
                                experiencePermissions[experience_key] = allow;
                            }
                        }

                        dbcon.Close();
                    }
                }
            }

            return experiencePermissions;
        }

        public bool ForgetExperiencePermissions(UUID agent_id, UUID experience_id)
        {
            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                using (MySqlCommand cmd
                    = new MySqlCommand("delete from `experience_permissions` where avatar = ?avatar AND experience = ?experience LIMIT 1", dbcon))
                {
                    cmd.Parameters.AddWithValue("?avatar", agent_id.ToString());
                    cmd.Parameters.AddWithValue("?experience", experience_id.ToString());

                    return (cmd.ExecuteNonQuery() > 0);
                }
            }
        }

        public bool SetExperiencePermissions(UUID agent_id, UUID experience_id, bool allow)
        {
            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                using (MySqlCommand cmd = new MySqlCommand("replace into `experience_permissions` (avatar, experience, allow) VALUES (?avatar, ?experience, ?allow)", dbcon))
                {
                    cmd.Parameters.AddWithValue("?avatar", agent_id.ToString());
                    cmd.Parameters.AddWithValue("?experience", experience_id.ToString());
                    cmd.Parameters.AddWithValue("?allow", allow);

                    return (cmd.ExecuteNonQuery() > 0);
                }
            }
        }

        public ExperienceInfoData[] GetExperienceInfos(UUID[] experiences)
        {
            if (experiences == null || experiences.Length == 0)
                return System.Array.Empty<ExperienceInfoData>();

            List<string> parameters = new List<string>();
            for (int i = 0; i < experiences.Length; ++i)
                parameters.Add("?id" + i);
            string joined = string.Join(",", parameters);

            List<ExperienceInfoData> infos = new List<ExperienceInfoData>();

            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                using (MySqlCommand cmd = new MySqlCommand("SELECT * FROM `experiences` WHERE public_id IN (" + joined + ")", dbcon))
                {
                    for (int i = 0; i < experiences.Length; ++i)
                        cmd.Parameters.AddWithValue("?id" + i, experiences[i].ToString());

                    using (IDataReader result = cmd.ExecuteReader())
                    {
                        while (result.Read())
                        {
                            if (TryReadExperienceInfo(result, out ExperienceInfoData info))
                                infos.Add(info);
                        }
                    }
                }

                dbcon.Close();
            }

            return infos.ToArray();
        }

        public UUID[] GetAgentExperiences(UUID agent_id)
        {
            List<UUID> experiences = new List<UUID>();

            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                using (MySqlCommand cmd = new MySqlCommand("SELECT * FROM `experiences` WHERE owner_id = ?avatar", dbcon))
                {
                    cmd.Parameters.AddWithValue("?avatar", agent_id.ToString());
                    using (IDataReader result = cmd.ExecuteReader())
                    {
                        while (result.Read())
                        {
                            if (UUID.TryParse(result["public_id"].ToString(), out UUID experienceID) &&
                                experienceID != UUID.Zero)
                            {
                                experiences.Add(experienceID);
                            }
                        }
                    }
                }

                dbcon.Close();
            }

            return experiences.ToArray();
        }

        public bool UpdateExperienceInfo(ExperienceInfoData data)
        {
            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                using (MySqlCommand cmd = new MySqlCommand(
                    "INSERT INTO `experiences` (public_id, owner_id, name, description, group_id, logo, marketplace, slurl, maturity, properties) " +
                    "VALUES (?public_id, ?owner_id, ?name, ?description, ?group_id, ?logo, ?marketplace, ?slurl, ?maturity, ?properties) " +
                    "ON DUPLICATE KEY UPDATE owner_id=VALUES(owner_id), name=VALUES(name), description=VALUES(description), " +
                    "group_id=VALUES(group_id), logo=VALUES(logo), marketplace=VALUES(marketplace), slurl=VALUES(slurl), " +
                    "maturity=VALUES(maturity), properties=VALUES(properties)", dbcon))
                {
                    cmd.Parameters.AddWithValue("?public_id", data.public_id.ToString());
                    cmd.Parameters.AddWithValue("?owner_id", data.owner_id.ToString());
                    cmd.Parameters.AddWithValue("?name", data.name ?? string.Empty);
                    cmd.Parameters.AddWithValue("?description", data.description ?? string.Empty);
                    cmd.Parameters.AddWithValue("?group_id", data.group_id.ToString());
                    cmd.Parameters.AddWithValue("?logo", data.logo.ToString());
                    cmd.Parameters.AddWithValue("?marketplace", data.marketplace ?? string.Empty);
                    cmd.Parameters.AddWithValue("?slurl", data.slurl ?? string.Empty);
                    cmd.Parameters.AddWithValue("?maturity", data.maturity);
                    cmd.Parameters.AddWithValue("?properties", data.properties);

                    return (cmd.ExecuteNonQuery() > 0);
                }
            }
        }

        public ExperienceInfoData[] FindExperiences(string search)
        {
            List<ExperienceInfoData> experiences = new List<ExperienceInfoData>();

            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                using (MySqlCommand cmd = new MySqlCommand("SELECT * FROM `experiences` WHERE name LIKE ?search LIMIT 1000", dbcon))
                {
                    cmd.Parameters.AddWithValue("?search", string.Format("%{0}%", search ?? string.Empty));

                    using (IDataReader result = cmd.ExecuteReader())
                    {
                        while (result.Read())
                        {
                            if (TryReadExperienceInfo(result, out ExperienceInfoData info))
                                experiences.Add(info);
                        }
                    }
                }

                dbcon.Close();
            }

            return experiences.ToArray();
        }

        private static bool TryReadExperienceInfo(IDataRecord result, out ExperienceInfoData info)
        {
            info = null;
            string publicID = result["public_id"].ToString();
            if (!UUID.TryParse(publicID, out UUID parsedPublicID) || parsedPublicID == UUID.Zero ||
                !UUID.TryParse(result["owner_id"].ToString(), out UUID ownerID) || ownerID == UUID.Zero ||
                !UUID.TryParse(result["group_id"].ToString(), out UUID groupID) ||
                !UUID.TryParse(result["logo"].ToString(), out UUID logoID) ||
                !int.TryParse(result["maturity"].ToString(), out int maturity) ||
                !int.TryParse(result["properties"].ToString(), out int properties))
            {
                m_log.WarnFormat(
                    "[EXPERIENCE DATA]: Ignoring malformed experience profile row {0}.",
                    string.IsNullOrEmpty(publicID) ? "(missing public_id)" : publicID);
                return false;
            }

            info = new ExperienceInfoData
            {
                public_id = parsedPublicID,
                owner_id = ownerID,
                group_id = groupID,
                name = result["name"].ToString(),
                description = result["description"].ToString(),
                logo = logoID,
                marketplace = result["marketplace"].ToString(),
                slurl = result["slurl"].ToString(),
                maturity = maturity,
                properties = properties
            };
            return true;
        }

        public UUID[] GetGroupExperiences(UUID group_id)
        {
            List<UUID> experiences = new List<UUID>();

            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                using (MySqlCommand cmd = new MySqlCommand("SELECT * FROM `experiences` WHERE group_id = ?group", dbcon))
                {
                    cmd.Parameters.AddWithValue("?group", group_id.ToString());
                    using (IDataReader result = cmd.ExecuteReader())
                    {
                        while (result.Read())
                        {
                            if (UUID.TryParse(result["public_id"].ToString(), out UUID experienceID) &&
                                experienceID != UUID.Zero)
                            {
                                experiences.Add(experienceID);
                            }
                        }
                    }
                }

                dbcon.Close();
            }

            return experiences.ToArray();
        }

        public UUID[] GetExperiencesForGroups(UUID[] groups)
        {
            if (groups == null || groups.Length == 0)
                return System.Array.Empty<UUID>();

            List<string> parameters = new List<string>();
            for (int i = 0; i < groups.Length; ++i)
                parameters.Add("?id" + i);
            string joined = string.Join(",", parameters);

            List<UUID> experiences = new List<UUID>();

            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                using (MySqlCommand cmd = new MySqlCommand("SELECT * FROM `experiences` WHERE group_id IN (" + joined + ")", dbcon))
                {
                    for (int i = 0; i < groups.Length; ++i)
                        cmd.Parameters.AddWithValue("?id" + i, groups[i].ToString());

                    using (IDataReader result = cmd.ExecuteReader())
                    {
                        while (result.Read())
                        {
                            if (UUID.TryParse(result["public_id"].ToString(), out UUID experienceID) &&
                                experienceID != UUID.Zero)
                            {
                                experiences.Add(experienceID);
                            }
                        }
                    }
                }

                dbcon.Close();
            }

            return experiences.ToArray();
        }

        // KeyValue


        public bool SetKeyValue(UUID experience, string key, string val)
        {
            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                using (MySqlCommand cmd = new MySqlCommand(@"
INSERT INTO `experience_kv` (`experience`, `key_hash`, `key`, `value`)
VALUES (?experience, UNHEX(SHA2(?key, 256)), ?key, ?value)
ON DUPLICATE KEY UPDATE `key` = ?key, `value` = ?value", dbcon))
                {
                    cmd.Parameters.AddWithValue("?experience", experience.ToString());
                    cmd.Parameters.AddWithValue("?key", key);
                    cmd.Parameters.AddWithValue("?value", val);

                    return (cmd.ExecuteNonQuery() > 0);
                }
            }
        }

        public string GetKeyValue(UUID experience, string key)
        {
            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                using (MySqlCommand cmd = new MySqlCommand("SELECT * FROM `experience_kv` WHERE `experience` = ?experience AND `key` = ?key LIMIT 1", dbcon))
                {
                    cmd.Parameters.AddWithValue("?experience", experience.ToString());
                    cmd.Parameters.AddWithValue("?key", key);

                    using (IDataReader result = cmd.ExecuteReader())
                    {
                        if (result.Read())
                        {
                            return result["value"].ToString();
                        }
                    }
                }

                dbcon.Close();
            }

            return null;
        }

        public bool DeleteKey(UUID experience, string key)
        {
            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                using (MySqlCommand cmd = new MySqlCommand("DELETE FROM `experience_kv` WHERE `experience` = ?experience AND `key` = ?key LIMIT 1", dbcon))
                {
                    cmd.Parameters.AddWithValue("?experience", experience.ToString());
                    cmd.Parameters.AddWithValue("?key", key);

                    return (cmd.ExecuteNonQuery() > 0);
                }
            }
        }

        public int GetKeyCount(UUID experience)
        {
            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                using (MySqlCommand cmd = new MySqlCommand("SELECT COUNT(*) AS `count` FROM `experience_kv` WHERE `experience` = ?experience", dbcon))
                {
                    cmd.Parameters.AddWithValue("?experience", experience.ToString());

                    using (IDataReader result = cmd.ExecuteReader())
                    {
                        if (result.Read())
                        {
                            return int.Parse(result["count"].ToString());
                        }
                    }
                }

                dbcon.Close();
            }

            return 0;
        }

        public string[] GetKeys(UUID experience, int start, int count)
        {
            start = System.Math.Max(0, start);
            count = System.Math.Clamp(count, 0, 1000);
            List<string> keys = new List<string>();
            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                using (MySqlCommand cmd = new MySqlCommand("SELECT `key` FROM `experience_kv` WHERE `experience` = ?experience ORDER BY `key` LIMIT ?start, ?count;", dbcon))
                {
                    cmd.Parameters.AddWithValue("?experience", experience.ToString());
                    cmd.Parameters.AddWithValue("?start", start);
                    cmd.Parameters.AddWithValue("?count", count);

                    using (IDataReader result = cmd.ExecuteReader())
                    {
                        while (result.Read())
                        {
                            keys.Add(result["key"].ToString());
                        }
                    }
                }

                dbcon.Close();
            }
            return keys.ToArray();
        }

        public int GetKeyValueSize(UUID experience)
        {
            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                using (MySqlCommand cmd = new MySqlCommand("SELECT IFNULL(SUM(LENGTH(`key`) + LENGTH(`value`)), 0) AS `size` FROM `experience_kv` WHERE `experience` = ?experience", dbcon))
                {
                    cmd.Parameters.AddWithValue("?experience", experience.ToString());

                    using (IDataReader result = cmd.ExecuteReader())
                    {
                        if (result.Read())
                        {
                            return int.Parse(result["size"].ToString());
                        }
                    }
                }

                dbcon.Close();
            }

            return 0;
        }
    }
}
