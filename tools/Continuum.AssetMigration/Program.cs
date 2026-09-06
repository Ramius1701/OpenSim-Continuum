using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Nini.Config;
using OpenSim.Data;
using OpenSim.Framework;
using OpenSim.Server.Base;

internal static class Program
{
    private const int DefaultBatchSize = 250;

    private static int Main(string[] args)
    {
        if (args.Length < 1 || args.Length > 2)
        {
            Console.Error.WriteLine("Usage: Continuum.AssetMigration <migration.ini> [--verify-only]");
            return 2;
        }

        bool verifyOnly = args.Length == 2 && args[1].Equals("--verify-only", StringComparison.OrdinalIgnoreCase);
        if (args.Length == 2 && !verifyOnly)
        {
            Console.Error.WriteLine("Unknown option: " + args[1]);
            return 2;
        }

        try
        {
            IniConfigSource config = new IniConfigSource(args[0]);
            IAssetDataPlugin source = Load(config, "SourceAssetData");
            IAssetDataPlugin destination = Load(config, "DestinationAssetData");
            int batchSize = config.Configs["Migration"]?.GetInt("BatchSize", DefaultBatchSize)
                ?? DefaultBatchSize;
            if (batchSize < 1 || batchSize > 10000)
                throw new InvalidOperationException("Migration BatchSize must be between 1 and 10000");

            return Migrate(source, destination, batchSize, verifyOnly);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Asset migration failed: " + e.Message);
            return 1;
        }
    }

    private static IAssetDataPlugin Load(IConfigSource source, string sectionName)
    {
        IConfig section = source.Configs[sectionName]
            ?? throw new InvalidOperationException("Missing [" + sectionName + "] section");
        string provider = section.GetString("StorageProvider", string.Empty);
        string connection = section.GetString("ConnectionString", string.Empty);
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException(sectionName + " requires StorageProvider and ConnectionString");

        IAssetDataPlugin plugin = ServerUtils.LoadPlugin<IAssetDataPlugin>(provider, Array.Empty<object>())
            ?? throw new InvalidOperationException("Unable to load provider " + provider);
        plugin.Initialise(connection);
        return plugin;
    }

    private static int Migrate(IAssetDataPlugin source, IAssetDataPlugin destination,
        int batchSize, bool verifyOnly)
    {
        int offset = 0;
        int copied = 0;
        int matched = 0;
        int conflicts = 0;
        int failures = 0;

        while (true)
        {
            List<AssetMetadata> page = source.FetchAssetMetadataSet(offset, batchSize);
            if (page == null || page.Count == 0)
                break;

            foreach (AssetMetadata metadata in page)
            {
                AssetBase sourceAsset = source.GetAsset(metadata.FullID);
                if (sourceAsset == null)
                {
                    ++failures;
                    Console.Error.WriteLine("SOURCE_MISSING " + metadata.ID);
                    continue;
                }

                AssetBase existing = destination.GetAsset(metadata.FullID);
                if (existing != null)
                {
                    if (SameContent(sourceAsset, existing))
                        ++matched;
                    else
                    {
                        ++conflicts;
                        Console.Error.WriteLine("CONTENT_CONFLICT " + metadata.ID);
                    }
                    continue;
                }

                if (verifyOnly)
                {
                    ++failures;
                    Console.Error.WriteLine("DESTINATION_MISSING " + metadata.ID);
                }
                else if (destination.StoreAsset(sourceAsset))
                    ++copied;
                else
                {
                    ++failures;
                    Console.Error.WriteLine("COPY_FAILED " + metadata.ID);
                }
            }

            offset += page.Count;
            Console.WriteLine($"Progress: scanned={offset} copied={copied} matched={matched} conflicts={conflicts} failures={failures}");
            if (page.Count < batchSize)
                break;
        }

        Console.WriteLine($"Complete: scanned={offset} copied={copied} matched={matched} conflicts={conflicts} failures={failures}");
        return conflicts == 0 && failures == 0 ? 0 : 1;
    }

    private static bool SameContent(AssetBase first, AssetBase second)
    {
        if (first.Data == null || second.Data == null || first.Data.Length != second.Data.Length)
            return false;
        return SHA256.HashData(first.Data).AsSpan().SequenceEqual(SHA256.HashData(second.Data));
    }
}
