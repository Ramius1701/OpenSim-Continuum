using System;
using System.Collections.Generic;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;

namespace OpenSim.Region.ClientStack.LindenCaps.AIS;

// LibraryAPIv3 read and category copy, adapted from Tranquillity v1.1.2.
public sealed class AisLibraryHandler(string path, UUID resident, ILibraryService library,
    IInventoryService inventory) : SimpleStreamHandler(path)
{
    protected override void ProcessRequest(IOSHttpRequest request, IOSHttpResponse response)
    {
        response.StatusCode = Dispatch(request.HttpMethod, (request.Url?.PathAndQuery ?? request.RawUrl), request.Headers["Destination"], out OSDMap body);
        if (body is not null)
        {
            response.ContentType = "application/llsd+xml";
            response.RawBuffer = OSDParser.SerializeLLSDXmlBytes(body);
        }
    }

    public int Dispatch(string method, string rawUrl, string destination, out OSDMap result)
    {
        result = null;
        if (resident == UUID.Zero || rawUrl is null || rawUrl.Length > 1024)
            return 400;
        string[] url = rawUrl.Split('?', 2);
        if (!url[0].StartsWith(Path + "/", StringComparison.Ordinal))
            return 404;
        string[] route = url[0][(Path.Length + 1)..].Split('/');
        if (route.Length is < 2 or > 3 || !UUID.TryParse(route[1], out UUID id) || id == UUID.Zero)
            return 404;
        UUID owner = library.LibraryRootFolder.Owner;
        if (method == "GET" && route[0] == "item" && route.Length == 2)
        {
            var item = library.GetItem(id);
            if (item is null || item.ID != id || item.Owner != owner)
                return 404;
            result = AisEnvelope.IsLink(item) ? AisEnvelope.Link(item, owner) : AisEnvelope.Item(item, owner);
            return 200;
        }
        if (route[0] != "category")
            return 404;
        var root = library.LibraryRootFolder.FindFolder(id);
        if (root is null || root.Owner != owner)
            return 404;

        if (method == "GET")
        {
            if (route.Length == 2)
                result = AisEnvelope.Category(root, owner);
            else if (route[2] == "children")
            {
                int depth = 0;
                if (url.Length == 2)
                    foreach (string parameter in url[1].Split('&'))
                        if (parameter.StartsWith("depth=", StringComparison.Ordinal))
                        {
                            if (!int.TryParse(parameter.AsSpan(6), out depth))
                                return 400;
                            depth = Math.Clamp(depth, 0, 50);
                        }
                int budget = 10000;
                if (!TryRead(root, owner, depth, new HashSet<UUID>(), ref budget, out result))
                    return 503;
            }
            else
                return 404;
            return 200;
        }
        if (method != "COPY" || route.Length != 2)
            return 405;
        if (!UUID.TryParse(destination, out UUID targetID) || targetID == UUID.Zero)
            return 400;
        var target = inventory.GetFolder(resident, targetID);
        if (target is null || target.ID != targetID || target.Owner != resident)
            return 404;

        bool recursive = url.Length != 2 || !Array.Exists(url[1].Split('&', ','), p => p == "depth=0");
        return AisWriteCoordinator.Run(resident, method, rawUrl, destination, null,
            (out OSDMap reply) => CopyTree(root, owner, targetID, recursive, out reply), out result);
    }

    private static bool TryRead(InventoryFolderImpl folder, UUID owner, int depth,
        HashSet<UUID> visited, ref int budget, out OSDMap result)
    {
        result = null;
        if (!visited.Add(folder.ID) || --budget < 0)
            return false;
        OSDMap children = new(), items = new(), links = new();
        foreach (var child in folder.RequestListOfFolderImpls())
        {
            if (child.Owner != owner || child.ParentID != folder.ID || child.ID == UUID.Zero)
                continue;
            OSDMap entry;
            if (depth == 0)
            {
                if (--budget < 0)
                    return false;
                entry = AisEnvelope.Category(child, owner);
            }
            else if (!TryRead(child, owner, depth - 1, visited, ref budget, out entry))
                return false;
            children[child.ID.ToString()] = entry;
        }
        foreach (var item in folder.Items.Values)
        {
            if (--budget < 0)
                return false;
            if (item is null || item.Owner != owner || item.Folder != folder.ID || item.ID == UUID.Zero)
                continue;
            if (AisEnvelope.IsLink(item))
                links[item.ID.ToString()] = AisEnvelope.Link(item, owner);
            else
                items[item.ID.ToString()] = AisEnvelope.Item(item, owner);
        }
        result = AisEnvelope.Category(folder, owner, AisEnvelope.EmbeddedMap(children, items, links));
        return true;
    }

    private int CopyTree(InventoryFolderImpl root, UUID owner, UUID targetID, bool recursive, out OSDMap result)
    {
        result = null;
        List<(InventoryFolderImpl Source, InventoryFolderBase Copy)> folders =
            [(root, new InventoryFolderBase(UUID.Random(), root.Name, resident, (short)FolderType.None, targetID, 1))];
        HashSet<UUID> seen = new() { root.ID };
        Dictionary<UUID, List<InventoryItemBase>> byFolder = new();
        Dictionary<UUID, InventoryItemBase> itemCopies = new();
        int total = 0;
        for (int i = 0; i < folders.Count; i++)
        {
            var (source, copy) = folders[i];
            List<InventoryItemBase> items = new();
            byFolder[copy.ID] = items;
            foreach (var original in source.Items.Values)
            {
                if (++total > 4096)
                    return 503;
                if (original is null || original.Owner != owner || original.Folder != source.ID || original.ID == UUID.Zero)
                    continue;
                var item = (InventoryItemBase)original.Clone();
                item.ID = UUID.Random();
                item.Owner = resident;
                item.Folder = copy.ID;
                item.GroupID = UUID.Zero;
                item.GroupOwned = false;
                item.ExperienceID = UUID.Zero;
                if (!itemCopies.TryAdd(original.ID, item)) return 409;
                items.Add(item);
            }
            if (!recursive)
                continue;
            foreach (var child in source.RequestListOfFolderImpls())
            {
                if (child.Owner != owner || child.ParentID != source.ID || child.ID == UUID.Zero || !seen.Add(child.ID))
                    return 409;
                if (folders.Count >= 128)
                    return 503;
                folders.Add((child, new InventoryFolderBase(UUID.Random(), child.Name, resident, (short)FolderType.None, copy.ID, 1)));
            }
        }
        // Resolve links before writing. Internal targets get the copied ID; targets
        // outside this copy retain their verified library ID (including shallow copies).
        Dictionary<UUID, InventoryFolderBase> folderCopies = new();
        foreach (var (source, copy) in folders) folderCopies.Add(source.ID, copy);
        foreach (var item in itemCopies.Values)
        {
            if (!AisEnvelope.IsLink(item)) continue;
            if (item.AssetType == (int)AssetType.LinkFolder)
            {
                var target = library.LibraryRootFolder.FindFolder(item.AssetID);
                if (target is null || target.ID != item.AssetID || target.Owner != owner) return 409;
                if (folderCopies.TryGetValue(item.AssetID, out var copied)) item.AssetID = copied.ID;
            }
            else
            {
                var target = library.GetItem(item.AssetID);
                if (target is null || target.ID != item.AssetID || target.Owner != owner || AisEnvelope.IsLink(target))
                    return 409;
                if (itemCopies.TryGetValue(item.AssetID, out var copied)) item.AssetID = copied.ID;
            }
        }
        OSDMap categories = new(), addedItems = new(), addedLinks = new();
        OSDArray categoryIDs = new(), itemIDs = new();
        int status = 200;
        foreach (var (_, copy) in folders)
        {
            var stored = inventory.AddFolder(copy) ? inventory.GetFolder(resident, copy.ID) : null;
            if (stored is null || stored.ID != copy.ID || stored.Owner != resident || stored.ParentID != copy.ParentID)
            {
                status = 409;
                break;
            }
            categoryIDs.Add(copy.ID);
            categories[copy.ID.ToString()] = AisEnvelope.Category(stored, resident);
        }
        // All folders and real item targets must exist before links can be created.
        for (int pass = 0; pass < 2 && status == 200; pass++)
        {
            foreach (var (_, copy) in folders)
            {
                foreach (var item in byFolder[copy.ID])
                {
                    bool link = AisEnvelope.IsLink(item);
                    if (link != (pass == 1)) continue;
                    var stored = inventory.AddItem(item) ? inventory.GetItem(resident, item.ID) : null;
                    if (stored is null || stored.ID != item.ID || stored.Owner != resident
                        || stored.Folder != item.Folder || stored.AssetID != item.AssetID || stored.AssetType != item.AssetType)
                    {
                        status = 409;
                        break;
                    }
                    itemIDs.Add(stored.ID);
                    if (link) addedLinks[stored.ID.ToString()] = AisEnvelope.Link(stored, resident);
                    else addedItems[stored.ID.ToString()] = AisEnvelope.Item(stored, resident);
                }
                if (status != 200) break;
            }
        }
        result = new OSDMap
        {
            ["_created_categories"] = categoryIDs, ["_created_items"] = itemIDs,
            ["_embedded"] = AisEnvelope.EmbeddedMap(categories, addedItems, addedLinks)
        };
        OSDMap versions = new();
        var destinationFolder = inventory.GetFolder(resident, targetID);
        if (destinationFolder?.Owner == resident)
            versions[targetID.ToString()] = (int)destinationFolder.Version;
        foreach (OSD folderID in categoryIDs)
        {
            UUID id = folderID.AsUUID();
            var stored = inventory.GetFolder(resident, id);
            if (stored is not null && stored.ID == id && stored.Owner == resident)
            {
                versions[id.ToString()] = (int)stored.Version;
                categories[id.ToString()] = AisEnvelope.Category(stored, resident);
            }
        }
        result["_updated_category_versions"] = versions;
        return status;
    }
}
