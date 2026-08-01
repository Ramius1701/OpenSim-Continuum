using System;

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

    public interface IEconomyLedger
    {
        void EnsureSchema();
        long GetBalance(Guid accountID);
        LedgerTransferResult Transfer(LedgerTransferRequest request);
    }
}
