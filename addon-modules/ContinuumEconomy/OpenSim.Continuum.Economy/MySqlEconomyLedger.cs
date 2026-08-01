using System;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MySql.Data.MySqlClient;

namespace OpenSim.Continuum.Economy
{
    // Independently implemented from DTL/NSL protocol and WhiteCore behavior references.
    public sealed class MySqlEconomyLedger : IEconomyLedger
    {
        private readonly string m_connectionString;

        public MySqlEconomyLedger(string connectionString)
        {
            if (String.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("An economy database connection string is required", nameof(connectionString));
            m_connectionString = connectionString;
        }

        public void EnsureSchema()
        {
            using MySqlConnection connection = new(m_connectionString);
            connection.Open();
            using MySqlCommand command = connection.CreateCommand();
            command.CommandText = SchemaSql;
            command.ExecuteNonQuery();
        }

        public long GetBalance(Guid accountID)
        {
            if (accountID == Guid.Empty)
                throw new ArgumentException("A non-zero account ID is required", nameof(accountID));
            using MySqlConnection connection = new(m_connectionString);
            connection.Open();
            using MySqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT balance FROM continuum_economy_accounts WHERE account_id = ?account";
            command.Parameters.AddWithValue("?account", accountID.ToString());
            object value = command.ExecuteScalar();
            return value == null || value == DBNull.Value ? 0L : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        public LedgerTransferResult Transfer(LedgerTransferRequest request)
        {
            LedgerTransferResult invalid = Validate(request);
            if (invalid != null)
                return invalid;
            string fingerprint = Fingerprint(request);

            try
            {
                using MySqlConnection connection = new(m_connectionString);
                connection.Open();
                using MySqlTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
                LedgerTransferResult prior = ReadPrior(connection, transaction, request.TransactionID, fingerprint);
                if (prior != null)
                {
                    transaction.Commit();
                    return prior;
                }

                EnsureAccount(connection, transaction, request.SenderID);
                EnsureAccount(connection, transaction, request.ReceiverID);
                Guid first = StringComparer.Ordinal.Compare(request.SenderID.ToString(), request.ReceiverID.ToString()) < 0
                    ? request.SenderID : request.ReceiverID;
                Guid second = first == request.SenderID ? request.ReceiverID : request.SenderID;
                long firstBalance = LockBalance(connection, transaction, first);
                long secondBalance = LockBalance(connection, transaction, second);
                long senderBalance = first == request.SenderID ? firstBalance : secondBalance;
                long receiverBalance = first == request.ReceiverID ? firstBalance : secondBalance;

                if (senderBalance < request.Amount)
                {
                    InsertTransaction(connection, transaction, request, fingerprint, 2, senderBalance, receiverBalance, "Insufficient funds");
                    transaction.Commit();
                    return Result(request.TransactionID, LedgerResultCode.InsufficientFunds, senderBalance, receiverBalance, "Insufficient funds");
                }

                long updatedSender;
                long updatedReceiver;
                try
                {
                    updatedSender = checked(senderBalance - request.Amount);
                    updatedReceiver = checked(receiverBalance + request.Amount);
                }
                catch (OverflowException)
                {
                    transaction.Rollback();
                    return Result(request.TransactionID, LedgerResultCode.InvalidRequest, senderBalance, receiverBalance,
                        "The transfer exceeds the supported balance range");
                }

                UpdateBalance(connection, transaction, request.SenderID, updatedSender);
                UpdateBalance(connection, transaction, request.ReceiverID, updatedReceiver);
                InsertTransaction(connection, transaction, request, fingerprint, 1, updatedSender, updatedReceiver, String.Empty);
                transaction.Commit();
                return Result(request.TransactionID, LedgerResultCode.Committed, updatedSender, updatedReceiver, "Transfer committed");
            }
            catch (MySqlException e) when (e.Number == 1062)
            {
                using MySqlConnection connection = new(m_connectionString);
                connection.Open();
                return ReadPrior(connection, null, request.TransactionID, fingerprint) ??
                    Result(request.TransactionID, LedgerResultCode.TransactionConflict, 0, 0,
                        "The transaction ID was committed concurrently with different data");
            }
        }

        private static LedgerTransferResult Validate(LedgerTransferRequest request)
        {
            if (request == null)
                return Result(Guid.Empty, LedgerResultCode.InvalidRequest, 0, 0, "A transfer request is required");
            if (request.TransactionID == Guid.Empty || request.SenderID == Guid.Empty || request.ReceiverID == Guid.Empty)
                return Result(request.TransactionID, LedgerResultCode.InvalidRequest, 0, 0,
                    "Transaction, sender and receiver IDs must be non-zero");
            if (request.SenderID == request.ReceiverID || request.Amount <= 0)
                return Result(request.TransactionID, LedgerResultCode.InvalidRequest, 0, 0,
                    "A positive transfer between different accounts is required");
            if ((request.Description ?? String.Empty).Length > 255)
                return Result(request.TransactionID, LedgerResultCode.InvalidRequest, 0, 0,
                    "The transaction description exceeds 255 characters");
            return null;
        }

        private static void EnsureAccount(MySqlConnection connection, MySqlTransaction transaction, Guid accountID)
        {
            using MySqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT IGNORE INTO continuum_economy_accounts (account_id, balance) VALUES (?account, 0)";
            command.Parameters.AddWithValue("?account", accountID.ToString());
            command.ExecuteNonQuery();
        }

        private static long LockBalance(MySqlConnection connection, MySqlTransaction transaction, Guid accountID)
        {
            using MySqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT balance FROM continuum_economy_accounts WHERE account_id = ?account FOR UPDATE";
            command.Parameters.AddWithValue("?account", accountID.ToString());
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private static void UpdateBalance(MySqlConnection connection, MySqlTransaction transaction, Guid accountID, long balance)
        {
            using MySqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE continuum_economy_accounts SET balance = ?balance, updated_utc = CURRENT_TIMESTAMP(6) WHERE account_id = ?account";
            command.Parameters.AddWithValue("?balance", balance);
            command.Parameters.AddWithValue("?account", accountID.ToString());
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("The locked economy account could not be updated");
        }

        private static void InsertTransaction(MySqlConnection connection, MySqlTransaction transaction,
            LedgerTransferRequest request, string fingerprint, int status, long senderBalance, long receiverBalance, string failureReason)
        {
            using MySqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"INSERT INTO continuum_economy_transactions
                (transaction_id, request_hash, sender_id, receiver_id, amount, transaction_type, region_id,
                 object_id, description, status, sender_balance, receiver_balance, failure_reason)
                VALUES (?transaction, ?hash, ?sender, ?receiver, ?amount, ?type, ?region,
                        ?object, ?description, ?status, ?senderBalance, ?receiverBalance, ?failure)";
            command.Parameters.AddWithValue("?transaction", request.TransactionID.ToString());
            command.Parameters.AddWithValue("?hash", fingerprint);
            command.Parameters.AddWithValue("?sender", request.SenderID.ToString());
            command.Parameters.AddWithValue("?receiver", request.ReceiverID.ToString());
            command.Parameters.AddWithValue("?amount", request.Amount);
            command.Parameters.AddWithValue("?type", request.TransactionType);
            command.Parameters.AddWithValue("?region", request.RegionID.ToString());
            command.Parameters.AddWithValue("?object", request.ObjectID.ToString());
            command.Parameters.AddWithValue("?description", request.Description ?? String.Empty);
            command.Parameters.AddWithValue("?status", status);
            command.Parameters.AddWithValue("?senderBalance", senderBalance);
            command.Parameters.AddWithValue("?receiverBalance", receiverBalance);
            command.Parameters.AddWithValue("?failure", failureReason ?? String.Empty);
            command.ExecuteNonQuery();
        }

        private static LedgerTransferResult ReadPrior(MySqlConnection connection, MySqlTransaction transaction,
            Guid transactionID, string fingerprint)
        {
            using MySqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"SELECT request_hash, status, sender_balance, receiver_balance, failure_reason
                FROM continuum_economy_transactions WHERE transaction_id = ?transaction";
            command.Parameters.AddWithValue("?transaction", transactionID.ToString());
            using MySqlDataReader reader = command.ExecuteReader();
            if (!reader.Read())
                return null;
            if (!String.Equals(reader.GetString(0), fingerprint, StringComparison.Ordinal))
                return Result(transactionID, LedgerResultCode.TransactionConflict, 0, 0,
                    "The transaction ID is already associated with a different request");
            int status = reader.GetInt32(1);
            return Result(transactionID, status == 1 ? LedgerResultCode.Replayed : LedgerResultCode.InsufficientFunds,
                reader.GetInt64(2), reader.GetInt64(3), status == 1 ? "Transfer already committed" : reader.GetString(4));
        }

        private static string Fingerprint(LedgerTransferRequest request)
        {
            string canonical = String.Join("|", request.SenderID, request.ReceiverID,
                request.Amount.ToString(CultureInfo.InvariantCulture), request.TransactionType.ToString(CultureInfo.InvariantCulture),
                request.RegionID, request.ObjectID, request.Description ?? String.Empty);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        }

        private static LedgerTransferResult Result(Guid transactionID, LedgerResultCode code,
            long senderBalance, long receiverBalance, string message)
        {
            return new LedgerTransferResult
            {
                TransactionID = transactionID, Code = code, SenderBalance = senderBalance,
                ReceiverBalance = receiverBalance, Message = message
            };
        }

        private const string SchemaSql = @"
CREATE TABLE IF NOT EXISTS continuum_economy_accounts (
 account_id CHAR(36) NOT NULL, balance BIGINT NOT NULL DEFAULT 0,
 account_type TINYINT UNSIGNED NOT NULL DEFAULT 0,
 created_utc TIMESTAMP(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 updated_utc TIMESTAMP(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6), PRIMARY KEY (account_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS continuum_economy_transactions (
 transaction_id CHAR(36) NOT NULL, request_hash CHAR(64) NOT NULL,
 sender_id CHAR(36) NOT NULL, receiver_id CHAR(36) NOT NULL, amount BIGINT NOT NULL,
 transaction_type INT NOT NULL, region_id CHAR(36) NOT NULL, object_id CHAR(36) NOT NULL,
 description VARCHAR(255) NOT NULL DEFAULT '', status TINYINT UNSIGNED NOT NULL,
 sender_balance BIGINT NOT NULL, receiver_balance BIGINT NOT NULL,
 failure_reason VARCHAR(255) NOT NULL DEFAULT '',
 created_utc TIMESTAMP(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6), PRIMARY KEY (transaction_id),
 KEY idx_continuum_economy_sender_time (sender_id, created_utc),
 KEY idx_continuum_economy_receiver_time (receiver_id, created_utc),
 KEY idx_continuum_economy_type_time (transaction_type, created_utc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
    }
}
