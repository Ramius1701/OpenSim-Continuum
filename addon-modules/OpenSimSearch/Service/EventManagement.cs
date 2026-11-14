using System;
using System.Collections;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ContinuumSearch.Service
{
    internal sealed partial class SearchStore
    {
        internal void EnsureEventManagementSchema()
        {
            using DbConnection connection = OpenConnection();
            try { Execute(connection, null, "ALTER TABLE search_events ADD COLUMN revision INTEGER NOT NULL DEFAULT 1"); }
            catch (DbException e) when (e.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("exists", StringComparison.OrdinalIgnoreCase)) { }
            Execute(connection, null, "CREATE TABLE IF NOT EXISTS continuum_event_requests (request_id VARCHAR(36) NOT NULL PRIMARY KEY, actor VARCHAR(36) NOT NULL, fingerprint VARCHAR(64) NOT NULL, event_id INTEGER NOT NULL)");
        }

        // This endpoint is private to an authenticated portal, never a viewer write API.
        // Conditional writes keep ownership and revision checks in the database transaction.
        internal int ChangeEvent(Hashtable input)
        {
            if (!Guid.TryParse(Text(input, "actor"), out Guid actor) || actor == Guid.Empty)
                throw new ArgumentException("A resident account is required");
            string action = Text(input, "action");
            int id = Number(input, "eventID"), revision = Number(input, "revision");
            if (action != "create" && action != "update" && action != "delete")
                throw new ArgumentException("Unknown event operation");
            if (action != "create" && (id <= 0 || revision <= 0))
                throw new ArgumentException("Reload the event before changing it");
            using DbConnection connection = OpenConnection();
            using DbTransaction transaction = connection.BeginTransaction();
            if (action == "delete")
            {
                if (Execute(connection, transaction, "DELETE FROM search_events WHERE eventid=@id AND creatoruuid=@actor AND revision=@revision",
                    ("@id", id), ("@actor", actor.ToString()), ("@revision", revision)) != 1)
                    throw new ArgumentException("The event changed or is not yours. Reload your events");
                transaction.Commit(); return id;
            }
            string name = Text(input, "name").Trim(), description = Text(input, "description").Trim();
            int category = Number(input, "category"), duration = Number(input, "duration"), flags = Number(input, "eventflags");
            if (name.Length < 1 || name.Length > 255 || description.Length < 1 || description.Length > 8192
                || (category != 0 && (category < 18 || category > 29 || category == 21)) || duration < 10 || duration > 1440
                || (flags != 0 && flags != 1 && flags != 2)
                || !Int64.TryParse(Text(input, "dateUTC"), NumberStyles.None, CultureInfo.InvariantCulture, out long date)
                || date <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() || date > DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds())
                throw new ArgumentException("Check the event title, description, future date, duration, category and maturity");
            if (!Guid.TryParse(Text(input, "regionID"), out Guid region) || region == Guid.Empty)
                throw new ArgumentException("Choose a region you own");
            // The portal also checks the native grid service. The indexed owner check is
            // an additional restriction; it must never grant authority beyond that check.
            string sim; ulong handle; int maturity;
            using (DbCommand regionQuery = Command(connection,
                "SELECT name,region_handle,maturity FROM continuum_search_regions WHERE region_id=@id AND owner_id=@actor",
                ("@id", region.ToString()), ("@actor", actor.ToString())))
            {
                regionQuery.Transaction = transaction;
                using DbDataReader regionReader = regionQuery.ExecuteReader();
                if (!regionReader.Read() || !UInt64.TryParse(Value(regionReader, 1), out handle))
                    throw new ArgumentException("That owned region is not indexed yet");
                sim = Value(regionReader, 0); maturity = Int(regionReader, 2);
            }
            if (flags > maturity) throw new ArgumentException("Event maturity exceeds the region maturity");
            if (String.IsNullOrWhiteSpace(sim)) throw new ArgumentException("That owned region is not indexed yet");
            if (!double.TryParse(Text(input, "x"), NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                || !double.TryParse(Text(input, "y"), NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                || !double.TryParse(Text(input, "z"), NumberStyles.Float, CultureInfo.InvariantCulture, out double z)
                || !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)
                || x < 0 || y < 0 || z < 0 || x > 65535 || y > 65535 || z > 65535)
                throw new ArgumentException("Enter valid coordinates");
            string position = String.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", (handle >> 32) + x, (handle & 0xffffffffUL) + y, z);
            (string, object)[] values = { ("@actor", actor.ToString()), ("@name", name), ("@description", description),
                ("@category", category), ("@date", date), ("@duration", duration), ("@flags", flags),
                ("@sim", sim), ("@position", position), ("@parcel", Guid.Empty.ToString()) };
            if (action == "create")
            {
                if (!Guid.TryParse(Text(input, "requestID"), out Guid request) || request == Guid.Empty)
                    throw new ArgumentException("Reload the event form");
                string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(new object[] { actor, name, description, category, date, duration, flags, region, position }))));
                using (DbCommand previous = Command(connection,
                    "SELECT actor,fingerprint,event_id FROM continuum_event_requests WHERE request_id=@request", ("@request", request.ToString())))
                {
                    previous.Transaction = transaction;
                    using DbDataReader reader = previous.ExecuteReader();
                    if (reader.Read())
                    {
                        if (Value(reader, 0) != actor.ToString() || Value(reader, 1) != fingerprint)
                            throw new ArgumentException("This event form has already been submitted. Reload it");
                        return Int(reader, 2);
                    }
                }
                using (DbCommand insert = Command(connection,
                    "INSERT INTO search_events(owneruuid,creatoruuid,name,description,category,dateUTC,duration,eventflags,simname,globalPos,parcelUUID,covercharge,coveramount) " +
                    "VALUES(@actor,@actor,@name,@description,@category,@date,@duration,@flags,@sim,@position,@parcel,0,0)" +
                    (Provider == "PostgreSQL" ? " RETURNING eventid" : ""), values))
                {
                    insert.Transaction = transaction;
                    if (Provider == "PostgreSQL") id = Convert.ToInt32(insert.ExecuteScalar(), CultureInfo.InvariantCulture);
                    else
                    {
                        insert.ExecuteNonQuery();
                        using DbCommand identity = Command(connection, Provider == "MySQL" ? "SELECT LAST_INSERT_ID()" : "SELECT last_insert_rowid()");
                        identity.Transaction = transaction;
                        id = Convert.ToInt32(identity.ExecuteScalar(), CultureInfo.InvariantCulture);
                    }
                }
                Execute(connection, transaction, "INSERT INTO continuum_event_requests(request_id,actor,fingerprint,event_id) VALUES(@request,@actor,@fingerprint,@id)",
                    ("@request", request.ToString()), ("@actor", actor.ToString()), ("@fingerprint", fingerprint), ("@id", id));
            }
            else
            {
                using DbCommand update = Command(connection,
                    "UPDATE search_events SET name=@name,description=@description,category=@category,dateUTC=@date,duration=@duration,eventflags=@flags," +
                    "simname=@sim,globalPos=@position,parcelUUID=@parcel,revision=revision+1 WHERE eventid=@id AND creatoruuid=@actor AND revision=@revision", values);
                update.Transaction = transaction;
                foreach (var pair in new[] { ("@id", id), ("@revision", revision) })
                { DbParameter parameter = update.CreateParameter(); parameter.ParameterName = pair.Item1; parameter.Value = pair.Item2; update.Parameters.Add(parameter); }
                if (update.ExecuteNonQuery() != 1) throw new ArgumentException("The event changed or is not yours. Reload your events");
            }
            transaction.Commit(); return id;
        }
    }
}
