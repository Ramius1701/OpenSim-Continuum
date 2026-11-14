using System;
using System.Collections.Generic;

namespace OpenSim.Continuum.Economy
{
    public enum LedgerResultCode
    {
        Committed = 0,
        Replayed = 1,
        InsufficientFunds = 2,
        InvalidRequest = 3,
        TransactionConflict = 4
    }

    public enum LedgerAdjustmentKind
    {
        Credit = 1,
        Debit = 2
    }

    public enum LedgerAccountType
    {
        Resident = 0,
        Group = 100,
        System = 200
    }

    public sealed class LedgerAccountRegistrationRequest
    {
        public Guid OperationID { get; set; }
        public Guid AccountID { get; set; }
        public Guid ActorID { get; set; }
        public LedgerAccountType AccountType { get; set; }
        public string DisplayName { get; set; } = String.Empty;
    }

    public interface IEconomyAccountService
    {
        LedgerResultCode Register(LedgerAccountRegistrationRequest request, out string message);
    }

    public sealed class LedgerTransferRequest
    {
        public Guid TransactionID { get; set; }
        public Guid SenderID { get; set; }
        public Guid ReceiverID { get; set; }
        public long Amount { get; set; }
        public int TransactionType { get; set; }
        public Guid RegionID { get; set; }
        public Guid ObjectID { get; set; }
        public string Description { get; set; } = String.Empty;
    }

    public sealed class LedgerTransferResult
    {
        public LedgerResultCode Code { get; set; }
        public Guid TransactionID { get; set; }
        public long SenderBalance { get; set; }
        public long ReceiverBalance { get; set; }
        public string Message { get; set; } = String.Empty;
        public bool Succeeded => Code == LedgerResultCode.Committed || Code == LedgerResultCode.Replayed;
    }

    public sealed class LedgerHistoryEntry
    {
        public Guid TransactionID { get; set; }
        public Guid AccountID { get; set; }
        public Guid CounterpartyID { get; set; }
        public Guid ActorID { get; set; }
        public long Amount { get; set; }
        public long ResultingBalance { get; set; }
        public int TransactionType { get; set; }
        public Guid RegionID { get; set; }
        public Guid ObjectID { get; set; }
        public string Description { get; set; } = String.Empty;
        public string FailureReason { get; set; } = String.Empty;
        public DateTime CreatedUtc { get; set; }
        public bool IsCredit { get; set; }
        public bool IsAdjustment { get; set; }
        public bool Succeeded { get; set; }
    }

    public sealed class LedgerAdjustmentRequest
    {
        public Guid OperationID { get; set; }
        public Guid AccountID { get; set; }
        public Guid ActorID { get; set; }
        public long Amount { get; set; }
        public LedgerAdjustmentKind Kind { get; set; }
        public int TransactionType { get; set; }
        public string Reason { get; set; } = String.Empty;
        public long MaximumBalance { get; set; }
        public long DailyCreditLimit { get; set; }
        public long WeeklyCreditLimit { get; set; }
        public long MonthlyCreditLimit { get; set; }
    }

    public sealed class LedgerAdjustmentResult
    {
        public LedgerResultCode Code { get; set; }
        public Guid OperationID { get; set; }
        public long Balance { get; set; }
        public string Message { get; set; } = String.Empty;
        public bool Succeeded => Code == LedgerResultCode.Committed || Code == LedgerResultCode.Replayed;
    }

    public enum LedgerPurchaseState
    {
        Authorized = 1,
        Captured = 2,
        Cancelled = 3,
        InsufficientFunds = 4
    }

    public sealed class LedgerPurchaseRequest
    {
        public Guid PurchaseID { get; set; }
        public Guid BuyerID { get; set; }
        public Guid SellerID { get; set; }
        public long Amount { get; set; }
        public int TransactionType { get; set; }
        public Guid RegionID { get; set; }
        public Guid ObjectID { get; set; }
        public string Description { get; set; } = String.Empty;
    }

    public sealed class LedgerPurchaseResult
    {
        public LedgerResultCode Code { get; set; }
        public Guid PurchaseID { get; set; }
        public LedgerPurchaseState State { get; set; }
        public long BuyerBalance { get; set; }
        public long BuyerAvailableBalance { get; set; }
        public long SellerBalance { get; set; }
        public string Message { get; set; } = String.Empty;
        public bool Succeeded => Code == LedgerResultCode.Committed || Code == LedgerResultCode.Replayed;
    }

    public interface IEconomyPurchaseService
    {
        LedgerPurchaseResult Authorize(LedgerPurchaseRequest request);
        LedgerPurchaseResult Capture(Guid purchaseID, Guid expectedBuyerID);
        LedgerPurchaseResult Cancel(Guid purchaseID, Guid expectedBuyerID);
        IReadOnlyList<LedgerPendingPurchase> GetPending(DateTime createdBeforeUtc, int limit);
    }

    public sealed class LedgerPendingPurchase
    {
        public Guid PurchaseID { get; set; }
        public Guid BuyerID { get; set; }
        public Guid SellerID { get; set; }
        public long Amount { get; set; }
        public int TransactionType { get; set; }
        public Guid RegionID { get; set; }
        public Guid ObjectID { get; set; }
        public string Description { get; set; } = String.Empty;
        public DateTime CreatedUtc { get; set; }
    }

    public interface ICurrencyCreditHistory
    {
        IReadOnlyList<LedgerHistoryEntry> GetCurrencyCredits(Guid account, DateTime? beforeUtc, Guid beforeID, int limit);
    }
    public static class CurrencyCreditHistoryQuery
    {
        public static IReadOnlyList<LedgerHistoryEntry> Read(System.Data.Common.DbConnection connection, string dialect, Guid account, DateTime? beforeUtc, Guid beforeID, int limit)
        {
            if (account == Guid.Empty || limit < 1 || limit > 101 || (beforeID != Guid.Empty && !beforeUtc.HasValue)) throw new ArgumentException("Invalid currency history cursor");
            bool postgres = dialect == "postgres", sqlite = dialect == "sqlite";
            if (!postgres && !sqlite && dialect != "mysql") throw new ArgumentException("Unsupported ledger provider");
            using var query = connection.CreateCommand();
            query.CommandText = "SELECT operation_id,actor_id,amount,reason,resulting_balance,created_utc FROM continuum_economy_adjustments "
                + "WHERE account_id=@account AND adjustment_kind=1 AND transaction_type=5010 AND status=1 AND amount>0"
                + (beforeUtc.HasValue ? (beforeID == Guid.Empty ? " AND created_utc<@before" : " AND (created_utc<@before OR (created_utc=@before AND operation_id<@cursor))") : "")
                + " ORDER BY created_utc DESC,operation_id DESC LIMIT @limit";
            void Parameter(string name, object value) { var parameter = query.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; query.Parameters.Add(parameter); }
            Parameter("@account", postgres ? (object)account : account.ToString()); Parameter("@limit", limit);
            if (beforeUtc.HasValue) Parameter("@before", sqlite ? (object)beforeUtc.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture) : beforeUtc.Value.ToUniversalTime());
            if (beforeID != Guid.Empty) Parameter("@cursor", postgres ? (object)beforeID : beforeID.ToString());
            using var reader = query.ExecuteReader(); var rows = new List<LedgerHistoryEntry>();
            while (reader.Read())
            {
                DateTime time = reader.GetValue(5) is DateTime date ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
                    : DateTime.Parse(Convert.ToString(reader.GetValue(5)), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
                rows.Add(new LedgerHistoryEntry { TransactionID = Guid.Parse(reader.GetValue(0).ToString()), AccountID = account,
                    ActorID = Guid.Parse(reader.GetValue(1).ToString()), CounterpartyID = Guid.Parse(reader.GetValue(1).ToString()),
                    Amount = reader.GetInt64(2), Description = reader.GetString(3), ResultingBalance = reader.GetInt64(4), CreatedUtc = time,
                    IsCredit = true, IsAdjustment = true, Succeeded = true, TransactionType = 5010 });
            }
            return rows;
        }
    }

    public interface IEconomyLedger
    {
        void EnsureSchema();
        void ValidateSchema();
        void EnsureAccount(Guid accountID);
        bool AccountExists(Guid accountID);
        IReadOnlyList<Guid> GetAccounts(LedgerAccountType accountType, Guid afterAccountID,
            DateTime? createdAfterUtc, int limit);
        long GetBalance(Guid accountID);
        long GetAvailableBalance(Guid accountID);
        LedgerTransferResult Transfer(LedgerTransferRequest request);
        LedgerAdjustmentResult Adjust(LedgerAdjustmentRequest request);
        IReadOnlyList<LedgerHistoryEntry> GetHistory(Guid accountID, DateTime? beforeUtc, int limit);
        long GetCreditedTotal(Guid accountID, int transactionType, DateTime sinceUtc);
        long CountHistory(Guid accountID, DateTime? startUtc, DateTime? endUtc);
        LedgerHistoryEntry GetOperation(Guid operationID);
    }
}
