// Behavioral checks for ports from the frozen opensim-lickx 6614599b7506 archive.
using System.Formats.Tar;
using System.Reflection;
using System.Runtime.CompilerServices;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Serialization.External;
using OpenSim.Region.CoreModules.World.Archiver;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.ScriptEngine.Interfaces;
using OpenSim.Region.ScriptEngine.Shared.Api;
using OpenSim.Region.ScriptEngine.Shared;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;
using OpenSim.Services.Interfaces;

var estateOwner = UUID.Random();
var tenant = UUID.Random();
var scene = new Scene(new RegionInfo { RegionID = UUID.Random(), RegionName = "Lickx regression" });
scene.RegionInfo.EstateSettings.EstateOwner = estateOwner;
scene.LandChannel = Proxy.Create<ILandChannel>((method, args) => method.Name == "AllParcels"
    ? new List<ILandObject>() : throw new NotSupportedException(method.Name));
scene.RegisterModuleInterface(Proxy.Create<ITerrainModule>((method, args) =>
{
    if (method.Name != "SaveToStream") throw new NotSupportedException(method.Name);
    ((Stream)args[1]).Write(new byte[] { 1, 2, 3, 4 });
    return null;
}));
scene.RegisterModuleInterface(Proxy.Create<IRegionSerialiserModule>((method, args) =>
    method.Name == "SerializeGroupToXml2" ? $"<object>{((SceneObjectGroup)args[0]).Name}</object>"
    : throw new NotSupportedException(method.Name)));
scene.RegisterModuleInterface(Proxy.Create<IAssetService>((method, args) => null));
scene.RegisterModuleInterface(Proxy.Create<IPermissionsModule>((method, args) =>
    method.Name == "GetPermissionClass" ? PermissionClass.Owner : throw new NotSupportedException(method.Name)));

AddObject("estate", estateOwner, true);
AddObject("tenant", tenant, true);
AddObject("no-copy", tenant, false);
Check(Export(null).SequenceEqual(new[] { "estate", "no-copy", "tenant" }), "Unfiltered OAR must retain all owners");
Check(Export(tenant.ToString()).SequenceEqual(new[] { "no-copy", "tenant" }), "Tenant OAR must exclude other owners");
Check(Export(estateOwner.ToString()).SequenceEqual(new[] { "estate" }), "Explicit estate-owner filter must remain a filter");
Check(Export(tenant.ToString(), "CT").SequenceEqual(new[] { "tenant" }), "Tenant export must preserve copy/transfer filtering");
foreach (object invalid in new object[] { "bad UUID", UUID.Zero.ToString(), "", 7 })
{
    using var output = new MemoryStream();
    try
    {
        new ArchiveWriteRequest(scene, output, Guid.NewGuid()).ArchiveRegion(new() { ["tenant"] = invalid });
        throw new Exception("Invalid tenant filter was accepted");
    }
    catch (ArgumentException)
    {
        Check(output.Length == 0, "Invalid filter must fail before writing archive content");
    }
}

var engine = Proxy.Create<IScriptEngine>((method, args) => method.Name == "get_World" ? scene
    : throw new NotSupportedException(method.Name));
var lsl = (LSL_Api)RuntimeHelpers.GetUninitializedObject(typeof(LSL_Api));
lsl.World = scene;
typeof(LSL_Api).GetField("m_ScriptEngine", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(lsl, engine);
foreach (var (rating, expected) in new[] { (0, "PG"), (1, "MATURE"), (2, "ADULT"), (9, "UNKNOWN") })
{
    scene.RegionInfo.RegionSettings.Maturity = rating;
    Check(lsl.llGetEnv("region_rating").ToString() == expected, "Region rating contract");
}
Check(lsl.llGetEnv("mailname").ToString() == "" && lsl.llGetEnv("objectmail_hostname").ToString() == "",
    "Disabled email must not advertise a mailbox hostname");
typeof(LSL_Api).GetField("m_internalObjectHost", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(lsl, "objects.test.invalid");
typeof(LSL_Api).GetField("m_emailModule", BindingFlags.Instance | BindingFlags.NonPublic)
    .SetValue(lsl, Proxy.Create<IEmailModule>((method, args) => null));
Check(lsl.llGetEnv("mailname").ToString() == "objects.test.invalid"
    && lsl.llGetEnv("objectmail_hostname").ToString() == "objects.test.invalid", "Email aliases must agree");
Check(ScriptBaseClass.PAYMENT_INFO_ON_FILE == 1 && ScriptBaseClass.PAYMENT_INFO_USED == 2, "Payment flag values");
var foreignItem = new InventoryItemBase(UUID.Random(), tenant)
{
    CreatorId = UUID.Random().ToString(), CreatorData = "https://home.test.invalid/;Foreign Creator", Name = "Foreign item"
};
var defaultItemXml = UserInventoryItemSerializer.Serialize(foreignItem, new(), null);
Check(defaultItemXml.Contains("<CreatorData>"), "IAR must preserve foreign creator data by default");
Check(UserInventoryItemSerializer.Deserialize(defaultItemXml).CreatorData == foreignItem.CreatorData,
    "IAR creator metadata must round trip");
Check(!UserInventoryItemSerializer.Serialize(foreignItem, new() { ["nocreators"] = null }, null).Contains("<CreatorData>"),
    "IAR explicit creator opt-out must omit foreign data");
Check(!UserInventoryItemSerializer.Serialize(foreignItem, new() { ["creators"] = null, ["nocreators"] = null }, null)
    .Contains("<CreatorData>"), "Creator opt-out must win over the legacy creators switch");
Check(lsl.llGetEnv("grid").ToString() == "", "Missing grid metadata must return an empty name");

var normalObject = new SceneObjectGroup(tenant, new Vector3(128, 128, 25), PrimitiveBaseShape.CreateBox());
typeof(LSL_Api).GetField("m_host", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(lsl, normalObject.RootPart);
FaceMaterial capturedMaterial = null;
typeof(LSL_Api).GetField("m_materialsModule", BindingFlags.Instance | BindingFlags.NonPublic)
    .SetValue(lsl, Proxy.Create<IMaterialsModule>((method, args) =>
    {
        if (method.Name == "AddNewMaterial")
        {
            capturedMaterial = (FaceMaterial)args[0];
            return UUID.Zero;
        }
        throw new NotSupportedException(method.Name);
    }));
var normalRules = new LSL_Types.list(new object[]
{
    new LSL_Types.LSLInteger(ScriptBaseClass.PRIM_NORMAL), new LSL_Types.LSLInteger(0),
    new LSL_Types.LSLString(UUID.Random().ToString()), new LSL_Types.Vector3(1, 1, 0),
    new LSL_Types.Vector3(-0.5, -2, 0), new LSL_Types.LSLFloat(0)
});
typeof(LSL_Api).GetMethod("SetPrimParams", BindingFlags.Instance | BindingFlags.NonPublic, null,
    new[] { typeof(SceneObjectPart), typeof(LSL_Types.list), typeof(string), typeof(uint).MakeByRefType() }, null)
    .Invoke(lsl, new object[] { normalObject.RootPart, normalRules, "regression", 0u });
Check(capturedMaterial != null && capturedMaterial.NormalOffsetX == -0.5f && capturedMaterial.NormalOffsetY == -1f,
    "PRIM_NORMAL must preserve negative offsets and clamp at -1");
var lickx = new Lickx_Api();
Check(lickx.lxGetAgentViewer("bad UUID").ToString() == "", "Invalid viewer lookup");
Console.WriteLine("PASS: tenant OAR selection/permissions, invalid filters, IAR creator identity, environment keys, payment constants and negative normal-map offsets.");
ServiceChecks.Run();
PostgresChecks.Run();
ImapChecks.Run();

void AddObject(string name, UUID owner, bool copyable)
{
    var obj = new SceneObjectGroup(owner, new Vector3(128, 128, 25), PrimitiveBaseShape.CreateBox());
    obj.UUID = UUID.Random();
    obj.Name = name;
    obj.RootPart.CreatorID = UUID.Random();
    obj.RootPart.BaseMask = (uint)(copyable ? OpenSim.Framework.PermissionMask.Copy | OpenSim.Framework.PermissionMask.Transfer
        : OpenSim.Framework.PermissionMask.Transfer);
    scene.Entities.Add(obj);
}

string[] Export(string tenantID, string permissions = null)
{
    using var output = new MemoryStream();
    var options = new Dictionary<string, object> { ["noassets"] = true };
    if (tenantID != null) options["tenant"] = tenantID;
    if (permissions != null) options["checkPermissions"] = permissions;
    new ArchiveWriteRequest(scene, output, Guid.NewGuid()).ArchiveRegion(options);
    using var input = new MemoryStream(output.ToArray());
    using var tar = new TarReader(input);
    var objects = new List<string>();
    bool terrain = false, settings = false;
    TarEntry entry;
    while ((entry = tar.GetNextEntry()) != null)
    {
        terrain |= entry.Name.StartsWith("terrains/");
        settings |= entry.Name.StartsWith("settings/");
        if (entry.Name.StartsWith("objects/"))
        {
            using var reader = new StreamReader(entry.DataStream);
            objects.Add(reader.ReadToEnd().Replace("<object>", "").Replace("</object>", ""));
        }
    }
    Check(terrain && settings, "Tenant OAR must retain terrain and region settings");
    return objects.Order().ToArray();
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

public class Proxy : DispatchProxy
{
    public Func<MethodInfo, object[], object> Handler;
    protected override object Invoke(MethodInfo method, object[] args) => Handler(method, args);
    public static T Create<T>(Func<MethodInfo, object[], object> handler) where T : class
    {
        var result = DispatchProxy.Create<T, Proxy>();
        ((Proxy)(object)result).Handler = handler;
        return result;
    }
}
