#nullable enable annotations

/*
 * OpenSim Marketplace
 * Version 2.1.0
 *
 * Append-only delivery receipt ledger.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using log4net;

namespace OpenSim.Addons.Marketplace;

internal sealed class DeliveryLedger
{
    private readonly object m_sync = new();
    private readonly Dictionary<string, DeliveryReceipt> m_receipts = new(StringComparer.Ordinal);
    private readonly ILog m_log;
    private readonly string m_path;

    public DeliveryLedger(string path, ILog log)
    {
        m_path = Path.GetFullPath(path);
        m_log = log;
        string? directory = Path.GetDirectoryName(m_path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        Load();
    }

    public string PathName => m_path;

    public bool TryGet(string deliveryId, out DeliveryReceipt receipt)
    {
        lock (m_sync)
            return m_receipts.TryGetValue(deliveryId, out receipt!);
    }

    public bool TryRecord(DeliveryReceipt receipt, out string error)
    {
        lock (m_sync)
        {
            if (m_receipts.TryGetValue(receipt.DeliveryId, out DeliveryReceipt? existing))
            {
                if (existing.Matches(
                    receipt.SellerId,
                    receipt.SnapshotFolderId,
                    receipt.RecipientId,
                    receipt.SnapshotFingerprint))
                {
                    error = string.Empty;
                    return true;
                }

                error = "Delivery ID is already bound to different delivery data.";
                return false;
            }

            try
            {
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(receipt);
                using FileStream stream = new(
                    m_path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    4096,
                    FileOptions.WriteThrough);

                // A process or host failure can leave a partial final JSON
                // record. Preserve it for diagnosis, but never concatenate a
                // new receipt onto it and thereby lose both records on reload.
                if (stream.Length > 0)
                {
                    stream.Seek(-1, SeekOrigin.End);
                    if (stream.ReadByte() != (byte)'\n')
                        stream.WriteByte((byte)'\n');
                }
                stream.Seek(0, SeekOrigin.End);
                stream.Write(json, 0, json.Length);
                stream.WriteByte((byte)'\n');
                stream.Flush(true);
                m_receipts.Add(receipt.DeliveryId, receipt);
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[OPENSIM MARKETPLACE]: Delivery ledger write failed: {0}", ex);
                error = ex.Message;
                return false;
            }
        }
    }

    private void Load()
    {
        if (!File.Exists(m_path))
            return;

        int lineNumber = 0;
        foreach (string line in File.ReadLines(m_path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                DeliveryReceipt? receipt = JsonSerializer.Deserialize<DeliveryReceipt>(line);
                if (receipt == null || string.IsNullOrWhiteSpace(receipt.DeliveryId))
                    continue;
                if (m_receipts.TryGetValue(receipt.DeliveryId, out DeliveryReceipt? existing))
                {
                    if (!existing.Matches(
                        receipt.SellerId,
                        receipt.SnapshotFolderId,
                        receipt.RecipientId,
                        receipt.SnapshotFingerprint))
                    {
                        m_log.ErrorFormat(
                            "[OPENSIM MARKETPLACE]: Ignoring conflicting duplicate delivery ID {0} on ledger line {1}; the first valid receipt remains authoritative",
                            receipt.DeliveryId,
                            lineNumber);
                    }
                    continue;
                }

                m_receipts.Add(receipt.DeliveryId, receipt);
            }
            catch (Exception ex)
            {
                m_log.WarnFormat(
                    "[OPENSIM MARKETPLACE]: Ignoring invalid ledger line {0}: {1}",
                    lineNumber,
                    ex.Message);
            }
        }
    }
}
