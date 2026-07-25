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

// Casperia Prime addition (not upstream OpenSim):
//
// A stable, always-available, non-capability HTTP endpoint that decodes a
// JPEG2000 texture asset (avatar profile pictures, snapshots, etc.) and
// returns it as a plain PNG. Exists so external web front ends (a PHP site,
// for example) can display in-world images without needing to install or
// shell out to a separate native JPEG2000 tool (e.g. OpenJPEG's
// opj_decompress.exe) themselves.
//
// This intentionally reuses the exact same decode path OpenSim's own
// GetTextureHandler capability and map tile renderer already use -
// OpenMetaverse.Imaging.OpenJPEG.DecodeToImage() - a fully managed .NET
// wrapper, not an external process. Nothing new to install; this endpoint
// is just a stable, session-independent doorway onto code OpenSim already
// ships and already trusts for this exact purpose.
//
// Example:
//   GET http://your-robust-host:8003/texture_png?id=<asset-uuid>
//   -> 200 OK, Content-Type: image/png, PNG bytes
//   -> 404 if the asset doesn't exist
//   -> 400 if id is missing/not a UUID
//   -> 415 if the asset isn't actually a JPEG2000 texture / failed to decode
//
// Enable via Robust.ini:
//   [TexturePngService]
//       Enabled = true
//       LocalServiceModule = "OpenSim.Services.AssetService.dll:AssetService"
//       ; (point this at the same asset DB/config your grid's real
//       ; [AssetService] section already uses)

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.Imaging;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Base;
using OpenSim.Server.Handlers.Base;
using OpenSim.Services.Interfaces;

namespace OpenSim.Server.Handlers.Asset
{
    public class TexturePngServiceConnector : ServiceConnector
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private IAssetService m_AssetService;

        public TexturePngServiceConnector(IConfigSource config, IHttpServer server, string configName) :
                base(config, server, configName)
        {
            string ourConfigName = string.IsNullOrEmpty(configName) ? "TexturePngService" : configName;

            IConfig serverConfig = config.Configs[ourConfigName];
            if (serverConfig == null)
                throw new Exception(string.Format("No section '{0}' in config file", ourConfigName));

            if (!serverConfig.GetBoolean("Enabled", false))
            {
                m_log.Info("[TEXTURE PNG SERVICE]: Disabled in config, not starting.");
                return;
            }

            string assetService = serverConfig.GetString("LocalServiceModule", string.Empty);
            if (string.IsNullOrEmpty(assetService))
                throw new Exception("No LocalServiceModule in [TexturePngService] config - point this at the same asset service class your grid's [AssetService] section uses.");

            object[] args = new object[] { config, ourConfigName };
            m_AssetService = ServerUtils.LoadPlugin<IAssetService>(assetService, args);

            if (m_AssetService == null)
                throw new Exception(string.Format("Failed to load AssetService from {0}; config is {1}", assetService, ourConfigName));

            server.AddSimpleStreamHandler(new SimpleStreamHandler("/texture_png", HandleGetTexturePng));

            m_log.Info("[TEXTURE PNG SERVICE]: Listening on /texture_png");
        }

        private void HandleGetTexturePng(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            httpResponse.KeepAlive = false;

            if (httpRequest.HttpMethod != "GET")
            {
                httpResponse.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                return;
            }

            string rawId = (string)httpRequest.Query["id"];
            if (string.IsNullOrEmpty(rawId) || !UUID.TryParse(rawId, out UUID assetId))
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                WriteText(httpResponse, "Missing or invalid 'id' query parameter (expected a UUID).");
                return;
            }

            AssetBase asset;
            try
            {
                asset = m_AssetService.Get(assetId.ToString());
            }
            catch (Exception e)
            {
                m_log.WarnFormat("[TEXTURE PNG SERVICE]: Error fetching asset {0}: {1}", assetId, e.Message);
                httpResponse.StatusCode = (int)HttpStatusCode.InternalServerError;
                return;
            }

            if (asset == null || asset.Data == null || asset.Data.Length == 0)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            byte[] pngBytes = DecodeJ2KToPng(assetId, asset.Data);
            if (pngBytes == null || pngBytes.Length == 0)
            {
                // Not a decodable JPEG2000 texture, or decode failed - let the
                // caller fall back to its own placeholder rather than guess.
                httpResponse.StatusCode = (int)HttpStatusCode.UnsupportedMediaType;
                return;
            }

            httpResponse.ContentType = "image/png";
            httpResponse.RawBuffer = pngBytes;
            httpResponse.StatusCode = (int)HttpStatusCode.OK;
        }

        /// <summary>
        /// Decodes raw JPEG2000 asset bytes to a PNG byte array, using the same
        /// managed OpenJPEG wrapper OpenSim's own GetTexture capability and map
        /// tile renderer already rely on - no external process involved.
        /// </summary>
        private byte[] DecodeJ2KToPng(UUID assetId, byte[] j2kData)
        {
            ManagedImage managedImage = null;
            Image image = null;

            try
            {
                if (!OpenJPEG.DecodeToImage(j2kData, out managedImage, out image) || image == null)
                {
                    m_log.WarnFormat("[TEXTURE PNG SERVICE]: Could not decode asset {0} as JPEG2000", assetId);
                    return null;
                }

                using (Bitmap bitmap = new Bitmap(image))
                using (MemoryStream stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Png);
                    return stream.ToArray();
                }
            }
            catch (Exception e)
            {
                m_log.WarnFormat("[TEXTURE PNG SERVICE]: Exception decoding asset {0}: {1}", assetId, e.Message);
                return null;
            }
            finally
            {
                image?.Dispose();
            }
        }

        private static void WriteText(IOSHttpResponse response, string text)
        {
            response.ContentType = "text/plain";
            response.RawBuffer = System.Text.Encoding.UTF8.GetBytes(text);
        }
    }
}
