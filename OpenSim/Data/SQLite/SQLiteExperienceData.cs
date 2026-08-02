using System.Data.Common;
using System.Data.SQLite;

namespace OpenSim.Data.SQLite
{
    public class SQLiteExperienceData : SqlExperienceDataBase
    {
        private readonly string m_connectionString;

        public SQLiteExperienceData(string connectionString)
        {
            m_connectionString = connectionString;
            using SQLiteConnection conn = new SQLiteConnection(m_connectionString);
            conn.Open();
            new Migration(conn, GetType().Assembly, "Experience").Update();
        }

        protected override DbConnection CreateConnection() =>
            new SQLiteConnection(m_connectionString);
    }
}
