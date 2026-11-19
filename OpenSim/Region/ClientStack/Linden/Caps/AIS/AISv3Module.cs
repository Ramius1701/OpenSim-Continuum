using System;
using System.Collections.Generic;
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using Caps = OpenSim.Framework.Capabilities.Caps;

namespace OpenSim.Region.ClientStack.LindenCaps.AIS;

// Tranquillity v1.1.2 capability registration adapted to Continuum's legacy
// Caps API. Default-off while the remaining compatibility matrix is open.
[Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "AISv3Module")]
public sealed class AISv3Module : ISharedRegionModule
{
    private sealed class Region
    {
        internal EventManager.RegisterCapsEvent Register;
        internal readonly List<(WeakReference<Caps> Caps, string Name)> Registrations = new();
        internal bool Active = true;
    }
    private readonly Dictionary<Scene, Region> regions = new();
    private bool enabled;
    private bool closed;
    public string Name => "AISv3Module";
    public Type ReplaceableInterface => null;
    public void Initialise(IConfigSource source) => enabled = source.Configs["AIS"]?.GetBoolean("ExperimentalEnabled", false) == true;
    public void AddRegion(Scene scene) { }
    public void PostInitialise() { }

    public void RegionLoaded(Scene scene)
    {
        if (!enabled || scene is null)
            return;
        // Only register where native services are available.
        IInventoryService inventory = scene.RequestModuleInterface<IInventoryService>();
        if (inventory is null)
            return;
        lock (regions)
        {
            if (closed || regions.ContainsKey(scene))
                return;
            var region = new Region();
            region.Register = (agent, caps) => Register(region, scene, agent, caps, inventory);
            regions.Add(scene, region);
            scene.EventManager.OnRegisterCaps += region.Register;
        }
    }

    private static void Register(Region region, Scene scene, UUID agent, Caps caps, IInventoryService inventory)
    {
        lock (region)
        {
            if (!region.Active || agent == UUID.Zero)
                return;
            Add("InventoryAPIv3", new AisReadHandler("/" + UUID.Random(), agent, inventory,
                (UUID transaction, InventoryItemBase item, out UUID asset) =>
                {
                    asset = UUID.Zero;
                    if (!scene.TryGetScenePresence(agent, out ScenePresence presence) || presence.IsChildAgent
                        || presence.ControllingClient is null)
                        return InventoryUploadResult.Rejected;
                    var uploader = scene.RequestModuleInterface<IConfirmedAgentAssetTransactions>();
                    return uploader is null ? InventoryUploadResult.Rejected
                        : uploader.CompleteInventoryUpload(presence.ControllingClient, transaction, item, out asset);
                }));
            var library = scene.RequestModuleInterface<ILibraryService>();
            if (library?.LibraryRootFolder is not null)
                Add("LibraryAPIv3", new AisLibraryHandler("/" + UUID.Random(), agent, library, inventory));

            void Add(string name, ISimpleStreamHandler handler)
            {
                // Caps owns deregistration on logout; region removal also revokes every live URL.
                region.Registrations.RemoveAll(entry => !entry.Caps.TryGetTarget(out var live) || live == caps && entry.Name == name);
                caps.RegisterSimpleHandler(name, handler, varPath: true);
                region.Registrations.Add((new WeakReference<Caps>(caps), name));
            }
        }
    }

    public void RemoveRegion(Scene scene)
    {
        Region region;
        lock (regions)
        {
            if (!regions.Remove(scene, out region))
                return;
            scene.EventManager.OnRegisterCaps -= region.Register;
        }
        lock (region)
        {
            region.Active = false;
            foreach (var entry in region.Registrations)
                if (entry.Caps.TryGetTarget(out var caps))
                    caps.CapsHandlers.Remove(entry.Name);
            region.Registrations.Clear();
        }
    }

    public void Close()
    {
        Scene[] scenes;
        lock (regions) { closed = true; scenes = new Scene[regions.Count]; regions.Keys.CopyTo(scenes, 0); }
        foreach (var scene in scenes)
            RemoveRegion(scene);
    }
}
