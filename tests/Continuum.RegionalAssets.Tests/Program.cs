using System;
using System.Collections.Generic;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Services.Connectors;
using OpenSim.Services.Interfaces;

internal static class Program
{
    private static int Main()
    {
        try
        {
            LocalFirstDoesNotReadGrid();
            GridFallbackPopulatesLocalStore();
            PermanentStoreMirrorsToGrid();
            TemporaryStoreRemainsRegional();
            RequiredMirrorFailureIsReported();
            DeleteDoesNotEraseGridByDefault();
            ExistenceCombinesBothStores();
            Console.WriteLine("Regional asset tests passed: 7");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }

    private static void LocalFirstDoesNotReadGrid()
    {
        FakeAssetService local = new FakeAssetService();
        FakeAssetService grid = new FakeAssetService();
        AssetBase asset = NewAsset();
        local.Store(asset);
        grid.Store(asset);
        grid.GetCalls = 0;

        RegionalAssetServicesConnector service = new RegionalAssetServicesConnector(local, grid);
        Equal(asset.ID, service.Get(asset.ID)?.ID, "local-first retrieval");
        Equal(0, grid.GetCalls, "local-first grid reads");
    }

    private static void GridFallbackPopulatesLocalStore()
    {
        FakeAssetService local = new FakeAssetService();
        FakeAssetService grid = new FakeAssetService();
        AssetBase asset = NewAsset();
        grid.Store(asset);

        RegionalAssetServicesConnector service = new RegionalAssetServicesConnector(local, grid);
        Equal(asset.ID, service.Get(asset.ID)?.ID, "grid fallback retrieval");
        True(local.Assets.ContainsKey(asset.ID), "grid fallback local cache");
    }

    private static void PermanentStoreMirrorsToGrid()
    {
        FakeAssetService local = new FakeAssetService();
        FakeAssetService grid = new FakeAssetService();
        AssetBase asset = NewAsset();
        RegionalAssetServicesConnector service = new RegionalAssetServicesConnector(local, grid);

        Equal(asset.ID, service.Store(asset), "permanent store result");
        True(local.Assets.ContainsKey(asset.ID), "permanent local store");
        True(grid.Assets.ContainsKey(asset.ID), "permanent grid mirror");
    }

    private static void TemporaryStoreRemainsRegional()
    {
        FakeAssetService local = new FakeAssetService();
        FakeAssetService grid = new FakeAssetService();
        AssetBase asset = NewAsset();
        asset.Temporary = true;
        RegionalAssetServicesConnector service = new RegionalAssetServicesConnector(local, grid);

        Equal(asset.ID, service.Store(asset), "temporary store result");
        True(local.Assets.ContainsKey(asset.ID), "temporary local store");
        True(!grid.Assets.ContainsKey(asset.ID), "temporary grid exclusion");
    }

    private static void RequiredMirrorFailureIsReported()
    {
        FakeAssetService local = new FakeAssetService();
        FakeAssetService grid = new FakeAssetService { FailStore = true };
        RegionalAssetServicesConnector service = new RegionalAssetServicesConnector(local, grid);

        Equal(string.Empty, service.Store(NewAsset()), "required mirror failure");
    }

    private static void DeleteDoesNotEraseGridByDefault()
    {
        FakeAssetService local = new FakeAssetService();
        FakeAssetService grid = new FakeAssetService();
        AssetBase asset = NewAsset();
        local.Store(asset);
        grid.Store(asset);
        RegionalAssetServicesConnector service = new RegionalAssetServicesConnector(local, grid);

        True(service.Delete(asset.ID), "regional delete result");
        True(!local.Assets.ContainsKey(asset.ID), "regional delete local removal");
        True(grid.Assets.ContainsKey(asset.ID), "regional delete grid preservation");
    }

    private static void ExistenceCombinesBothStores()
    {
        FakeAssetService local = new FakeAssetService();
        FakeAssetService grid = new FakeAssetService();
        AssetBase first = NewAsset();
        AssetBase second = NewAsset();
        local.Store(first);
        grid.Store(second);
        RegionalAssetServicesConnector service = new RegionalAssetServicesConnector(local, grid);

        bool[] result = service.AssetsExist(new[] { first.ID, second.ID, UUID.Random().ToString() });
        True(result[0] && result[1] && !result[2], "combined existence result");
    }

    private static AssetBase NewAsset()
    {
        UUID id = UUID.Random();
        return new AssetBase(id, "regional-test", 7, UUID.Random().ToString())
        {
            Data = new byte[] { 1, 2, 3, 4 }
        };
    }

    private static void True(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException("FAILED: " + name);
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"FAILED: {name}; expected {expected}, got {actual}");
    }

    private sealed class FakeAssetService : IAssetService
    {
        public readonly Dictionary<string, AssetBase> Assets = new Dictionary<string, AssetBase>();
        public bool FailStore;
        public int GetCalls;

        public AssetBase Get(string id)
        {
            ++GetCalls;
            Assets.TryGetValue(id, out AssetBase asset);
            return asset;
        }

        public AssetBase Get(string id, string foreignAssetService, bool storeOnLocalGrid) => Get(id);
        public AssetMetadata GetMetadata(string id) => Get(id)?.Metadata;
        public byte[] GetData(string id) => Get(id)?.Data;
        public AssetBase GetCached(string id) => Get(id);
        public bool Get(string id, object sender, AssetRetrieved handler)
        {
            handler(id, sender, Get(id));
            return true;
        }
        public void Get(string id, string foreignAssetService, bool storeOnLocalGrid,
            SimpleAssetRetrieved callBack) => callBack(Get(id));
        public bool[] AssetsExist(string[] ids)
        {
            bool[] result = new bool[ids.Length];
            for (int i = 0; i < ids.Length; ++i)
                result[i] = Assets.ContainsKey(ids[i]);
            return result;
        }
        public string Store(AssetBase asset)
        {
            if (FailStore)
                return string.Empty;
            Assets[asset.ID] = asset;
            return asset.ID;
        }
        public bool UpdateContent(string id, byte[] data)
        {
            if (!Assets.TryGetValue(id, out AssetBase asset))
                return false;
            asset.Data = data;
            return true;
        }
        public bool Delete(string id) => Assets.Remove(id);
    }
}
