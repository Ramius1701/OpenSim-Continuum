#nullable enable annotations

/*
 * OpenSim Marketplace
 * Version 2.1.0
 *
 * Protected Direct Delivery inventory, snapshot, and fulfilment endpoints.
 */
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.ServiceAuth;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

[assembly: Addin("OpenSimMarketplace", "2.1.0")]
[assembly: AddinDescription("OpenSim Direct Delivery marketplace inventory and fulfilment")]
[assembly: AddinDependency("OpenSim.Region.Framework", OpenSim.VersionInfo.VersionNumber)]

namespace OpenSim.Addons.Marketplace;

[Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "OpenSimMarketplaceModule")]
public sealed class MarketplaceModule : ISharedRegionModule
{
    private const string ConfigSection = "OpenSimMarketplace";
    private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);
    private static readonly Regex OperationIdPattern = new("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex FingerprintPattern = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly object m_sceneSync = new();
    private readonly ConcurrentDictionary<string, byte> m_inflight = new(StringComparer.Ordinal);

    private bool m_enabled;
    private bool m_registered;
    private bool m_requireHttps;
    private bool m_notifyLocalUser = true;
    private int m_maxRequestBodyBytes = 32768;
    private int m_maxInventoryNodes = 5000;
    private UUID m_serviceRegionId = UUID.Zero;
    private UUID m_serviceAccountId = UUID.Zero;
    private string m_inventoryPath = "/opensim/marketplace/v2/inventory";
    private string m_inspectPath = "/opensim/marketplace/v2/inspect";
    private string m_snapshotPath = "/opensim/marketplace/v2/snapshot";
    private string m_deliveryPath = "/opensim/marketplace/v2/deliver";
    private Scene? m_serviceScene;
    private IServiceAuth? m_auth;
    private DeliveryLedger? m_ledger;

    public string Name => "OpenSim Marketplace";
    public Type ReplaceableInterface => null!;

    public void PostInitialise()
    {
    }

    public void Initialise(IConfigSource source)
    {
        IConfig? config = source.Configs[ConfigSection];
        if (config == null || !config.GetBoolean("Enabled", false))
            return;

        m_enabled = true;

        if (!UUID.TryParse(config.GetString("ServiceRegionUUID", string.Empty).Trim(), out m_serviceRegionId) || m_serviceRegionId == UUID.Zero)
            throw new Exception("[OpenSimMarketplace] ServiceRegionUUID must be a non-zero region UUID.");
        if (!UUID.TryParse(config.GetString("ServiceAccountUUID", string.Empty).Trim(), out m_serviceAccountId) || m_serviceAccountId == UUID.Zero)
            throw new Exception("[OpenSimMarketplace] ServiceAccountUUID must be a non-zero local account UUID.");

        m_inventoryPath = NormalizePath(config.GetString("InventoryEndpointPath", m_inventoryPath));
        m_inspectPath = NormalizePath(config.GetString("InspectEndpointPath", m_inspectPath));
        m_snapshotPath = NormalizePath(config.GetString("SnapshotEndpointPath", m_snapshotPath));
        m_deliveryPath = NormalizePath(config.GetString("DeliveryEndpointPath", m_deliveryPath));

        if (new[] { m_inventoryPath, m_inspectPath, m_snapshotPath, m_deliveryPath }.Distinct(StringComparer.Ordinal).Count() != 4)
            throw new Exception("[OpenSimMarketplace] Marketplace endpoint paths must be unique.");

        m_requireHttps = config.GetBoolean("RequireHttps", false);
        m_notifyLocalUser = config.GetBoolean("NotifyLocalUser", true);
        m_maxRequestBodyBytes = Math.Clamp(config.GetInt("MaxRequestBodyBytes", 32768), 1024, 1024 * 1024);
        m_maxInventoryNodes = Math.Clamp(config.GetInt("MaxInventoryNodes", 5000), 10, 100000);

        if (!string.Equals(config.GetString("AuthType", string.Empty).Trim(), "BasicHttpAuthentication", StringComparison.Ordinal))
            throw new Exception("[OpenSimMarketplace] AuthType must be BasicHttpAuthentication.");
        if (string.IsNullOrWhiteSpace(config.GetString("HttpAuthUsername", string.Empty)) || string.IsNullOrEmpty(config.GetString("HttpAuthPassword", string.Empty)))
            throw new Exception("[OpenSimMarketplace] HttpAuthUsername and HttpAuthPassword are required.");

        m_auth = ServiceAuth.Create(source, ConfigSection)
            ?? throw new Exception("[OpenSimMarketplace] OpenSim service authentication could not be created.");

        string ledgerPath = config.GetString("LedgerPath", "Data/OpenSimMarketplace/marketplace-deliveries-v2.jsonl").Trim();
        if (string.IsNullOrWhiteSpace(ledgerPath))
            throw new Exception("[OpenSimMarketplace] LedgerPath is required.");
        m_ledger = new DeliveryLedger(ledgerPath, Log);

        Log.InfoFormat(
            "[OPENSIM MARKETPLACE]: Direct Delivery enabled; service region={0}, service account={1}, ledger={2}",
            m_serviceRegionId,
            m_serviceAccountId,
            m_ledger.PathName);

        if (!m_requireHttps)
            Log.Warn("[OPENSIM MARKETPLACE]: RequireHttps is false. Keep the service endpoint on loopback/private transport or behind HTTPS.");
    }

    public void AddRegion(Scene scene)
    {
        if (!m_enabled || scene.RegionInfo.RegionID != m_serviceRegionId)
            return;

        lock (m_sceneSync)
        {
            if (m_serviceScene != null && !ReferenceEquals(m_serviceScene, scene))
                throw new Exception("[OpenSimMarketplace] More than one scene matched ServiceRegionUUID.");

            m_serviceScene = scene;
            if (m_registered)
                return;
            if (m_auth == null || m_ledger == null)
                throw new Exception("[OpenSimMarketplace] Module was not initialized correctly.");

            MainServer.Instance.AddSimpleStreamHandler(new SimpleStreamHandler(m_inventoryPath, m_auth, HandleInventoryRequest, Name + ".inventory"));
            MainServer.Instance.AddSimpleStreamHandler(new SimpleStreamHandler(m_inspectPath, m_auth, HandleInspectRequest, Name + ".inspect"));
            MainServer.Instance.AddSimpleStreamHandler(new SimpleStreamHandler(m_snapshotPath, m_auth, HandleSnapshotRequest, Name + ".snapshot"));
            MainServer.Instance.AddSimpleStreamHandler(new SimpleStreamHandler(m_deliveryPath, m_auth, HandleDeliveryRequest, Name + ".deliver"));
            m_registered = true;

            Log.InfoFormat("[OPENSIM MARKETPLACE]: Registered Direct Delivery endpoints on region process hosting {0}.", scene.RegionInfo.RegionName);
        }
    }

    public void RegionLoaded(Scene scene) { }

    public void RemoveRegion(Scene scene)
    {
        lock (m_sceneSync)
        {
            if (ReferenceEquals(m_serviceScene, scene))
                m_serviceScene = null;
        }
    }

    public void Close()
    {
        lock (m_sceneSync)
            m_serviceScene = null;
        m_inflight.Clear();
    }

    private void HandleInventoryRequest(IOSHttpRequest request, IOSHttpResponse response)
    {
        response.ContentType = "application/json; charset=utf-8";
        if (!ValidatePost(request, response))
            return;

        try
        {
            InventoryRequest? payload = JsonSerializer.Deserialize<InventoryRequest>(ReadRequestBody(request.InputStream), JsonOptions);
            if (!TryUuid(payload?.SellerId, out UUID sellerId))
            {
                Write(response, 400, new InventoryResponse { Ok = false, SellerId = payload?.SellerId ?? string.Empty, Message = "seller_id must be a non-zero UUID." });
                return;
            }

            string action = (payload?.Action ?? "list").Trim().ToLowerInvariant();
            if (action != "list" && action != "ensure")
            {
                Write(response, 400, new InventoryResponse { Ok = false, SellerId = sellerId.ToString(), Message = "action must be list or ensure." });
                return;
            }

            Scene? scene = SceneOrUnavailable(response);
            if (scene == null)
                return;

            InventoryResponse result = MarketplaceInventoryOperations.Inventory(scene, sellerId, m_maxInventoryNodes);
            Write(response, result.Ok ? 200 : 422, result);
        }
        catch (RequestBodyTooLargeException)
        {
            Write(response, 413, new { ok = false, message = "Request body is too large." });
        }
        catch (JsonException)
        {
            Write(response, 400, new { ok = false, message = "Request body contains invalid JSON." });
        }
        catch (Exception ex)
        {
            Log.ErrorFormat("[OPENSIM MARKETPLACE]: Inventory request failed: {0}", ex);
            Write(response, 500, new { ok = false, message = "Inventory request failed." });
        }
    }

    private void HandleInspectRequest(IOSHttpRequest request, IOSHttpResponse response)
    {
        response.ContentType = "application/json; charset=utf-8";
        if (!ValidatePost(request, response))
            return;

        try
        {
            InspectRequest? payload = JsonSerializer.Deserialize<InspectRequest>(ReadRequestBody(request.InputStream), JsonOptions);
            if (!TryUuid(payload?.SellerId, out UUID sellerId) || !TryUuid(payload?.SourceFolderId, out UUID sourceFolderId))
            {
                Write(response, 400, new { ok = false, message = "seller_id and source_folder_id must be non-zero UUIDs." });
                return;
            }

            Scene? scene = SceneOrUnavailable(response);
            if (scene == null)
                return;

            try
            {
                ProductFolderInfo result = MarketplaceInventoryOperations.Inspect(scene, sellerId, sourceFolderId, m_maxInventoryNodes);
                Write(response, 200, new { ok = true, product = result });
            }
            catch (MarketplaceInventoryException ex)
            {
                Write(response, (int)ex.StatusCode, new { ok = false, message = ex.Message, retryable = ex.Retryable });
            }
        }
        catch (RequestBodyTooLargeException)
        {
            Write(response, 413, new { ok = false, message = "Request body is too large." });
        }
        catch (JsonException)
        {
            Write(response, 400, new { ok = false, message = "Request body contains invalid JSON." });
        }
        catch (Exception ex)
        {
            Log.ErrorFormat("[OPENSIM MARKETPLACE]: Inspect request failed: {0}", ex);
            Write(response, 500, new { ok = false, message = "Inspect request failed." });
        }
    }

    private void HandleSnapshotRequest(IOSHttpRequest request, IOSHttpResponse response)
    {
        response.ContentType = "application/json; charset=utf-8";
        if (!ValidatePost(request, response))
            return;

        string versionKey = string.Empty;
        try
        {
            SnapshotRequest? payload = JsonSerializer.Deserialize<SnapshotRequest>(ReadRequestBody(request.InputStream), JsonOptions);
            versionKey = payload?.VersionKey?.Trim() ?? string.Empty;
            if (!OperationIdPattern.IsMatch(versionKey))
            {
                Write(response, 400, new { ok = false, version_key = versionKey, message = "version_key is invalid." });
                return;
            }
            if (!TryUuid(payload?.SellerId, out UUID sellerId) || !TryUuid(payload?.SourceFolderId, out UUID sourceFolderId))
            {
                Write(response, 400, new { ok = false, version_key = versionKey, message = "seller_id and source_folder_id must be non-zero UUIDs." });
                return;
            }

            Scene? scene = SceneOrUnavailable(response);
            if (scene == null)
                return;

            if (!m_inflight.TryAdd("snapshot:" + versionKey, 0))
            {
                Write(response, 409, new { ok = false, version_key = versionKey, retryable = true, message = "This listing version snapshot is already in progress." });
                return;
            }

            try
            {
                SnapshotResponse result = MarketplaceInventoryOperations.Snapshot(
                    scene,
                    m_serviceAccountId,
                    sellerId,
                    sourceFolderId,
                    versionKey,
                    m_maxInventoryNodes);
                Write(response, 200, result);
            }
            catch (MarketplaceInventoryException ex)
            {
                Write(response, (int)ex.StatusCode, new { ok = false, version_key = versionKey, retryable = ex.Retryable, message = ex.Message });
            }
            finally
            {
                m_inflight.TryRemove("snapshot:" + versionKey, out _);
            }
        }
        catch (RequestBodyTooLargeException)
        {
            Write(response, 413, new { ok = false, version_key = versionKey, message = "Request body is too large." });
        }
        catch (JsonException)
        {
            Write(response, 400, new { ok = false, version_key = versionKey, message = "Request body contains invalid JSON." });
        }
        catch (Exception ex)
        {
            Log.ErrorFormat("[OPENSIM MARKETPLACE]: Snapshot request failed for {0}: {1}", versionKey, ex);
            Write(response, 500, new { ok = false, version_key = versionKey, retryable = true, message = "Snapshot request failed." });
        }
    }

    private void HandleDeliveryRequest(IOSHttpRequest request, IOSHttpResponse response)
    {
        response.ContentType = "application/json; charset=utf-8";
        if (!ValidatePost(request, response))
            return;

        string deliveryId = string.Empty;
        try
        {
            DeliveryRequest? payload = JsonSerializer.Deserialize<DeliveryRequest>(ReadRequestBody(request.InputStream), JsonOptions);
            deliveryId = payload?.DeliveryId?.Trim() ?? string.Empty;
            if (!OperationIdPattern.IsMatch(deliveryId))
            {
                Write(response, 400, DeliveryResponse.Error(deliveryId, "delivery_id is invalid."));
                return;
            }
            if (!TryUuid(payload?.SellerId, out UUID sellerId)
                || !TryUuid(payload?.SnapshotFolderId, out UUID snapshotFolderId)
                || !TryUuid(payload?.RecipientId, out UUID recipientId))
            {
                Write(
                    response,
                    400,
                    DeliveryResponse.Error(
                        deliveryId,
                        "seller_id, snapshot_folder_id and recipient_id must be non-zero UUIDs."));
                return;
            }

            string snapshotFingerprint =
                payload?.SnapshotFingerprint?.Trim() ?? string.Empty;

            if (!FingerprintPattern.IsMatch(snapshotFingerprint))
            {
                Write(
                    response,
                    400,
                    DeliveryResponse.Error(
                        deliveryId,
                        "snapshot_fingerprint must be a 64-character hexadecimal SHA-256 fingerprint."));
                return;
            }

            Scene? scene = SceneOrUnavailable(response);
            if (scene == null || m_ledger == null)
                return;

            if (!m_inflight.TryAdd("delivery:" + deliveryId, 0))
            {
                Write(response, 409, DeliveryResponse.Error(deliveryId, "This delivery is already in progress. Retry with the same delivery_id.", true));
                return;
            }

            try
            {
                DeliveryResponse result = MarketplaceInventoryOperations.Deliver(
                    scene,
                    m_serviceAccountId,
                    sellerId,
                    snapshotFolderId,
                    recipientId,
                    snapshotFingerprint,
                    deliveryId,
                    m_maxInventoryNodes,
                    m_ledger,
                    Log,
                    m_notifyLocalUser);
                Write(response, result.Ok ? 200 : (result.Retryable ? 503 : 422), result);
            }
            finally
            {
                m_inflight.TryRemove("delivery:" + deliveryId, out _);
            }
        }
        catch (RequestBodyTooLargeException)
        {
            Write(response, 413, DeliveryResponse.Error(deliveryId, "Request body is too large."));
        }
        catch (JsonException)
        {
            Write(response, 400, DeliveryResponse.Error(deliveryId, "Request body contains invalid JSON."));
        }
        catch (Exception ex)
        {
            Log.ErrorFormat("[OPENSIM MARKETPLACE]: Delivery request failed for {0}: {1}", deliveryId, ex);
            Write(response, 500, DeliveryResponse.Error(deliveryId, "Delivery request failed.", true));
        }
    }

    private Scene? SceneOrUnavailable(IOSHttpResponse response)
    {
        Scene? scene;
        lock (m_sceneSync)
            scene = m_serviceScene;
        if (scene == null)
            Write(response, 503, new { ok = false, retryable = true, message = "Marketplace service region is unavailable." });
        return scene;
    }

    private bool ValidatePost(IOSHttpRequest request, IOSHttpResponse response)
    {
        response.AddHeader("Cache-Control", "no-store");
        response.AddHeader("X-Content-Type-Options", "nosniff");
        if (!string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            response.AddHeader("Allow", "POST");
            Write(response, 405, new { ok = false, message = "Only POST is supported." });
            return false;
        }
        if (m_requireHttps && !request.IsSecured)
        {
            Write(response, 403, new { ok = false, message = "HTTPS is required." });
            return false;
        }
        if (!request.HasEntityBody)
        {
            Write(response, 400, new { ok = false, message = "Request body is required." });
            return false;
        }
        if (request.ContentLength64 > m_maxRequestBodyBytes)
        {
            Write(response, 413, new { ok = false, message = "Request body is too large." });
            return false;
        }
        if (string.IsNullOrWhiteSpace(request.ContentType)
            || !request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            Write(response, 415, new { ok = false, message = "Content-Type must be application/json." });
            return false;
        }
        return true;
    }

    private byte[] ReadRequestBody(Stream input)
    {
        using MemoryStream output = new();
        byte[] buffer = new byte[4096];
        while (true)
        {
            int count = input.Read(buffer, 0, buffer.Length);
            if (count <= 0)
                break;
            if (output.Length + count > m_maxRequestBodyBytes)
                throw new RequestBodyTooLargeException();
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static bool TryUuid(string? value, out UUID id)
    {
        return UUID.TryParse((value ?? string.Empty).Trim(), out id) && id != UUID.Zero;
    }

    private static void Write<T>(IOSHttpResponse response, int statusCode, T payload)
    {
        response.StatusCode = statusCode;
        response.RawBuffer = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
    }

    private static string NormalizePath(string value)
    {
        string path = string.IsNullOrWhiteSpace(value)
            ? throw new Exception("[OpenSimMarketplace] Endpoint path is required.")
            : value.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;
        path = Util.TrimEndSlash(path);
        if (path == "/")
            throw new Exception("[OpenSimMarketplace] Endpoint path must not be HTTP root.");
        return path;
    }

    private sealed class RequestBodyTooLargeException : Exception { }
}
