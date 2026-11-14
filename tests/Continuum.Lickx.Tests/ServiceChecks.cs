using System.Reflection;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Data;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Region.CoreModules.Avatar.Friends;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.CoreModules.Framework.UserManagement;
using OpenSim.Services.PresenceService;
using OpenSim.Services.UserAccountService;
using OpenSim.Services.Interfaces;

static class ServiceChecks
{
    public static void Run()
    {
        MainConsole.Instance = new MockConsole();
        var user = UUID.Random().ToString();
        var session = UUID.Random();
        var firstRegion = UUID.Random();
        var nextRegion = UUID.Random();
        var data = new PresenceData { UserID = user, SessionID = session, RegionID = firstRegion, Data = new() };
        int reads = 0;
        var database = Proxy.Create<IPresenceData>((method, args) =>
        {
            if (method.Name == "Get")
            {
                reads++;
                var copy = new PresenceData { UserID = data.UserID, SessionID = data.SessionID,
                    RegionID = data.RegionID, Data = new(data.Data) };
                return args.Length == 1 ? copy : new[] { copy };
            }
            throw new NotSupportedException(method.Name);
        });
        PresenceService Presence(bool enabled)
        {
            var config = Config("PresenceService", "OpenSim.Data.Null.dll", enabled);
            var service = new PresenceService(config);
            typeof(PresenceServiceBase).GetField("m_Database", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(service, database);
            return service;
        }
        var cached = Presence(true);
        Check(cached.GetAgent(session).RegionID == firstRegion, "Initial cached presence");
        int cachedReads = reads;
        data.RegionID = nextRegion;
        Check(cached.GetAgent(session).RegionID == firstRegion && reads == cachedReads, "Enabled presence cache");
        var uncached = Presence(false);
        Check(uncached.GetAgent(session).RegionID == nextRegion, "Disabled cache must see authoritative presence");
        data.RegionID = firstRegion;
        Check(uncached.GetAgents(new[] { user })[0].RegionID == firstRegion, "Disabled user presence lookup");
        Check(Presence(true).GetAgent(session).RegionID == firstRegion, "Presence caches must be per service instance");

        var gridData = new GridUserData { UserID = user, Data = new() { ["Online"] = "True", ["Login"] = "1", ["Logout"] = "0" } };
        var gridDatabase = Proxy.Create<IGridUserData>((method, args) => method.Name switch
        {
            "GetAll" => new[] { new GridUserData { UserID = gridData.UserID, Data = new(gridData.Data) } },
            _ => throw new NotSupportedException(method.Name)
        });
        GridUserService Grid(bool enabled)
        {
            var config = Config("GridUserService", "OpenSim.Data.Null.dll", enabled);
            config.Configs["GridUserService"].Set("StorageProvider",
                typeof(TestGridProvider).Assembly.Location);
            var service = new GridUserService(config);
            typeof(GridUserServiceBase).GetField("m_Database", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(service, gridDatabase);
            return service;
        }
        var gridCached = Grid(true);
        Check(gridCached.GetGridUserInfo(user).Online, "Initial cached grid user");
        gridData.Data["Online"] = "False";
        Check(gridCached.GetGridUserInfo(user).Online, "Enabled grid user cache");
        var gridUncached = Grid(false);
        Check(!gridUncached.GetGridUserInfo(user).Online, "Second instance must honor UseCache=false");
        gridData.Data["Online"] = "True";
        Check(gridUncached.GetGridUserInfo(user).Online, "Disabled grid user cache reads each time");
        gridData.Data["Online"] = "False";
        Check(!Grid(true).GetGridUserInfo(user).Online, "Grid user caches must not cross instances");

        var friendA = UUID.Random();
        var friendB = UUID.Random();
        var users = Proxy.Create<IUserManagement>((method, args) => method.Name == "GetUserServerURL"
            ? (UUID)args[0] == friendA ? "https://a.test.invalid/" : "https://b.test.invalid/"
            : throw new NotSupportedException(method.Name));
        var friends = new[]
        {
            new OpenSim.Services.Interfaces.FriendInfo { Friend = friendA + ";https://home.test.invalid/;First Friend" },
            new OpenSim.Services.Interfaces.FriendInfo { Friend = friendB + ";https://home.test.invalid/;Second Friend" },
            new OpenSim.Services.Interfaces.FriendInfo { Friend = friendA.ToString() },
            new OpenSim.Services.Interfaces.FriendInfo { Friend = "invalid" }
        };
        var targets = (Dictionary<string, List<string>>)typeof(HGStatusNotifier)
            .GetMethod("GetNotificationTargets", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { users, friends });
        Check(targets.Count == 2 && targets["https://a.test.invalid"].SequenceEqual(new[] { friendA.ToString() })
            && targets["https://b.test.invalid"].SequenceEqual(new[] { friendB.ToString() }),
            "HG notifications must send deduplicated UUIDs to each friend's resolved endpoint");
        var creator = UUID.Random();
        var management = new UserManagementModule();
        var accountService = Proxy.Create<IUserAccountService>((method, args) =>
            method.Name == "GetUserAccount" ? new UserAccount { PrincipalID = creator } : null);
        typeof(UserManagementModule).GetField("m_userAccountService", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(management, accountService);
        var cache = typeof(UserManagementModule).GetField("m_userCacheByID", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(management);
        bool ContainsCreator(UUID id) => (bool)cache.GetType().GetMethod("ContainsKey", new[] { typeof(UUID) })
            .Invoke(cache, new object[] { id });
        var forged = UUID.Random();
        management.AddCreatorUser(forged, ";Local Resident");
        Check(!ContainsCreator(forged), "Creator metadata must not substitute a local account's UUID");
        management.AddCreatorUser(creator, ";Local Resident");
        Check(ContainsCreator(creator), "Matching authoritative local creator must be cached");

        foreach (var module in new object[] { new OpenSim.Groups.GroupsModule(),
            new OpenSim.Region.OptionalModules.Avatar.XmlRpcGroups.GroupsModule() })
        {
            var scene = new Scene(new RegionInfo { RegionID = UUID.Random(), RegionName = "Group regression" });
            scene.RegisterModuleInterface(accountService);
            var scenes = (List<Scene>)module.GetType().GetField("m_sceneList", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(module);
            scenes.Add(scene);
            var activeGroup = UUID.Random();
            GroupMembershipData membership = module is OpenSim.Groups.GroupsModule
                ? new OpenSim.Groups.ExtendedGroupMembershipData { GroupID = activeGroup, GroupTitle = "Updated title" }
                : new GroupMembershipData { GroupID = activeGroup, GroupTitle = "Updated title" };
            int activations = 0;
            Func<MethodInfo, object[], object> handleGroup = (method, args) =>
            {
                if (method.Name == "GetAgentActiveMembership") return membership;
                if (method.Name == "SetAgentActiveGroup") activations++;
                return null;
            };
            var groupField = module.GetType().GetField("m_groupData", BindingFlags.Instance | BindingFlags.NonPublic);
            var create = typeof(Proxy).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Single(m => m.Name == "Create").MakeGenericMethod(groupField.FieldType);
            groupField.SetValue(module, create.Invoke(null, new object[] { handleGroup }));
            var client = Proxy.Create<IClientAPI>((method, args) => method.Name switch
            {
                "get_AgentId" => creator,
                "get_Scene" => scene,
                _ => null
            });
            var update = module.GetType().GetMethod("GroupTitleUpdate");
            update.Invoke(module, new object[] { client, activeGroup, UUID.Random() });
            Check(activations == 1, "Active group title must refresh its authoritative group");
            update.Invoke(module, new object[] { client, UUID.Random(), UUID.Random() });
            Check(activations == 1, "Editing another group must not activate it");
            membership = null;
            update.Invoke(module, new object[] { client, activeGroup, UUID.Random() });
            Check(activations == 1, "Missing membership must not activate a group");
        }
        Console.WriteLine("PASS: presence/grid-user cache opt-out, per-instance isolation and Hypergrid UUID/endpoint notification contracts.");
        Console.WriteLine("PASS: creator UUID validation and native/XML-RPC active-group title refresh boundaries.");
    }

    static IniConfigSource Config(string section, string provider, bool enabled)
    {
        var config = new IniConfigSource();
        var service = config.AddConfig(section);
        service.Set("StorageProvider", Path.Combine(AppContext.BaseDirectory, provider));
        service.Set("ConnectionString", "URI=file::memory:");
        service.Set("UseCache", enabled);
        return config;
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}

// Lets the real service constructor/configuration run without a database server.
public class TestGridProvider : IGridUserData
{
    public TestGridProvider(string connection, string realm) { }
    public GridUserData Get(string userID) => null;
    public GridUserData[] GetAll(string query) => Array.Empty<GridUserData>();
    public bool Store(GridUserData data) => true;
}
