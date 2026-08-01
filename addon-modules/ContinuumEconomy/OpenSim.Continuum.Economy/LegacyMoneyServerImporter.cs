using System;
using System.Data;
using System.Globalization;
using MySql.Data.MySqlClient;

namespace OpenSim.Continuum.Economy
{
    public sealed class LegacyImportReport
    {
        public long LegacyAccountCount { get; set; }
        public decimal LegacyBalanceTotal { get; set; }
        public long LegacyTransactionCount { get; set; }
        public long InvalidAccountCount { get; set; }
        public long TargetAccountCount { get; set; }
        public long ReconciliationMismatchCount { get; set; }
        public Guid ImportID { get; set; }
        public bool Imported { get; set; }
    }

    /// <summary>
    /// Imports a stopped DTL/NSL MoneyServer database into independent
    /// ContinuumEconomy tables. Legacy tables are only read.
    /// </summary>
    public sealed class LegacyMoneyServerImporter
    {
        private readonly string m_connectionString;

        public LegacyMoneyServerImporter(string connectionString)
        {
            if (String.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("An economy database connection string is required", nameof(connectionString));
            m_connectionString = connectionString;
        }

        public LegacyImportReport Analyze()
        {
            using MySqlConnection connection = new(m_connectionString);
            connection.Open();
            RequireTable(connection, "balances");
            RequireTable(connection, "transactions");
            return Analyze(connection, null);
        }

        public LegacyImportReport Import()
        {
            using MySqlConnection connection = new(m_connectionString);
            connection.Open();
            RequireTable(connection, "balances");
            RequireTable(connection, "transactions");
            new MySqlEconomyLedger(m_connectionString).EnsureSchema();
            EnsureImportSchema(connection);

            using MySqlTransaction transaction = connection.BeginTransaction(IsolationLevel.RepeatableRead);
            LegacyImportReport report = Analyze(connection, transaction);
            if (report.InvalidAccountCount != 0)
                throw new InvalidOperationException("Legacy balances contain invalid UUIDs or negative balances; import refused");
            if (report.TargetAccountCount != 0)
                throw new InvalidOperationException("ContinuumEconomy accounts are not empty; import refused");

            report.ImportID = Guid.NewGuid();
            using (MySqlCommand command = CreateCommand(connection, transaction, @"
                INSERT INTO continuum_economy_accounts (account_id, balance, account_type)
                SELECT LOWER(user), CAST(balance AS SIGNED), CAST(type AS UNSIGNED)
                FROM balances"))
            {
                if (command.ExecuteNonQuery() != report.LegacyAccountCount)
                    throw new InvalidOperationException("Not every legacy balance was copied");
            }

            using (MySqlCommand command = CreateCommand(connection, transaction, @"
                INSERT INTO continuum_economy_legacy_transactions
                (legacy_transaction_id, import_id, sender_id, receiver_id, amount,
                 sender_balance, receiver_balance, object_id, object_name, region_handle,
                 region_id, transaction_type, created_unix, legacy_status, common_name, description)
                SELECT UUID, ?import, sender, receiver, amount, senderBalance, receiverBalance,
                       COALESCE(objectUUID, ''), COALESCE(objectName, ''), regionHandle,
                       regionUUID, type, time, status, commonName, COALESCE(description, '')
                FROM transactions"))
            {
                command.Parameters.AddWithValue("?import", report.ImportID.ToString());
                if (command.ExecuteNonQuery() != report.LegacyTransactionCount)
                    throw new InvalidOperationException("Not every legacy transaction-history row was archived");
            }

            report.ReconciliationMismatchCount = ScalarLong(connection, transaction, @"
                SELECT COUNT(*) FROM balances b
                LEFT JOIN continuum_economy_accounts a ON a.account_id = LOWER(b.user)
                WHERE a.account_id IS NULL OR a.balance <> CAST(b.balance AS SIGNED)");
            if (report.ReconciliationMismatchCount != 0)
                throw new InvalidOperationException("Imported balances do not reconcile with the legacy snapshot");

            using (MySqlCommand command = CreateCommand(connection, transaction, @"
                INSERT INTO continuum_economy_imports
                (import_id, source_name, account_count, balance_total, transaction_count, completed_utc)
                VALUES (?import, 'DTL/NSL MoneyServer', ?accounts, ?total, ?transactions, CURRENT_TIMESTAMP(6))"))
            {
                command.Parameters.AddWithValue("?import", report.ImportID.ToString());
                command.Parameters.AddWithValue("?accounts", report.LegacyAccountCount);
                command.Parameters.AddWithValue("?total", report.LegacyBalanceTotal);
                command.Parameters.AddWithValue("?transactions", report.LegacyTransactionCount);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
            report.Imported = true;
            report.TargetAccountCount = report.LegacyAccountCount;
            return report;
        }

        private static LegacyImportReport Analyze(MySqlConnection connection, MySqlTransaction transaction)
        {
            return new LegacyImportReport
            {
                LegacyAccountCount = ScalarLong(connection, transaction, "SELECT COUNT(*) FROM balances"),
                LegacyBalanceTotal = ScalarDecimal(connection, transaction, "SELECT COALESCE(SUM(balance), 0) FROM balances"),
                LegacyTransactionCount = ScalarLong(connection, transaction, "SELECT COUNT(*) FROM transactions"),
                InvalidAccountCount = ScalarLong(connection, transaction, @"
                    SELECT COUNT(*) FROM balances
                    WHERE user NOT REGEXP '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$'
                       OR balance < 0"),
                TargetAccountCount = TableExists(connection, transaction, "continuum_economy_accounts")
                    ? ScalarLong(connection, transaction, "SELECT COUNT(*) FROM continuum_economy_accounts") : 0
            };
        }

        private static void EnsureImportSchema(MySqlConnection connection)
        {
            using MySqlCommand command = connection.CreateCommand();
            command.CommandText = ImportSchemaSql;
            command.ExecuteNonQuery();
        }

        private static void RequireTable(MySqlConnection connection, string table)
        {
            if (!TableExists(connection, null, table))
                throw new InvalidOperationException("Required legacy MoneyServer table is missing: " + table);
        }

        private static bool TableExists(MySqlConnection connection, MySqlTransaction transaction, string table)
        {
            using MySqlCommand command = CreateCommand(connection, transaction,
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = ?table");
            command.Parameters.AddWithValue("?table", table);
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
        }

        private static long ScalarLong(MySqlConnection connection, MySqlTransaction transaction, string sql)
        {
            using MySqlCommand command = CreateCommand(connection, transaction, sql);
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private static decimal ScalarDecimal(MySqlConnection connection, MySqlTransaction transaction, string sql)
        {
            using MySqlCommand command = CreateCommand(connection, transaction, sql);
            return Convert.ToDecimal(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private static MySqlCommand CreateCommand(MySqlConnection connection, MySqlTransaction transaction, string sql)
        {
            MySqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            return command;
        }

        private const string ImportSchemaSql = @"
CREATE TABLE IF NOT EXISTS continuum_economy_imports (
 import_id CHAR(36) NOT NULL, source_name VARCHAR(64) NOT NULL,
 account_count BIGINT NOT NULL, balance_total DECIMAL(65,0) NOT NULL,
 transaction_count BIGINT NOT NULL, completed_utc TIMESTAMP(6) NOT NULL,
 PRIMARY KEY (import_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS continuum_economy_legacy_transactions (
 legacy_transaction_id VARCHAR(36) NOT NULL, import_id CHAR(36) NOT NULL,
 sender_id VARCHAR(64) NOT NULL, receiver_id VARCHAR(64) NOT NULL, amount BIGINT NOT NULL,
 sender_balance BIGINT NOT NULL, receiver_balance BIGINT NOT NULL,
 object_id VARCHAR(64) NOT NULL, object_name VARCHAR(255) NOT NULL,
 region_handle VARCHAR(36) NOT NULL, region_id VARCHAR(36) NOT NULL,
 transaction_type INT NOT NULL, created_unix BIGINT NOT NULL, legacy_status INT NOT NULL,
 common_name VARCHAR(128) NOT NULL, description VARCHAR(255) NOT NULL,
 PRIMARY KEY (legacy_transaction_id), KEY idx_ce_legacy_sender (sender_id),
 KEY idx_ce_legacy_receiver (receiver_id), KEY idx_ce_legacy_time (created_unix)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
    }
}
