using System;
using System.Collections.Generic;
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

        public bool AccountExists(Guid accountID)
        {
            if (accountID == Guid.Empty)
                throw new ArgumentException("A non-zero account ID is required", nameof(accountID));
            using MySqlConnection connection = new(m_connectionString);
            connection.Open();
            using MySqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM continuum_economy_accounts WHERE account_id = ?account LIMIT 1";
            command.Parameters.AddWithValue("?account", accountID.ToString());
            return command.ExecuteScalar() != null;
        }

        public void EnsureAccount(Guid accountID)
        {
            if (accountID == Guid.Empty)
                throw new ArgumentException("A non-zero account ID is required", nameof(accountID));
            using MySqlConnection connection = new(m_connectionString);
            connection.Open();
            using MySqlCommand command = connection.CreateCommand();
            command.CommandText = "INSERT IGNORE INTO continuum_economy_accounts (account_id, balance) VALUES (?account, 0)";
            command.Parameters.AddWithValue("?account", accountID.ToString());
            command.ExecuteNonQuery();
        }

        public long GetAvailableBalance(Guid accountID)
        {
            if (accountID == Guid.Empty)
                throw new ArgumentException("A non-zero account ID is required", nameof(accountID));
            using MySqlConnection connection = new(m_connectionString);
            connection.Open();
            using MySqlCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT a.balance - COALESCE(SUM(CASE WHEN p.state = 1 THEN p.amount ELSE 0 END), 0)
                FROM continuum_economy_accounts a
                LEFT JOIN continuum_economy_purchases p ON p.buyer_id = a.account_id
                WHERE a.account_id = ?account GROUP BY a.balance";
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

                ReserveOperation(connection, transaction, request.TransactionID, fingerprint, 1);

                EnsureAccount(connection, transaction, request.SenderID);
                EnsureAccount(connection, transaction, request.ReceiverID);
                Guid first = StringComparer.Ordinal.Compare(request.SenderID.ToString(), request.ReceiverID.ToString()) < 0
                    ? request.SenderID : request.ReceiverID;
                Guid second = first == request.SenderID ? request.ReceiverID : request.SenderID;
                long firstBalance = LockBalance(connection, transaction, first);
                long secondBalance = LockBalance(connection, transaction, second);
                long senderBalance = first == request.SenderID ? firstBalance : secondBalance;
                long receiverBalance = first == request.ReceiverID ? firstBalance : secondBalance;
                long senderAvailable = checked(senderBalance - GetHeldAmount(connection, transaction, request.SenderID));

                if (senderAvailable < request.Amount)
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

        public LedgerAdjustmentResult Adjust(LedgerAdjustmentRequest request)
        {
            LedgerAdjustmentResult invalid = ValidateAdjustment(request);
            if (invalid != null)
                return invalid;
            string fingerprint = AdjustmentFingerprint(request);

            try
            {
                using MySqlConnection connection = new(m_connectionString);
                connection.Open();
                using MySqlTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
                LedgerAdjustmentResult prior = ReadPriorAdjustment(connection, transaction, request.OperationID, fingerprint);
                if (prior != null)
                {
                    transaction.Commit();
                    return prior;
                }
                if (TransferExists(connection, transaction, request.OperationID))
                {
                    transaction.Rollback();
                    return AdjustmentResult(request.OperationID, LedgerResultCode.TransactionConflict, 0,
                        "The operation ID is already associated with a transfer");
                }

                ReserveOperation(connection, transaction, request.OperationID, fingerprint, 2);
                EnsureAccount(connection, transaction, request.AccountID);
                long balance = LockBalance(connection, transaction, request.AccountID);
                long available = checked(balance - GetHeldAmount(connection, transaction, request.AccountID));
                if (request.Kind == LedgerAdjustmentKind.Debit && available < request.Amount)
                {
                    InsertAdjustment(connection, transaction, request, fingerprint, 2, balance, "Insufficient funds");
                    transaction.Commit();
                    return AdjustmentResult(request.OperationID, LedgerResultCode.InsufficientFunds, balance, "Insufficient funds");
                }

                string policyFailure = request.Kind == LedgerAdjustmentKind.Credit
                    ? CheckCreditPolicy(connection, transaction, request, balance) : null;
                if (policyFailure != null)
                {
                    InsertAdjustment(connection, transaction, request, fingerprint, 3, balance, policyFailure);
                    transaction.Commit();
                    return AdjustmentResult(request.OperationID, LedgerResultCode.InvalidRequest, balance, policyFailure);
                }

                long signedAmount = request.Kind == LedgerAdjustmentKind.Credit ? request.Amount : -request.Amount;
                long updatedBalance;
                try
                {
                    updatedBalance = checked(balance + signedAmount);
                }
                catch (OverflowException)
                {
                    transaction.Rollback();
                    return AdjustmentResult(request.OperationID, LedgerResultCode.InvalidRequest, balance,
                        "The adjustment exceeds the supported balance range");
                }

                UpdateBalance(connection, transaction, request.AccountID, updatedBalance);
                InsertAdjustment(connection, transaction, request, fingerprint, 1, updatedBalance, String.Empty);
                transaction.Commit();
                return AdjustmentResult(request.OperationID, LedgerResultCode.Committed, updatedBalance, "Adjustment committed");
            }
            catch (MySqlException e) when (e.Number == 1062)
            {
                using MySqlConnection connection = new(m_connectionString);
                connection.Open();
                return ReadPriorAdjustment(connection, null, request.OperationID, fingerprint) ??
                    AdjustmentResult(request.OperationID, LedgerResultCode.TransactionConflict, 0,
                        "The operation ID was committed concurrently with different data");
            }
        }

        public IReadOnlyList<LedgerHistoryEntry> GetHistory(Guid accountID, DateTime? beforeUtc, int limit)
        {
            if (accountID == Guid.Empty)
                throw new ArgumentException("A non-zero account ID is required", nameof(accountID));
            if (limit < 1 || limit > 500)
                throw new ArgumentOutOfRangeException(nameof(limit), "History limit must be between 1 and 500");

            using MySqlConnection connection = new(m_connectionString);
            connection.Open();
            using MySqlCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT transaction_id, sender_id, receiver_id, amount, transaction_type,
                    region_id, object_id, description, status, sender_balance, receiver_balance,
                    failure_reason, created_utc
                FROM continuum_economy_transactions
                WHERE (sender_id = ?account OR receiver_id = ?account)
                  AND (?before IS NULL OR created_utc < ?before)
                ORDER BY created_utc DESC, transaction_id DESC
                LIMIT ?limit";
            command.Parameters.AddWithValue("?account", accountID.ToString());
            command.Parameters.AddWithValue("?before", beforeUtc.HasValue ? beforeUtc.Value.ToUniversalTime() : DBNull.Value);
            command.Parameters.AddWithValue("?limit", limit);

            List<LedgerHistoryEntry> entries = new(limit * 2);
            using MySqlDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                Guid senderID = Guid.Parse(reader.GetString(1));
                Guid receiverID = Guid.Parse(reader.GetString(2));
                bool isCredit = receiverID == accountID;
                entries.Add(new LedgerHistoryEntry
                {
                    TransactionID = Guid.Parse(reader.GetString(0)),
                    AccountID = accountID,
                    CounterpartyID = isCredit ? senderID : receiverID,
                    ActorID = Guid.Empty,
                    Amount = reader.GetInt64(3),
                    TransactionType = reader.GetInt32(4),
                    RegionID = Guid.Parse(reader.GetString(5)),
                    ObjectID = Guid.Parse(reader.GetString(6)),
                    Description = reader.GetString(7),
                    Succeeded = reader.GetInt32(8) == 1,
                    ResultingBalance = isCredit ? reader.GetInt64(10) : reader.GetInt64(9),
                    FailureReason = reader.GetString(11),
                    CreatedUtc = DateTime.SpecifyKind(reader.GetDateTime(12), DateTimeKind.Utc),
                    IsCredit = isCredit,
                    IsAdjustment = false
                });
            }
            reader.Close();

            using MySqlCommand adjustmentCommand = connection.CreateCommand();
            adjustmentCommand.CommandText = @"SELECT operation_id, actor_id, amount, adjustment_kind,
                    transaction_type, reason, status, resulting_balance, failure_reason, created_utc
                FROM continuum_economy_adjustments
                WHERE account_id = ?account AND (?before IS NULL OR created_utc < ?before)
                ORDER BY created_utc DESC, operation_id DESC
                LIMIT ?limit";
            adjustmentCommand.Parameters.AddWithValue("?account", accountID.ToString());
            adjustmentCommand.Parameters.AddWithValue("?before", beforeUtc.HasValue ? beforeUtc.Value.ToUniversalTime() : DBNull.Value);
            adjustmentCommand.Parameters.AddWithValue("?limit", limit);
            using MySqlDataReader adjustmentReader = adjustmentCommand.ExecuteReader();
            while (adjustmentReader.Read())
            {
                bool isCredit = adjustmentReader.GetInt32(3) == (int)LedgerAdjustmentKind.Credit;
                Guid actorID = Guid.Parse(adjustmentReader.GetString(1));
                entries.Add(new LedgerHistoryEntry
                {
                    TransactionID = Guid.Parse(adjustmentReader.GetString(0)),
                    AccountID = accountID,
                    CounterpartyID = actorID,
                    ActorID = actorID,
                    Amount = adjustmentReader.GetInt64(2),
                    TransactionType = adjustmentReader.GetInt32(4),
                    RegionID = Guid.Empty,
                    ObjectID = Guid.Empty,
                    Description = adjustmentReader.GetString(5),
                    Succeeded = adjustmentReader.GetInt32(6) == 1,
                    ResultingBalance = adjustmentReader.GetInt64(7),
                    FailureReason = adjustmentReader.GetString(8),
                    CreatedUtc = DateTime.SpecifyKind(adjustmentReader.GetDateTime(9), DateTimeKind.Utc),
                    IsCredit = isCredit,
                    IsAdjustment = true
                });
            }
            entries.Sort((left, right) =>
            {
                int timeComparison = right.CreatedUtc.CompareTo(left.CreatedUtc);
                return timeComparison != 0 ? timeComparison : right.TransactionID.CompareTo(left.TransactionID);
            });
            if (entries.Count > limit)
                entries.RemoveRange(limit, entries.Count - limit);
            return entries;
        }

        public long GetCreditedTotal(Guid accountID, int transactionType, DateTime sinceUtc)
        {
            if (accountID == Guid.Empty)
                throw new ArgumentException("A non-zero account ID is required", nameof(accountID));
            using MySqlConnection connection = new(m_connectionString);
            connection.Open();
            using MySqlCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT COALESCE(SUM(amount), 0) FROM continuum_economy_adjustments
                WHERE account_id = ?account AND transaction_type = ?type
                  AND adjustment_kind = 1 AND status = 1 AND created_utc >= ?since";
            command.Parameters.AddWithValue("?account", accountID.ToString());
            command.Parameters.AddWithValue("?type", transactionType);
            command.Parameters.AddWithValue("?since", sinceUtc.ToUniversalTime());
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        public long CountHistory(Guid accountID, DateTime? startUtc, DateTime? endUtc)
        {
            if (accountID == Guid.Empty)
                throw new ArgumentException("A non-zero account ID is required", nameof(accountID));
            using MySqlConnection connection = new(m_connectionString);
            connection.Open();
            using MySqlCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT
                (SELECT COUNT(*) FROM continuum_economy_transactions
                 WHERE (sender_id=?account OR receiver_id=?account)
                   AND (?start IS NULL OR created_utc>=?start) AND (?end IS NULL OR created_utc<=?end)) +
                (SELECT COUNT(*) FROM continuum_economy_adjustments
                 WHERE account_id=?account
                   AND (?start IS NULL OR created_utc>=?start) AND (?end IS NULL OR created_utc<=?end))";
            command.Parameters.AddWithValue("?account", accountID.ToString());
            command.Parameters.AddWithValue("?start", startUtc.HasValue ? startUtc.Value.ToUniversalTime() : DBNull.Value);
            command.Parameters.AddWithValue("?end", endUtc.HasValue ? endUtc.Value.ToUniversalTime() : DBNull.Value);
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        public LedgerHistoryEntry GetOperation(Guid operationID)
        {
            if (operationID == Guid.Empty)
                throw new ArgumentException("A non-zero operation ID is required", nameof(operationID));
            using MySqlConnection connection = new(m_connectionString);
            connection.Open();
            using (MySqlCommand command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT sender_id,receiver_id,amount,transaction_type,region_id,object_id,
                        description,status,sender_balance,failure_reason,created_utc
                    FROM continuum_economy_transactions WHERE transaction_id=?operation";
                command.Parameters.AddWithValue("?operation", operationID.ToString());
                using MySqlDataReader reader = command.ExecuteReader();
                if (reader.Read())
                {
                    Guid sender = Guid.Parse(reader.GetString(0));
                    return new LedgerHistoryEntry { TransactionID=operationID, AccountID=sender,
                        CounterpartyID=Guid.Parse(reader.GetString(1)), Amount=reader.GetInt64(2),
                        TransactionType=reader.GetInt32(3), RegionID=Guid.Parse(reader.GetString(4)),
                        ObjectID=Guid.Parse(reader.GetString(5)), Description=reader.GetString(6),
                        Succeeded=reader.GetInt32(7)==1, ResultingBalance=reader.GetInt64(8),
                        FailureReason=reader.GetString(9), CreatedUtc=DateTime.SpecifyKind(reader.GetDateTime(10),DateTimeKind.Utc),
                        IsCredit=false, IsAdjustment=false };
                }
            }
            using (MySqlCommand command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT account_id,actor_id,amount,adjustment_kind,transaction_type,reason,
                        status,resulting_balance,failure_reason,created_utc
                    FROM continuum_economy_adjustments WHERE operation_id=?operation";
                command.Parameters.AddWithValue("?operation", operationID.ToString());
                using MySqlDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return null;
                return new LedgerHistoryEntry { TransactionID=operationID, AccountID=Guid.Parse(reader.GetString(0)),
                    CounterpartyID=Guid.Parse(reader.GetString(1)), ActorID=Guid.Parse(reader.GetString(1)),
                    Amount=reader.GetInt64(2), IsCredit=reader.GetInt32(3)==(int)LedgerAdjustmentKind.Credit,
                    TransactionType=reader.GetInt32(4), Description=reader.GetString(5),
                    Succeeded=reader.GetInt32(6)==1, ResultingBalance=reader.GetInt64(7),
                    FailureReason=reader.GetString(8), CreatedUtc=DateTime.SpecifyKind(reader.GetDateTime(9),DateTimeKind.Utc),
                    IsAdjustment=true };
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

        private static LedgerAdjustmentResult ValidateAdjustment(LedgerAdjustmentRequest request)
        {
            if (request == null)
                return AdjustmentResult(Guid.Empty, LedgerResultCode.InvalidRequest, 0, "An adjustment request is required");
            if (request.OperationID == Guid.Empty || request.AccountID == Guid.Empty || request.ActorID == Guid.Empty)
                return AdjustmentResult(request.OperationID, LedgerResultCode.InvalidRequest, 0,
                    "Operation, account and actor IDs must be non-zero");
            if (request.Amount <= 0 || (request.Kind != LedgerAdjustmentKind.Credit && request.Kind != LedgerAdjustmentKind.Debit))
                return AdjustmentResult(request.OperationID, LedgerResultCode.InvalidRequest, 0,
                    "A positive credit or debit amount is required");
            if (String.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 255)
                return AdjustmentResult(request.OperationID, LedgerResultCode.InvalidRequest, 0,
                    "A reason between 1 and 255 characters is required");
            if (request.MaximumBalance < 0 || request.DailyCreditLimit < 0 ||
                request.WeeklyCreditLimit < 0 || request.MonthlyCreditLimit < 0)
                return AdjustmentResult(request.OperationID, LedgerResultCode.InvalidRequest, 0,
                    "Credit limits cannot be negative");
            return null;
        }

        private static string CheckCreditPolicy(MySqlConnection connection, MySqlTransaction transaction,
            LedgerAdjustmentRequest request, long balance)
        {
            if (request.MaximumBalance > 0 &&
                (balance > request.MaximumBalance || request.Amount > request.MaximumBalance - balance))
                return "The purchase would exceed the maximum account balance";
            DateTime now = DateTime.UtcNow;
            DateTime day = now.Date;
            DateTime week = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
            DateTime month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            if (ExceedsCreditLimit(connection, transaction, request, day, request.DailyCreditLimit))
                return "The daily currency purchase limit would be exceeded";
            if (ExceedsCreditLimit(connection, transaction, request, week, request.WeeklyCreditLimit))
                return "The weekly currency purchase limit would be exceeded";
            if (ExceedsCreditLimit(connection, transaction, request, month, request.MonthlyCreditLimit))
                return "The monthly currency purchase limit would be exceeded";
            return null;
        }

        private static bool ExceedsCreditLimit(MySqlConnection connection, MySqlTransaction transaction,
            LedgerAdjustmentRequest request, DateTime sinceUtc, long limit)
        {
            if (limit == 0) return false;
            using MySqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"SELECT COALESCE(SUM(amount), 0) FROM continuum_economy_adjustments
                WHERE account_id = ?account AND transaction_type = ?type AND adjustment_kind = 1
                  AND status = 1 AND created_utc >= ?since";
            command.Parameters.AddWithValue("?account", request.AccountID.ToString());
            command.Parameters.AddWithValue("?type", request.TransactionType);
            command.Parameters.AddWithValue("?since", sinceUtc);
            long prior = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            return prior > limit || request.Amount > limit - prior;
        }

        private static void ReserveOperation(MySqlConnection connection, MySqlTransaction transaction,
            Guid operationID, string fingerprint, int operationKind)
        {
            using MySqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"INSERT INTO continuum_economy_operations
                (operation_id, request_hash, operation_kind) VALUES (?operation, ?hash, ?kind)";
            command.Parameters.AddWithValue("?operation", operationID.ToString());
            command.Parameters.AddWithValue("?hash", fingerprint);
            command.Parameters.AddWithValue("?kind", operationKind);
            command.ExecuteNonQuery();
        }

        private static bool TransferExists(MySqlConnection connection, MySqlTransaction transaction, Guid operationID)
        {
            using MySqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT 1 FROM continuum_economy_transactions WHERE transaction_id = ?operation LIMIT 1";
            command.Parameters.AddWithValue("?operation", operationID.ToString());
            return command.ExecuteScalar() != null;
        }

        private static void InsertAdjustment(MySqlConnection connection, MySqlTransaction transaction,
            LedgerAdjustmentRequest request, string fingerprint, int status, long balance, string failureReason)
        {
            using MySqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"INSERT INTO continuum_economy_adjustments
                (operation_id, request_hash, account_id, actor_id, amount, adjustment_kind,
                 transaction_type, reason, status, resulting_balance, failure_reason)
                VALUES (?operation, ?hash, ?account, ?actor, ?amount, ?kind,
                        ?type, ?reason, ?status, ?balance, ?failure)";
            command.Parameters.AddWithValue("?operation", request.OperationID.ToString());
            command.Parameters.AddWithValue("?hash", fingerprint);
            command.Parameters.AddWithValue("?account", request.AccountID.ToString());
            command.Parameters.AddWithValue("?actor", request.ActorID.ToString());
            command.Parameters.AddWithValue("?amount", request.Amount);
            command.Parameters.AddWithValue("?kind", (int)request.Kind);
            command.Parameters.AddWithValue("?type", request.TransactionType);
            command.Parameters.AddWithValue("?reason", request.Reason);
            command.Parameters.AddWithValue("?status", status);
            command.Parameters.AddWithValue("?balance", balance);
            command.Parameters.AddWithValue("?failure", failureReason);
            command.ExecuteNonQuery();
        }

        private static LedgerAdjustmentResult ReadPriorAdjustment(MySqlConnection connection, MySqlTransaction transaction,
            Guid operationID, string fingerprint)
        {
            using MySqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"SELECT request_hash, status, resulting_balance, failure_reason
                FROM continuum_economy_adjustments WHERE operation_id = ?operation";
            command.Parameters.AddWithValue("?operation", operationID.ToString());
            using MySqlDataReader reader = command.ExecuteReader();
            if (!reader.Read())
                return null;
            if (!String.Equals(reader.GetString(0), fingerprint, StringComparison.Ordinal))
                return AdjustmentResult(operationID, LedgerResultCode.TransactionConflict, 0,
                    "The operation ID is already associated with a different request");
            int status = reader.GetInt32(1);
            LedgerResultCode code = status == 1 ? LedgerResultCode.Replayed :
                status == 2 ? LedgerResultCode.InsufficientFunds : LedgerResultCode.InvalidRequest;
            return AdjustmentResult(operationID, code, reader.GetInt64(2),
                status == 1 ? "Adjustment already committed" : reader.GetString(3));
        }

        private static string AdjustmentFingerprint(LedgerAdjustmentRequest request)
        {
            string canonical = String.Join("|", request.AccountID, request.ActorID,
                request.Amount.ToString(CultureInfo.InvariantCulture), ((int)request.Kind).ToString(CultureInfo.InvariantCulture),
                request.TransactionType.ToString(CultureInfo.InvariantCulture), request.Reason);
            // Preserve request hashes produced before credit-policy fields were
            // introduced when all policy values use their original zero defaults.
            if (request.MaximumBalance != 0 || request.DailyCreditLimit != 0 ||
                request.WeeklyCreditLimit != 0 || request.MonthlyCreditLimit != 0)
                canonical = String.Join("|", canonical,
                    request.MaximumBalance.ToString(CultureInfo.InvariantCulture),
                    request.DailyCreditLimit.ToString(CultureInfo.InvariantCulture),
                    request.WeeklyCreditLimit.ToString(CultureInfo.InvariantCulture),
                    request.MonthlyCreditLimit.ToString(CultureInfo.InvariantCulture));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        }

        private static LedgerAdjustmentResult AdjustmentResult(Guid operationID, LedgerResultCode code, long balance, string message)
        {
            return new LedgerAdjustmentResult { OperationID = operationID, Code = code, Balance = balance, Message = message };
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

        private static long GetHeldAmount(MySqlConnection connection, MySqlTransaction transaction, Guid accountID)
        {
            using MySqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT COALESCE(SUM(amount), 0) FROM continuum_economy_purchases WHERE buyer_id = ?account AND state = 1";
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS continuum_economy_operations (
 operation_id CHAR(36) NOT NULL, request_hash CHAR(64) NOT NULL,
 operation_kind TINYINT UNSIGNED NOT NULL,
 created_utc TIMESTAMP(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6), PRIMARY KEY (operation_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS continuum_economy_adjustments (
 operation_id CHAR(36) NOT NULL, request_hash CHAR(64) NOT NULL,
 account_id CHAR(36) NOT NULL, actor_id CHAR(36) NOT NULL, amount BIGINT NOT NULL,
 adjustment_kind TINYINT UNSIGNED NOT NULL, transaction_type INT NOT NULL,
 reason VARCHAR(255) NOT NULL, status TINYINT UNSIGNED NOT NULL,
 resulting_balance BIGINT NOT NULL, failure_reason VARCHAR(255) NOT NULL DEFAULT '',
 created_utc TIMESTAMP(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6), PRIMARY KEY (operation_id),
 KEY idx_continuum_economy_adjustment_account_time (account_id, created_utc),
 KEY idx_continuum_economy_adjustment_actor_time (actor_id, created_utc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS continuum_economy_purchases (
 purchase_id CHAR(36) NOT NULL, request_hash CHAR(64) NOT NULL,
 buyer_id CHAR(36) NOT NULL, seller_id CHAR(36) NOT NULL, amount BIGINT NOT NULL,
 transaction_type INT NOT NULL, region_id CHAR(36) NOT NULL, object_id CHAR(36) NOT NULL,
 description VARCHAR(255) NOT NULL DEFAULT '', state TINYINT UNSIGNED NOT NULL,
 buyer_balance BIGINT NOT NULL, seller_balance BIGINT NOT NULL,
 failure_reason VARCHAR(255) NOT NULL DEFAULT '',
 created_utc TIMESTAMP(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 completed_utc TIMESTAMP(6) NULL, PRIMARY KEY (purchase_id),
 KEY idx_continuum_economy_purchase_buyer_state (buyer_id, state),
 KEY idx_continuum_economy_purchase_seller_time (seller_id, created_utc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
    }
}
