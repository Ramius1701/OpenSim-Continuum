using System;
using System.Collections.Generic;
using Mono.Addins;
using Nini.Config;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Server.Handlers.Appearance;

namespace OpenSim.Region.CoreModules.ServiceConnectorsIn.Appearance;

// Standalone hosting of the same native appearance connector used by ROBUST.
[Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "AppearanceServiceInConnectorModule")]
public sealed class AppearanceServiceInConnectorModule : ISharedRegionModule
{
    private readonly HashSet<Scene> scenes = new();
    private IConfigSource config;
    private IHttpServer server;
    private bool enabled;
    public string Name => "AppearanceServiceInConnectorModule";
    public Type ReplaceableInterface => null;
    public void Initialise(IConfigSource source)
    {
        config = source;
        enabled = source.Configs["Modules"]?.GetBoolean("AppearanceServiceInConnector", false) == true;
    }
    public void PostInitialise() { }
    public void AddRegion(Scene scene) { }
    public void RegionLoaded(Scene scene)
    {
        if (!enabled) return;
        lock (scenes)
        {
            if (scenes.Contains(scene)) return;
            if (server is null)
            {
                var listener = MainServer.Instance;
                new AppearanceServerConnector(config, listener, "AppearanceService");
                server = listener;
            }
            scenes.Add(scene);
        }
    }
    public void RemoveRegion(Scene scene)
    {
        lock (scenes)
            if (scenes.Remove(scene) && scenes.Count == 0) RemoveHandler();
    }
    public void Close()
    {
        lock (scenes) { scenes.Clear(); RemoveHandler(); }
    }
    private void RemoveHandler()
    {
        server?.RemoveSimpleStreamHandler("/texture");
        server = null;
    }
}
