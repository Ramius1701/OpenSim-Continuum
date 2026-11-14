using System.Reflection;
using System.Runtime.CompilerServices;
using System.Data.SQLite;
using System.Text;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Continuum.WebUI;
using OpenSim.Data.SQLite;
using OpenSim.Services.ProfilesService;
using OpenSim.Services.Interfaces;

static class PartnershipChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run(string root)
    {
        string path = Path.Combine(root, "partnerships.sqlite");
        var storage = new SQLiteUserProfilesData("Data Source=" + path + ";Version=3");
        SQLiteConnection Connection(SQLiteUserProfilesData data) => (SQLiteConnection)typeof(SQLiteUserProfilesData).GetField("m_connection", Private).GetValue(data);
        UserProfilesService Service(SQLiteUserProfilesData data)
        {
            var service = (UserProfilesService)RuntimeHelpers.GetUninitializedObject(typeof(UserProfilesService)); service.ProfilesData = data; return service;
        }
        var service = Service(storage); var accounts = new Dictionary<UUID, UserAccount>();
        UserAccount Account(string name)
        {
            var account = new UserAccount(UUID.Zero, name, "Resident", "resident@example.invalid"); accounts.Add(account.PrincipalID, account);
            var profile = new UserProfileProperties { UserId = account.PrincipalID }; string result = "";
            Check(service.AvatarPropertiesRequest(ref profile, ref result), "Native partnership profile initialization failed"); return account;
        }
        UUID Partner(UserAccount account)
        {
            var profile = new UserProfileProperties { UserId = account.PrincipalID }; string result = "";
            Check(service.AvatarPropertiesRequest(ref profile, ref result), "Native partner read failed"); return profile.PartnerId;
        }
        bool Rejected(Action action) { try { action(); return false; } catch (InvalidOperationException) { return true; } catch (ArgumentException) { return true; } }
        try
        {
            var alice = Account("Alice"); var bob = Account("Bob"); var carol = Account("Carol"); UUID id = UUID.Random();
            string Change(UserAccount actor, UserAccount other, UUID proposal, string action, string message = "") => service.ChangePartnership(actor.PrincipalID, other.PrincipalID, proposal, action, message);
            Check(Change(alice, bob, id, "propose", "<script>proposal</script>") == "Partnership proposal saved", "Proposal was not saved");
            Check(Change(alice, bob, id, "propose", "<script>proposal</script>") == "Partnership proposal saved" && service.GetPartnershipProposals(alice.PrincipalID).Length == 1, "Proposal replay duplicated rows");
            Check(service.GetPartnershipProposals(carol.PrincipalID).Length == 0 && Partner(alice) == UUID.Zero && Partner(bob) == UUID.Zero, "Proposal exposed private data or assigned a partner without consent");
            Check(Rejected(() => Change(carol, alice, id, "accept")) && Rejected(() => Change(bob, alice, UUID.Random(), "accept")), "Foreign or stale proposal accepted");
            Check(Rejected(() => Change(alice, carol, id, "propose", "<script>proposal</script>")), "Proposal ID accepted a different recipient");
            Check(Rejected(() => Change(alice, carol, UUID.Random(), "propose")), "Multiple outgoing proposals were allowed");
            var reopened = new SQLiteUserProfilesData("Data Source=" + path + ";Version=3");
            try { Check(Service(reopened).GetPartnershipProposals(bob.PrincipalID).Single().Message == "<script>proposal</script>", "Proposal restart persistence failed"); }
            finally { Connection(reopened).Dispose(); }
            Check(Change(bob, alice, id, "accept") == "Partnership accepted" && Partner(alice) == bob.PrincipalID && Partner(bob) == alice.PrincipalID
                && service.GetPartnershipProposals(alice.PrincipalID).Length == 0, "Partnership acceptance failed or was not reciprocal");
            Check(Rejected(() => Change(carol, alice, UUID.Random(), "propose")), "Partnered resident received a new proposal");
            Check(Change(alice, bob, UUID.Random(), "dissolve") == "Partnership dissolved" && Partner(alice) == UUID.Zero && Partner(bob) == UUID.Zero, "Partnership dissolution was not reciprocal");
            id = UUID.Random(); Change(alice, bob, id, "propose"); Check(Change(bob, alice, id, "decline") == "Partnership proposal declined", "Recipient could not decline");
            id = UUID.Random(); Change(alice, bob, id, "propose"); Check(Change(alice, bob, id, "cancel") == "Partnership proposal withdrawn", "Sender could not withdraw");
            id = UUID.Random(); Change(alice, bob, id, "propose");
            using (var expired = Connection(storage).CreateCommand()) { expired.CommandText = "UPDATE continuum_partnership_proposals SET expires=1"; expired.ExecuteNonQuery(); }
            Check(service.GetPartnershipProposals(bob.PrincipalID).Length == 0 && Rejected(() => Change(bob, alice, id, "accept")), "Expired proposal accepted or displayed");
            // A forced second-row failure must roll the first profile update back.
            id = UUID.Random(); Change(alice, bob, id, "propose");
            using (var trigger = Connection(storage).CreateCommand())
            {
                trigger.CommandText = "CREATE TRIGGER reject_partner BEFORE UPDATE OF profilePartner ON userprofile WHEN NEW.useruuid='" + alice.PrincipalID + "' BEGIN SELECT RAISE(ABORT,'fixture failure'); END"; trigger.ExecuteNonQuery();
            }
            bool failed = false; try { Change(bob, alice, id, "accept"); } catch (SQLiteException) { failed = true; }
            Check(failed && Partner(alice) == UUID.Zero && Partner(bob) == UUID.Zero && service.GetPartnershipProposals(bob.PrincipalID).Length == 1, "Partial partner update survived transaction failure");
            using (var trigger = Connection(storage).CreateCommand()) { trigger.CommandText = "DROP TRIGGER reject_partner"; trigger.ExecuteNonQuery(); }
            Change(alice, bob, id, "cancel");
            var assembly = typeof(WebUIContentStore).Assembly; var adapter = assembly.GetType("OpenSim.Continuum.WebUI.WebUIServiceIntegration", true);
            var integration = RuntimeHelpers.GetUninitializedObject(adapter);
            void Set(string field, object value) => adapter.GetField(field, Private).SetValue(integration, value);
            var accountService = DispatchProxy.Create<IUserAccountService, RecoveryProxy>();
            ((RecoveryProxy)(object)accountService).Handler = (method, args) => method.Name == "GetUserAccount" && args.Length == 2
                && args[1] is UUID uuid && accounts.TryGetValue(uuid, out var account) ? account : null;
            Set("_profiles", service); Set("_accounts", accountService); Set("_allowPartnerships", true); Set("_adminLevel", 200);
            var post = adapter.GetMethod("HandlePost", Private); var form = new Dictionary<string, string>
                { ["action"] = "propose", ["other"] = bob.PrincipalID.ToString(), ["requestID"] = UUID.Random().ToString(), ["message"] = "<img src=x onerror=bad()>" };
            string Portal(UserAccount actor)
            { object[] args = { "user/partnership.html", form, actor, null }; post.Invoke(integration, args); return (string)args[3]; }
            Check(Portal(alice) == "Partnership proposal saved", "Portal could not save proposal");
            var add = adapter.GetMethod("AddPartnership", Private); var values = new Dictionary<string, object>(); add.Invoke(integration, new object[] { bob, values });
            var rows = (List<Dictionary<string, object>>)values["PartnershipIncoming"];
            Check(rows.Count == 1 && ((string)rows[0]["ProposalMessage"]).Contains("&lt;img"), "Incoming proposal was not escaped");
            var siteType = assembly.GetType("OpenSim.Continuum.WebUI.WhiteCoreSite", true); var site = RuntimeHelpers.GetUninitializedObject(siteType);
            siteType.GetField("_mountPath", Private).SetValue(site, "/portal"); siteType.GetField("_systemName", Private).SetValue(site, "Fixture"); siteType.GetField("_integration", Private).SetValue(site, integration);
            var defaults = (Dictionary<string, object>)siteType.GetMethod("DefaultVariables", Private).Invoke(site, new object[] { bob });
            add.Invoke(integration, new object[] { bob, defaults });
            var bytes = (byte[])siteType.GetMethod("ReadResource", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { "user/partnership.html" });
            string html = (string)siteType.GetMethod("Render", Private).Invoke(site, new object[] { "user/partnership.html", Encoding.UTF8.GetString(bytes), defaults, true, false, 0 });
            Check(html.Contains("partnership_accept_") && html.Contains("&lt;img") && !html.Contains("<img src=x"), "Embedded partnership response form failed");
            form["action"] = "accept"; form["other"] = alice.PrincipalID.ToString();
            Check(Portal(bob).Contains("Confirm") && Partner(bob) == UUID.Zero, "Portal accepted partnership without consent");
            form["confirm"] = "true"; Check(Portal(bob) == "Partnership accepted" && Partner(bob) == alice.PrincipalID, "Portal consent did not establish partnership");
            form["action"] = "dissolve"; Check(Portal(bob) == "Partnership dissolved", "Portal dissolution failed");
            Check(Portal(null) == "A local account is required", "Anonymous partnership change accepted");
            Set("_allowPartnerships", false); Check(Portal(alice) == "Partnerships are unavailable", "Disabled partnership feature accepted a write");
            Console.WriteLine("PASS: native partnership migration/SQLite persistence, consent, actor isolation, stale/expired proposals, reciprocal accept/dissolve, injected transaction rollback and escaped embedded forms.");
        }
        finally { Connection(storage).Dispose(); }
    }
}
