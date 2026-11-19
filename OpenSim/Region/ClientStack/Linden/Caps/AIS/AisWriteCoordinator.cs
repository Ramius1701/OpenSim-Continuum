using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace OpenSim.Region.ClientStack.LindenCaps.AIS;

// Process-local coordination shared by resident and library capabilities.
// Lock stripes bound memory and serialize writes even across capability renewal.
internal static class AisWriteCoordinator
{
    internal delegate int Mutation(out OSDMap result);
    private sealed record Reply(string Signature, long Expires, int Status, byte[] Body);
    private sealed class Stripe
    {
        public readonly Dictionary<(UUID Resident, UUID Transaction), Reply> Replies = new();
        public int Bytes;
    }

    private static readonly Stripe[] Stripes = Array.ConvertAll(new int[64], _ => new Stripe());

    internal static int Run(UUID resident, string method, string url, string destination,
        byte[] payload, Mutation mutation, out OSDMap result, UUID fallbackTransaction = default)
    {
        result = null;
        if (resident == UUID.Zero || url is null || url.Length > 1024 || destination?.Length > 64)
            return 400;
        string[] parts = url.Split('?', 2);
        UUID transaction = UUID.Zero;
        if (parts.Length == 2)
        {
            bool seen = false;
            foreach (string parameter in parts[1].Split('&', ','))
            {
                if (!parameter.StartsWith("tid=", StringComparison.Ordinal))
                    continue;
                if (seen || !UUID.TryParse(parameter[4..], out transaction) || transaction == UUID.Zero)
                    return 400;
                seen = true;
            }
        }

        if (transaction == UUID.Zero)
            transaction = fallbackTransaction;

        Stripe stripe = Stripes[resident.GetHashCode() & 63];
        lock (stripe)
        {
            if (transaction == UUID.Zero)
                return mutation(out result);

            long now = Stopwatch.GetTimestamp();
            foreach (var pair in stripe.Replies)
            {
                if (pair.Value.Expires <= now)
                {
                    stripe.Bytes -= pair.Value.Body?.Length ?? 0;
                    stripe.Replies.Remove(pair.Key);
                }
            }

            var key = (resident, transaction);
            string signature = method + "\n" + url + "\n" + destination + "\n"
                + (payload is null ? "" : Convert.ToHexString(SHA256.HashData(payload)));
            if (stripe.Replies.TryGetValue(key, out Reply prior))
            {
                if (!string.Equals(prior.Signature, signature, StringComparison.Ordinal))
                    return 409;
                if (prior.Body is not null)
                    result = OSDParser.DeserializeLLSDXml(prior.Body) as OSDMap;
                return prior.Status;
            }

            if (stripe.Replies.Count >= 128 || stripe.Bytes >= 1024 * 1024)
                return 503;

            int status;
            byte[] response = null;
            try
            {
                status = mutation(out result);
                if (result is not null)
                    response = OSDParser.SerializeLLSDXmlBytes(result);
            }
            catch
            {
                // The provider may have written part of a request before throwing.
                // Record an ambiguous failure so a retry cannot repeat the mutation.
                status = 500;
                result = null;
            }
            // A known unfinished upload has made no inventory mutation and must
            // be retryable once the native uploader finishes.
            if (status == 503 && result is null)
                return status;

            if (response?.Length > 1024 * 1024 - stripe.Bytes)
            {
                // Keep a retry tombstone even if the original reply won't fit.
                status = 409;
                response = null;
                result = null;
            }
            stripe.Bytes += response?.Length ?? 0;
            stripe.Replies.Add(key, new Reply(signature,
                Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency, status, response));
            return status;
        }
    }
}
