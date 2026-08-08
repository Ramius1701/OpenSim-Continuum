using System;
using System.Collections.Generic;
using Npgsql;
using OpenMetaverse;

namespace OpenSim.Data.PGSQL
{
    public class PGSQLUserAliasData : PGSQLGenericTableHandler<UserAliasData>, IUserAliasData
    {
        public PGSQLUserAliasData(string connectionString, string realm)
            : base(connectionString, realm, "UserAlias")
        {
        }

        public override bool Store(UserAliasData data)
        {
            ArgumentNullException.ThrowIfNull(data);

            using NpgsqlConnection conn = new NpgsqlConnection(m_ConnectionString);
            using NpgsqlCommand cmd = new NpgsqlCommand(
                $"INSERT INTO {m_Realm} (\"AliasID\",\"UserID\",\"Description\") " +
                "VALUES (:AliasID,:UserID,:Description) RETURNING \"Id\"", conn);
            cmd.Parameters.AddWithValue("AliasID", Guid.Parse(data.AliasID.ToString()));
            cmd.Parameters.AddWithValue("UserID", Guid.Parse(data.UserID.ToString()));
            cmd.Parameters.AddWithValue("Description", data.Description ?? string.Empty);
            conn.Open();
            data.Id = Convert.ToInt32(cmd.ExecuteScalar());
            return data.Id > 0;
        }

        public UserAliasData Get(int id) => GetOne("Id", id.ToString());

        public UserAliasData GetUserForAlias(UUID aliasID) =>
            GetOne("AliasID", aliasID.ToString());

        public List<UserAliasData> GetUserAliases(UUID userID)
        {
            UserAliasData[] aliases = Get("UserID", userID.ToString());
            return aliases.Length == 0 ? null : new List<UserAliasData>(aliases);
        }

        private UserAliasData GetOne(string field, string value)
        {
            UserAliasData[] aliases = Get(field, value);
            return aliases.Length == 0 ? null : aliases[0];
        }
    }
}
