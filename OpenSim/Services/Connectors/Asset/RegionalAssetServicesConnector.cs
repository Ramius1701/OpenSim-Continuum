/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the conditions in the OpenSim
 * BSD licence are met.
 */

using System;
using System.Reflection;
using log4net;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;

namespace OpenSim.Services.Connectors
{
    /// <summary>
    /// Region-owned asset store with a grid fallback and safety mirror.
    ///
    /// This service is intended to be loaded as RegionAssetConnector's
    /// LocalGridAssetService.  The outer connector continues to own caching,
    /// Hypergrid policy and viewer compatibility.
    /// </summary>
    public sealed class RegionalAssetServicesConnector : IAssetService
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly IAssetService m_localService;
        private readonly IAssetService m_gridService;
        private readonly bool m_mirrorPermanentAssets;
        private readonly bool m_requireGridMirror;
        private readonly bool m_cacheGridAssetsLocally;
        private readonly bool m_deleteFromGrid;

        public RegionalAssetServicesConnector(IConfigSource source)
        {
            IConfig config = source.Configs["RegionalAssetService"]
                ?? throw new Exception("RegionalAssetService missing from configuration");

            string localModule = config.GetString(
                "LocalServiceModule", "OpenSim.Services.AssetService.dll:AssetService");
            string gridModule = config.GetString(
                "GridServiceModule", "OpenSim.Services.Connectors.dll:AssetServicesConnector");
            string localSection = config.GetString("LocalServiceSection", "RegionalAssetService");
            string gridSection = config.GetString("GridServiceSection", "GridAssetService");

            m_mirrorPermanentAssets = config.GetBoolean("MirrorPermanentAssetsToGrid", true);
            m_requireGridMirror = config.GetBoolean("RequireGridMirror", true);
            m_cacheGridAssetsLocally = config.GetBoolean("CacheGridAssetsLocally", true);
            m_deleteFromGrid = config.GetBoolean("DeleteFromGrid", false);

            m_localService = ServerUtils.LoadPlugin<IAssetService>(
                localModule, new object[] { source, localSection });
            m_gridService = ServerUtils.LoadPlugin<IAssetService>(
                gridModule, new object[] { source, gridSection });

            if (m_localService == null)
                throw new Exception("Unable to load the regional asset service " + localModule);
            if (m_gridService == null)
                throw new Exception("Unable to load the grid asset service " + gridModule);

            m_log.InfoFormat(
                "[REGIONAL ASSETS]: enabled; local section {0}, grid section {1}, mirror permanent assets {2}",
                localSection, gridSection, m_mirrorPermanentAssets);
        }

        public RegionalAssetServicesConnector(IAssetService localService, IAssetService gridService,
            bool mirrorPermanentAssets = true, bool requireGridMirror = true,
            bool cacheGridAssetsLocally = true, bool deleteFromGrid = false)
        {
            m_localService = localService ?? throw new ArgumentNullException(nameof(localService));
            m_gridService = gridService ?? throw new ArgumentNullException(nameof(gridService));
            m_mirrorPermanentAssets = mirrorPermanentAssets;
            m_requireGridMirror = requireGridMirror;
            m_cacheGridAssetsLocally = cacheGridAssetsLocally;
            m_deleteFromGrid = deleteFromGrid;
        }

        public AssetBase Get(string id)
        {
            AssetBase asset = m_localService.Get(id);
            if (asset != null)
                return asset;

            asset = m_gridService.Get(id);
            if (asset != null && m_cacheGridAssetsLocally)
                StoreLocalCopy(asset);
            return asset;
        }

        public AssetBase Get(string id, string foreignAssetService, bool storeOnLocalGrid)
        {
            AssetBase asset = m_localService.Get(id);
            if (asset != null)
                return asset;

            asset = m_gridService.Get(id, foreignAssetService, storeOnLocalGrid);
            if (asset != null && (storeOnLocalGrid || m_cacheGridAssetsLocally))
                StoreLocalCopy(asset);
            return asset;
        }

        public AssetMetadata GetMetadata(string id)
        {
            AssetMetadata metadata = m_localService.GetMetadata(id);
            return metadata ?? m_gridService.GetMetadata(id);
        }

        public byte[] GetData(string id)
        {
            byte[] data = m_localService.GetData(id);
            return data ?? m_gridService.GetData(id);
        }

        public AssetBase GetCached(string id)
        {
            return m_localService.GetCached(id);
        }

        public bool Get(string id, object sender, AssetRetrieved handler)
        {
            if (string.IsNullOrEmpty(id) || handler == null)
                return false;

            AssetBase asset = Get(id);
            handler(id, sender, asset);
            return true;
        }

        public void Get(string id, string foreignAssetService, bool storeOnLocalGrid,
            SimpleAssetRetrieved callBack)
        {
            callBack?.Invoke(Get(id, foreignAssetService, storeOnLocalGrid));
        }

        public bool[] AssetsExist(string[] ids)
        {
            if (ids == null)
                return Array.Empty<bool>();

            bool[] local = m_localService.AssetsExist(ids) ?? new bool[ids.Length];
            bool[] result = new bool[ids.Length];
            string[] missing = new string[ids.Length];
            int missingCount = 0;

            for (int i = 0; i < ids.Length; ++i)
            {
                if (i < local.Length && local[i])
                    result[i] = true;
                else
                    missing[missingCount++] = ids[i];
            }

            if (missingCount == 0)
                return result;

            Array.Resize(ref missing, missingCount);
            bool[] remote = m_gridService.AssetsExist(missing) ?? new bool[missingCount];
            int remoteIndex = 0;
            for (int i = 0; i < ids.Length; ++i)
            {
                if (!result[i])
                    result[i] = remoteIndex < remote.Length && remote[remoteIndex++];
            }
            return result;
        }

        public string Store(AssetBase asset)
        {
            if (asset == null)
                return string.Empty;

            string localID = m_localService.Store(asset);
            if (string.IsNullOrEmpty(localID))
                return string.Empty;

            if (!m_mirrorPermanentAssets || asset.Local || asset.Temporary)
                return localID;

            string gridID = m_gridService.Store(asset);
            if (!string.IsNullOrEmpty(gridID))
                return localID;

            m_log.ErrorFormat("[REGIONAL ASSETS]: Grid mirror failed for permanent asset {0}", asset.ID);
            return m_requireGridMirror ? string.Empty : localID;
        }

        public bool UpdateContent(string id, byte[] data)
        {
            bool local = m_localService.UpdateContent(id, data);
            if (!m_mirrorPermanentAssets)
                return local;

            bool grid = m_gridService.UpdateContent(id, data);
            return m_requireGridMirror ? local && grid : local;
        }

        public bool Delete(string id)
        {
            bool local = m_localService.Delete(id);
            if (!m_deleteFromGrid)
                return local;

            bool grid = m_gridService.Delete(id);
            return local || grid;
        }

        private void StoreLocalCopy(AssetBase asset)
        {
            try
            {
                m_localService.Store(asset);
            }
            catch (Exception e)
            {
                m_log.WarnFormat("[REGIONAL ASSETS]: Unable to cache grid asset {0} locally: {1}",
                    asset.ID, e.Message);
            }
        }
    }
}
