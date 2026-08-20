using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nini.Config;
using OpenSim.Continuum.Economy;

namespace ContinuumEconomy.Service
{
    /// <summary>
    /// WhiteCore-derived stipend policy implemented on the independent,
    /// replay-safe Continuum ledger. It never reads or changes MoneyServer.
    /// </summary>
    internal sealed class ScheduledStipends : IDisposable
    {
        private readonly IEconomyLedger m_ledger;
        private readonly Func<IReadOnlyCollection<Guid>> m_onlineAccounts;
        private readonly Guid m_actor;
        private readonly long m_amount;
        private readonly long m_maximumBalance;
        private readonly int m_every;
        private readonly string m_period;
        private readonly DateTime m_anchorUtc;
        private readonly DateTime? m_createdAfterUtc;
        private readonly bool m_onlyWhenLoggedIn;
        private readonly TimeSpan m_pollInterval;
        private readonly CancellationTokenSource m_stop = new();
        private readonly Task m_worker;

        internal static ScheduledStipends Start(EconomyBackend backend, IConfig config, Guid actor,
            Func<IReadOnlyCollection<Guid>> onlineAccounts)
        {
            if (!config.GetBoolean("StipendsEnabled", false))
                return null;
            return new ScheduledStipends(backend.Ledger, config, actor, onlineAccounts);
        }

        private ScheduledStipends(IEconomyLedger ledger, IConfig config, Guid actor,
            Func<IReadOnlyCollection<Guid>> onlineAccounts)
        {
            m_ledger = ledger;
            m_actor = actor;
            m_onlineAccounts = onlineAccounts;
            m_amount = config.GetLong("StipendAmount", 0);
            m_maximumBalance = Math.Max(0, config.GetLong("MaximumBalance", 0));
            m_every = config.GetInt("StipendsEvery", 1);
            m_period = config.GetString("StipendsEveryType", "week").Trim().ToLowerInvariant();
            m_onlyWhenLoggedIn = config.GetBoolean("GiveStipendsOnlyWhenLoggedIn", false);
            bool loadExisting = config.GetBoolean("StipendsLoadOldUsers", true);
            m_createdAfterUtc = loadExisting ? null : DateTime.UtcNow;
            int pollSeconds = Math.Clamp(config.GetInt("StipendPollSeconds", 60), 10, 3600);
            m_pollInterval = TimeSpan.FromSeconds(pollSeconds);

            if (m_amount <= 0 || m_every <= 0 ||
                (m_period != "day" && m_period != "week" && m_period != "month"))
                throw new InvalidOperationException("Enabled stipends require a positive amount/every value and day, week, or month period");
            if (config.GetBoolean("StipendsPremiumOnly", false))
                throw new InvalidOperationException("StipendsPremiumOnly requires a premium-account authority and cannot be inferred from the economy database");
            string anchor = config.GetString("StipendAnchorUtc", String.Empty);
            if (!DateTime.TryParse(anchor, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out m_anchorUtc))
                throw new InvalidOperationException("Enabled stipends require StipendAnchorUtc in an unambiguous UTC format");
            m_anchorUtc = DateTime.SpecifyKind(m_anchorUtc, DateTimeKind.Utc);
            m_worker = Task.Run(RunAsync);
        }

        private async Task RunAsync()
        {
            while (!m_stop.IsCancellationRequested)
            {
                try { Process(DateTime.UtcNow); }
                catch (Exception e) { Console.Error.WriteLine("ContinuumEconomy stipend pass failed: {0}", e); }
                try { await Task.Delay(m_pollInterval, m_stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (m_stop.IsCancellationRequested) { break; }
            }
        }

        private void Process(DateTime nowUtc)
        {
            if (nowUtc < m_anchorUtc)
                return;
            DateTime periodStart = PeriodStart(nowUtc);

            if (m_onlyWhenLoggedIn)
            {
                foreach (Guid account in m_onlineAccounts().Distinct())
                    Pay(account, periodStart);
            }
            else
            {
                Guid after = Guid.Empty;
                while (true)
                {
                    IReadOnlyList<Guid> accounts = m_ledger.GetAccounts(
                        LedgerAccountType.Resident, after, m_createdAfterUtc, 500);
                    foreach (Guid account in accounts)
                        Pay(account, periodStart);
                    if (accounts.Count < 500)
                        break;
                    after = accounts[accounts.Count - 1];
                }
            }
        }

        private void Pay(Guid account, DateTime periodStart)
        {
            LedgerAdjustmentResult result = m_ledger.Adjust(new LedgerAdjustmentRequest
            {
                OperationID = OperationID(account, periodStart), AccountID = account, ActorID = m_actor,
                Amount = m_amount, Kind = LedgerAdjustmentKind.Credit, TransactionType = 10000,
                Reason = "Continuum scheduled stipend", MaximumBalance = m_maximumBalance
            });
            if (!result.Succeeded)
                Console.Error.WriteLine("ContinuumEconomy stipend rejected for {0}: {1}", account, result.Message);
        }

        private DateTime PeriodStart(DateTime now)
        {
            if (m_period == "month")
            {
                int months = (now.Year - m_anchorUtc.Year) * 12 + now.Month - m_anchorUtc.Month;
                int interval = Math.Max(0, months / m_every) * m_every;
                DateTime candidate = m_anchorUtc.AddMonths(interval);
                return candidate > now ? candidate.AddMonths(-m_every) : candidate;
            }
            int days = m_period == "week" ? checked(m_every * 7) : m_every;
            long intervals = (long)Math.Floor((now - m_anchorUtc).TotalDays / days);
            return m_anchorUtc.AddDays(intervals * days);
        }

        private static Guid OperationID(Guid account, DateTime periodStart)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
                account.ToString("D") + "|ContinuumStipend|" + periodStart.Ticks.ToString(CultureInfo.InvariantCulture)));
            byte[] value = new byte[16]; Buffer.BlockCopy(hash, 0, value, 0, value.Length); return new Guid(value);
        }

        public void Dispose()
        {
            m_stop.Cancel();
            try { m_worker.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            m_stop.Dispose();
        }
    }
}
