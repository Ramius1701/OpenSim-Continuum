using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;

namespace ContinuumSearch.Service
{
    internal sealed partial class SearchStore
    {
        // Invoked only by the explicit self-test against a dedicated test database.
        internal void CheckClassifiedManagement()
        {
            string uuid = Provider == "PostgreSQL" ? "UUID" : "VARCHAR(36)";
            using (DbConnection c = OpenConnection())
                Execute(c, null, $"CREATE TABLE IF NOT EXISTS classifieds(classifieduuid {uuid} PRIMARY KEY,creatoruuid {uuid} NOT NULL,creationdate INTEGER NOT NULL,expirationdate INTEGER NOT NULL,category VARCHAR(20) NOT NULL,name VARCHAR(255) NOT NULL,description TEXT NOT NULL,parceluuid {uuid} NOT NULL,parentestate INTEGER NOT NULL,snapshotuuid {uuid} NOT NULL,simname VARCHAR(255) NOT NULL,posglobal VARCHAR(255) NOT NULL,parcelname VARCHAR(255) NOT NULL,classifiedflags INTEGER NOT NULL,priceforlisting INTEGER NOT NULL)");
            void Require(bool ok, string text) { if (!ok) throw new InvalidOperationException("Classified acceptance failed: " + text); }
            void Reject(Action action, string text)
            { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Classified acceptance failed to reject: " + text); }
            Guid actor = Guid.NewGuid(), request = Guid.NewGuid();
            Hashtable form = new()
            {
                ["actor"] = actor.ToString(), ["action"] = "create", ["requestID"] = request.ToString(),
                ["name"] = "Acceptance classified", ["description"] = "<script>display as text</script>",
                ["category"] = "1", ["snapshotID"] = Guid.Empty.ToString(), ["price"] = "50", ["maturity"] = "0",
                ["regionID"] = "11111111-1111-1111-1111-111111111111", ["x"] = "128", ["y"] = "128", ["z"] = "25"
            };
            int debits = 0; bool lostReply = true;
            var paidIDs = new HashSet<Guid>();
            Hashtable Charge(Guid id, Guid buyer, int amount)
            {
                Require(buyer == actor && amount == 50, "payment actor/amount binding");
                if (paidIDs.Add(id)) ++debits;
                if (lostReply) throw new IOException("Simulated lost capture reply");
                return new Hashtable { ["success"] = true, ["state"] = "Captured" };
            }
            Reject(() => ManageClassified(form, null, 50), "payments disabled");
            bool interrupted = false;
            try { ManageClassified(form, Charge, 50); } catch (IOException) { interrupted = true; }
            Require(interrupted && debits == 1 && GetClassified(new Hashtable { ["classifiedID"] = request.ToString() }).Count == 0,
                "unconfirmed payment cannot publish");
            Hashtable List(Guid owner) => (Hashtable)ManageClassified(new Hashtable { ["action"] = "list", ["actor"] = owner.ToString() }, Charge, 50)[0];
            Require(((ArrayList)List(actor)["pending"]).Count == 1 && ((ArrayList)List(Guid.NewGuid())["pending"]).Count == 0, "pending ownership");
            form["action"] = "resume"; form["actor"] = Guid.NewGuid().ToString();
            Reject(() => ManageClassified(form, Charge, 50), "resume ownership");
            form["actor"] = actor.ToString(); lostReply = false;
            // New store instance proves the publication intent was loaded from the database.
            using var reopened = new SearchStore(Provider, m_connectionString, m_connectionFactory);
            reopened.ManageClassified(form, Charge, 100); // changed storefront minimum cannot break a saved quote
            Require(debits == 1 && ((ArrayList)List(actor)["pending"]).Count == 0, "restart and idempotent payment");
            form["action"] = "create"; ManageClassified(form, Charge, 100);
            Require(debits == 1, "duplicate publication cannot charge again");
            form["price"] = "51"; Reject(() => ManageClassified(form, Charge, 50), "immutable payment payload"); form["price"] = "50";
            Hashtable edit = new() { ["action"] = "detail", ["actor"] = actor.ToString(), ["classifiedID"] = request.ToString() };
            Hashtable before = (Hashtable)ManageClassified(edit, Charge, 50)[0];
            Require(Text(before, "classifiedflags") == "4" && Text(before, "posglobal") == "<256128, 256128, 25>"
                && Int64.Parse(Text(before, "expirationdate")) - Int64.Parse(Text(before, "creationdate")) == 7 * 86400, "viewer flags, global position and seven-day lifetime");
            edit["action"] = "update"; edit["editToken"] = before["editToken"]; edit["name"] = "Edited without renewal";
            edit["description"] = "Updated text"; edit["category"] = "2"; edit["snapshotID"] = Guid.Empty.ToString();
            edit["price"] = "0"; edit["creationdate"] = "0"; edit["expirationdate"] = "2147483647";
            edit["actor"] = Guid.NewGuid().ToString(); Reject(() => ManageClassified(edit, Charge, 50), "edit ownership"); edit["actor"] = actor.ToString();
            ManageClassified(edit, Charge, 50);
            Reject(() => ManageClassified(edit, Charge, 50), "stale update");
            edit["action"] = "delete"; Reject(() => ManageClassified(edit, Charge, 50), "stale delete");
            edit["action"] = "detail"; Hashtable after = (Hashtable)ManageClassified(edit, Charge, 50)[0];
            foreach (string key in new[] { "creationdate", "expirationdate", "priceforlisting", "classifiedflags", "posglobal", "creatoruuid" })
                Require(Text(before, key) == Text(after, key), "immutable paid metadata " + key);
            Require(debits == 1 && Text(after, "name") == "Edited without renewal", "edit does not charge");
            Require(FindClassifieds(new Hashtable { ["text"] = "Edited without renewal", ["flags"] = "4", ["category"] = "2" }).Count == 1
                && FindClassifieds(new Hashtable { ["text"] = "Edited without renewal", ["flags"] = "4", ["category"] = "1" }).Count == 0,
                "native text category filtering");
            using (DbConnection c = OpenConnection())
                Execute(c, null, "UPDATE classifieds SET expirationdate=1 WHERE classifieduuid=@id", ("@id", ProfileUUID(request)));
            Require(FindClassifieds(new Hashtable { ["text"] = "Edited without renewal", ["flags"] = "4" }).Count == 0, "expired listing is excluded from search");
            edit["action"] = "detail"; after = (Hashtable)ManageClassified(edit, Charge, 50)[0];
            edit["action"] = "delete"; edit["editToken"] = after["editToken"];
            edit["actor"] = Guid.NewGuid().ToString(); Reject(() => ManageClassified(edit, Charge, 50), "delete ownership"); edit["actor"] = actor.ToString();
            ManageClassified(edit, Charge, 50); ManageClassified(form, Charge, 50);
            Require(debits == 1 && GetClassified(new Hashtable { ["classifiedID"] = request.ToString() }).Count == 0, "old receipt cannot resurrect deleted listing");
            Guid denied = Guid.NewGuid(); form["requestID"] = denied.ToString();
            Hashtable Insufficient(Guid id, Guid owner, int amount) => new() { ["success"] = false, ["state"] = "InsufficientFunds" };
            Reject(() => ManageClassified(form, Insufficient, 50), "insufficient funds");
            Require(GetClassified(new Hashtable { ["classifiedID"] = denied.ToString() }).Count == 0
                && ((ArrayList)List(actor)["pending"]).Count == 0, "rejected payment cannot publish or remain pending");
            form["requestID"] = Guid.NewGuid().ToString();
            Reject(() => ManageClassified(form, (_, _, _) => new Hashtable { ["success"] = false, ["state"] = "Cancelled" }, 50), "cancelled payment");
            Require(((ArrayList)List(actor)["pending"]).Count == 0, "cancelled intent remains pending");
            form["price"] = "0"; form["requestID"] = Guid.NewGuid().ToString(); Reject(() => ManageClassified(form, Charge, 50), "minimum price");
            form["price"] = "49"; Reject(() => ManageClassified(form, Charge, 50), "new invoice below current minimum");
            form["price"] = "50"; form["x"] = "NaN"; Reject(() => ManageClassified(form, Charge, 50), "invalid coordinates"); form["x"] = "128";
            form["maturity"] = "2"; Reject(() => ManageClassified(form, Charge, 50), "region maturity ceiling");
            form["maturity"] = "0"; form["requestID"] = Guid.NewGuid().ToString();
            Guid parallelID = Guid.Parse(Text(form, "requestID"));
            int concurrentDebits = 0; var captured = new HashSet<Guid>();
            Hashtable ConcurrentCharge(Guid id, Guid owner, int amount)
            {
                lock (captured) { if (captured.Add(id)) ++concurrentDebits; }
                return new Hashtable { ["success"] = true, ["state"] = "Captured" };
            }
            System.Threading.Tasks.Parallel.For(0, 2, _ =>
            {
                try { ManageClassified((Hashtable)form.Clone(), ConcurrentCharge, 50); }
                catch (DbException) { /* A competing insert may lose; the same request is safely retried below. */ }
            });
            ManageClassified(form, ConcurrentCharge, 50);
            Require(concurrentDebits == 1 && GetClassified(new Hashtable { ["classifiedID"] = parallelID.ToString() }).Count == 1,
                "concurrent duplicate publication");
            var rpc = new SearchRpc(this, new string('k', 48), Charge);
            Require(!Convert.ToBoolean(((Hashtable)rpc.ClassifiedManage(new Nwc.XmlRpc.XmlRpcRequest("continuum_classified_manage", new ArrayList { form }), null).Value)["success"]), "private API rejects missing secret");
            Console.WriteLine("PASS: classified payment intent, lost reply/restart replay, ownership, revisions, expiry, immutable paid metadata, insufficient funds and private API authentication");
        }
    }
}
