using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Region.ClientStack.LindenCaps.AIS;
using OpenSim.Services.Interfaces;

static class LibraryLinkChecks
{
    public static void Run(IInventoryService inventory, UUID resident, UUID destination)
    {
        var library = new LinkLibrary();
        var root = library.LibraryRootFolder;
        var child = root.CreateChildFolder(UUID.Random(), "linked child", 0);
        var external = root.CreateChildFolder(UUID.Random(), "outside", 0);
        var target = Add(child, "real target", AssetType.Notecard, UUID.Random());
        var externalTarget = Add(external, "external target", AssetType.Notecard, UUID.Random());
        // Copy the child: sibling targets remain verified library references.
        var internalLink = Add(child, "internal item link", AssetType.Link, target.ID);
        var folderLink = Add(child, "internal folder link", AssetType.LinkFolder, child.ID);
        var externalLink = Add(child, "external item link", AssetType.Link, externalTarget.ID);
        var handler = new AisLibraryHandler("/link-library", resident, library, inventory);
        string route = "/link-library/category/" + child.ID;
        string retry = route + "?tid=" + UUID.Random();
        Check(handler.Dispatch("COPY", retry, destination.ToString(), out var result) == 200, "Library link copy failed");
        var ids = (OSDArray)result["_created_items"];
        var copies = ids.Select(id => inventory.GetItem(resident, id.AsUUID())).ToDictionary(item => item.Name);
        UUID copiedFolder = ((OSDArray)result["_created_categories"])[0].AsUUID();
        Check(copies.Count == 4 && copies[target.Name].AssetID == target.AssetID
            && copies[internalLink.Name].AssetID == copies[target.Name].ID
            && copies[folderLink.Name].AssetID == copiedFolder
            && copies[externalLink.Name].AssetID == externalTarget.ID,
            "Library links were lost or targets were not remapped correctly");
        Check(copies.Values.All(item => item.Owner == resident && item.Folder == copiedFolder
            && item.ExperienceID == UUID.Zero && item.GroupID == UUID.Zero), "Copied link ownership or Experience policy failed");
        var embedded = (OSDMap)result["_embedded"];
        Check(((OSDMap)embedded["links"]).Count == 3 && ((OSDMap)embedded["items"]).Count == 1,
            "Copied links were reported as real items");
        Check(handler.Dispatch("COPY", retry, destination.ToString(), out var replay) == 200
            && ((OSDArray)replay["_created_items"])[0].AsUUID() == ids[0].AsUUID(), "Link copy replay duplicated rows");

        var descendant = child.CreateChildFolder(UUID.Random(), "descendant", 0);
        var nested = Add(descendant, "nested target", AssetType.Notecard, UUID.Random());
        var nestedLink = Add(child, "nested link", AssetType.Link, nested.ID);
        Check(handler.Dispatch("COPY", route + "?tid=" + UUID.Random() + ",depth=0", destination.ToString(), out var shallow) == 200,
            "Shallow copy with library target outside copy failed");
        var shallowItems = ((OSDArray)shallow["_created_items"]).Select(id => inventory.GetItem(resident, id.AsUUID())).ToList();
        Check(((OSDArray)shallow["_created_categories"]).Count == 1
            && shallowItems.Single(item => item.Name == nestedLink.Name).AssetID == nested.ID,
            "Shallow copy changed a target excluded from the copy");
        Check(handler.Dispatch("COPY", route, destination.ToString(), out var deep) == 200, "Deep cross-folder link copy failed");
        var deepItems = ((OSDArray)deep["_created_items"]).Select(id => inventory.GetItem(resident, id.AsUUID())).ToList();
        Check(deepItems.Single(item => item.Name == nestedLink.Name).AssetID == deepItems.Single(item => item.Name == nested.Name).ID,
            "Cross-folder copied target was not remapped");

        int foldersBefore = inventory.GetFolderContent(resident, destination).Folders.Count;
        UUID previous = externalLink.AssetID;
        externalLink.AssetID = UUID.Random();
        Check(handler.Dispatch("COPY", route, destination.ToString(), out _) == 409
            && inventory.GetFolderContent(resident, destination).Folders.Count == foldersBefore,
            "Dangling library link was copied or preflight mutated inventory");
        externalLink.AssetID = internalLink.ID;
        Check(handler.Dispatch("COPY", route, destination.ToString(), out _) == 409, "A link-to-link target was accepted");
        externalLink.AssetID = previous;
        var failingInventory = FixtureProxy.Create<IInventoryService>((method, args) =>
            method.Name == "AddItem" && ((InventoryItemBase)args[0]).Name == target.Name
                ? false : method.Invoke(inventory, args));
        var failingHandler = new AisLibraryHandler("/failed-library", resident, library, failingInventory);
        string failedRoute = "/failed-library/category/" + child.ID + "?tid=" + UUID.Random();
        Check(failingHandler.Dispatch("COPY", failedRoute, destination.ToString(), out var failed) == 409
            && ((OSDArray)failed["_created_items"]).Count == 0,
            "Failed target creation was reported as completed");
        foreach (OSD id in (OSDArray)failed["_created_categories"])
            Check(inventory.GetFolderItems(resident, id.AsUUID()).All(item => !AisEnvelope.IsLink(item)),
                "Partial copy created a link before its target was saved");
        int afterFailure = inventory.GetFolderContent(resident, destination).Folders.Count;
        Check(failingHandler.Dispatch("COPY", failedRoute, destination.ToString(), out _) == 409
            && inventory.GetFolderContent(resident, destination).Folders.Count == afterFailure,
            "Failed copy replay created another partial tree");
        Console.WriteLine("PASS: library links, target remapping, shallow/external targets, preflight and partial-failure replay.");
    }

    static InventoryItemBase Add(InventoryFolderImpl folder, string name, AssetType type, UUID asset)
    {
        var item = new InventoryItemBase(UUID.Random(), folder.Owner)
        {
            Name = name, Description = "", Folder = folder.ID, AssetID = asset, AssetType = (int)type,
            InvType = (int)InventoryType.Notecard, CreatorId = folder.Owner.ToString(), ExperienceID = UUID.Random()
        };
        folder.Items.Add(item.ID, item);
        return item;
    }
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    sealed class LinkLibrary : ILibraryService
    {
        public InventoryFolderImpl LibraryRootFolder { get; } = new(new InventoryFolderBase(UUID.Random(), "Library", UUID.Random(), 0, UUID.Zero, 1));
        public InventoryItemBase GetItem(UUID id) => LibraryRootFolder.FindItem(id);
        public Dictionary<UUID, InventoryFolderImpl> GetAllFolders() => throw new NotSupportedException();
        public InventoryItemBase[] GetMultipleItems(UUID[] ids) => ids.Select(GetItem).ToArray();
    }
}
