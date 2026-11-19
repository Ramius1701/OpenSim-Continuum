using OpenMetaverse;
using OpenSim.Data.SQLite;
using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;
using System.Reflection;

string path = Path.Combine(Path.GetTempPath(), $"continuum-regionextras-{Guid.NewGuid():N}.db");
UUID a = UUID.Random(), b = UUID.Random();
UUID primID = UUID.Random(), itemID = UUID.Random(), experienceID = UUID.Random();
const string key = "module:key'";
try
{
    WithStore(store =>
    {
        Check(store.GetExtra(a).Count == 0, "Empty region must have no extras");
        store.SaveExtra(a, key, "original");
        store.SaveExtra(a, key, "updated cafÃƒÂ© Ã¦â€”Â¥Ã¦Å“Â¬Ã¨ÂªÅ¾");
        store.SaveExtra(b, key, "other region");
        store.StorePrimInventory(primID, new[] { new TaskInventoryItem
        {
            ItemID = itemID, ParentPartID = primID, ParentID = primID, AssetID = UUID.Random(),
            Name = "experience script", Description = string.Empty, CreatorID = UUID.Random(),
            ExperienceID = experienceID
        } });
    });
    WithStore(store =>
    {
        Check(store.GetExtra(a).Count == 1 && store.GetExtra(a)[key] == "updated cafÃƒÂ© Ã¦â€”Â¥Ã¦Å“Â¬Ã¨ÂªÅ¾",
            "Latest value did not survive restart");
        var part = new SceneObjectPart(UUID.Random(), PrimitiveBaseShape.CreateBox(),
            Vector3.Zero, Quaternion.Identity, Vector3.Zero) { UUID = primID };
        var group = new SceneObjectGroup(part);
        typeof(SQLiteSimulationData).GetMethod("LoadItems", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(store, new object[] { part });
        Check(part.TaskInventory[itemID].ExperienceID == experienceID,
            "The script Experience association did not survive database reopen");
        GC.KeepAlive(group);
        store.RemoveExtra(a, key);
        Check(store.GetExtra(a).Count == 0 && store.GetExtra(b)[key] == "other region",
            "Removal crossed region boundaries");
    });
    Console.WriteLine("PASS: SQLite region extras and script Experience links persist across reopen.");
}
finally
{
    System.Data.SQLite.SQLiteConnection.ClearAllPools();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    File.Delete(path);
}

foreach (float ambiance in new[] { -1f, 0f, 1f, 50f, 100f, 101f })
{
    var shape = PrimitiveBaseShape.CreateBox();
    shape.ReflectionProbe = new Primitive.ReflectionProbe { Ambiance = ambiance, ClipDistance = 12f, Flags = 1 };
    var loaded = PrimitiveBaseShape.CreateBox();
    loaded.ExtraParams = shape.ExtraParams;
    Check(loaded.ReflectionProbe.Ambiance == Math.Clamp(ambiance, 0f, 100f)
        && loaded.ReflectionProbe.ClipDistance == 12f && loaded.ReflectionProbe.Flags == 1,
        $"Reflection probe serialization failed for ambiance {ambiance}");
}
Console.WriteLine("PASS: reflection probe ambiance, clip distance, and flags round-trip.");

Check(Util.IsNanOrInfinity(new Vector3(0, 0, float.PositiveInfinity)), "Infinite Z was accepted");
Check(!Util.IsNanOrInfinity(new Vector3(0, 0, 3)), "Finite vector was rejected");
Console.WriteLine("PASS: vector validation rejects infinite Z.");

var presence = (ScenePresence)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ScenePresence));
Check(presence.GetMass() == 0, "An avatar without a previous actor must report zero mass");
var actor = new TestPhysicsActor();
var actorField = typeof(ScenePresence).GetField("m_physActor", BindingFlags.Instance | BindingFlags.NonPublic);
actorField.SetValue(presence, actor);
Check(presence.GetMass() == actor.Mass, "Avatar mass must come from its current actor");
actorField.SetValue(presence, null);
Check(presence.GetMass() == actor.Mass, "Avatar must retain its last known mass while detached from physics");
Console.WriteLine("PASS: avatar mass survives a temporary physics actor removal.");

var sculpt = PrimitiveBaseShape.CreateBox();
sculpt.SculptEntry = true;
sculpt.SculptTexture = UUID.Random();
var keys = new HashSet<ulong>();
foreach (byte type in new byte[] { 1, 3, 65, 129, 193 })
{
    sculpt.SculptType = type;
    keys.Add(sculpt.GetMeshKey(new Vector3(1, 1, 1), 32));
}
Check(keys.Count == 5, "Sculpt shapes and flags share a mesh cache entry");

var movingPart = new SceneObjectPart(UUID.Random(), PrimitiveBaseShape.CreateBox(),
    Vector3.Zero, Quaternion.Identity, Vector3.Zero);
var movingGroup = new SceneObjectGroup(movingPart);
var movingActor = new TestPhysicsActor();
movingPart.PhysActor = movingActor;
movingGroup.MoveToTarget(new Vector3(10, 20, 30), 1);
Check(movingActor.PIDActive, "Valid move target did not start");
movingGroup.MoveToTarget(new Vector3(99, 99, 99), 0);
Check(movingActor.PIDActive && movingActor.LastTarget == new Vector3(10, 20, 30),
    "Zero tau replaced or stopped an active move target");
movingPart.VehicleParams = new SOPVehicle();
movingPart.VehicleParams.vd.m_linearMotorTimescale = 2f;
typeof(EntityBase).GetField("m_scene", BindingFlags.Instance | BindingFlags.NonPublic)
    .SetValue(movingGroup, new Scene(new RegionInfo { RegionID = UUID.Random() }));
var copiedPart = movingPart.Copy(0, movingPart.OwnerID, UUID.Zero, 0, false);
Check(copiedPart.VehicleParams.vd.m_linearMotorTimescale == 2f, "Vehicle settings were not copied");
copiedPart.VehicleParams.vd.m_linearMotorTimescale = 3f;
Check(movingPart.VehicleParams.vd.m_linearMotorTimescale == 2f, "The copy mutated the source vehicle");
movingActor.Flags = 0xff;
movingActor.SetVehicle(new OpenSim.Region.PhysicsModules.SharedBase.VehicleData());
Check(movingActor.Flags == 0, "Applying vehicle settings did not clear stale flags");
Console.WriteLine("PASS: sculpt mesh cache separation, zero-tau movement and vehicle flags.");

string inventoryPath = Path.Combine(Path.GetTempPath(), $"continuum-experience-inventory-{Guid.NewGuid():N}.db");
var inventoryConfig = new Nini.Config.IniConfigSource();
var inventorySection = inventoryConfig.AddConfig("InventoryService");
inventorySection.Set("StorageProvider", Path.Combine(AppContext.BaseDirectory, "OpenSim.Data.SQLite.dll") + ":SQLiteXInventoryData");
inventorySection.Set("ConnectionString", $"Data Source={inventoryPath};Version=3;Pooling=False");
var localInventory = new OpenSim.Services.InventoryService.XInventoryService(inventoryConfig);
var foreignInventory = new ForeignInventoryFixture(inventoryConfig);
var scriptItem = new InventoryItemBase(UUID.Random(), a)
{
    AssetID = UUID.Random(), InvType = (int)InventoryType.LSL, AssetType = (int)AssetType.LSLText,
    Name = "linked script", Description = "", CreatorId = a.ToString(), ExperienceID = experienceID
};
Check(localInventory.AddItem(scriptItem), "Local inventory add failed");
CloseInventoryFixtureConnections();
var reopenedInventory = new OpenSim.Services.InventoryService.XInventoryService(inventoryConfig);
Check(reopenedInventory.GetItem(a, scriptItem.ID).ExperienceID == experienceID, "Inventory link did not survive reopen");
var changed = (InventoryItemBase)scriptItem.Clone();
changed.ExperienceID = UUID.Random();
Check(!foreignInventory.AddItem(changed), "Foreign add replaced an existing item");
Check(foreignInventory.UpdateItem(changed), "Foreign metadata update failed");
Check(localInventory.GetItem(a, scriptItem.ID).ExperienceID == experienceID, "Foreign metadata replaced a local link");
changed.AssetID = UUID.Random();
Check(foreignInventory.UpdateItem(changed), "Foreign asset update failed");
Check(localInventory.GetItem(a, scriptItem.ID).ExperienceID == UUID.Zero, "New foreign asset inherited a local link");
changed.ID = UUID.Random();
Check(foreignInventory.AddItem(changed), "Foreign inventory add failed");
Check(localInventory.GetItem(a, changed.ID).ExperienceID == UUID.Zero, "Foreign link was imported");
System.Data.SQLite.SQLiteConnection.ClearAllPools();
var ais = new OpenSim.Region.ClientStack.LindenCaps.AIS.AisReadHandler("/fixture", a, localInventory);
scriptItem.ExperienceID = experienceID;
Check(localInventory.UpdateItem(scriptItem), "Could not restore AIS fixture item");
Check(ais.Fetch("GET", $"/fixture/item/{scriptItem.ID}", out var aisItem) == 200
    && aisItem["item_id"].AsUUID() == scriptItem.ID && !aisItem.ContainsKey("ExperienceID"),
    "AIS item projection failed");
var otherAis = new OpenSim.Region.ClientStack.LindenCaps.AIS.AisReadHandler("/fixture", b, localInventory);
Check(otherAis.Fetch("GET", $"/fixture/item/{scriptItem.ID}", out _) == 404, "AIS returned another owner's item");
Check(ais.Fetch("DELETE", $"/fixture/item/{scriptItem.ID}", out _) == 405, "Read handler accepted mutation");
Check(ais.Fetch("GET", $"/fixture-other/item/{scriptItem.ID}", out _) == 404, "Wrong capability path accepted");
Check(localInventory.CreateUserInventory(a), "AIS fixture root creation failed");
var aisRoot = localInventory.GetRootFolder(a);
Check(ais.Fetch("GET", $"/fixture/category/{aisRoot.ID}/children?depth=0", out var aisCategory) == 200,
    "AIS children read failed");
var embedded = (OpenMetaverse.StructuredData.OSDMap)aisCategory["_embedded"];
Check(embedded.ContainsKey("categories") && embedded.ContainsKey("items") && embedded.ContainsKey("links"),
    "AIS folder expansion is missing a collection");
var patch = new OpenMetaverse.StructuredData.OSDMap { ["name"] = "renamed", ["desc"] = "updated description" };
Check(otherAis.Patch($"/fixture/item/{scriptItem.ID}", patch, out _) == 404, "Foreign item PATCH succeeded");
Check(ais.Patch($"/fixture/item/{scriptItem.ID}", patch, out var patchResult) == 200
    && patchResult["name"].AsString() == "renamed", "Owned item PATCH failed");
var afterPatch = localInventory.GetItem(a, scriptItem.ID);
Check(afterPatch.Description == "updated description" && afterPatch.AssetID == scriptItem.AssetID
    && afterPatch.ExperienceID == experienceID, "Metadata PATCH changed protected inventory fields");
patch["asset_id"] = UUID.Random();
Check(ais.Patch($"/fixture/item/{scriptItem.ID}", patch, out _) == 400, "Unsupported field was accepted");
patch.Remove("asset_id");
patch.Remove("desc");
Check(ais.Patch($"/fixture/category/{aisRoot.ID}", patch, out _) == 403, "Root rename was accepted");
var customFolder = new InventoryFolderBase { ID = UUID.Random(), Owner = a, ParentID = aisRoot.ID,
    Name = "before", Type = (short)FolderType.None, Version = 1 };
Check(localInventory.AddFolder(customFolder), "Fixture folder creation failed");
Check(ais.Patch($"/fixture/category/{customFolder.ID}", patch, out _) == 200
    && localInventory.GetFolder(a, customFolder.ID).Name == "renamed", "Owned custom folder rename failed");
var create = new OpenMetaverse.StructuredData.OSDMap
{
    ["categories"] = new OpenMetaverse.StructuredData.OSDArray
    {
        new OpenMetaverse.StructuredData.OSDMap { ["name"] = "created folder", ["parent_id"] = aisRoot.ID }
    },
    ["links"] = new OpenMetaverse.StructuredData.OSDArray
    {
        new OpenMetaverse.StructuredData.OSDMap { ["name"] = "created link", ["linked_id"] = scriptItem.ID }
    }
};
string createRoute = $"/fixture/category/{aisRoot.ID}?tid={UUID.Random()}";
Check(otherAis.Create(createRoute, create, out _) == 404, "Foreign parent was accepted");
var folderFields = (OpenMetaverse.StructuredData.OSDMap)((OpenMetaverse.StructuredData.OSDArray)create["categories"])[0];
folderFields["parent_id"] = UUID.Random();
Check(ais.Create(createRoute, create, out _) == 400, "Mismatched parent was accepted");
folderFields["parent_id"] = aisRoot.ID;
Check(ais.Create(createRoute, create, out var created) == 200, "Folder and link creation failed");
var newFolderID = ((OpenMetaverse.StructuredData.OSDArray)created["_created_categories"])[0].AsUUID();
var newLinkID = ((OpenMetaverse.StructuredData.OSDArray)created["_created_items"])[0].AsUUID();
Check(localInventory.GetFolder(a, newFolderID).ParentID == aisRoot.ID
    && localInventory.GetItem(a, newLinkID).AssetID == scriptItem.ID
    && localInventory.GetItem(a, newLinkID).ExperienceID == UUID.Zero,
    "Created inventory records did not match the requested parent and target");
Check(otherAis.RemoveItem($"/fixture/item/{newLinkID}", out _) == 404, "Foreign item deletion succeeded");
Check(ais.RemoveItem($"/fixture/item/{newLinkID}", out var removed) == 200
    && ((OpenMetaverse.StructuredData.OSDArray)removed["_removed_items"])[0].AsUUID() == newLinkID
    && localInventory.GetItem(a, newLinkID) is null && localInventory.GetItem(a, scriptItem.ID) is not null,
    "Link deletion did not leave the target intact");
Check(ais.RemoveCategory($"/fixture/category/{aisRoot.ID}", out _) == 403, "Root removal succeeded");
Check(ais.RemoveCategory($"/fixture/category/{newFolderID}", out _) == 403, "Non-Trash folder removal succeeded");
var trash = localInventory.GetFolderForType(a, FolderType.Trash);
var nested = new InventoryFolderBase(UUID.Random(), "nested", a, (short)FolderType.None, trash.ID, 1);
Check(localInventory.AddFolder(nested), "Trash fixture folder failed");
var disposable = (InventoryItemBase)scriptItem.Clone();
disposable.ID = UUID.Random();
disposable.Folder = nested.ID;
Check(localInventory.AddItem(disposable), "Trash fixture item failed");
Check(otherAis.RemoveCategory($"/fixture/category/{trash.ID}/children", out _) == 404, "Foreign purge succeeded");
Check(ais.RemoveCategory($"/fixture/category/{trash.ID}/children", out var purged) == 200
    && ((OpenMetaverse.StructuredData.OSDArray)purged["_categories_removed"])[0].AsUUID() == nested.ID
    && ((OpenMetaverse.StructuredData.OSDArray)purged["_removed_items"])[0].AsUUID() == disposable.ID
    && localInventory.GetFolder(a, trash.ID) is not null
    && localInventory.GetFolder(a, nested.ID) is null && localInventory.GetItem(a, disposable.ID) is null,
    "Trash purge did not remove exactly the expected rows");
Check(localInventory.AddFolder(nested) &&
    ais.RemoveCategory($"/fixture/category/{nested.ID}", out _) == 200
    && localInventory.GetFolder(a, nested.ID) is null, "Trash folder removal failed");
var lost = localInventory.GetFolderForType(a, FolderType.LostAndFound);
var lostChild = new InventoryFolderBase(UUID.Random(), "lost child", a, (short)FolderType.None, lost.ID, 1);
Check(localInventory.AddFolder(lostChild) &&
    ais.RemoveCategory($"/fixture/category/{lostChild.ID}", out _) == 200 &&
    localInventory.GetFolder(a, lostChild.ID) is null, "Lost And Found child removal failed");
var cof = localInventory.GetFolderForType(a, FolderType.CurrentOutfit);
string outfitRoute = $"/fixture/category/{cof.ID}/links?tid={UUID.Random()}";
var outfit = new OpenMetaverse.StructuredData.OSDArray
{
    new OpenMetaverse.StructuredData.OSDMap { ["name"] = "fixture link", ["linked_id"] = scriptItem.ID }
};
Check(otherAis.ReplaceOutfitLinks(outfitRoute, outfit, out _) == 404, "Foreign outfit replacement succeeded");
Check(ais.ReplaceOutfitLinks(outfitRoute, outfit, out var outfitResult) == 200, "Outfit replacement failed");
var outfitID = ((OpenMetaverse.StructuredData.OSDArray)outfitResult["_created_items"])[0].AsUUID();
Check(ais.ReplaceOutfitLinks(outfitRoute, new OpenMetaverse.StructuredData.OSDMap(), out _) == 400
    && localInventory.GetItem(a, outfitID) is not null, "Invalid outfit body changed existing links");
var failing = new FailingOutfitInventory(inventoryConfig);
var failingAis = new OpenSim.Region.ClientStack.LindenCaps.AIS.AisReadHandler("/fixture", a, failing);
outfit.Add(new OpenMetaverse.StructuredData.OSDMap { ["name"] = "second link", ["linked_id"] = scriptItem.ID });
Check(failingAis.ReplaceOutfitLinks(outfitRoute, outfit, out _) == 409
    && localInventory.GetFolderItems(a, cof.ID).Count == 1 && localInventory.GetItem(a, outfitID) is not null,
    "Add failure did not roll back new links and preserve the previous outfit");
Check(ais.ReplaceOutfitLinks(outfitRoute, new OpenMetaverse.StructuredData.OSDArray(), out _) == 200
    && localInventory.GetItem(a, outfitID) is null && localInventory.GetItem(a, scriptItem.ID) is not null,
    "Explicit outfit clear removed the wrong records");
Console.WriteLine("PASS: AIS outfit replacement, validation and rollback on partial add failure.");
Console.WriteLine("PASS: AIS reads, metadata updates, creation, and scoped item/folder removal and purge.");
var lib = new AisLibraryFixture();
var libraryHandler = new OpenSim.Region.ClientStack.LindenCaps.AIS.AisLibraryHandler("/library", a, lib, localInventory);
string libRoute = $"/library/category/{lib.LibraryRootFolder.ID}";
Check(libraryHandler.Dispatch("GET", libRoute + "/children", null, out var libRead) == 200
    && libRead.ContainsKey("_embedded"), "Library category read failed");
Check(libraryHandler.Dispatch("GET", $"/library/item/{lib.Sample.ID}", null, out var libItem) == 200
    && libItem["item_id"].AsUUID() == lib.Sample.ID, "Library item read failed");
Check(libraryHandler.Dispatch("PATCH", libRoute, null, out _) == 405, "Library write was accepted");
var foreignLibraryHandler = new OpenSim.Region.ClientStack.LindenCaps.AIS.AisLibraryHandler("/library", b, lib, localInventory);
Check(foreignLibraryHandler.Dispatch("COPY", libRoute, aisRoot.ID.ToString(), out _) == 404, "Foreign copy destination accepted");
Check(libraryHandler.Dispatch("COPY", libRoute + $"?tid={UUID.Random()},depth=0", aisRoot.ID.ToString(), out var shallowCopy) == 200
    && ((OpenMetaverse.StructuredData.OSDArray)shallowCopy["_created_categories"]).Count == 1,
    "Shallow library copy failed");
Check(libraryHandler.Dispatch("COPY", libRoute, aisRoot.ID.ToString(), out var deepCopy) == 200
    && ((OpenMetaverse.StructuredData.OSDArray)deepCopy["_created_categories"]).Count == 2, "Recursive library copy failed");
var copiedLibraryID = ((OpenMetaverse.StructuredData.OSDArray)deepCopy["_created_items"])[0].AsUUID();
var copiedLibraryItem = localInventory.GetItem(a, copiedLibraryID);
Check(copiedLibraryID != lib.Sample.ID && copiedLibraryItem.Owner == a && copiedLibraryItem.AssetID == lib.Sample.AssetID
    && copiedLibraryItem.CurrentPermissions == lib.Sample.CurrentPermissions && copiedLibraryItem.ExperienceID == UUID.Zero,
    "Copied library item did not preserve asset and permission fields or carried a foreign link");
string retryRoute = libRoute + $"?tid={UUID.Random()}";
Check(libraryHandler.Dispatch("COPY", retryRoute, aisRoot.ID.ToString(), out var originalReply) == 200,
    "Initial transaction failed");
Check(libraryHandler.Dispatch("COPY", retryRoute, aisRoot.ID.ToString(), out var replayed) == 200
    && ((OpenMetaverse.StructuredData.OSDArray)originalReply["_created_categories"])[0].AsUUID()
        == ((OpenMetaverse.StructuredData.OSDArray)replayed["_created_categories"])[0].AsUUID(),
    "Repeated COPY created new records");
Check(libraryHandler.Dispatch("COPY", retryRoute + ",depth=0", aisRoot.ID.ToString(), out _) == 409,
    "Reused transaction accepted a different copy request");
var dispatchWrite = ais.GetType().GetMethod("DispatchWrite", BindingFlags.Instance | BindingFlags.NonPublic);
int Write(string uri, OpenMetaverse.StructuredData.OSD body, out OpenMetaverse.StructuredData.OSDMap response)
{
    object[] parameters = ["PUT", uri, body, OpenMetaverse.StructuredData.OSDParser.SerializeLLSDXmlBytes(body), null];
    int status = (int)dispatchWrite.Invoke(ais, parameters);
    response = (OpenMetaverse.StructuredData.OSDMap)parameters[4];
    return status;
}
var oneLink = new OpenMetaverse.StructuredData.OSDArray
{
    new OpenMetaverse.StructuredData.OSDMap { ["name"] = "serial", ["linked_id"] = scriptItem.ID }
};
string sharedTransaction = $"/fixture/category/{cof.ID}/links?tid={UUID.Random()}";
var concurrent = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
{
    int status = Write(sharedTransaction, oneLink, out var response);
    return (status, ((OpenMetaverse.StructuredData.OSDArray)response["_created_items"])[0].AsUUID());
})).ToArray();
Task.WaitAll(concurrent);
Check(concurrent.All(t => t.Result.status == 200)
    && concurrent.Select(t => t.Result.Item2).Distinct().Count() == 1
    && localInventory.GetFolderItems(a, cof.ID).Count == 1, "Concurrent outfit retry was applied more than once");
Check(Write(sharedTransaction, new OpenMetaverse.StructuredData.OSDArray(), out _) == 409,
    "Reused transaction accepted a different outfit body");
Console.WriteLine("PASS: AIS concurrent retry replay and transaction request binding.");
foreach (OpenMetaverse.StructuredData.OSD copiedFolder in (OpenMetaverse.StructuredData.OSDArray)deepCopy["_created_categories"])
{
    var storedFolder = localInventory.GetFolder(a, copiedFolder.AsUUID());
    Check(((OpenMetaverse.StructuredData.OSDMap)deepCopy["_updated_category_versions"])[storedFolder.ID.ToString()].AsInteger()
        == storedFolder.Version, "Library COPY response did not report the final child-folder version");
}
Console.WriteLine("PASS: AIS library reads and shallow/recursive copy into owned inventory.");
LibraryLinkChecks.Run(localInventory, a, aisRoot.ID);
scriptItem.BasePermissions = (uint)OpenSim.Framework.PermissionMask.Copy | (uint)OpenSim.Framework.PermissionMask.Transfer;
scriptItem.CurrentPermissions = scriptItem.BasePermissions;
Check(localInventory.UpdateItem(scriptItem), "Permission fixture update failed");
ushort beforeMetadata = localInventory.GetFolder(a, scriptItem.Folder)?.Version ?? 0;
var fullMetadata = new OpenMetaverse.StructuredData.OSDMap
{
    ["item_id"] = scriptItem.ID, ["parent_id"] = scriptItem.Folder, ["type"] = scriptItem.AssetType,
    ["name"] = "full metadata", ["desc"] = "preserved identity", ["flags"] = 1,
    ["permissions"] = new OpenMetaverse.StructuredData.OSDMap
    {
        ["base_mask"] = -1, ["owner_mask"] = -1, ["next_owner_mask"] = -1, ["everyone_mask"] = -1, ["group_mask"] = -1
    },
    ["sale_info"] = new OpenMetaverse.StructuredData.OSDMap { ["sale_price"] = 25, ["sale_type"] = 2 }
};
Check(ais.Patch($"/fixture/item/{scriptItem.ID}", fullMetadata, out _) == 200, "Whole-item viewer PATCH failed");
var permissionResult = localInventory.GetItem(a, scriptItem.ID);
Check(permissionResult.BasePermissions == scriptItem.BasePermissions && permissionResult.CurrentPermissions == scriptItem.CurrentPermissions
    && (permissionResult.NextPermissions & ~scriptItem.BasePermissions) == 0
    && (permissionResult.EveryOnePermissions & ~scriptItem.BasePermissions) == 0
    && permissionResult.ExperienceID == experienceID && permissionResult.SalePrice == 25,
    "Permission or sale PATCH changed protected fields");
Check(ais.Fetch("GET", $"/fixture/category/{aisRoot.ID}/children?children={newFolderID}", out var subset) == 200
    && ((OpenMetaverse.StructuredData.OSDMap)((OpenMetaverse.StructuredData.OSDMap)subset["_embedded"])["categories"]).Count == 1,
    "AIS subset returned the wrong categories");
Check(ais.Fetch("GET", "/fixture/orphans", out var orphans) == 200 && orphans.ContainsKey("_embedded"),
    "AIS orphan request failed");
Console.WriteLine("PASS: whole-item metadata, permission limits, subset and orphan routes.");
var movedFixture = (InventoryItemBase)localInventory.GetItem(a, scriptItem.ID).Clone();
movedFixture.Folder = aisRoot.ID;
Check(localInventory.MoveItems(a, new List<InventoryItemBase> { movedFixture }), "HTTP item fixture move failed");
UploadChecks.Run(localInventory, a, aisRoot.ID);
RunAisHttp(localInventory, lib, a, b, scriptItem.ID, aisRoot.ID);
CloseInventoryFixtureConnections();
try { File.Delete(inventoryPath); }
catch (IOException) { Console.WriteLine($"Disposable SQLite fixture retained until process exit: {inventoryPath}"); }
Console.WriteLine("PASS: resident inventory retains local Experience links and rejects foreign associations.");

static void RunAisHttp(OpenSim.Services.Interfaces.IInventoryService inventory,
    OpenSim.Services.Interfaces.ILibraryService library, UUID owner, UUID stranger, UUID itemID, UUID rootID)
{
    using var socket = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    socket.Start();
    uint port = (uint)((System.Net.IPEndPoint)socket.LocalEndpoint).Port;
    socket.Stop();
    var server = new OpenSim.Framework.Servers.HttpServer.BaseHttpServer(port);
    var previous = OpenSim.Framework.Servers.MainServer.Instance;
    OpenSim.Framework.Servers.MainServer.AddHttpServer(server);
    OpenSim.Framework.Servers.MainServer.Instance = server;
    var scene = new Scene(new RegionInfo { RegionID = UUID.Random(), RegionName = "AIS fixture" });
    scene.RegisterModuleInterface(inventory);
    scene.RegisterModuleInterface(library);
    OpenSim.Framework.MainConsole.Instance ??= new OpenSim.Framework.Console.LocalConsole("HTTP tests");
    var config = new Nini.Config.IniConfigSource();
    config.AddConfig("AIS");
    var disabledBaking = new OpenSim.Region.OptionalModules.Avatar.ServerSideBaking.ServerSideBakingModule();
    disabledBaking.Initialise(config);
    disabledBaking.RegionLoaded(scene);
    var disabled = new OpenSim.Region.ClientStack.LindenCaps.AIS.AISv3Module();
    disabled.Initialise(config);
    disabled.RegionLoaded(scene);
    using var noCaps = new OpenSim.Framework.Capabilities.Caps(server, "127.0.0.1", port, UUID.Random().ToString(), owner, "AIS fixture");
    scene.EventManager.TriggerOnRegisterCaps(owner, noCaps);
    Check(!noCaps.CapsHandlers.ContainsCap("InventoryAPIv3"), "AIS registered without opt-in");
    Check(!noCaps.CapsHandlers.ContainsCap("UpdateAvatarAppearance"), "Baking registered without opt-in");
    disabledBaking.Close();
    config.AddConfig("Appearance").Set("ServerSideBaking", true);
    var baking = new OpenSim.Region.OptionalModules.Avatar.ServerSideBaking.ServerSideBakingModule();
    baking.Initialise(config);
    baking.AddRegion(scene);
    baking.RegionLoaded(scene);
    baking.RegionLoaded(scene);
    config.Configs["AIS"].Set("ExperimentalEnabled", true);
    var module = new OpenSim.Region.ClientStack.LindenCaps.AIS.AISv3Module();
    module.Initialise(config);
    module.RegionLoaded(scene);
    module.RegionLoaded(scene);
    using var caps = new OpenSim.Framework.Capabilities.Caps(server, "127.0.0.1", port, UUID.Random().ToString(), owner, "AIS fixture");
    using var foreignCaps = new OpenSim.Framework.Capabilities.Caps(server, "127.0.0.1", port, UUID.Random().ToString(), stranger, "AIS fixture");
    scene.EventManager.TriggerOnRegisterCaps(owner, caps);
    scene.EventManager.TriggerOnRegisterCaps(stranger, foreignCaps);
    string invPath = caps.CapsHandlers.GetCapsLocalPaths()["InventoryAPIv3"];
    string libPath = caps.CapsHandlers.GetCapsLocalPaths()["LibraryAPIv3"];
    string otherPath = foreignCaps.CapsHandlers.GetCapsLocalPaths()["InventoryAPIv3"];
    string bakePath = caps.CapsHandlers.GetCapsLocalPaths()["UpdateAvatarAppearance"];
    UUID bakedAsset = UUID.Random();
    var appearanceService = FixtureProxy.Create<OpenSim.Services.Interfaces.IAppearanceService>((method, args) =>
        method.Name == "GetBake" && (UUID)args[0] == owner && (string)args[1] == "head" && (UUID)args[2] == bakedAsset
            ? new AssetBase(bakedAsset, "fixture bake", (sbyte)AssetType.Texture, owner.ToString()) { Data = new byte[] { 255, 79, 255, 81 } } : null);
    server.AddSimpleStreamHandler(new OpenSim.Server.Handlers.Appearance.AppearanceServerHandler(appearanceService, null), true);
    OpenSim.Framework.MainConsole.Instance ??= new OpenSim.Framework.Console.LocalConsole("AIS HTTP tests");
    server.Start();
    try
    {
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(10) };
        using (var bakeRequest = client.PostAsync(bakePath, new StringContent("<llsd><map><key>cof_version</key><integer>1</integer></map></llsd>")).GetAwaiter().GetResult())
        {
            var reply = (OpenMetaverse.StructuredData.OSDMap)OpenMetaverse.StructuredData.OSDParser.DeserializeLLSDXml(bakeRequest.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Check(bakeRequest.IsSuccessStatusCode && !reply["success"].AsBoolean(), "Baking acknowledged an absent root presence");
        }
        using (var oversizedBake = client.PostAsync(bakePath, new StringContent(new string('x', 4097))).GetAwaiter().GetResult())
            Check((int)oversizedBake.StatusCode == 413, "Baking did not bound its request body");
        using (var dtdBake = client.PostAsync(bakePath, new StringContent("<!DOCTYPE llsd [<!ENTITY x '1'>]><llsd><map><key>cof_version</key><integer>&x;</integer></map></llsd>")).GetAwaiter().GetResult())
            Check((int)dtdBake.StatusCode == 400, "Baking accepted a DTD");
        using (var texture = client.GetAsync($"/texture/{owner}/head/{bakedAsset}").GetAwaiter().GetResult())
            Check(texture.IsSuccessStatusCode && texture.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult().Length == 4, "Appearance texture route did not serve the requested bake");
        using (var missingBake = client.GetAsync($"/texture/{owner}/head/{UUID.Random()}").GetAwaiter().GetResult())
            Check((int)missingBake.StatusCode == 404, "Appearance texture route ignored the expected asset");
        using (var extraPath = client.GetAsync($"/texture/{owner}/head/{bakedAsset}/extra").GetAwaiter().GetResult())
            Check((int)extraPath.StatusCode == 404, "Appearance texture route accepted an extra path segment");
        using var response = client.GetAsync(invPath + "/item/" + itemID).GetAwaiter().GetResult();
        Check(response.IsSuccessStatusCode, "Registered inventory variable path did not dispatch");
        using var denied = client.GetAsync(otherPath + "/item/" + itemID).GetAwaiter().GetResult();
        Check((int)denied.StatusCode == 404, "Registered inventory route leaked another resident's item");
        using var readLibrary = client.GetAsync(libPath + "/category/" + library.LibraryRootFolder.ID + "/children").GetAwaiter().GetResult();
        Check(readLibrary.IsSuccessStatusCode, "Registered library variable path did not dispatch");
        var createBody = new OpenMetaverse.StructuredData.OSDMap
        {
            ["categories"] = new OpenMetaverse.StructuredData.OSDArray
            {
                new OpenMetaverse.StructuredData.OSDMap { ["name"] = "HTTP created" }
            }
        };
        byte[] payload = OpenMetaverse.StructuredData.OSDParser.SerializeLLSDXmlBytes(createBody);
        string uri = invPath + "/category/" + rootID + "?tid=" + UUID.Random();
        string first;
        using (var content = new ByteArrayContent(payload))
        using (var write = client.PostAsync(uri, content).GetAwaiter().GetResult())
        {
            Check(write.IsSuccessStatusCode, "Registered AIS POST failed");
            first = write.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }
        using (var content = new ByteArrayContent(payload))
        using (var replay = client.PostAsync(uri, content).GetAwaiter().GetResult())
        {
            var repeatBody = (OpenMetaverse.StructuredData.OSDMap)OpenMetaverse.StructuredData.OSDParser.DeserializeLLSDXml(
                replay.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            var firstBody = (OpenMetaverse.StructuredData.OSDMap)OpenMetaverse.StructuredData.OSDParser.DeserializeLLSDXml(first);
            var firstID = ((OpenMetaverse.StructuredData.OSDArray)firstBody["_created_categories"])[0].AsUUID();
            Check(replay.IsSuccessStatusCode
                && ((OpenMetaverse.StructuredData.OSDArray)repeatBody["_created_categories"])[0].AsUUID() == firstID,
                "Registered HTTP replay created a different folder");
            var firstVersions = (OpenMetaverse.StructuredData.OSDMap)firstBody["_updated_category_versions"];
            var replayVersions = (OpenMetaverse.StructuredData.OSDMap)repeatBody["_updated_category_versions"];
            Check(firstVersions[rootID.ToString()].AsInteger() == replayVersions[rootID.ToString()].AsInteger()
                && replayVersions[rootID.ToString()].AsInteger() == inventory.GetFolder(owner, rootID).Version,
                "HTTP reply reported an incorrect or unpersisted folder version");
        }
        string concurrentUri = invPath + "/category/" + rootID + "?tid=" + UUID.Random();
        var parallelRequests = Enumerable.Range(0, 8).Select(async _ =>
        {
            using var content = new ByteArrayContent(payload);
            using var response = await client.PostAsync(concurrentUri, content);
            Check(response.IsSuccessStatusCode, "Concurrent registered HTTP create failed");
            var body = (OpenMetaverse.StructuredData.OSDMap)OpenMetaverse.StructuredData.OSDParser.DeserializeLLSDXml(
                await response.Content.ReadAsStringAsync());
            return ((OpenMetaverse.StructuredData.OSDArray)body["_created_categories"])[0].AsUUID();
        }).ToArray();
        Task.WaitAll(parallelRequests);
        Check(parallelRequests.Select(t => t.Result).Distinct().Count() == 1,
            "Concurrent registered HTTP retries created duplicate folders");
        using (var oversized = new ByteArrayContent(new byte[16385]))
        using (var rejected = client.PostAsync(uri, oversized).GetAwaiter().GetResult())
            Check((int)rejected.StatusCode == 413, "Registered POST ignored request bound");
        using (var subset = client.GetAsync(invPath + "/category/" + rootID + "/children?children=" + itemID).GetAwaiter().GetResult())
            Check(subset.IsSuccessStatusCode, "Registered subset route failed");
        using (var badDepth = client.GetAsync(invPath + "/category/" + rootID + "/children?depth=invalid").GetAwaiter().GetResult())
            Check((int)badDepth.StatusCode == 400, "HTTP query validation ignored the depth parameter");
        using (var dtd = new StringContent("<!DOCTYPE llsd [<!ENTITY x 'bad'>]><llsd><map/></llsd>"))
        using (var badXml = client.PostAsync(invPath + "/category/" + rootID, dtd).GetAwaiter().GetResult())
            Check((int)badXml.StatusCode == 400, "Registered mutation accepted a DTD");
        var metadata = new OpenMetaverse.StructuredData.OSDMap { ["name"] = "HTTP rename", ["desc"] = "HTTP description" };
        using (var patchRequest = new HttpRequestMessage(HttpMethod.Patch, invPath + "/item/" + itemID))
        {
            patchRequest.Content = new ByteArrayContent(OpenMetaverse.StructuredData.OSDParser.SerializeLLSDXmlBytes(metadata));
            using var patched = client.SendAsync(patchRequest).GetAwaiter().GetResult();
            Check(patched.IsSuccessStatusCode && inventory.GetItem(owner, itemID).Name == "HTTP rename",
                "Registered metadata PATCH did not persist");
            var reply = (OpenMetaverse.StructuredData.OSDMap)OpenMetaverse.StructuredData.OSDParser.DeserializeLLSDXml(
                patched.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Check(((OpenMetaverse.StructuredData.OSDMap)reply["_updated_category_versions"])[rootID.ToString()].AsInteger()
                == inventory.GetFolder(owner, rootID).Version, "HTTP item PATCH has an unpersisted parent version");
        }
        var outfit = inventory.GetFolderForType(owner, FolderType.CurrentOutfit);
        var links = new OpenMetaverse.StructuredData.OSDArray
        {
            new OpenMetaverse.StructuredData.OSDMap { ["name"] = "HTTP outfit link", ["linked_id"] = itemID }
        };
        UUID newLink;
        using (var putRequest = new HttpRequestMessage(HttpMethod.Put, invPath + "/category/current/links?tid=" + UUID.Random()))
        {
            putRequest.Content = new ByteArrayContent(OpenMetaverse.StructuredData.OSDParser.SerializeLLSDXmlBytes(links));
            using var put = client.SendAsync(putRequest).GetAwaiter().GetResult();
            Check(put.IsSuccessStatusCode, "Registered Current Outfit PUT failed");
            var body = (OpenMetaverse.StructuredData.OSDMap)OpenMetaverse.StructuredData.OSDParser.DeserializeLLSDXml(
                put.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            newLink = ((OpenMetaverse.StructuredData.OSDArray)body["_created_items"])[0].AsUUID();
            Check(((OpenMetaverse.StructuredData.OSDMap)body["_updated_category_versions"])[outfit.ID.ToString()].AsInteger()
                == inventory.GetFolder(owner, outfit.ID).Version, "HTTP PUT response has a stale folder version");
        }
        using (var delete = client.DeleteAsync(invPath + "/item/" + newLink).GetAwaiter().GetResult())
            Check(delete.IsSuccessStatusCode && inventory.GetItem(owner, newLink) is null
                && inventory.GetItem(owner, itemID) is not null, "Registered DELETE removed the wrong item");
        using (var copyRequest = new HttpRequestMessage(new HttpMethod("COPY"),
            libPath + "/category/" + library.LibraryRootFolder.ID + "?tid=" + UUID.Random() + ",depth=0"))
        {
            copyRequest.Headers.Add("Destination", rootID.ToString());
            using var copy = client.SendAsync(copyRequest).GetAwaiter().GetResult();
            Check(copy.IsSuccessStatusCode, "Registered library COPY failed");
            var body = (OpenMetaverse.StructuredData.OSDMap)OpenMetaverse.StructuredData.OSDParser.DeserializeLLSDXml(
                copy.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Check(((OpenMetaverse.StructuredData.OSDArray)body["_created_categories"]).Count == 1,
                "HTTP library COPY lost the shallow-copy query");
        }
        caps.DeregisterHandlers();
        using var removed = client.GetAsync(invPath + "/item/" + itemID).GetAwaiter().GetResult();
        Check((int)removed.StatusCode == 404, "Logout left the variable AIS route registered");
        module.RemoveRegion(scene);
        using var revoked = client.GetAsync(otherPath + "/item/" + itemID).GetAwaiter().GetResult();
        Check((int)revoked.StatusCode == 404, "Region removal left an AIS route registered");
        baking.RemoveRegion(scene);
        using var revokedBake = client.GetAsync(bakePath).GetAwaiter().GetResult();
        Check((int)revokedBake.StatusCode == 404, "Region removal left a baking capability registered");
        Console.WriteLine("PASS: registered AIS/baking HTTP routing, opt-in, ownership, writes, replay, bounds and teardown.");
    }
    finally
    {
        module.Close();
        baking.Close();
        server.Stop();
        if (previous is not null) OpenSim.Framework.Servers.MainServer.Instance = previous;
        OpenSim.Framework.Servers.MainServer.RemoveHttpServer(port);
    }
}

static void CloseInventoryFixtureConnections()
{
    foreach (Type handler in new[] { typeof(OpenSim.Data.SQLite.SqliteItemHandler), typeof(OpenSim.Data.SQLite.SqliteFolderHandler) })
    {
        var field = handler.GetField("m_Connection", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
        ((System.Data.SQLite.SQLiteConnection)field.GetValue(null))?.Dispose();
        field.SetValue(null, null);
        field.DeclaringType.GetField("m_initialized", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
    }
}

var linkedItem = new TaskInventoryItem { ItemID = UUID.Random(), ExperienceID = experienceID,
    PermsMask = 0x2004, PermsGranter = a };
var archiveInventory = new TaskInventoryDictionary { [linkedItem.ItemID] = linkedItem };
using var buffer = new StringWriter();
using (var writer = new System.Xml.XmlTextWriter(buffer))
    OpenSim.Region.Framework.Scenes.Serialization.SceneObjectSerializer.WriteTaskInventory(writer, archiveInventory, new(), null);
string xml = buffer.ToString();
TaskInventoryItem ReadArchivedItem(string input)
{
    using var reader = System.Xml.XmlReader.Create(new StringReader(input));
    reader.MoveToContent();
    return OpenSim.Region.Framework.Scenes.Serialization.SceneObjectSerializer
        .ReadTaskInventory(reader, "TaskInventory")[linkedItem.ItemID];
}
Check(ReadArchivedItem(xml).ExperienceID == experienceID, "Local object XML lost its Experience link");
var cleaned = OpenSim.Region.Framework.Scenes.Serialization.ForeignExperienceLinks.ClearObjectXml(System.Text.Encoding.UTF8.GetBytes(xml));
var foreignItem = ReadArchivedItem(System.Text.Encoding.UTF8.GetString(cleaned));
Check(foreignItem.ExperienceID == UUID.Zero && foreignItem.PermsMask == 0 && foreignItem.PermsGranter == UUID.Zero,
    "Foreign object retained its Experience or grant");
string state = $"<ScriptData><ScriptState><ExperienceKey>{experienceID}</ExperienceKey><Permissions mask='8196' granter='{a}'/></ScriptState></ScriptData>";
var cleanState = OpenSim.Region.Framework.Scenes.Serialization.ForeignExperienceLinks.ClearScriptState(state);
var parsedState = System.Xml.Linq.XDocument.Parse(cleanState);
Check(parsedState.Descendants("ExperienceKey").Single().Value == UUID.Zero.ToString()
    && parsedState.Descendants("Permissions").Single().Attribute("mask").Value == "0",
    "Foreign script state retained its Experience or grant");
Check(OpenSim.Region.Framework.Scenes.Serialization.ForeignExperienceLinks.ClearScriptState("<!DOCTYPE x><x/>") is null,
    "Invalid foreign state was not rejected");
Console.WriteLine("PASS: object XML preserves local Experience links and foreign sanitization clears links and grants.");

void WithStore(Action<SQLiteSimulationData> action)
{
    var store = new SQLiteSimulationData();
    store.Initialise($"Data Source={path};Version=3;Pooling=False");
    try { action(store); }
    finally { try { store.Shutdown(); } finally { store.Dispose(); } }
}
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class TestPhysicsActor : OpenSim.Region.PhysicsModules.SharedBase.NullPhysicsActor
{
    public override float Mass => 75f;
    public int Flags { get; set; }
    public Vector3 LastTarget { get; private set; }
    public override Vector3 PIDTarget { set => LastTarget = value; }
    public override bool PIDActive { get; set; }
    public override void VehicleFlags(int param, bool remove) =>
        Flags = remove ? Flags & ~param : Flags | param;
}

sealed class ForeignInventoryFixture(Nini.Config.IConfigSource config)
    : OpenSim.Services.InventoryService.XInventoryService(config)
{
    public override bool AddItem(InventoryItemBase item)
    {
        m_AcceptsExperienceLinks = false;
        return base.AddItem(item);
    }
}

sealed class FailingOutfitInventory(Nini.Config.IConfigSource config)
    : OpenSim.Services.InventoryService.XInventoryService(config)
{
    private int attempts;
    public override bool AddItem(InventoryItemBase item) => ++attempts == 1 && base.AddItem(item);
}

sealed class AisLibraryFixture : OpenSim.Services.Interfaces.ILibraryService
{
    public InventoryFolderImpl LibraryRootFolder { get; }
    public InventoryItemBase Sample { get; }
    public AisLibraryFixture()
    {
        UUID owner = UUID.Random();
        LibraryRootFolder = new InventoryFolderImpl(new InventoryFolderBase(UUID.Random(), "Library", owner, 0, UUID.Zero, 1));
        LibraryRootFolder.CreateChildFolder(UUID.Random(), "Child", 0);
        Sample = new InventoryItemBase(UUID.Random(), owner)
        {
            AssetID = UUID.Random(), Folder = LibraryRootFolder.ID, Name = "sample asset", Description = "",
            CreatorId = owner.ToString(), InvType = (int)InventoryType.LSL, AssetType = (int)AssetType.LSLText,
            CurrentPermissions = 0x8000, ExperienceID = UUID.Random()
        };
        LibraryRootFolder.Items.Add(Sample.ID, Sample);
    }
    public Dictionary<UUID, InventoryFolderImpl> GetAllFolders() => throw new NotSupportedException();
    public InventoryItemBase GetItem(UUID id) => id == Sample.ID ? Sample : null;
    public InventoryItemBase[] GetMultipleItems(UUID[] ids) => throw new NotSupportedException();
}
