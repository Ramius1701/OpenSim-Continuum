using System;
using System.Collections.Generic;
using System.Data.SQLite;
using OpenMetaverse;

namespace OpenSim.Data.SQLite
{
    public class SQLiteUserAliasData : SQLiteGenericTableHandler<UserAliasData>, IUserAliasData
    {
        public SQLiteUserAliasData(string connectionString, string realm)
            : base(connectionString, realm, "UserAlias")
        {
        }

        public override bool Store(UserAliasData data)
        {
            ArgumentNullException.ThrowIfNull(data);

            using SQLiteCommand cmd = new SQLiteCommand(
                $"INSERT INTO `{m_Realm}` (`AliasID`,`UserID`,`Description`) " +
                "VALUES (:AliasID,:UserID,:Description)", m_Connection);
            cmd.Parameters.AddWithValue(":AliasID", data.AliasID.ToString());
            cmd.Parameters.AddWithValue(":UserID", data.UserID.ToString());
            cmd.Parameters.AddWithValue(":Description", data.Description ?? string.Empty);
            if (cmd.ExecuteNonQuery() <= 0)
                return false;

            data.Id = (int)m_Connection.LastInsertRowId;
            return true;
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
