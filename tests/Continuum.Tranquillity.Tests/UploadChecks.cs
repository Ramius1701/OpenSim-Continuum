using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Region.ClientStack.LindenCaps.AIS;
using OpenSim.Region.CoreModules.Agent.AssetTransaction;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;

static class UploadChecks
{
    public static void Run(IInventoryService inventory, UUID owner, UUID folder)
    {
        UUID session = UUID.Random();
        var client = FixtureProxy.Create<IClientAPI>((method, args) => method.Name switch
        {
            "get_AgentId" => owner,
            "get_SessionId" => session,
            _ => null
        });
        var assets = new Dictionary<string, AssetBase>();
        var assetService = FixtureProxy.Create<IAssetService>((method, args) =>
        {
            if (method.Name == "Store")
            {
                var asset = (AssetBase)args[0];
                assets[asset.FullID.ToString()] = asset;
                return asset.FullID.ToString();
            }
            if (method.Name == "Get")
                return assets.GetValueOrDefault((string)args[0]);
            return null;
        });
        var scene = new Scene(new RegionInfo { RegionID = UUID.Random() });
        scene.RegisterModuleInterface(inventory);
        scene.RegisterModuleInterface(assetService);
        var transactions = new AgentAssetTransactions(owner, scene, false);
        var item = new InventoryItemBase(UUID.Random(), owner)
        {
            AssetID = UUID.Random(), Name = "upload test", Description = "",
            Folder = folder, AssetType = (int)AssetType.Notecard, InvType = (int)InventoryType.Notecard,
            CreatorId = owner.ToString(), ExperienceID = UUID.Zero
        };
        Check(inventory.AddItem(item), "Upload fixture inventory creation failed");
        UUID transaction = UUID.Random(), uploadedID = UUID.Random();
        var uploader = transactions.RequestXferUploader(transaction);
        Set(uploader, "ourClient", client);
        Set(uploader, "m_asset", new AssetBase(uploadedID, "uploaded", (sbyte)AssetType.Notecard, owner.ToString())
        {
            Data = Encoding.UTF8.GetBytes("fixture content")
        });
        SetState(uploader, "Uploading");
        var ais = new AisReadHandler("/uploads", owner, inventory,
            (UUID tx, InventoryItemBase requested, out UUID stored) =>
                transactions.CompleteInventoryUpload(client, tx, requested, out stored));
        var body = new OSDMap { ["hash_id"] = transaction, ["name"] = "saved name" };
        string url = "/uploads/item/" + item.ID;
        int Dispatch(AisReadHandler handler, string uri, OSDMap content, out OSDMap reply)
        {
            var method = typeof(AisReadHandler).GetMethod("DispatchWrite", BindingFlags.Instance | BindingFlags.NonPublic);
            object[] args = ["PATCH", uri, content, OSDParser.SerializeLLSDXmlBytes(content), null];
            int code = (int)method.Invoke(handler, args);
            reply = (OSDMap)args[4];
            return code;
        }
        Check(Dispatch(ais, url, body, out _) == 503, "Pending upload was reported as completed");
        Check(inventory.GetItem(owner, item.ID).AssetID == item.AssetID
            && inventory.GetItem(owner, item.ID).Name == item.Name && assets.Count == 0,
            "Pending upload changed an item or stored its unfinished data");

        var otherSession = FixtureProxy.Create<IClientAPI>((method, args) => method.Name switch
        {
            "get_AgentId" => owner,
            "get_SessionId" => UUID.Random(),
            _ => null
        });
        Check(transactions.CompleteInventoryUpload(otherSession, transaction, item, out _) == InventoryUploadResult.Rejected,
            "A different viewer session consumed an upload");
        SetState(uploader, "Complete");
        Check(Dispatch(ais, url, body, out OSDMap completed) == 200
            && completed["asset_id"].AsUUID() == uploadedID
            && inventory.GetItem(owner, item.ID).AssetID == uploadedID
            && inventory.GetItem(owner, item.ID).Name == "saved name" && assets.ContainsKey(uploadedID.ToString()),
            "Completed native upload did not store both asset and inventory");
        Check(Dispatch(ais, url, body, out OSDMap replay) == 200
            && replay["asset_id"].AsUUID() == uploadedID,
            "Retry without query tid failed after upload consumption");
        Check(transactions.CompleteInventoryUpload(client, transaction, item, out _) == InventoryUploadResult.Missing,
            "Confirmed upload was not consumed");
        body["name"] = "changed request";
        Check(Dispatch(ais, url, body, out _) == 409, "Reused upload transaction accepted different metadata");

        var clothing = new InventoryItemBase(UUID.Random(), owner)
        {
            AssetID = UUID.Random(), Name = "restricted wearable", Description = "", Folder = folder,
            AssetType = (int)AssetType.Clothing, InvType = (int)InventoryType.Wearable, CreatorId = owner.ToString()
        };
        Check(inventory.AddItem(clothing), "Wearable fixture creation failed");
        UUID badTransaction = UUID.Random(), badAsset = UUID.Random();
        var rejected = transactions.RequestXferUploader(badTransaction);
        Set(rejected, "ourClient", client);
        Set(rejected, "m_asset", new AssetBase(badAsset, "restricted", (sbyte)AssetType.Clothing, owner.ToString())
        {
            Data = Encoding.ASCII.GetBytes("textures 1\n0 " + UUID.Random() + "\n")
        });
        SetState(rejected, "Complete");
        Check(transactions.CompleteInventoryUpload(client, badTransaction, clothing, out _) == InventoryUploadResult.Rejected
            && inventory.GetItem(owner, clothing.ID).AssetID == clothing.AssetID
            && !assets.ContainsKey(badAsset.ToString()),
            "Native texture permission rejection did not preserve the previous wearable");
        Check(transactions.CompleteInventoryUpload(client, UUID.Random(), clothing, out _) == InventoryUploadResult.Missing,
            "Unknown transaction was manufactured");
        Console.WriteLine("PASS: native AIS completed/pending/rejected uploads, session binding, asset validation, and hash replay.");
    }

    static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    static void SetState(object target, string value)
    {
        var field = target.GetType().GetField("m_uploadState", BindingFlags.Instance | BindingFlags.NonPublic);
        field.SetValue(target, Enum.Parse(field.FieldType, value));
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

public class FixtureProxy : DispatchProxy
{
    public Func<MethodInfo, object[], object> Callback;
    public static T Create<T>(Func<MethodInfo, object[], object> callback) where T : class
    {
        var proxy = DispatchProxy.Create<T, FixtureProxy>();
        ((FixtureProxy)(object)proxy).Callback = callback;
        return proxy;
    }
    protected override object Invoke(MethodInfo method, object[] args)
    {
        var result = Callback(method, args);
        return result ?? (method.ReturnType != typeof(void) && method.ReturnType.IsValueType
            ? Activator.CreateInstance(method.ReturnType) : null);
    }
}
