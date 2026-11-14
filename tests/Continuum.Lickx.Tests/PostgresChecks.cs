using Npgsql;
using OpenMetaverse;
using OpenSim.Data.PGSQL;

static class PostgresChecks
{
    public static void Run()
    {
        string connection = Environment.GetEnvironmentVariable("CONTINUUM_LICKX_PG");
        if (string.IsNullOrEmpty(connection))
        {
            Console.WriteLine("SKIP: optional PostgreSQL provider checks (set CONTINUUM_LICKX_PG to a disposable continuum_lickx_* database).");
            return;
        }
        var settings = new NpgsqlConnectionStringBuilder(connection);
        if (settings.Host != "127.0.0.1" || settings.Port != 9543 || !settings.Database.StartsWith("continuum_lickx_"))
            throw new Exception("Provider checks require a dedicated loopback continuum_lickx_* database on 9543");

        var grid = new PGSQLGridUserData(connection, "griduser");
        var groups = new PGSQLGroupsData(connection, "os_groups");
        var accounts = new PGSQLUserAccountData(connection, "useraccounts");
        var regions = new PGSQLRegionData(connection, "regions");
        var user = UUID.Random();
        var other = UUID.Random();
        var scope = UUID.Random();
        var group = UUID.Random();
        var hidden = UUID.Random();
        using var database = new NpgsqlConnection(connection);
        database.Open();
        using var insert = new NpgsqlCommand(@"
INSERT INTO griduser (""UserID"") VALUES (@upperUser);
INSERT INTO os_groups_groups (""GroupID"",""Name"",""Charter"",""ShowInList"",""InsigniaID"",""FounderID"",""OwnerRoleID"") VALUES (@group,@groupName,'',1,@scope,@scope,@scope),(@hidden,@groupName,'',0,@scope,@scope,@scope);
INSERT INTO useraccounts (""PrincipalID"",""ScopeID"",""FirstName"",""LastName"",""DisplayName"") VALUES (@user,@scope,'O''Neil','Resident','Quoted Ω'),(@other,@otherScope,'O''Neil','Resident','Quoted Ω');
INSERT INTO regions (uuid,""regionName"",""ScopeID"",""locX"",""locY"") VALUES (@region,'O''Neil Region Ω',@scope,256000,256000);", database);
        insert.Parameters.AddWithValue("upperUser", user.ToString().ToUpperInvariant());
        insert.Parameters.AddWithValue("group", group.Guid);
        insert.Parameters.AddWithValue("hidden", hidden.Guid);
        string groupName = "O'Neil Ω " + group;
        insert.Parameters.AddWithValue("groupName", groupName);
        insert.Parameters.AddWithValue("user", user.Guid);
        insert.Parameters.AddWithValue("other", other.Guid);
        insert.Parameters.AddWithValue("scope", scope.Guid);
        insert.Parameters.AddWithValue("otherScope", UUID.Random().Guid);
        insert.Parameters.AddWithValue("region", UUID.Random().Guid);
        insert.ExecuteNonQuery();
        Check(grid.GetAll(user.ToString()).Length == 1, "Case-insensitive grid-user UUID lookup");
        Check(grid.GetAll("' OR 1=1 --").Length == 0, "Grid-user prefix must be bound data");
        Check(groups.RetrieveGroups(groupName.Replace("O'Neil", "o'neil")).Select(g => g.GroupID).SequenceEqual(new[] { group }), "Quoted group query and directory privacy");
        Check(groups.RetrieveGroups("' OR 1=1 --").Length == 0, "Group search must be bound data");
        Check(accounts.GetUsers(scope, "o'neil").Select(a => a.PrincipalID).SequenceEqual(new[] { user }), "Account name lookup must retain scope");
        Check(accounts.GetUsers(scope, "quoted Ω").Select(a => a.PrincipalID).SequenceEqual(new[] { user }), "Display-name search must retain scope");
        Check(regions.Get("o'neil%", scope).Count == 1 && regions.Get("o'neil%", UUID.Random()).Count == 0,
            "Region search must retain case-insensitive lookup and scope");
        Console.WriteLine("PASS: PostgreSQL grid-user/group quoted-input boundaries and scoped account/display-name/region queries.");
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
