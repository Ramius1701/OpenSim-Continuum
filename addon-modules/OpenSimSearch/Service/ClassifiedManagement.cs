using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nini.Config;
using Nwc.XmlRpc;

namespace ContinuumSearch.Service
{
    internal sealed partial class SearchRpc
    {
        private readonly Func<Guid, Guid, int, Hashtable> m_classifiedCharge;
        private readonly int m_classifiedMinimum;

        internal static Func<Guid, Guid, int, Hashtable> ClassifiedPayments(IConfig config)
        {
            string url = config.GetString("ClassifiedEconomyURL", "");
            string secret = config.GetString("ClassifiedEconomySecret", "");
            if (url.Length == 0 && secret.Length == 0) return null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri endpoint)
                || (endpoint.Scheme != "https" && !(endpoint.Scheme == "http" && endpoint.IsLoopback))
                || endpoint.UserInfo.Length > 0 || endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0
                || secret.Length < 32 || secret == config.GetString("WebUIWriteSecret", "") || secret.StartsWith("CHANGE", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Classified economy requires HTTPS (or loopback HTTP) and a dedicated secret of at least 32 characters");
            int timeout = Math.Clamp(config.GetInt("ClassifiedEconomyTimeoutMilliseconds", 5000), 500, 30000);
            return (request, actor, price) =>
            {
                // No browser-supplied URLs, transaction types or recipient accounts.
                XmlRpcResponse response = new XmlRpcRequest("WebUIClassifiedCharge", new ArrayList { new Hashtable
                {
                    ["classifiedSecret"] = secret, ["purchaseID"] = request.ToString(),
                    ["buyerID"] = actor.ToString(), ["amount"] = price.ToString(CultureInfo.InvariantCulture)
                } }).Send(url, timeout);
                return response?.IsFault == false ? response.Value as Hashtable : null;
            };
        }

        internal XmlRpcResponse ClassifiedManage(XmlRpcRequest request, System.Net.IPEndPoint remote)
        {
            Hashtable input = Args(request);
            if (!WriteAuthorized(input)) return ClassifiedFailure("Not authorized");
            try { return Reply(m_store.ManageClassified(input, m_classifiedCharge, m_classifiedMinimum)); }
            catch (ArgumentException e) { return ClassifiedFailure(e.Message); }
            catch (Exception)
            { return ClassifiedFailure("Unable to confirm the operation. Open My classifieds and resume the pending listing before submitting another payment"); }
        }
        private static XmlRpcResponse ClassifiedFailure(string message) => new()
        { Value = new Hashtable { ["success"] = false, ["errorMessage"] = message } };
    }

    internal sealed partial class SearchStore
    {
        private object ProfileUUID(Guid id) => Provider == "PostgreSQL" ? (object)id : id.ToString();

        private const string ClassifiedColumns = "classifieduuid,creatoruuid,creationdate,expirationdate,category,name,description,parceluuid,parentestate,snapshotuuid,simname,posglobal,parcelname,classifiedflags,priceforlisting";
        private static readonly string[] ClassifiedNames = ClassifiedColumns.Split(',');
        internal void EnsureClassifiedManagementSchema()
        {
            using DbConnection c = OpenConnection();
            Execute(c, null, "CREATE TABLE IF NOT EXISTS continuum_classified_orders (request_id VARCHAR(36) NOT NULL PRIMARY KEY,actor VARCHAR(36) NOT NULL,fingerprint VARCHAR(64) NOT NULL,payload TEXT NOT NULL,published INTEGER NOT NULL DEFAULT 0)");
        }
        private static Hashtable ClassifiedRow(DbDataReader reader)
        {
            Hashtable row = new();
            for (int i = 0; i < ClassifiedNames.Length; i++) row[ClassifiedNames[i]] = Value(reader, i);
            row["editToken"] = ClassifiedToken(row); return row;
        }
        private static string ClassifiedToken(Hashtable row) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(ClassifiedNames.Select(x => Text(row, x)).ToArray()))));

        internal ArrayList ManageClassified(Hashtable input, Func<Guid, Guid, int, Hashtable> charge, int minimum)
        {
            if (!Guid.TryParse(Text(input, "actor"), out Guid actor) || actor == Guid.Empty)
                throw new ArgumentException("Log in to manage classifieds");
            string action = Text(input, "action");
            if (action == "list")
            {
                ArrayList rows = Query("SELECT " + ClassifiedColumns + " FROM classifieds WHERE creatoruuid=@actor ORDER BY creationdate DESC LIMIT 100",
                    new List<(string, object)> { ("@actor", ProfileUUID(actor)) }, null, ClassifiedRow);
                ArrayList pending = Query("SELECT request_id,payload FROM continuum_classified_orders WHERE actor=@actor AND published=0 LIMIT 100",
                    new List<(string, object)> { ("@actor", actor.ToString()) }, null, r =>
                    {
                        var ad = JsonSerializer.Deserialize<ClassifiedDraft>(Value(r, 1));
                        return new Hashtable { ["requestID"] = Value(r, 0), ["name"] = ad.Name, ["price"] = ad.Price };
                    });
                return new ArrayList { new Hashtable { ["listings"] = rows, ["pending"] = pending, ["minimumPrice"] = minimum,
                    ["paymentsAvailable"] = charge != null } };
            }
            if (action == "create" || action == "resume")
            {
                if (charge == null) throw new ArgumentException("Paid classified creation is not configured. No payment was requested");
                if (!Guid.TryParse(Text(input, "requestID"), out Guid request) || request == Guid.Empty)
                    throw new ArgumentException("Reload the classified form");
                if (action == "create") PrepareClassified(input, actor, request, minimum);
                PublishClassified(actor, request, charge);
                return new ArrayList { new Hashtable { ["classifieduuid"] = request.ToString() } };
            }
            if (!Guid.TryParse(Text(input, "classifiedID"), out Guid id) || id == Guid.Empty)
                throw new ArgumentException("Choose a classified from your account");
            using DbConnection c = OpenConnection();
            using DbTransaction transaction = c.BeginTransaction();
            Hashtable current;
            using (DbCommand read = Command(c, "SELECT " + ClassifiedColumns + " FROM classifieds WHERE classifieduuid=@id AND creatoruuid=@actor"
                + (Provider == "SQLite" ? "" : " FOR UPDATE"), ("@id", ProfileUUID(id)), ("@actor", ProfileUUID(actor))))
            {
                read.Transaction = transaction;
                using DbDataReader reader = read.ExecuteReader();
                if (!reader.Read()) throw new ArgumentException("Classified not found in your account");
                current = ClassifiedRow(reader);
            }
            if (action == "detail") { transaction.Commit(); return new ArrayList { current }; }
            if (action != "update" && action != "delete") throw new ArgumentException("Unknown classified operation");
            if (Text(input, "editToken") != Text(current, "editToken"))
                throw new ArgumentException("This classified changed. Reload it before saving or deleting");
            if (action == "delete")
                Execute(c, transaction, "DELETE FROM classifieds WHERE classifieduuid=@id AND creatoruuid=@actor", ("@id", ProfileUUID(id)), ("@actor", ProfileUUID(actor)));
            else
            {
                ValidateClassifiedText(input, out string name, out string description, out int category, out Guid image);
                // Editing cannot renew a paid listing or alter its price, maturity or location.
                Execute(c, transaction, "UPDATE classifieds SET name=@name,description=@description,category=@category,snapshotuuid=@image WHERE classifieduuid=@id AND creatoruuid=@actor",
                    ("@name", name), ("@description", description), ("@category", category.ToString(CultureInfo.InvariantCulture)), ("@image", ProfileUUID(image)), ("@id", ProfileUUID(id)), ("@actor", ProfileUUID(actor)));
            }
            transaction.Commit(); return new ArrayList { new Hashtable { ["classifieduuid"] = id.ToString() } };
        }

        private static void ValidateClassifiedText(Hashtable input, out string name, out string description, out int category, out Guid image)
        {
            name = Text(input, "name").Trim(); description = Text(input, "description").Trim();
            category = Number(input, "category");
            if (name.Length == 0 || name.Length > 63 || name.Any(Char.IsControl) || description.Length == 0 || description.Length > 8192
                || !Int32.TryParse(Text(input, "category"), out category) || category < 1 || category > 9
                || !Guid.TryParse(Text(input, "snapshotID"), out image))
                throw new ArgumentException("Check the title (1–63 characters), description, category and image UUID");
        }
        private void PrepareClassified(Hashtable input, Guid actor, Guid request, int minimum)
        {
            ValidateClassifiedText(input, out string name, out string description, out int category, out Guid image);
            if (!Int32.TryParse(Text(input, "price"), NumberStyles.None, CultureInfo.InvariantCulture, out int price) || price <= 0
                || !Guid.TryParse(Text(input, "regionID"), out Guid region) || region == Guid.Empty
                || !Int32.TryParse(Text(input, "maturity"), out int maturity) || maturity < 0 || maturity > 2)
                throw new ArgumentException("Choose a valid region, maturity and listing price");
            double Coordinate(string key)
            {
                if (!double.TryParse(Text(input, key), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                    || !double.IsFinite(v) || v < 0 || v > 65535) throw new ArgumentException("Enter valid landing coordinates");
                return v;
            }
            double x = Coordinate("x"), y = Coordinate("y"), z = Coordinate("z");
            string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                new object[] { actor, name, description, category, image, price, region, maturity, x, y, z }))));
            using DbConnection c = OpenConnection();
            using DbTransaction transaction = c.BeginTransaction();
            using (DbCommand prior = Command(c, "SELECT actor,fingerprint FROM continuum_classified_orders WHERE request_id=@id", ("@id", request.ToString())))
            {
                prior.Transaction = transaction;
                using DbDataReader r = prior.ExecuteReader();
                if (r.Read())
                {
                    if (Value(r, 0) != actor.ToString() || Value(r, 1) != fingerprint) throw new ArgumentException("This payment form was already used. Open My classifieds");
                    return;
                }
            }
            if (price < minimum) throw new ArgumentException("The listing price is below the current minimum. Reload the form");
            string sim; ulong handle;
            using (DbCommand lookup = Command(c, "SELECT name,region_handle,maturity FROM continuum_search_regions WHERE region_id=@id", ("@id", region.ToString())))
            {
                lookup.Transaction = transaction;
                using DbDataReader r = lookup.ExecuteReader();
                if (!r.Read() || !UInt64.TryParse(Value(r, 1), out handle)) throw new ArgumentException("This region is not indexed yet");
                sim = Value(r, 0);
                if (maturity > Int(r, 2)) throw new ArgumentException("Classified maturity exceeds the region maturity");
            }
            using (DbCommand limit = Command(c, "SELECT COUNT(*) FROM classifieds WHERE creatoruuid=@actor", ("@actor", ProfileUUID(actor))))
            {
                limit.Transaction = transaction;
                if (Convert.ToInt32(limit.ExecuteScalar()) >= 100) throw new ArgumentException("The account has reached its 100 classified limit");
            }
            using (DbCommand pending = Command(c, "SELECT COUNT(*) FROM continuum_classified_orders WHERE actor=@actor AND published=0", ("@actor", actor.ToString())))
            {
                pending.Transaction = transaction;
                if (Convert.ToInt32(pending.ExecuteScalar()) >= 10) throw new ArgumentException("Resume your pending classifieds before creating another");
            }
            using (DbCommand exists = Command(c, "SELECT COUNT(*) FROM classifieds WHERE classifieduuid=@id", ("@id", ProfileUUID(request))))
            {
                exists.Transaction = transaction;
                if (Convert.ToInt32(exists.ExecuteScalar()) != 0) throw new ArgumentException("Reload the classified form");
            }
            var ad = new ClassifiedDraft { Actor = actor, ID = request, Name = name, Description = description, Category = category,
                Image = image, Price = price, Sim = sim, Flags = maturity == 0 ? 4 : maturity == 1 ? 10 : 64,
                Position = String.Format(CultureInfo.InvariantCulture, "<{0}, {1}, {2}>", (handle >> 32) + x, (handle & 0xffffffffUL) + y, z) };
            Execute(c, transaction, "INSERT INTO continuum_classified_orders(request_id,actor,fingerprint,payload,published) VALUES(@id,@actor,@fingerprint,@payload,0)",
                ("@id", request.ToString()), ("@actor", actor.ToString()), ("@fingerprint", fingerprint), ("@payload", JsonSerializer.Serialize(ad)));
            transaction.Commit();
        }
        private void PublishClassified(Guid actor, Guid request, Func<Guid, Guid, int, Hashtable> charge)
        {
            ClassifiedDraft ad;
            using (DbConnection c = OpenConnection())
            using (DbCommand query = Command(c, "SELECT payload,published FROM continuum_classified_orders WHERE request_id=@id AND actor=@actor", ("@id", request.ToString()), ("@actor", actor.ToString())))
            using (DbDataReader r = query.ExecuteReader())
            {
                if (!r.Read()) throw new ArgumentException("Pending listing not found in your account");
                if (Int(r, 1) == 1) return;
                if (Int(r, 1) == 2) throw new ArgumentException("The previous payment was rejected or cancelled. Start a new listing if you want to publish it");
                ad = JsonSerializer.Deserialize<ClassifiedDraft>(Value(r, 0));
            }
            Hashtable paid = charge(request, actor, ad.Price);
            if (Text(paid, "state") is "InsufficientFunds" or "Cancelled")
            {
                using DbConnection rejected = OpenConnection();
                Execute(rejected, null, "UPDATE continuum_classified_orders SET published=2 WHERE request_id=@id AND actor=@actor AND published=0", ("@id", request.ToString()), ("@actor", actor.ToString()));
            }
            if (paid == null || !Boolean.TryParse(Text(paid, "success"), out bool ok) || !ok || Text(paid, "state") != "Captured")
                throw new ArgumentException(Text(paid, "state") == "InsufficientFunds"
                    ? "Insufficient funds. No listing was published. Create a new listing after adding funds"
                    : Text(paid, "state") == "Cancelled" ? "The listing payment was cancelled. No listing was published. Start a new listing if you still want to publish it"
                    : "Payment could not be confirmed. Resume this listing from My classifieds before making another payment");
            using DbConnection connection = OpenConnection();
            using DbTransaction transaction = connection.BeginTransaction();
            using (DbCommand check = Command(connection, "SELECT published FROM continuum_classified_orders WHERE request_id=@id AND actor=@actor"
                + (Provider == "SQLite" ? "" : " FOR UPDATE"), ("@id", request.ToString()), ("@actor", actor.ToString())))
            {
                check.Transaction = transaction;
                object state = check.ExecuteScalar();
                if (state == null) throw new InvalidOperationException("The publication receipt is missing");
                if (Convert.ToInt32(state) == 1) return;
                if (Convert.ToInt32(state) != 0) throw new InvalidOperationException("The publication receipt is not pending");
            }
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Execute(connection, transaction, "INSERT INTO classifieds(" + ClassifiedColumns + ") VALUES(@id,@actor,@created,@expires,@category,@name,@description,@parcel,0,@image,@sim,@position,'',@flags,@price)",
                ("@id", ProfileUUID(request)), ("@actor", ProfileUUID(actor)), ("@created", now), ("@expires", now + 7 * 86400),
                ("@category", ad.Category.ToString(CultureInfo.InvariantCulture)), ("@name", ad.Name), ("@description", ad.Description), ("@parcel", ProfileUUID(Guid.Empty)),
                ("@image", ProfileUUID(ad.Image)), ("@sim", ad.Sim), ("@position", ad.Position), ("@flags", ad.Flags), ("@price", ad.Price));
            Execute(connection, transaction, "UPDATE continuum_classified_orders SET published=1 WHERE request_id=@id AND actor=@actor", ("@id", request.ToString()), ("@actor", actor.ToString()));
            transaction.Commit();
        }
    }
    internal sealed class ClassifiedDraft
    {
        public Guid ID { get; set; }
        public Guid Actor { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Category { get; set; }
        public Guid Image { get; set; }
        public int Price { get; set; }
        public int Flags { get; set; }
        public string Sim { get; set; }
        public string Position { get; set; }
    }
}
