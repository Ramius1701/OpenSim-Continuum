/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using log4net;
using Nini.Config;
using OpenMetaverse;

/// <summary>
/// Loads assets from the filesystem location.  Not yet a plugin, though it should be.
/// </summary>
namespace OpenSim.Framework.AssetLoader.Filesystem
{
    public class AssetLoaderFileSystem : IAssetLoader
    {
        private const string LIBRARY_OWNER_IDstr = "11111111-1111-0000-0000-000100bba000";
        private static readonly UUID LIBRARY_OWNER_ID = new UUID(LIBRARY_OWNER_IDstr);

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected static AssetBase CreateAsset(string assetIdStr, string name, string path, sbyte type)
        {
            AssetBase asset = new AssetBase(new UUID(assetIdStr), name, type, LIBRARY_OWNER_IDstr);

            if (!String.IsNullOrEmpty(path))
            {
                //m_log.InfoFormat("[ASSETS]: Loading: [{0}][{1}]", name, path);

                if (!LoadAsset(asset, path))
                    return null;
            }
            else
            {
                asset.Data = Array.Empty<byte>();
                m_log.InfoFormat("[ASSETS]: Instantiated: [{0}]", name);
            }

            return asset;
        }

        protected static bool LoadAsset(AssetBase info, string path)
        {
//            bool image =
//               (info.Type == (sbyte)AssetType.Texture ||
//                info.Type == (sbyte)AssetType.TextureTGA ||
//                info.Type == (sbyte)AssetType.ImageJPEG ||
//                info.Type == (sbyte)AssetType.ImageTGA);

            if (!File.Exists(path))
            {
                m_log.ErrorFormat("[ASSETS]: file: [{0}] not found; asset [{1}] was skipped.", path, info.Name);
                return false;
            }

            try
            {
                info.Data = File.ReadAllBytes(path);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                m_log.ErrorFormat(
                    "[ASSETS]: unable to read file [{0}] for asset [{1}]; asset was skipped: {2}",
                    path, info.Name, e.Message);
                return false;
            }
        }

        public void ForEachDefaultXmlAsset(string assetSetFilename, Action<AssetBase> action)
        {
            List<AssetBase> assets = new List<AssetBase>();
            if (File.Exists(assetSetFilename))
            {
                string assetSetPath = "ERROR";
                string assetRootPath = "";
                try
                {
                    XmlConfigSource source = new XmlConfigSource(assetSetFilename);
                    assetRootPath = Path.GetFullPath(source.SavePath);
                    assetRootPath = Path.GetDirectoryName(assetRootPath);

                    for (int i = 0; i < source.Configs.Count; i++)
                    {
                        assetSetPath = source.Configs[i].GetString("file", String.Empty);

                        LoadXmlAssetSet(Path.Combine(assetRootPath, assetSetPath), assets);
                    }
                }
                catch (XmlException e)
                {
                    m_log.ErrorFormat("[ASSETS]: Error loading {0} : {1}", assetSetPath, e.Message);
                }
            }
            else
            {
                m_log.ErrorFormat("[ASSETS]: Asset set control file {0} does not exist! No assets loaded.", assetSetFilename);
            }

            assets.ForEach(action);
        }

        /// <summary>
        /// Use the asset set information at path to load assets
        /// </summary>
        /// <param name="assetSetPath"></param>
        /// <param name="assets"></param>
        protected static void LoadXmlAssetSet(string assetSetPath, List<AssetBase> assets)
        {
            //m_log.InfoFormat("[ASSETS]: Loading asset set {0}", assetSetPath);

            if (File.Exists(assetSetPath))
            {
                try
                {
                    XmlConfigSource source = new XmlConfigSource(assetSetPath);
                    String dir = Path.GetDirectoryName(assetSetPath);

                    for (int i = 0; i < source.Configs.Count; i++)
                    {
                        string assetIdStr = source.Configs[i].GetString("assetID", String.Empty).Trim();
                        if (!UUID.TryParse(assetIdStr, out UUID assetID) || assetID.IsZero())
                        {
                            m_log.ErrorFormat(
                                "[ASSETS]: asset [{0}] in [{1}] has no valid non-zero assetID; entry was skipped.",
                                source.Configs[i].Name, assetSetPath);
                            continue;
                        }

                        string name = source.Configs[i].GetString("name", String.Empty);
                        sbyte type = (sbyte)source.Configs[i].GetInt("assetType", 0);

                        string assetPath =  source.Configs[i].GetString("fileName", String.Empty);
                        AssetBase newAsset;
                        if (string.IsNullOrEmpty(assetPath))
                            newAsset = CreateAsset(assetID.ToString(), name, null, type);
                        else
                            newAsset = CreateAsset(assetID.ToString(), name, Path.Combine(dir, assetPath), type);

                        if (newAsset != null)
                        {
                            newAsset.Type = type;
                            assets.Add(newAsset);
                        }
                    }
                }
                catch (XmlException e)
                {
                    m_log.ErrorFormat("[ASSETS]: Error loading {0} : {1}", assetSetPath, e.Message);
                }
            }
            else
            {
                m_log.ErrorFormat("[ASSETS]: Asset set file {0} does not exist!", assetSetPath);
            }
        }
    }
}
