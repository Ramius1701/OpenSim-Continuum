using System.Data.SQLite;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Collections.Specialized;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Continuum.WebUI;
using OpenSim.Data.SQLite;
using OpenSim.Services.ProfilesService;
using OpenSim.Services.Interfaces;

static class ContactChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run(string root)
    {
        string path = Path.Combine(root, "contacts.sqlite");
        using var connection = new SQLiteConnection("Data Source=" + path + ";Version=3"); connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE userdata(UserId TEXT NOT NULL,TagId TEXT NOT NULL,DataKey TEXT,DataVal TEXT,PRIMARY KEY(UserId,TagId))";
            command.ExecuteNonQuery();
        }
        UserProfilesService Service(SQLiteConnection db)
        {
            var provider = new SQLiteUserProfilesData();
            typeof(SQLiteUserProfilesData).GetField("m_connection", Private).SetValue(provider, db);
            var service = (UserProfilesService)RuntimeHelpers.GetUninitializedObject(typeof(UserProfilesService));
            service.ProfilesData = provider; return service;
        }
        var native = Service(connection);
        var resident = new UserAccount { PrincipalID = UUID.Random(), FirstName = "Local", LastName = "Resident" };
        var stranger = new UserAccount { PrincipalID = UUID.Random() };
        var contact = new UserContactInfo { Address = "<img src=x onerror=bad()> " + new string('é', 170), City = "Wellington", PostalCode = "6011", Country = "New Zealand" };
        Check(native.StoreContact(resident.PrincipalID, contact), "Native contact write failed");
        using (var reopened = new SQLiteConnection("Data Source=" + path + ";Version=3"))
        {
            reopened.Open();
            Check(Service(reopened).GetContact(resident.PrincipalID).Address == contact.Address, "Contact Unicode/restart persistence failed");
        }
        Check(native.GetContact(stranger.PrincipalID).Address == "", "Other resident read contact details");
        foreach (string tag in new[] { "continuum.private.contact.v1", "CONTINUUM.PRIVATE.contact.v1", "continuum.private.contact.v1 ", "conti\u00adnuum.private.contact.v1", "continuum.priva\0te.contact.v1" })
        {
            var record = new UserAppData { UserId = resident.PrincipalID.ToString(), TagId = tag, DataVal = "Overwrite" }; string message = "";
            Check(!native.RequestUserAppData(ref record, ref message) && !native.SetUserAppData(record, ref message), "Private contact namespace exposed through public profile application API");
        }
        var assembly = typeof(WebUIContentStore).Assembly;
        var adapter = assembly.GetType("OpenSim.Continuum.WebUI.WebUIServiceIntegration", true);
        var integration = RuntimeHelpers.GetUninitializedObject(adapter);
        void Set(string field, object value) => adapter.GetField(field, Private).SetValue(integration, value);
        Set("_profiles", native); Set("_residentContactEnabled", true); Set("_contactPrivacyNotice", "Used only to contact you about your account."); Set("_adminLevel", 200);
        var add = adapter.GetMethod("AddContact", Private); var post = adapter.GetMethod("HandlePost", Private);
        var values = new Dictionary<string, object>(); add.Invoke(integration, new object[] { resident, values });
        Check(((string)values["ContactAddress"]).Contains("&lt;img") && !((string)values["ContactAddress"]).Contains("<img"), "Private contact output was not escaped");
        var form = new Dictionary<string, string> { ["action"] = "save", ["address"] = "Changed address", ["userID"] = stranger.PrincipalID.ToString() };
        object[] update = { "user/contact.html", form, resident, null }; post.Invoke(integration, update);
        Check(((string)update[3]).Contains("Confirm") && native.GetContact(resident.PrincipalID).Address == contact.Address, "Missing consent changed private details");
        form["consent"] = "true"; post.Invoke(integration, update);
        Check((string)update[3] == "Contact details updated" && native.GetContact(resident.PrincipalID).Address == "Changed address"
            && native.GetContact(stranger.PrincipalID).Address == "", "Contact actor was taken from posted UUID");
        form["address"] = new string('x', 201); post.Invoke(integration, update);
        Check((string)update[3] != "Contact details updated" && native.GetContact(resident.PrincipalID).Address == "Changed address", "Contact length bound bypassed");
        update[2] = null; post.Invoke(integration, update); Check((string)update[3] == "A local account is required", "Anonymous contact mutation accepted");
        var foreign = new UserAccount { PrincipalID = UUID.Random(), LocalToGrid = false }; update[2] = foreign; post.Invoke(integration, update);
        Check((string)update[3] == "A local account is required", "Foreign account contact mutation accepted");
        update[2] = resident; form["action"] = "clear"; post.Invoke(integration, update);
        Check((string)update[3] == "Contact details updated" && native.GetContact(resident.PrincipalID).Address == "", "Contact clearing failed");
        Set("_residentContactEnabled", false); form["action"] = "save"; post.Invoke(integration, update);
        Check((string)update[3] == "Contact storage is disabled by the grid operator", "Disabled contact storage accepted a write");
        Set("_residentContactEnabled", true);
        var siteType = assembly.GetType("OpenSim.Continuum.WebUI.WhiteCoreSite", true);
        var site = RuntimeHelpers.GetUninitializedObject(siteType);
        siteType.GetField("_mountPath", Private).SetValue(site, "/portal"); siteType.GetField("_systemName", Private).SetValue(site, "Fixture");
        siteType.GetField("_integration", Private).SetValue(site, integration);
        var auth = DispatchProxy.Create<IAuthenticationService, RecoveryProxy>();
        ((RecoveryProxy)(object)auth).Handler = (method, args) => method.Name == "Verify" ? true : null;
        var accounts = DispatchProxy.Create<IUserAccountService, RecoveryProxy>();
        ((RecoveryProxy)(object)accounts).Handler = (method, args) => method.Name == "GetUserAccount" ? resident : null;
        siteType.GetField("_authentication", Private).SetValue(site, auth); siteType.GetField("_accounts", Private).SetValue(site, accounts);
        var request = DispatchProxy.Create<IOSHttpRequest, HttpProxy>(); var fields = ((HttpProxy)(object)request).Values;
        var headers = new NameValueCollection { ["Cookie"] = "ContinuumWebUI=" + resident.PrincipalID + ".fixture", ["Origin"] = "https://grid.example" };
        fields["Headers"] = headers; fields["HttpMethod"] = "POST"; fields["UriPath"] = "/portal/user/contact.html";
        fields["Url"] = new Uri("https://grid.example/portal/user/contact.html"); fields["IsSecured"] = true;
        fields["QueryAsDictionary"] = new Dictionary<string, string>(); fields["ContentEncoding"] = Encoding.UTF8;
        byte[] body = Encoding.UTF8.GetBytes("action=save&consent=true&address=HTTP+address"); fields["ContentLength64"] = (long)body.Length;
        Dictionary<string, object> Request()
        {
            fields["InputStream"] = new MemoryStream(body);
            var response = DispatchProxy.Create<IOSHttpResponse, HttpProxy>(); siteType.GetMethod("Handle", Private).Invoke(site, new object[] { request, response });
            return ((HttpProxy)(object)response).Values;
        }
        headers["Origin"] = "https://attacker.example"; Check((int)Request()["StatusCode"] == 403, "Cross-site contact write accepted");
        headers["Origin"] = "https://grid.example"; fields["IsSecured"] = false;
        Check((int)Request()["StatusCode"] == 403, "Contact feature did not require secure account transport"); fields["IsSecured"] = true;
        var response = Request(); Check((int)response["StatusCode"] == 200 && Encoding.UTF8.GetString((byte[])response["RawBuffer"]) == "Contact details updated"
            && native.GetContact(resident.PrincipalID).Address == "HTTP address", "Actual contact HTTP path failed");
        fields["HttpMethod"] = "GET"; response = Request(); string html = Encoding.UTF8.GetString((byte[])response["RawBuffer"]);
        Check((int)response["StatusCode"] == 200 && html.Contains("contact_form") && html.Contains("HTTP address") && html.Contains("contact_clear_form"), "Embedded contact page failed");
        fields["UriPath"] = "/portal/user/update_user.html"; html = Encoding.UTF8.GetString((byte[])Request()["RawBuffer"]);
        Check(html.Contains("user-password") && html.Contains("user-email") && !html.Contains("change_user_information.html"), "Account settings retained broken legacy actions");
        Console.WriteLine("PASS: private native contact SQLite persistence/Unicode, public API refusal, actor isolation, consent/clear, bounded fields, HTTPS/origin and embedded account forms.");
    }
}
