/* Copyright (c) OpenSimulator contributors. BSD-3-Clause; see LICENSE.txt. */
using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Nwc.XmlRpc;

namespace ContinuumSearch.Service
{
    internal sealed partial class SearchStore
    {
        internal ArrayList EventNotifications(Hashtable input)
        {
            if (!Guid.TryParse(input["actor"]?.ToString(), out Guid actor) || actor == Guid.Empty) throw new ArgumentException("Invalid subscriber");
            string action = input["action"]?.ToString();
            string owner = actor.ToString(); long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (action is not ("list" or "add" or "remove")) throw new ArgumentException("Unknown subscription operation");
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            Execute(connection, transaction, "DELETE FROM continuum_event_notifications WHERE actor=@actor AND eventid NOT IN (SELECT eventid FROM search_events WHERE dateUTC>=@now)", ("@actor", owner), ("@now", now));
            if (action != "list")
            {
                if (!int.TryParse(input["event"]?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id < 1) throw new ArgumentException("Invalid event");
                if (action == "remove") Execute(connection, transaction, "DELETE FROM continuum_event_notifications WHERE actor=@actor AND eventid=@id", ("@actor", owner), ("@id", id));
                else
                {
                    using var exists = Command(connection, "SELECT COUNT(*) FROM search_events WHERE eventid=@id AND dateUTC>=@now", ("@id", id), ("@now", now)); exists.Transaction = transaction;
                    if (Convert.ToInt32(exists.ExecuteScalar()) != 1) throw new ArgumentException("Event is unavailable or has started");
                    using var duplicate = Command(connection, "SELECT COUNT(*) FROM continuum_event_notifications WHERE actor=@actor AND eventid=@id", ("@actor", owner), ("@id", id)); duplicate.Transaction = transaction;
                    if (Convert.ToInt32(duplicate.ExecuteScalar()) == 0)
                    {
                        using var count = Command(connection, "SELECT COUNT(*) FROM continuum_event_notifications WHERE actor=@actor", ("@actor", owner)); count.Transaction = transaction;
                        if (Convert.ToInt32(count.ExecuteScalar()) >= 100) throw new ArgumentException("Event reminder limit reached");
                        string insert = "INSERT INTO continuum_event_notifications(actor,eventid) VALUES(@actor,@id)";
                        insert = Provider == "MySQL" ? insert.Replace("INSERT INTO", "INSERT IGNORE INTO") : insert + " ON CONFLICT(actor,eventid) DO NOTHING";
                        Execute(connection, transaction, insert, ("@actor", owner), ("@id", id));
                    }
                }
            }
            var rows = new ArrayList();
            using (var command = Command(connection, "SELECT e.eventid,e.name,e.description,e.dateUTC FROM continuum_event_notifications n JOIN search_events e ON e.eventid=n.eventid WHERE n.actor=@actor AND e.dateUTC>=@now ORDER BY e.dateUTC,e.eventid LIMIT 100", ("@actor", owner), ("@now", now)))
            {
                command.Transaction = transaction;
                using var reader = command.ExecuteReader();
                while (reader.Read()) rows.Add(new Hashtable { ["event_id"] = Convert.ToInt32(reader.GetValue(0)), ["event_name"] = reader.GetString(1),
                    ["event_date_ut"] = Convert.ToInt64(reader.GetValue(3)).ToString(CultureInfo.InvariantCulture) });
            }
            transaction.Commit(); return rows;
        }
    }
    internal sealed partial class SearchRpc
    {
        private readonly string m_notificationSecret;
        internal XmlRpcResponse Notifications(XmlRpcRequest request, IPEndPoint remote)
        {
            var input = Args(request); string supplied = input["notificationSecret"]?.ToString() ?? "";
            if (m_notificationSecret.Length == 0 || supplied.Length > 256 || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(m_notificationSecret)))
                return new XmlRpcResponse { Value = new Hashtable { ["success"] = false, ["errorMessage"] = "Not authorized" } };
            return Invoke(() => m_store.EventNotifications(input));
        }
    }
}
