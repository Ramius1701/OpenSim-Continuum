using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;
using OpenSim.Region.Framework.Interfaces;

namespace OpenSim.Region.ClientStack.LindenCaps.AIS;

// Adapted from Tranquillity's v1.1.2 AIS read routes. Do not advertise a
// production capability until Firestorm's expected write routes are present.
public delegate InventoryUploadResult AisUploadTransaction(UUID transaction, InventoryItemBase item, out UUID storedAsset);

public sealed class AisReadHandler(string path, UUID agent, IInventoryService inventory, AisUploadTransaction uploads = null) : SimpleStreamHandler(path)
{
    private const int MaxDepth = 50;
    private const int MaxExpandedFolders = 256;
    private const int MaxEntries = 10000;

    protected override void ProcessRequest(IOSHttpRequest request, IOSHttpResponse response)
    {
        OSDMap result;
        int code = request.HttpMethod is "PATCH" or "POST" or "PUT"
            ? ReadWriteRequest(request, out result)
            : request.HttpMethod == "DELETE" ? DispatchWrite("DELETE", (request.Url?.PathAndQuery ?? request.RawUrl), null, null, out result)
            : Fetch(request.HttpMethod, (request.Url?.PathAndQuery ?? request.RawUrl), out result);
        response.StatusCode = code;
        if (result is not null)
        {
            response.ContentType = "application/llsd+xml";
            response.RawBuffer = OSDParser.SerializeLLSDXmlBytes(result);
        }
    }

    private int ReadWriteRequest(IOSHttpRequest request, out OSDMap result)
    {
        result = null;
        const int limit = 16 * 1024;
        if (request.ContentLength64 > limit)
            return 413;
        if (request.InputStream is null)
            return 400;

        OSD body;
        byte[] payload;
        try
        {
            using var buffer = new MemoryStream();
            byte[] block = new byte[4096];
            int read;
            while ((read = request.InputStream.Read(block, 0, block.Length)) > 0)
            {
                if (buffer.Length + read > limit)
                    return 413;
                buffer.Write(block, 0, read);
            }
            buffer.Position = 0;
            using (var reader = XmlReader.Create(buffer, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = limit
            }))
            {
                if (reader.MoveToContent() != XmlNodeType.Element || reader.Name != "llsd")
                    return 400;
                while (reader.Read()) { }
            }
            payload = buffer.ToArray();
            body = OSDParser.DeserializeLLSDXml(payload);
        }
        catch (Exception e) when (e is XmlException or IOException or FormatException or ArgumentException)
        {
            return 400;
        }
        return DispatchWrite(request.HttpMethod, (request.Url?.PathAndQuery ?? request.RawUrl), body, payload, out result);
    }

    internal int DispatchWrite(string method, string rawUrl, OSD body, byte[] payload, out OSDMap result)
    {
        UUID transaction = UUID.Zero;
        if (method == "PATCH" && body is OSDMap map && map.TryGetValue("hash_id", out var hash))
            UUID.TryParse(hash.AsString(), out transaction);
        return AisWriteCoordinator.Run(agent, method, rawUrl, null, payload,
            (out OSDMap reply) => method switch
            {
                "PUT" => ReplaceOutfitLinks(rawUrl, body, out reply),
                "POST" => Create(rawUrl, body as OSDMap, out reply),
                "PATCH" => Patch(rawUrl, body as OSDMap, out reply),
                "DELETE" => Remove(rawUrl, out reply),
                _ => Fetch(method, rawUrl, out reply)
            }, out result, transaction);
    }

    // Tranquillity AisSlam: validate all targets before writing; add new
    // links before removing old ones so an add failure does not strip an outfit.
    public int ReplaceOutfitLinks(string rawUrl, OSD body, out OSDMap result)
    {
        result = null;
        if (agent == UUID.Zero || rawUrl is null || rawUrl.Length > 1024)
            return 400;
        string route = rawUrl.Split('?', 2)[0], prefix = Path + "/category/";
        if (!route.StartsWith(prefix, StringComparison.Ordinal))
            return 404;
        string[] segments = route[prefix.Length..].Split('/');
        if (segments.Length != 2 || segments[1] != "links")
            return 404;
        bool current = segments[0] == "current";
        UUID folderID = UUID.Zero;
        if (!current && (!UUID.TryParse(segments[0], out folderID) || folderID == UUID.Zero))
            return 404;
        var folder = current ? inventory.GetFolderForType(agent, FolderType.CurrentOutfit) : inventory.GetFolder(agent, folderID);
        if (current && folder?.Type == (short)FolderType.CurrentOutfit)
            folderID = folder.ID;
        if (folder is null || folder.ID != folderID || folder.Owner != agent)
            return 404;
        if (folder.Type != (short)FolderType.CurrentOutfit && folder.Type != (short)FolderType.Outfit)
            return 403;
        OSDArray entries = body as OSDArray;
        if (entries is null && body is OSDMap map && map.Count == 1)
            entries = map["links"] as OSDArray ?? map["contents"] as OSDArray;
        if (entries is null || entries.Count > 64)
            return 400;

        List<InventoryItemBase> wanted = new(entries.Count);
        foreach (OSD entry in entries)
        {
            if (entry is not OSDMap fields || fields["name"].Type != OSDType.String
                || !UUID.TryParse(fields["linked_id"].AsString(), out UUID targetID) || targetID == UUID.Zero)
                return 400;
            string name = fields["name"].AsString(), desc = fields["desc"].AsString();
            if (name.Length > 64 || desc.Length > 128
                || name.IndexOfAny(['\0', '\r', '\n']) >= 0 || desc.IndexOfAny(['\0', '\r', '\n']) >= 0)
                return 400;
            if (fields.TryGetValue("parent_id", out var parent) && parent.AsUUID() != folderID)
                return 400;
            if (fields.TryGetValue("type", out var kind) && kind.Type != OSDType.Integer)
                return 400;
            int type = kind is null ? (int)AssetType.Link : kind.AsInteger();
            int invType;
            if (type == (int)AssetType.Link)
            {
                var target = inventory.GetItem(agent, targetID);
                if (target is null || target.ID != targetID || target.Owner != agent || AisEnvelope.IsLink(target))
                    return 404;
                invType = target.InvType;
            }
            else if (type == (int)AssetType.LinkFolder)
            {
                var target = inventory.GetFolder(agent, targetID);
                if (target is null || target.ID != targetID || target.Owner != agent || targetID == folderID
                    || target.Type != (short)FolderType.Outfit)
                    return 404;
                invType = (int)InventoryType.Category;
            }
            else
                return 400;
            wanted.Add(new InventoryItemBase(UUID.Random(), agent)
            {
                Folder = folderID, AssetID = targetID, AssetType = type, InvType = invType,
                Name = name, Description = desc, CreatorId = agent.ToString(), CreationDate = Util.UnixTimeSinceEpoch()
            });
        }

        InventoryCollection content = inventory.GetFolderContent(agent, folderID);
        if (content?.Items is null || content.Items.Count > 256)
            return 503;
        List<UUID> old = new();
        foreach (var row in content.Items)
        {
            if (row is null || row.Owner != agent || row.Folder != folderID || row.ID == UUID.Zero)
                return 409;
            if (AisEnvelope.IsLink(row))
                old.Add(row.ID);
        }

        List<InventoryItemBase> attempted = new();
        bool ok = true;
        foreach (var row in wanted)
        {
            attempted.Add(row);
            if (!inventory.AddItem(row) || !MatchesStoredLink(row, inventory.GetItem(agent, row.ID)))
            {
                ok = false;
                break;
            }
        }
        if (!ok)
            inventory.DeleteItems(agent, attempted.ConvertAll(row => row.ID));
        else if (old.Count != 0)
            ok = inventory.DeleteItems(agent, old);

        OSDArray createdIDs = new(), removedIDs = new();
        OSDMap links = new();
        foreach (var row in attempted)
        {
            var stored = inventory.GetItem(agent, row.ID);
            if (MatchesStoredLink(row, stored))
            {
                createdIDs.Add(row.ID);
                links[row.ID.ToString()] = AisEnvelope.Link(stored, agent);
            }
            else
                ok = false;
        }
        foreach (UUID id in old)
        {
            if (inventory.GetItem(agent, id) is null)
                removedIDs.Add(id);
            else
                ok = false;
        }
        result = new OSDMap
        {
            ["_created_items"] = createdIDs, ["_removed_items"] = removedIDs,
            ["_embedded"] = new OSDMap { ["links"] = links }
        };
        ReportVersion(result, folderID);
        return ok ? 200 : 409;
    }

    private static bool MatchesStoredLink(InventoryItemBase expected, InventoryItemBase stored) =>
        stored is not null && stored.ID == expected.ID && stored.Owner == expected.Owner
        && stored.Folder == expected.Folder && stored.AssetID == expected.AssetID && stored.AssetType == expected.AssetType;

    // Tranquillity AIS creation contract: the route owns the parent, the
    // server assigns identifiers, and preflight validates the entire batch.
    public int Create(string rawUrl, OSDMap body, out OSDMap result)
    {
        result = null;
        if (agent == UUID.Zero || rawUrl is null || rawUrl.Length > 1024 || body is null)
            return 400;
        string route = rawUrl.Split('?', 2)[0];
        if (!route.StartsWith(Path + "/category/", StringComparison.Ordinal)
            || !UUID.TryParse(route[(Path.Length + 10)..], out UUID parentID) || parentID == UUID.Zero)
            return 404;
        var parent = inventory.GetFolder(agent, parentID);
        if (parent is null || parent.ID != parentID || parent.Owner != agent)
            return 404;

        List<InventoryFolderBase> folders = new();
        List<InventoryItemBase> links = new();
        foreach (string key in body.Keys)
        {
            if (body[key] is not OSDArray entries)
                return 400;
            if (key == "items")
            {
                if (entries.Count != 0)
                    return 501;
                continue;
            }
            if (key is not ("categories" or "links") || entries.Count > 64 - folders.Count - links.Count)
                return 400;

            foreach (OSD entry in entries)
            {
                if (entry is not OSDMap fields || fields["name"].Type != OSDType.String)
                    return 400;
                string name = fields["name"].AsString();
                string description = fields["desc"].AsString();
                if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || description.Length > 128
                    || name.IndexOfAny(['\0', '\r', '\n']) >= 0
                    || description.IndexOfAny(['\0', '\r', '\n']) >= 0)
                    return 400;
                if (fields.TryGetValue("parent_id", out var named)
                    && (!UUID.TryParse(named.AsString(), out UUID requested) || requested != parentID))
                    return 400;
                if (key == "categories")
                {
                    short type = (short)FolderType.None;
                    if (fields.TryGetValue("type_default", out var kind) || fields.TryGetValue("type", out kind))
                    {
                        if (kind.Type != OSDType.Integer || (kind.AsInteger() != (int)FolderType.None
                            && kind.AsInteger() != (int)FolderType.Outfit))
                            return 400;
                        type = (short)kind.AsInteger();
                    }
                    folders.Add(new InventoryFolderBase(UUID.Random(), name, agent, type, parentID, 1));
                }
                else
                {
                    if (!UUID.TryParse(fields["linked_id"].AsString(), out UUID targetID) || targetID == UUID.Zero)
                        return 400;
                    // Resolve an owned, non-link item. Folder links require the separate outfit/library work.
                    var target = inventory.GetItem(agent, targetID);
                    if (target is null || target.ID != targetID || target.Owner != agent || AisEnvelope.IsLink(target))
                        return 404;
                    links.Add(new InventoryItemBase(UUID.Random(), agent)
                    {
                        Name = name, Description = description, Folder = parentID, AssetID = targetID,
                        AssetType = (int)AssetType.Link, InvType = target.InvType, CreatorId = agent.ToString(),
                        CreationDate = Util.UnixTimeSinceEpoch()
                    });
                }
            }
        }
        if (folders.Count + links.Count == 0)
            return 400;

        OSDMap categories = new(), createdLinks = new();
        OSDArray categoryIDs = new(), itemIDs = new();
        result = new OSDMap
        {
            ["_created_categories"] = categoryIDs,
            ["_created_items"] = itemIDs,
            ["_embedded"] = AisEnvelope.EmbeddedMap(categories, null, createdLinks)
        };
        int status = 200;
        foreach (var folder in folders)
        {
            if (!inventory.AddFolder(folder)) { status = 409; break; }
            categoryIDs.Add(folder.ID);
            categories[folder.ID.ToString()] = AisEnvelope.Category(folder, agent);
        }
        if (status == 200)
        {
            foreach (var link in links)
            {
                if (!inventory.AddItem(link)) { status = 409; break; }
                itemIDs.Add(link.ID);
                createdLinks[link.ID.ToString()] = AisEnvelope.Link(link, agent);
            }
        }
        ReportVersion(result, parentID);
        return status;
    }

    private int Remove(string rawUrl, out OSDMap result) =>
        rawUrl?.StartsWith(Path + "/category/", StringComparison.Ordinal) == true
            ? RemoveCategory(rawUrl, out result) : RemoveItem(rawUrl, out result);

    // Keep Continuum's Trash/Lost And Found deletion policy. Inspect the
    // entire bounded subtree before asking the existing provider to remove it.
    public int RemoveCategory(string rawUrl, out OSDMap result)
    {
        result = null;
        if (agent == UUID.Zero || rawUrl is null || rawUrl.Length > 1024)
            return 400;
        string route = rawUrl.Split('?', 2)[0], prefix = Path + "/category/";
        if (!route.StartsWith(prefix, StringComparison.Ordinal))
            return 404;
        string[] parts = route[prefix.Length..].Split('/');
        bool purge = parts.Length == 2 && parts[1] == "children";
        if ((!purge && parts.Length != 1) || !UUID.TryParse(parts[0], out UUID id) || id == UUID.Zero)
            return 404;

        var folder = inventory.GetFolder(agent, id);
        if (folder is null || folder.Owner != agent || folder.ID != id)
            return 404;
        if (folder.ParentID == UUID.Zero || (!IsOrdinary(folder) && !(purge && IsTrashOrLost(folder))))
            return 403;

        var ancestors = new HashSet<UUID>();
        var cursor = folder;
        while (!IsTrashOrLost(cursor))
        {
            if (!ancestors.Add(cursor.ID) || ancestors.Count > MaxDepth || cursor.ParentID == UUID.Zero)
                return 403;
            var parent = inventory.GetFolder(agent, cursor.ParentID);
            if (parent is null || parent.Owner != agent || parent.ID != cursor.ParentID)
                return 403;
            cursor = parent;
        }

        List<UUID> folders = new() { id }, items = new();
        HashSet<UUID> seenFolders = new() { id }, seenItems = new();
        for (int i = 0; i < folders.Count; i++)
        {
            InventoryCollection content = inventory.GetFolderContent(agent, folders[i]);
            if (content?.Folders is null || content.Items is null)
                return 503;
            if (content.Folders.Count > MaxExpandedFolders - folders.Count
                || content.Items.Count > MaxEntries - items.Count)
                return 503;
            foreach (var child in content.Folders)
            {
                if (child is null || child.Owner != agent || child.ParentID != folders[i]
                    || child.ID == UUID.Zero || !IsOrdinary(child) || !seenFolders.Add(child.ID))
                    return 409;
                folders.Add(child.ID);
            }
            foreach (var item in content.Items)
            {
                if (item is null || item.Owner != agent || item.Folder != folders[i]
                    || item.ID == UUID.Zero || !seenItems.Add(item.ID))
                    return 409;
                items.Add(item.ID);
            }
        }

        bool ok = purge ? inventory.PurgeFolder(folder) : inventory.DeleteFolders(agent, new List<UUID> { id });
        OSDArray removedFolders = new(), removedItems = new();
        foreach (UUID child in folders)
        {
            bool exists = inventory.GetFolder(agent, child) is not null;
            if (purge && child == id)
                ok &= exists;
            else if (exists)
                ok = false;
            else
                removedFolders.Add(OSD.FromUUID(child));
        }
        foreach (UUID item in items)
        {
            if (inventory.GetItem(agent, item) is null)
                removedItems.Add(OSD.FromUUID(item));
            else
                ok = false;
        }
        result = new OSDMap { ["_categories_removed"] = removedFolders, ["_removed_items"] = removedItems };
        ReportVersion(result, purge ? id : folder.ParentID);
        return ok ? 200 : 409;
    }

    private static bool IsOrdinary(InventoryFolderBase folder) =>
        folder.Type == (short)FolderType.None || folder.Type == (short)FolderType.Outfit;

    private static bool IsTrashOrLost(InventoryFolderBase folder) =>
        folder.Type == (short)FolderType.Trash || folder.Type == (short)FolderType.LostAndFound;

    public int RemoveItem(string rawUrl, out OSDMap result)
    {
        result = null;
        if (agent == UUID.Zero || rawUrl is null || rawUrl.Length > 1024)
            return 400;
        string route = rawUrl.Split('?', 2)[0];
        string prefix = Path + "/item/";
        if (!route.StartsWith(prefix, StringComparison.Ordinal)
            || !UUID.TryParse(route[prefix.Length..], out UUID itemID) || itemID == UUID.Zero)
            return 404;
        var item = inventory.GetItem(agent, itemID);
        if (item is null || item.Owner != agent || item.ID != itemID)
            return 404;
        // Some providers return true even when policy suppresses deletion.
        if (!inventory.DeleteItems(agent, new List<UUID> { itemID }) || inventory.GetItem(agent, itemID) is not null)
            return 409;
        result = new OSDMap { ["_removed_items"] = new OSDArray { OSD.FromUUID(itemID) } };
        ReportVersion(result, item.Folder);
        return 200;
    }

    // Metadata-only subset of the Tranquillity AisMutation contract. Use the
    // stored object as the authority for identifiers, masks and associations.
    public int Patch(string rawUrl, OSDMap body, out OSDMap result)
    {
        result = null;
        if (agent == UUID.Zero || rawUrl is null || rawUrl.Length > 1024 || body is null || body.Count == 0)
            return 400;
        string uri = rawUrl.Split('?', 2)[0];
        if (!uri.StartsWith(Path + "/", StringComparison.Ordinal))
            return 404;
        string[] segments = uri[(Path.Length + 1)..].Split('/');
        if (segments.Length != 2 || !UUID.TryParse(segments[1], out UUID id) || id == UUID.Zero)
            return 404;

        bool itemRoute = segments[0] == "item";
        if (!itemRoute && segments[0] != "category")
            return 404;
        foreach (string field in body.Keys)
        {
            OSD value = body[field];
            if (field is "name" or "desc")
            {
                if (value.Type != OSDType.String || (!itemRoute && field == "desc"))
                    return 400;
                string text = value.AsString();
                if (text.Length > (field == "name" ? 64 : 128)
                    || (field == "name" && string.IsNullOrWhiteSpace(text))
                    || text.IndexOfAny(['\0', '\r', '\n']) >= 0)
                    return 400;
            }
            else if (itemRoute && field == "hash_id")
            {
                if (!UUID.TryParse(value.AsString(), out _)) return 400;
            }
            else if (itemRoute && field == "asset_id")
            {
                var existing = inventory.GetItem(agent, id);
                if (existing is null || existing.Owner != agent || value.AsUUID() != existing.AssetID)
                    return 400;
            }
            else if (itemRoute && field is "flags")
            {
                if (value.Type != OSDType.Integer) return 400;
            }
            else if (itemRoute && field is "permissions" or "sale_info")
            {
                if (value is not OSDMap fields) return 400;
                foreach (string key in fields.Keys)
                {
                    if (field == "permissions" && key is "next_owner_mask" or "everyone_mask" or "group_mask"
                        && fields[key].Type != OSDType.Integer)
                        return 400;
                    if (field == "sale_info")
                    {
                        if (key is not ("sale_price" or "sale_type") || fields[key].Type != OSDType.Integer)
                            return 400;
                        if ((key == "sale_price" && fields[key].AsInteger() < 0)
                            || (key == "sale_type" && (fields[key].AsInteger() < 0 || fields[key].AsInteger() > 3)))
                            return 400;
                    }
                }
            }
            else if (field is "item_id" or "category_id" or "parent_id" or "agent_id" or "type" or "inv_type"
                or "created_at" or "type_default" or "version" or "thumbnail" or "favorite")
            {
                // The viewer sends complete item/category maps for metadata edits.
                // Identifiers and parents remain server-owned; reject an attempted move.
                if (field == "parent_id")
                {
                    UUID parentID = itemRoute ? inventory.GetItem(agent, id)?.Folder ?? UUID.Zero
                        : inventory.GetFolder(agent, id)?.ParentID ?? UUID.Zero;
                    if (value.AsUUID() != parentID) return 400;
                }
            }
            else
                return 400;
        }

        if (itemRoute)
        {
            var existing = inventory.GetItem(agent, id);
            if (existing is null || existing.ID != id || existing.Owner != agent)
                return 404;
            var item = (InventoryItemBase)existing.Clone();
            if (body.TryGetValue("name", out var name))
                item.Name = name.AsString();
            if (body.TryGetValue("desc", out var desc))
                item.Description = desc.AsString();
            if (body.TryGetValue("flags", out var flags))
                item.Flags = (item.Flags & ~255u) | ((uint)flags.AsInteger() & 255u);
            if (body["permissions"] is OSDMap perms)
            {
                uint oldNext = item.NextPermissions, oldEveryone = item.EveryOnePermissions, oldGroup = item.GroupPermissions;
                if (perms.TryGetValue("next_owner_mask", out var next)) item.NextPermissions = (uint)next.AsInteger() & item.BasePermissions;
                if (perms.TryGetValue("everyone_mask", out var everyone))
                {
                    uint export = (uint)OpenSim.Framework.PermissionMask.Export;
                    item.EveryOnePermissions = ((uint)everyone.AsInteger() & item.BasePermissions & ~export)
                        | (item.EveryOnePermissions & export);
                }
                if (perms.TryGetValue("group_mask", out var group)) item.GroupPermissions = (uint)group.AsInteger() & item.BasePermissions;
                if (oldNext != item.NextPermissions) item.Flags |= (uint)InventoryItemFlags.ObjectOverwriteNextOwner;
                if (oldEveryone != item.EveryOnePermissions) item.Flags |= (uint)InventoryItemFlags.ObjectOverwriteEveryone;
                if (oldGroup != item.GroupPermissions) item.Flags |= (uint)InventoryItemFlags.ObjectOverwriteGroup;
            }
            if (body["sale_info"] is OSDMap sale)
            {
                int price = sale.TryGetValue("sale_price", out var priceValue) ? priceValue.AsInteger() : item.SalePrice;
                byte type = sale.TryGetValue("sale_type", out var typeValue) ? (byte)typeValue.AsInteger() : item.SaleType;
                if (price != item.SalePrice || type != item.SaleType) item.Flags |= (uint)InventoryItemFlags.ObjectSlamSale;
                item.SalePrice = price;
                item.SaleType = type;
            }
            if (body.TryGetValue("hash_id", out var hash) && hash.AsUUID() != UUID.Zero)
            {
                if (uploads is null)
                    return 501;
                var outcome = uploads(hash.AsUUID(), item, out UUID asset);
                if (outcome != InventoryUploadResult.Applied)
                    return outcome == InventoryUploadResult.Pending ? 503
                        : outcome == InventoryUploadResult.Missing ? 404 : 403;
                var saved = inventory.GetItem(agent, item.ID);
                if (saved is null || saved.ID != item.ID || saved.Owner != agent
                    || asset == UUID.Zero || saved.AssetID != asset)
                    return 409;
                result = AisEnvelope.Item(saved, agent);
                ReportVersion(result, saved.Folder);
                return 200;
            }
            bool changed = item.Name != existing.Name || item.Description != existing.Description
                || item.Flags != existing.Flags || item.NextPermissions != existing.NextPermissions
                || item.EveryOnePermissions != existing.EveryOnePermissions || item.GroupPermissions != existing.GroupPermissions
                || item.SalePrice != existing.SalePrice || item.SaleType != existing.SaleType;
            if (changed && !inventory.UpdateItem(item))
                return 409;
            result = AisEnvelope.IsLink(item) ? AisEnvelope.Link(item, agent) : AisEnvelope.Item(item, agent);
            ReportVersion(result, item.Folder);
            return 200;
        }

        var folder = inventory.GetFolder(agent, id);
        if (folder is null || folder.ID != id || folder.Owner != agent)
            return 404;
        if (folder.ParentID == UUID.Zero || (folder.Type != (short)FolderType.None && folder.Type != (short)FolderType.Outfit))
            return 403;
        var updated = new InventoryFolderBase
        {
            ID = folder.ID, Owner = folder.Owner, ParentID = folder.ParentID, Type = folder.Type,
            Version = folder.Version, Name = body.TryGetValue("name", out var folderName) ? folderName.AsString() : folder.Name
        };
        if (updated.Name != folder.Name && !inventory.UpdateFolder(updated))
            return 409;
        result = AisEnvelope.Category(updated, agent);
        ReportVersion(result, updated.ParentID);
        return 200;
    }

    private void ReportVersion(OSDMap result, UUID folderID)
    {
        var folder = inventory.GetFolder(agent, folderID);
        if (folder is not null && folder.ID == folderID && folder.Owner == agent)
            result["_updated_category_versions"] = new OSDMap { [folderID.ToString()] = (int)folder.Version };
    }

    public int Fetch(string method, string rawUrl, out OSDMap result)
    {
        result = null;
        if (agent == UUID.Zero || rawUrl is null || rawUrl.Length > 1024)
            return 400;

        int queryAt = rawUrl.IndexOf('?');
        string uriPath = queryAt < 0 ? rawUrl : rawUrl[..queryAt];
        if (!uriPath.StartsWith(Path + "/", StringComparison.Ordinal))
            return 404;
        if (method != "GET")
            return 405;

        if (uriPath == Path + "/orphans")
            return FetchOrphans(out result);

        string[] segments = uriPath[(Path.Length + 1)..].Split('/');
        if (segments.Length is < 2 or > 3)
            return 404;
        bool currentOutfit = segments.Length == 3 && segments[0] == "category"
            && segments[1] == "current" && segments[2] == "links";
        UUID id = UUID.Zero;
        if (!currentOutfit && (!UUID.TryParse(segments[1], out id) || id == UUID.Zero))
            return 404;
        if (segments[0] == "item" && segments.Length == 2)
        {
            var item = inventory.GetItem(agent, id);
            if (item is null || item.Owner != agent || item.ID != id)
                return 404;
            result = AisEnvelope.IsLink(item) ? AisEnvelope.Link(item, agent) : AisEnvelope.Item(item, agent);
            return 200;
        }

        if (segments[0] != "category" || (segments.Length == 3 && segments[2] is not ("children" or "categories" or "links")))
            return 404;
        var folder = currentOutfit ? inventory.GetFolderForType(agent, FolderType.CurrentOutfit)
            : inventory.GetFolder(agent, id);
        if (folder is null || folder.Owner != agent || (currentOutfit
            ? folder.Type != (short)FolderType.CurrentOutfit : folder.ID != id))
            return 404;
        if (segments.Length == 2)
        {
            result = AisEnvelope.Category(folder, agent);
            return 200;
        }

        if (segments[2] == "children" && queryAt >= 0)
        {
            foreach (string parameter in rawUrl[(queryAt + 1)..].Split('&'))
            {
                if (parameter.StartsWith("children=", StringComparison.Ordinal))
                    return FetchSubset(folder, parameter[9..], out result);
            }
        }

        if (segments[2] == "links" || segments[2] == "categories")
            return FetchProjection(folder, segments[2] == "links", out result);

        int depth = 0;
        if (queryAt >= 0)
        {
            foreach (string param in rawUrl[(queryAt + 1)..].Split('&'))
            {
                if (param.StartsWith("depth=", StringComparison.Ordinal))
                {
                    if (!int.TryParse(param.AsSpan(6), out depth))
                        return 400;
                    depth = Math.Clamp(depth, 0, MaxDepth);
                }
            }
        }

        int folders = MaxExpandedFolders;
        int entries = MaxEntries;
        return TryExpand(folder, depth, new HashSet<UUID>(), ref folders, ref entries, out result) ? 200 : 503;
    }

    private int FetchSubset(InventoryFolderBase folder, string children, out OSDMap result)
    {
        result = null;
        HashSet<UUID> wanted = new();
        foreach (string text in children.Split(','))
        {
            if (!UUID.TryParse(text, out UUID id) || id == UUID.Zero || wanted.Count >= 64)
                return 400;
            wanted.Add(id);
        }
        var content = inventory.GetFolderContent(agent, folder.ID);
        if (content?.Folders is null || content.Items is null
            || content.Folders.Count > MaxEntries || content.Items.Count > MaxEntries - content.Folders.Count)
            return 503;
        OSDMap categories = new(), items = new(), links = new();
        foreach (var child in content.Folders)
            if (child is not null && child.Owner == agent && child.ParentID == folder.ID && wanted.Contains(child.ID))
                categories[child.ID.ToString()] = AisEnvelope.Category(child, agent);
        foreach (var item in content.Items)
        {
            if (item is null || item.Owner != agent || item.Folder != folder.ID || !wanted.Contains(item.ID))
                continue;
            if (AisEnvelope.IsLink(item))
                links[item.ID.ToString()] = AisEnvelope.Link(item, agent);
            else
                items[item.ID.ToString()] = AisEnvelope.Item(item, agent);
        }
        result = AisEnvelope.Category(folder, agent, AisEnvelope.EmbeddedMap(categories, items, links));
        return 200;
    }

    private int FetchOrphans(out OSDMap result)
    {
        result = null;
        var folders = inventory.GetInventorySkeleton(agent);
        if (folders is null || folders.Count > MaxEntries)
            return 503;
        HashSet<UUID> ids = new();
        foreach (var folder in folders)
            if (folder is not null && folder.Owner == agent) ids.Add(folder.ID);
        OSDMap categories = new(), items = new();
        foreach (var folder in folders)
            if (folder is not null && folder.Owner == agent && folder.ParentID != UUID.Zero && !ids.Contains(folder.ParentID))
                categories[folder.ID.ToString()] = AisEnvelope.Category(folder, agent);
        var loose = inventory.GetFolderContent(agent, UUID.Zero);
        if (loose?.Items is null || loose.Items.Count > MaxEntries)
            return 503;
        foreach (var item in loose.Items)
            if (item is not null && item.Owner == agent && item.Folder == UUID.Zero)
                items[item.ID.ToString()] = AisEnvelope.Item(item, agent);
        result = new OSDMap { ["_embedded"] = new OSDMap { ["categories"] = categories, ["items"] = items } };
        return 200;
    }

    private int FetchProjection(InventoryFolderBase folder, bool fetchLinks, out OSDMap result)
    {
        result = null;
        InventoryCollection content = inventory.GetFolderContent(agent, folder.ID);
        if (content?.Folders is null || content.Items is null)
            return 503;
        if (content.Folders.Count > MaxEntries || content.Items.Count > MaxEntries - content.Folders.Count)
            return 503;

        OSDMap entries = new(), targets = new();
        if (!fetchLinks)
        {
            foreach (var child in content.Folders)
                if (child is not null && child.Owner == agent && child.ParentID == folder.ID && child.ID != folder.ID)
                    entries[child.ID.ToString()] = AisEnvelope.Category(child, agent);
        }
        else
        {
            // Bound target lookups independently of the folder entry limit.
            const int maxTargets = 256;
            HashSet<UUID> seen = new();
            foreach (var link in content.Items)
            {
                if (link is null || link.Owner != agent || link.Folder != folder.ID || !AisEnvelope.IsLink(link))
                    continue;
                entries[link.ID.ToString()] = AisEnvelope.Link(link, agent);
                if (link.AssetType == (int)AssetType.LinkFolder || link.AssetID == UUID.Zero || !seen.Add(link.AssetID))
                    continue;
                if (seen.Count > maxTargets)
                    return 503;
                var target = inventory.GetItem(agent, link.AssetID);
                if (target is not null && target.Owner == agent && target.ID == link.AssetID && !AisEnvelope.IsLink(target))
                    targets[target.ID.ToString()] = AisEnvelope.Item(target, agent);
            }
        }
        OSDMap embedded = fetchLinks ? new() { [AisEnvelope.Links] = entries, [AisEnvelope.Items] = targets }
            : new() { [AisEnvelope.Categories] = entries };
        result = AisEnvelope.Category(folder, agent, embedded);
        return 200;
    }

    private bool TryExpand(InventoryFolderBase folder, int depth, HashSet<UUID> visited,
        ref int folderBudget, ref int entryBudget, out OSDMap result)
    {
        result = null;
        if (--folderBudget < 0 || !visited.Add(folder.ID))
            return false;

        InventoryCollection content = inventory.GetFolderContent(agent, folder.ID);
        if (content is null || content.Folders is null || content.Items is null)
            return false;
        if (content.Folders.Count > entryBudget || content.Items.Count > entryBudget - content.Folders.Count)
            return false;
        entryBudget -= content.Folders.Count + content.Items.Count;

        OSDMap categories = new(), items = new(), links = new();
        foreach (var child in content.Folders)
        {
            if (child is null || child.Owner != agent || child.ParentID != folder.ID || visited.Contains(child.ID))
                continue;
            if (depth == 0)
                categories[child.ID.ToString()] = AisEnvelope.Category(child, agent);
            else if (TryExpand(child, depth - 1, visited, ref folderBudget, ref entryBudget, out OSDMap expanded))
                categories[child.ID.ToString()] = expanded;
            else
                return false;
        }

        foreach (var item in content.Items)
        {
            if (item is null || item.Owner != agent || item.Folder != folder.ID)
                continue;
            if (AisEnvelope.IsLink(item))
                links[item.ID.ToString()] = AisEnvelope.Link(item, agent);
            else
                items[item.ID.ToString()] = AisEnvelope.Item(item, agent);
        }
        result = AisEnvelope.Category(folder, agent, AisEnvelope.EmbeddedMap(categories, items, links));
        return true;
    }
}
