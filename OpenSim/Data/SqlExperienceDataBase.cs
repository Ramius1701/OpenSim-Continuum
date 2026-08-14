using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Reflection;
using log4net;
using OpenMetaverse;
using OpenSim.Framework;

namespace OpenSim.Data
{
    public abstract class SqlExperienceDataBase : IExperienceData
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected abstract DbConnection CreateConnection();
        protected abstract string KeyValueSizeExpression { get; }

        protected SqlExperienceDataBase()
        {
        }

        public Dictionary<UUID, bool> GetExperiencePermissions(UUID agentID)
        {
            Dictionary<UUID, bool> permissions = new Dictionary<UUID, bool>();
            using DbConnection conn = OpenConnection();
            using DbCommand cmd = Command(conn,
                "SELECT experience, allow FROM experience_permissions WHERE avatar=@avatar",
                ("@avatar", agentID.ToString()));
            using DbDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (UUID.TryParse(Convert.ToString(reader.GetValue(0)), out UUID experienceID) &&
                    experienceID != UUID.Zero)
                {
                    permissions[experienceID] = Convert.ToBoolean(reader.GetValue(1));
                }
            }
            return permissions;
        }

        public bool ForgetExperiencePermissions(UUID agentID, UUID experienceID) =>
            Execute("DELETE FROM experience_permissions WHERE avatar=@avatar AND experience=@experience",
                ("@avatar", agentID.ToString()), ("@experience", experienceID.ToString())) > 0;

        public bool SetExperiencePermissions(UUID agentID, UUID experienceID, bool allow) =>
            Execute("INSERT INTO experience_permissions (experience, avatar, allow) " +
                "VALUES (@experience,@avatar,@allow) ON CONFLICT(experience,avatar) " +
                "DO UPDATE SET allow=excluded.allow",
                ("@experience", experienceID.ToString()), ("@avatar", agentID.ToString()),
                ("@allow", allow)) > 0;

        public ExperienceInfoData[] GetExperienceInfos(UUID[] experiences)
        {
            if (experiences == null || experiences.Length == 0)
                return Array.Empty<ExperienceInfoData>();
            return QueryExperienceInfos("public_id IN " + ParameterList(experiences, out var args), args);
        }

        public ExperienceInfoData[] FindExperiences(string search) =>
            QueryExperienceInfos("name LIKE @search LIMIT 1000", new[] { ("@search", (object)("%" + (search ?? string.Empty) + "%")) });

        public UUID[] GetAgentExperiences(UUID agentID) =>
            QueryIDs("SELECT public_id FROM experiences WHERE owner_id=@id", ("@id", agentID.ToString()));

        public UUID[] GetGroupExperiences(UUID groupID) =>
            QueryIDs("SELECT public_id FROM experiences WHERE group_id=@id", ("@id", groupID.ToString()));

        public UUID[] GetExperiencesForGroups(UUID[] groups)
        {
            if (groups == null || groups.Length == 0)
                return Array.Empty<UUID>();
            string list = ParameterList(groups, out var args);
            return QueryIDs("SELECT public_id FROM experiences WHERE group_id IN " + list, args);
        }

        public bool UpdateExperienceInfo(ExperienceInfoData data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return Execute(
                "INSERT INTO experiences (public_id,owner_id,name,description,group_id,logo,marketplace,slurl,maturity,properties) " +
                "VALUES (@public,@owner,@name,@description,@group,@logo,@marketplace,@slurl,@maturity,@properties) " +
                "ON CONFLICT(public_id) DO UPDATE SET owner_id=excluded.owner_id,name=excluded.name," +
                "description=excluded.description,group_id=excluded.group_id,logo=excluded.logo," +
                "marketplace=excluded.marketplace,slurl=excluded.slurl,maturity=excluded.maturity," +
                "properties=excluded.properties",
                ("@public", data.public_id.ToString()), ("@owner", data.owner_id.ToString()),
                ("@name", data.name ?? string.Empty), ("@description", data.description ?? string.Empty),
                ("@group", data.group_id.ToString()), ("@logo", data.logo.ToString()),
                ("@marketplace", data.marketplace ?? string.Empty), ("@slurl", data.slurl ?? string.Empty),
                ("@maturity", data.maturity), ("@properties", data.properties)) > 0;
        }

        public string GetKeyValue(UUID experience, string key)
        {
            using DbConnection conn = OpenConnection();
            using DbCommand cmd = Command(conn,
                "SELECT value FROM experience_kv WHERE experience=@experience AND key=@key",
                ("@experience", experience.ToString()), ("@key", key));
            object value = cmd.ExecuteScalar();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        public bool SetKeyValue(UUID experience, string key, string value) =>
            Execute("INSERT INTO experience_kv (experience,key,value) VALUES (@experience,@key,@value) " +
                "ON CONFLICT(experience,key) DO UPDATE SET value=excluded.value",
                ("@experience", experience.ToString()), ("@key", key), ("@value", value)) > 0;

        public bool DeleteKey(UUID experience, string key) =>
            Execute("DELETE FROM experience_kv WHERE experience=@experience AND key=@key",
                ("@experience", experience.ToString()), ("@key", key)) > 0;

        public int GetKeyCount(UUID experience) => Convert.ToInt32(Scalar(
            "SELECT COUNT(*) FROM experience_kv WHERE experience=@experience",
            ("@experience", experience.ToString())));

        public string[] GetKeys(UUID experience, int start, int count)
        {
            start = Math.Max(0, start);
            count = Math.Clamp(count, 0, 1000);
            List<string> keys = new List<string>();
            using DbConnection conn = OpenConnection();
            using DbCommand cmd = Command(conn,
                "SELECT key FROM experience_kv WHERE experience=@experience ORDER BY key LIMIT @count OFFSET @start",
                ("@experience", experience.ToString()), ("@count", count), ("@start", start));
            using DbDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
                keys.Add(reader.GetString(0));
            return keys.ToArray();
        }

        public int GetKeyValueSize(UUID experience) => Convert.ToInt32(Scalar(
            "SELECT COALESCE(SUM(" + KeyValueSizeExpression + "),0) FROM experience_kv WHERE experience=@experience",
            ("@experience", experience.ToString())));

        private ExperienceInfoData[] QueryExperienceInfos(string where,
            (string Name, object Value)[] args)
        {
            List<ExperienceInfoData> infos = new List<ExperienceInfoData>();
            using DbConnection conn = OpenConnection();
            using DbCommand cmd = Command(conn, "SELECT * FROM experiences WHERE " + where, args);
            using DbDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (TryReadExperienceInfo(reader, out ExperienceInfoData info))
                    infos.Add(info);
            }
            return infos.ToArray();
        }

        private static bool TryReadExperienceInfo(DbDataReader reader, out ExperienceInfoData info)
        {
            info = null;
            string publicID = Convert.ToString(reader["public_id"]);
            if (!UUID.TryParse(publicID, out UUID parsedPublicID) || parsedPublicID == UUID.Zero ||
                !UUID.TryParse(Convert.ToString(reader["owner_id"]), out UUID ownerID) || ownerID == UUID.Zero ||
                !UUID.TryParse(Convert.ToString(reader["group_id"]), out UUID groupID) ||
                !UUID.TryParse(Convert.ToString(reader["logo"]), out UUID logoID) ||
                !int.TryParse(Convert.ToString(reader["maturity"]), out int maturity) ||
                !int.TryParse(Convert.ToString(reader["properties"]), out int properties))
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
                name = Convert.ToString(reader["name"]),
                description = Convert.ToString(reader["description"]),
                logo = logoID,
                marketplace = Convert.ToString(reader["marketplace"]),
                slurl = Convert.ToString(reader["slurl"]),
                maturity = maturity,
                properties = properties
            };
            return true;
        }

        private UUID[] QueryIDs(string sql, params (string Name, object Value)[] args)
        {
            List<UUID> ids = new List<UUID>();
            using DbConnection conn = OpenConnection();
            using DbCommand cmd = Command(conn, sql, args);
            using DbDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (UUID.TryParse(Convert.ToString(reader.GetValue(0)), out UUID id) && id != UUID.Zero)
                    ids.Add(id);
            }
            return ids.ToArray();
        }

        private static string ParameterList(UUID[] ids, out (string Name, object Value)[] args)
        {
            args = new (string, object)[ids.Length];
            string[] names = new string[ids.Length];
            for (int i = 0; i < ids.Length; ++i)
            {
                names[i] = "@id" + i;
                args[i] = (names[i], ids[i].ToString());
            }
            return "(" + string.Join(",", names) + ")";
        }

        private int Execute(string sql, params (string Name, object Value)[] args)
        {
            using DbConnection conn = OpenConnection();
            using DbCommand cmd = Command(conn, sql, args);
            return cmd.ExecuteNonQuery();
        }

        private object Scalar(string sql, params (string Name, object Value)[] args)
        {
            using DbConnection conn = OpenConnection();
            using DbCommand cmd = Command(conn, sql, args);
            return cmd.ExecuteScalar();
        }

        private DbConnection OpenConnection()
        {
            DbConnection conn = CreateConnection();
            conn.Open();
            return conn;
        }

        private static DbCommand Command(DbConnection conn, string sql,
            params (string Name, object Value)[] args)
        {
            DbCommand cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach ((string name, object value) in args)
            {
                DbParameter parameter = cmd.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value ?? DBNull.Value;
                cmd.Parameters.Add(parameter);
            }
            return cmd;
        }
    }
}
