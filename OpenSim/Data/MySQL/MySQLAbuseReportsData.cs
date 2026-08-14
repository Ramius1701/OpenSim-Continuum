using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Linq;
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

        public AbuseReportData Get(int reportID, bool includeImage)
        {
            AbuseReportData[] reports;
            if (includeImage)
            {
                reports = Get(nameof(AbuseReportData.ReportID), reportID.ToString());
            }
            else
            {
                string columns = string.Join(",", m_Fields.Keys
                    .Where(name => name != nameof(AbuseReportData.ImageData))
                    .Select(name => "`" + name + "`"));
                using MySqlCommand cmd = new MySqlCommand(
                    $"SELECT {columns}, CAST('' AS BINARY) AS `ImageData` FROM `AbuseReports` WHERE `ReportID`=?ReportID LIMIT 1");
                cmd.Parameters.AddWithValue("ReportID", reportID);
                reports = DoQuery(cmd);
            }
            if (reports.Length == 0)
                return null;
            return reports[0];
        }

        public AbuseReportData[] Get(int start, int count, string status)
        {
            start = Math.Max(0, start);
            count = Math.Clamp(count, 1, 200);
            string columns = string.Join(",", m_Fields.Keys
                .Where(name => name != nameof(AbuseReportData.ImageData))
                .Select(name => "`" + name + "`"));
            using MySqlCommand cmd = new MySqlCommand();
            cmd.CommandText = $"SELECT {columns}, CAST('' AS BINARY) AS `ImageData` " +
                "FROM `AbuseReports` " +
                (string.IsNullOrWhiteSpace(status) ? string.Empty : "WHERE `Status`=?Status ") +
                $"ORDER BY `ReportID` DESC LIMIT {start},{count}";
            if (!string.IsNullOrWhiteSpace(status))
                cmd.Parameters.AddWithValue("Status", status);
            return DoQuery(cmd);
        }

        public bool UpdateModeration(int reportID, string status, string notes,
            UUID moderatorID, string moderatorName, int lastUpdated)
        {
            using MySqlCommand cmd = new MySqlCommand();
            cmd.CommandText = "UPDATE `AbuseReports` SET `Status`=?Status, " +
                "`ModeratorNotes`=?ModeratorNotes, `ModeratorID`=?ModeratorID, " +
                "`ModeratorName`=?ModeratorName, `LastUpdated`=?LastUpdated " +
                "WHERE `ReportID`=?ReportID";
            cmd.Parameters.AddWithValue("Status", status ?? string.Empty);
            cmd.Parameters.AddWithValue("ModeratorNotes", notes ?? string.Empty);
            cmd.Parameters.AddWithValue("ModeratorID", moderatorID.ToString());
            cmd.Parameters.AddWithValue("ModeratorName", moderatorName ?? string.Empty);
            cmd.Parameters.AddWithValue("LastUpdated", lastUpdated);
            cmd.Parameters.AddWithValue("ReportID", reportID);
            return ExecuteNonQuery(cmd) > 0;
        }
    }
}
