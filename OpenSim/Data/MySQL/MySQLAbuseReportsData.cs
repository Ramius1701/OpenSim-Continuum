using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using MySql.Data.MySqlClient;
using OpenMetaverse;
using OpenSim.Framework;

namespace OpenSim.Data.MySQL
{
    public class MySqlAbuseReportsData : MySQLGenericTableHandler<AbuseReportData>, IAbuseReportsData
    {
        public MySqlAbuseReportsData(string connectionString)
            : base(connectionString, "AbuseReports", "AbuseReports")
        {
        }

        public override bool Store(AbuseReportData row)
        {
            if (row == null)
                throw new ArgumentNullException(nameof(row));

            using MySqlCommand cmd = new MySqlCommand();
            List<string> names = new List<string>();
            List<string> values = new List<string>();

            foreach (FieldInfo field in m_Fields.Values)
            {
                if (field.Name == nameof(AbuseReportData.ReportID))
                    continue;

                names.Add(field.Name);
                values.Add("?" + field.Name);

                object value = field.GetValue(row);

                if (field.Name == nameof(AbuseReportData.ImageData))
                {
                    cmd.Parameters.Add(field.Name, MySqlDbType.Blob).Value =
                        value as byte[] ?? Array.Empty<byte>();
                }
                else if (value is UUID uuid)
                {
                    cmd.Parameters.AddWithValue(field.Name, uuid.ToString());
                }
                else
                {
                    cmd.Parameters.AddWithValue(field.Name, value ?? DBNull.Value);
                }
            }

            cmd.CommandText = string.Format(
                "INSERT INTO `{0}` (`{1}`) VALUES ({2})",
                m_Realm,
                string.Join("`,`", names),
                string.Join(",", values));

            if (ExecuteNonQuery(cmd) <= 0)
                return false;

            if (cmd.LastInsertedId > 0 && cmd.LastInsertedId <= int.MaxValue)
                row.ReportID = (int)cmd.LastInsertedId;

            return true;
        }
    }
}
