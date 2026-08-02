using System;
using System.Data;
using System.Data.SQLite;
using System.Globalization;

namespace OpenSim.Continuum.Economy
{
    internal sealed class SQLiteEconomyStore
    {
        private readonly string m_connectionString;
        internal object SyncRoot { get; } = new();

        internal SQLiteEconomyStore(string connectionString)
        {
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
