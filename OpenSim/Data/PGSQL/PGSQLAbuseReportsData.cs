using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Linq;
using Npgsql;
using OpenMetaverse;
using OpenSim.Framework;

namespace OpenSim.Data.PGSQL
{
    public class PGSQLAbuseReportsData : PGSQLGenericTableHandler<AbuseReportData>, IAbuseReportsData
    {
        public PGSQLAbuseReportsData(string connectionString)
            : base(connectionString, "AbuseReports", "AbuseReports")
        {
        }

        public override bool Store(AbuseReportData row)
        {
            ArgumentNullException.ThrowIfNull(row);
            using NpgsqlConnection conn = new NpgsqlConnection(m_ConnectionString);
            using NpgsqlCommand cmd = new NpgsqlCommand();
            List<string> names = new List<string>();
            List<string> values = new List<string>();

            foreach (FieldInfo field in m_Fields.Values)
            {
                if (field.Name == nameof(AbuseReportData.ReportID))
                    continue;
                names.Add("\"" + field.Name + "\"");
                values.Add(":" + field.Name);
                object value = field.GetValue(row);
                if (value is UUID uuid)
                    value = uuid.ToString();
                cmd.Parameters.AddWithValue(field.Name, value ?? DBNull.Value);
            }

            cmd.Connection = conn;
            cmd.CommandText = $"INSERT INTO AbuseReports ({string.Join(",", names)}) " +
                $"VALUES ({string.Join(",", values)}) RETURNING \"ReportID\"";
            conn.Open();
            row.ReportID = Convert.ToInt32(cmd.ExecuteScalar());
            return row.ReportID > 0;
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
                    .Select(name => "\"" + name + "\""));
                using NpgsqlCommand cmd = new NpgsqlCommand(
                    $"SELECT {columns}, '\\x'::bytea AS \"ImageData\" FROM AbuseReports WHERE \"ReportID\"=:ReportID LIMIT 1");
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
                .Select(name => "\"" + name + "\""));
            using NpgsqlCommand cmd = new NpgsqlCommand();
            cmd.CommandText = $"SELECT {columns}, '\\x'::bytea AS \"ImageData\" " +
                "FROM AbuseReports " +
                (string.IsNullOrWhiteSpace(status) ? string.Empty : "WHERE \"Status\"=:Status ") +
                $"ORDER BY \"ReportID\" DESC LIMIT {count} OFFSET {start}";
            if (!string.IsNullOrWhiteSpace(status))
                cmd.Parameters.AddWithValue("Status", status);
            return DoQuery(cmd);
        }

        public bool UpdateModeration(int reportID, string status, string notes,
            UUID moderatorID, string moderatorName, int lastUpdated)
        {
            using NpgsqlConnection conn = new NpgsqlConnection(m_ConnectionString);
            using NpgsqlCommand cmd = new NpgsqlCommand(
                "UPDATE AbuseReports SET \"Status\"=:Status, \"ModeratorNotes\"=:Notes, " +
                "\"ModeratorID\"=:ModeratorID, \"ModeratorName\"=:ModeratorName, " +
                "\"LastUpdated\"=:LastUpdated WHERE \"ReportID\"=:ReportID", conn);
            cmd.Parameters.AddWithValue("Status", status ?? string.Empty);
            cmd.Parameters.AddWithValue("Notes", notes ?? string.Empty);
            cmd.Parameters.AddWithValue("ModeratorID", moderatorID.ToString());
            cmd.Parameters.AddWithValue("ModeratorName", moderatorName ?? string.Empty);
            cmd.Parameters.AddWithValue("LastUpdated", lastUpdated);
            cmd.Parameters.AddWithValue("ReportID", reportID);
            conn.Open();
            return cmd.ExecuteNonQuery() > 0;
        }
    }
}
