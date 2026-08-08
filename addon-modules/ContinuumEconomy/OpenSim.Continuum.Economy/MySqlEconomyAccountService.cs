using System;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using MySql.Data.MySqlClient;

namespace OpenSim.Continuum.Economy
{
    public sealed class MySqlEconomyAccountService : IEconomyAccountService
    {
        private readonly string m_connectionString;

        public MySqlEconomyAccountService(string connectionString)
        {
            if (String.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("An economy database connection string is required", nameof(connectionString));
            m_connectionString = connectionString;
        }

        public LedgerResultCode Register(LedgerAccountRegistrationRequest request, out string message)
        {
            if (request == null || request.OperationID == Guid.Empty || request.AccountID == Guid.Empty ||
                request.ActorID == Guid.Empty || (request.AccountType != LedgerAccountType.Group &&
                request.AccountType != LedgerAccountType.System) || String.IsNullOrWhiteSpace(request.DisplayName) ||
                request.DisplayName.Length > 255)
            {
                message = "Valid operation, account, actor, non-resident type and display name are required";
                return LedgerResultCode.InvalidRequest;
            }
            string hash = Fingerprint(request);
            try
            {
                using MySqlConnection connection = new(m_connectionString);
                connection.Open();
                using MySqlTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
                LedgerResultCode? prior = ReadPrior(connection, transaction, request.OperationID, hash, out message);
                if (prior.HasValue) { transaction.Commit(); return prior.Value; }
                using (MySqlCommand reserve = connection.CreateCommand())
                {
                    reserve.Transaction = transaction;
                    reserve.CommandText = "INSERT INTO continuum_economy_operations (operation_id,request_hash,operation_kind) VALUES (?id,?hash,4)";
                    reserve.Parameters.AddWithValue("?id", request.OperationID.ToString());
                    reserve.Parameters.AddWithValue("?hash", hash);
                    reserve.ExecuteNonQuery();
                }
                int? existingType = null;
                using (MySqlCommand select = connection.CreateCommand())
                {
                    select.Transaction = transaction;
                    select.CommandText = "SELECT account_type FROM continuum_economy_accounts WHERE account_id=?account FOR UPDATE";
                    select.Parameters.AddWithValue("?account", request.AccountID.ToString());
                    object value = select.ExecuteScalar();
                    if (value != null) existingType = Convert.ToInt32(value);
                }
                int requestedType = (int)request.AccountType;
                if (existingType.HasValue && existingType.Value != requestedType)
                {
                    transaction.Rollback();
                    message = "The UUID already belongs to a different economy account class";
                    return LedgerResultCode.TransactionConflict;
                }
                if (!existingType.HasValue)
                {
                    using MySqlCommand insertAccount = connection.CreateCommand();
                    insertAccount.Transaction = transaction;
                    insertAccount.CommandText = "INSERT INTO continuum_economy_accounts (account_id,balance,account_type) VALUES (?account,0,?type)";
                    insertAccount.Parameters.AddWithValue("?account", request.AccountID.ToString());
                    insertAccount.Parameters.AddWithValue("?type", requestedType);
                    insertAccount.ExecuteNonQuery();
                }
                using (MySqlCommand insert = connection.CreateCommand())
                {
                    insert.Transaction = transaction;
                    insert.CommandText = @"INSERT INTO continuum_economy_account_registrations
                        (operation_id,request_hash,account_id,actor_id,account_type,display_name)
                        VALUES (?id,?hash,?account,?actor,?type,?name)";
                    insert.Parameters.AddWithValue("?id", request.OperationID.ToString());
                    insert.Parameters.AddWithValue("?hash", hash);
                    insert.Parameters.AddWithValue("?account", request.AccountID.ToString());
                    insert.Parameters.AddWithValue("?actor", request.ActorID.ToString());
                    insert.Parameters.AddWithValue("?type", requestedType);
                    insert.Parameters.AddWithValue("?name", request.DisplayName.Trim());
                    insert.ExecuteNonQuery();
                }
                transaction.Commit();
                message = "Economy account registered";
                return LedgerResultCode.Committed;
            }
            catch (MySqlException e) when (e.Number == 1062)
            {
                using MySqlConnection connection = new(m_connectionString);
                connection.Open();
                return ReadPrior(connection, null, request.OperationID, hash, out message) ?? LedgerResultCode.TransactionConflict;
            }
        }

        private static LedgerResultCode? ReadPrior(MySqlConnection connection, MySqlTransaction transaction,
            Guid operationID, string hash, out string message)
        {
            using MySqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT request_hash FROM continuum_economy_account_registrations WHERE operation_id=?id";
            command.Parameters.AddWithValue("?id", operationID.ToString());
            object prior = command.ExecuteScalar();
            if (prior == null) { message = String.Empty; return null; }
            if (!String.Equals(Convert.ToString(prior), hash, StringComparison.Ordinal))
            { message = "The operation ID is already associated with different registration data"; return LedgerResultCode.TransactionConflict; }
            message = "Economy account already registered";
            return LedgerResultCode.Replayed;
        }

        private static string Fingerprint(LedgerAccountRegistrationRequest request)
        {
            string value = String.Join("|", request.AccountID, request.ActorID,
                (int)request.AccountType, request.DisplayName.Trim());
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        }
    }
}
