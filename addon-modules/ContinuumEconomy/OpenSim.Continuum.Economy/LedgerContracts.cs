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

    public interface IEconomyLedger
    {
        void EnsureSchema();
        void ValidateSchema();
        void EnsureAccount(Guid accountID);
        bool AccountExists(Guid accountID);
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
