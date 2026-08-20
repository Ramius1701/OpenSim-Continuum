using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenSim.Continuum.Economy
{
    /// <summary>
    /// Destructive provider-neutral ledger acceptance checks. Callers are
    /// responsible for enforcing use of an isolated test database.
    /// </summary>
    public static class EconomyAcceptanceSuite
    {
        public static void Run(IEconomyLedger ledger, IEconomyAccountService accounts,
            IEconomyPurchaseService purchases,
            Action<string> report = null)
        {
            if (ledger == null)
                throw new ArgumentNullException(nameof(ledger));
            if (purchases == null)
                throw new ArgumentNullException(nameof(purchases));
            if (accounts == null)
                throw new ArgumentNullException(nameof(accounts));

            ledger.ValidateSchema();
            Guid actor = Guid.NewGuid();
            Guid buyer = Guid.NewGuid();
            Guid sellerA = Guid.NewGuid();
            Guid sellerB = Guid.NewGuid();

            LedgerAdjustmentResult credit = ledger.Adjust(new LedgerAdjustmentRequest
            {
                OperationID = Guid.NewGuid(),
                AccountID = buyer,
                ActorID = actor,
                Amount = 100,
                Kind = LedgerAdjustmentKind.Credit,
                TransactionType = 9000,
                Reason = "Continuum provider acceptance self-test",
                MaximumBalance = 1000,
                DailyCreditLimit = 1000,
                WeeklyCreditLimit = 1000,
                MonthlyCreditLimit = 1000
            });
            Require(credit.Code == LedgerResultCode.Committed && ledger.GetBalance(buyer) == 100,
                "initial audited credit", report);

            Guid replayID = Guid.NewGuid();
            LedgerTransferRequest replay = new()
            {
                TransactionID = replayID,
                SenderID = buyer,
                ReceiverID = sellerA,
                Amount = 10,
                TransactionType = 9001,
                Description = "idempotency self-test"
            };
            Require(ledger.Transfer(replay).Code == LedgerResultCode.Committed,
                "first transfer", report);
            Require(ledger.Transfer(replay).Code == LedgerResultCode.Replayed,
                "idempotent replay", report);
            replay.Amount = 11;
            Require(ledger.Transfer(replay).Code == LedgerResultCode.TransactionConflict,
                "fingerprint conflict", report);

            LedgerTransferRequest concurrentA = new()
            {
                TransactionID = Guid.NewGuid(), SenderID = buyer, ReceiverID = sellerA,
                Amount = 70, TransactionType = 9002, Description = "concurrency A"
            };
            LedgerTransferRequest concurrentB = new()
            {
                TransactionID = Guid.NewGuid(), SenderID = buyer, ReceiverID = sellerB,
                Amount = 70, TransactionType = 9002, Description = "concurrency B"
            };
            Task<LedgerTransferResult> taskA = Task.Run(() => ledger.Transfer(concurrentA));
            Task<LedgerTransferResult> taskB = Task.Run(() => ledger.Transfer(concurrentB));
            Task.WaitAll(taskA, taskB);
            int committed = (taskA.Result.Code == LedgerResultCode.Committed ? 1 : 0) +
                (taskB.Result.Code == LedgerResultCode.Committed ? 1 : 0);
            int insufficient = (taskA.Result.Code == LedgerResultCode.InsufficientFunds ? 1 : 0) +
                (taskB.Result.Code == LedgerResultCode.InsufficientFunds ? 1 : 0);
            Require(committed == 1 && insufficient == 1 && ledger.GetBalance(buyer) == 20,
                "concurrent overspend prevention", report);

            Guid purchaseID = Guid.NewGuid();
            LedgerPurchaseResult authorized = purchases.Authorize(new LedgerPurchaseRequest
            {
                PurchaseID = purchaseID, BuyerID = buyer, SellerID = sellerB,
                Amount = 15, TransactionType = 9003, Description = "hold self-test"
            });
            Require(authorized.State == LedgerPurchaseState.Authorized &&
                ledger.GetAvailableBalance(buyer) == 5, "purchase hold", report);
            Require(purchases.Capture(purchaseID, buyer).State == LedgerPurchaseState.Captured &&
                ledger.GetBalance(buyer) == 5, "purchase capture", report);
            Require(ledger.CountHistory(buyer, null, null) >= 3, "account history", report);

            Guid registrationID = Guid.NewGuid();
            LedgerAccountRegistrationRequest registration = new()
            {
                OperationID = registrationID,
                AccountID = Guid.NewGuid(),
                ActorID = actor,
                AccountType = LedgerAccountType.Group,
                DisplayName = "Continuum provider acceptance group"
            };
            Require(accounts.Register(registration, out _) == LedgerResultCode.Committed,
                "account registration", report);
            Require(accounts.Register(registration, out _) == LedgerResultCode.Replayed,
                "account registration replay", report);
            registration.DisplayName = "Conflicting acceptance group";
            Require(accounts.Register(registration, out _) == LedgerResultCode.TransactionConflict,
                "account registration fingerprint conflict", report);

            LedgerTransferResult groupPayment = ledger.Transfer(new LedgerTransferRequest
            {
                TransactionID = Guid.NewGuid(), SenderID = buyer, ReceiverID = registration.AccountID,
                Amount = 1, TransactionType = 6004, Description = "group account history self-test"
            });
            Require(groupPayment.Code == LedgerResultCode.Committed &&
                ledger.GetBalance(registration.AccountID) == 1,
                "group account transfer", report);
            IReadOnlyList<LedgerHistoryEntry> groupHistory =
                ledger.GetHistory(registration.AccountID, DateTime.UtcNow.AddMinutes(1), 500);
            Require(groupHistory.Count > 0 && groupHistory[0].IsCredit &&
                groupHistory[0].CounterpartyID == buyer && groupHistory[0].Amount == 1,
                "group account bounded history", report);
        }

        private static void Require(bool condition, string test, Action<string> report)
        {
            if (!condition)
                throw new InvalidOperationException("Self-test failed: " + test);
            report?.Invoke("PASS: " + test);
        }
    }
}
