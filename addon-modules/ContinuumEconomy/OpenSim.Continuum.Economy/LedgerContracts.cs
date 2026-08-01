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
        public long Amount { get; set; }
        public long ResultingBalance { get; set; }
        public int TransactionType { get; set; }
        public Guid RegionID { get; set; }
        public Guid ObjectID { get; set; }
        public string Description { get; set; } = String.Empty;
        public string FailureReason { get; set; } = String.Empty;
        public DateTime CreatedUtc { get; set; }
        public bool IsCredit { get; set; }
        public bool Succeeded { get; set; }
    }

    public interface IEconomyLedger
    {
        void EnsureSchema();
        long GetBalance(Guid accountID);
        LedgerTransferResult Transfer(LedgerTransferRequest request);
        IReadOnlyList<LedgerHistoryEntry> GetHistory(Guid accountID, DateTime? beforeUtc, int limit);
    }
}
