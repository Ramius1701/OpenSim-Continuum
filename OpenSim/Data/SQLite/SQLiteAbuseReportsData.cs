using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Data.SQLite;
using OpenMetaverse;
using OpenSim.Framework;

namespace OpenSim.Data.SQLite
{
    public class SQLiteAbuseReportsData : SQLiteGenericTableHandler<AbuseReportData>, IAbuseReportsData
    {
        public SQLiteAbuseReportsData(string connectionString)
            : base(connectionString, "AbuseReports", "AbuseReports")
        {
        }

        public override bool Store(AbuseReportData row)
        {
            ArgumentNullException.ThrowIfNull(row);
            using SQLiteCommand cmd = new SQLiteCommand();
            List<string> names = new List<string>();
            List<string> values = new List<string>();

            foreach (FieldInfo field in m_Fields.Values)
            {
                if (field.Name == nameof(AbuseReportData.ReportID))
                    continue;
                names.Add(field.Name);
                values.Add(":" + field.Name);
                object value = field.GetValue(row);
                if (value is UUID uuid)
                    value = uuid.ToString();
                cmd.Parameters.Add(new SQLiteParameter(":" + field.Name, value ?? DBNull.Value));
            }

            cmd.CommandText = $"INSERT INTO `{m_Realm}` (`{string.Join("`,`", names)}`) " +
                $"VALUES ({string.Join(",", values)})";
            if (ExecuteNonQuery(cmd, m_Connection) <= 0)
                return false;
            row.ReportID = (int)m_Connection.LastInsertRowId;
            return true;
        }

        public AbuseReportData Get(int reportID, bool includeImage)
        {
            AbuseReportData[] reports = Get(nameof(AbuseReportData.ReportID), reportID.ToString());
            if (reports.Length == 0)
                return null;
            if (!includeImage)
                reports[0].ImageData = Array.Empty<byte>();
            return reports[0];
        }

        public AbuseReportData[] Get(int start, int count, string status)
        {
            start = Math.Max(0, start);
            count = Math.Clamp(count, 1, 200);
            string options = $"ORDER BY `ReportID` DESC LIMIT {count} OFFSET {start}";
            AbuseReportData[] reports = string.IsNullOrWhiteSpace(status)
                ? Get("1=1 " + options)
                : Get("`Status` = :Status " + options,
                    new SQLiteParameter(":Status", status));
            foreach (AbuseReportData report in reports)
                report.ImageData = Array.Empty<byte>();
            return reports;
        }

        public bool UpdateModeration(int reportID, string status, string notes,
            UUID moderatorID, string moderatorName, int lastUpdated)
        {
            using SQLiteCommand cmd = new SQLiteCommand(
                "UPDATE AbuseReports SET Status=:Status, ModeratorNotes=:Notes, " +
                "ModeratorID=:ModeratorID, ModeratorName=:ModeratorName, LastUpdated=:LastUpdated " +
                "WHERE ReportID=:ReportID");
            cmd.Parameters.AddWithValue(":Status", status ?? string.Empty);
            cmd.Parameters.AddWithValue(":Notes", notes ?? string.Empty);
            cmd.Parameters.AddWithValue(":ModeratorID", moderatorID.ToString());
            cmd.Parameters.AddWithValue(":ModeratorName", moderatorName ?? string.Empty);
            cmd.Parameters.AddWithValue(":LastUpdated", lastUpdated);
            cmd.Parameters.AddWithValue(":ReportID", reportID);
            return ExecuteNonQuery(cmd, m_Connection) > 0;
        }
    }
}
