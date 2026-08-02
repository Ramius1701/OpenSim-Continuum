using System;
using MySql.Data.MySqlClient;

namespace OpenSim.Continuum.Economy
{
    public enum EconomyStorageProvider { MySql, PostgreSql, SQLite }

    public sealed class EconomyBackend
    {
        public EconomyStorageProvider Provider { get; }
        public IEconomyLedger Ledger { get; }
        public IEconomyAccountService Accounts { get; }
        public IEconomyPurchaseService Purchases { get; }

        internal EconomyBackend(EconomyStorageProvider provider, IEconomyLedger ledger,
            IEconomyAccountService accounts, IEconomyPurchaseService purchases)
        {
            Provider = provider;
            Ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            Accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            Purchases = purchases ?? throw new ArgumentNullException(nameof(purchases));
        }
    }

    public static class EconomyProviderFactory
    {
        public static EconomyStorageProvider Parse(string value)
        {
            string normalized = (value ?? String.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "mysql" or "mariadb" or "opensim.data.mysql.dll" => EconomyStorageProvider.MySql,
                "pgsql" or "postgres" or "postgresql" or "opensim.data.pgsql.dll" => EconomyStorageProvider.PostgreSql,
                "sqlite" or "opensim.data.sqlite.dll" => EconomyStorageProvider.SQLite,
                _ => throw new ArgumentException(
                    "StorageProvider must be MySQL/MariaDB, PostgreSQL, or SQLite", nameof(value))
            };
        }

        public static EconomyBackend Create(string providerName, string connectionString)
        {
            if (String.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("An economy database connection string is required", nameof(connectionString));

            EconomyStorageProvider provider = Parse(providerName);
            return provider switch
            {
                EconomyStorageProvider.MySql => new EconomyBackend(provider,
                    new MySqlEconomyLedger(connectionString),
                    new MySqlEconomyAccountService(connectionString),
                    new MySqlEconomyPurchaseService(connectionString)),
                EconomyStorageProvider.PostgreSql => throw MissingProvider(provider),
                EconomyStorageProvider.SQLite => CreateSQLite(provider, connectionString),
                _ => throw new ArgumentOutOfRangeException(nameof(provider))
            };
        }

        private static EconomyBackend CreateSQLite(EconomyStorageProvider provider,string connectionString)
        {
            SQLiteEconomyStore store=new(connectionString);
            return new EconomyBackend(provider,new SQLiteEconomyLedger(store),
                new SQLiteEconomyAccountService(store),new SQLiteEconomyPurchaseService(store));
        }

        public static bool IsDedicatedTestDatabase(string providerName, string connectionString)
        {
            if (Parse(providerName) != EconomyStorageProvider.MySql)
                return false;
            MySqlConnectionStringBuilder builder = new(connectionString);
            return !String.IsNullOrWhiteSpace(builder.Database) &&
                builder.Database.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static NotSupportedException MissingProvider(EconomyStorageProvider provider)
        {
            return new NotSupportedException(
                $"ContinuumEconomy {provider} storage is not implemented or certified. " +
                "The service will not fall back to another database provider.");
        }
    }
}
