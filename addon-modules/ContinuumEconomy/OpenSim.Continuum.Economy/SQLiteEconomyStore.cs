using System;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace OpenSim.Continuum.Economy
{
    internal sealed class SQLiteEconomyStore
    {
        private readonly string m_connectionString;
        private static readonly ConcurrentDictionary<string, object> s_locks = new(StringComparer.Ordinal);
        private static int s_nativeResolverInitialized;
        internal object SyncRoot { get; }

        internal SQLiteEconomyStore(string connectionString)
        {
            InitializeNativeResolver();
            if (String.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("An economy database connection string is required", nameof(connectionString));
            SQLiteConnectionStringBuilder builder = new(connectionString)
            {
                ForeignKeys = true,
                JournalMode = SQLiteJournalModeEnum.Wal,
                SyncMode = SynchronizationModes.Full,
                DefaultTimeout = 30
            };
            m_connectionString = builder.ConnectionString;
            SyncRoot = s_locks.GetOrAdd(m_connectionString, _ => new object());
        }

        private static void InitializeNativeResolver()
        {
            if (Interlocked.Exchange(ref s_nativeResolverInitialized, 1) != 0)
                return;
            try
            {
                NativeLibrary.SetDllImportResolver(typeof(SQLiteConnection).Assembly, ResolveNativeSQLite);
            }
            catch (InvalidOperationException)
            {
                // A hosting executable may already have installed its own resolver.
            }
        }

        private static IntPtr ResolveNativeSQLite(string libraryName, Assembly assembly,
            DllImportSearchPath? searchPath)
        {
            if (!String.Equals(libraryName, "e_sqlite3", StringComparison.OrdinalIgnoreCase))
                return IntPtr.Zero;
            string file = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "e_sqlite3.dll" :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ?
                    (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ?
                        "libe_sqlite3_OSX_arm64.dylib" : "libe_sqlite3_OSX_x64.dylib") :
                    (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ?
                        "libe_sqlite3-arm64.so" : "libe_sqlite3.so");
            string path = Path.Combine(AppContext.BaseDirectory, "lib64", file);
            return File.Exists(path) ? NativeLibrary.Load(path) : IntPtr.Zero;
        }

        internal SQLiteConnection Open()
        {
            SQLiteConnection connection = new(m_connectionString);
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA busy_timeout=30000; PRAGMA foreign_keys=ON;";
            command.ExecuteNonQuery();
            return connection;
        }

        internal void EnsureSchema()
        {
            lock (SyncRoot)
            using (SQLiteConnection connection = Open())
                EconomySchemaResources.Apply(connection, EconomyStorageProvider.SQLite);
        }

        internal void ValidateSchema()
        {
            using SQLiteConnection connection = Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN " +
                "('continuum_economy_accounts','continuum_economy_transactions','continuum_economy_operations'," +
                "'continuum_economy_adjustments','continuum_economy_purchases','continuum_economy_account_registrations')";
            int count = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (count != 6)
                throw new InvalidOperationException($"ContinuumEconomy SQLite schema validation found {count} of 6 required tables");

            command.CommandText = "PRAGMA integrity_check";
            if (!String.Equals(Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture),
                "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ContinuumEconomy SQLite integrity check failed");
        }

        internal static void Add(SQLiteCommand command, string name, object value)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }
}
