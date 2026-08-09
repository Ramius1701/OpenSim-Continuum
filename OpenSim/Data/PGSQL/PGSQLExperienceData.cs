using System.Data.Common;
using Npgsql;

namespace OpenSim.Data.PGSQL
{
    public class PGSQLExperienceData : SqlExperienceDataBase
    {
        private readonly string m_connectionString;

        public PGSQLExperienceData(string connectionString)
        {
            m_connectionString = connectionString;
            using NpgsqlConnection conn = new NpgsqlConnection(m_connectionString);
            conn.Open();
            new Migration(conn, GetType().Assembly, "Experience").Update();
        }

        protected override DbConnection CreateConnection() =>
            new NpgsqlConnection(m_connectionString);

        protected override string KeyValueSizeExpression =>
            "OCTET_LENGTH(key) + OCTET_LENGTH(value)";
    }
}
