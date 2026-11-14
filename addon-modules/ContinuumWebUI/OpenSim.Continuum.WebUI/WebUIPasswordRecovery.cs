/* Copyright (c) OpenSimulator contributors. BSD-3-Clause; see LICENSE.txt. */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace OpenSim.Continuum.WebUI
{
    public sealed class RecoveryIdentity
    {
        public Guid Account { get; set; }
        public string Email { get; set; }
        public string CredentialProof { get; set; }
    }

    public sealed class RecoveryTicket
    {
        public string TokenHash { get; set; }
        public Guid Account { get; set; }
        public string EmailHash { get; set; }
        public string CredentialProof { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public bool Used { get; set; }
    }

    public sealed class PasswordRecoveryStore
    {
        private readonly string m_path;
        private readonly object m_gate = new();
        private readonly Func<DateTime> m_clock;
        public PasswordRecoveryStore(string path, Func<DateTime> clock = null)
        {
            m_path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(m_path));
            m_clock = clock ?? (() => DateTime.UtcNow);
            Read();
        }
        public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        public static bool Equal(string left, string right) => left != null && right != null
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
        private static string EmailHash(string email) => Hash(email.Trim().ToUpperInvariant());
        private List<RecoveryTicket> Read()
        {
            if (!File.Exists(m_path)) return new();
            using var stream = new FileStream(m_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("Recovery storage exceeds its bound");
            var rows = JsonSerializer.Deserialize<List<RecoveryTicket>>(stream)
                ?? throw new InvalidDataException("Invalid recovery storage");
            if (rows.Count > 4096 || rows.Any(x => x == null || x.Account == Guid.Empty
                || x.TokenHash?.Length != 64 || x.EmailHash?.Length != 64 || x.CredentialProof?.Length != 64
                || x.ExpiresUtc.Kind != DateTimeKind.Utc)
                || rows.Select(x => x.TokenHash).Distinct().Count() != rows.Count)
                throw new InvalidDataException("Invalid recovery records");
            return rows;
        }
        private T Mutate<T>(Func<List<RecoveryTicket>, T> operation)
        {
            lock (m_gate)
            {
                using var lease = new FileStream(m_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var rows = Read(); rows.RemoveAll(x => x.ExpiresUtc <= m_clock());
                T result = operation(rows);
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(rows);
                using (var stream = new FileStream(m_path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
                { stream.Write(json); stream.Flush(true); }
                File.Move(m_path + ".tmp", m_path, true);
                return result;
            }
        }
        public string Issue(RecoveryIdentity identity)
        {
            if (identity == null || identity.Account == Guid.Empty || String.IsNullOrWhiteSpace(identity.Email)
                || identity.CredentialProof?.Length != 64) throw new ArgumentException("Invalid recovery identity");
            return Mutate(rows =>
            {
                // Keep prior links usable: an unauthenticated request must not invalidate a valid link.
                if (rows.Count >= 4096 || rows.Count(x => x.Account == identity.Account) >= 3) return null;
                string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                rows.Add(new RecoveryTicket { Account = identity.Account, TokenHash = Hash(token),
                    EmailHash = EmailHash(identity.Email), CredentialProof = identity.CredentialProof,
                    ExpiresUtc = m_clock().AddMinutes(30) });
                return token;
            });
        }
        public Guid Consume(string token, Func<Guid, RecoveryIdentity> find)
        {
            if (token?.Length != 64 || !token.All(Uri.IsHexDigit)) return Guid.Empty;
            return Mutate(rows =>
            {
                string hash = Hash(token);
                var row = rows.FirstOrDefault(x => Equal(x.TokenHash, hash));
                if (row == null || row.Used) return Guid.Empty;
                RecoveryIdentity current = find(row.Account);
                if (current == null || !Equal(row.CredentialProof, current.CredentialProof)
                    || String.IsNullOrWhiteSpace(current.Email) || !Equal(row.EmailHash, EmailHash(current.Email)))
                    return Guid.Empty;
                // Durable consume precedes the external password write. A crash fails closed.
                foreach (var other in rows.Where(x => x.Account == row.Account)) other.Used = true;
                return row.Account;
            });
        }
    }

    /// <summary>Bounded background dispatch hides lookup/mail timing from the public request response.</summary>
    public sealed class WebUIPasswordRecovery
    {
        private readonly ConcurrentQueue<(string Name, string Email)> m_queue = new();
        private readonly PasswordRecoveryStore m_store;
        private readonly Func<string, string, RecoveryIdentity> m_lookup;
        private readonly Func<Guid, RecoveryIdentity> m_find;
        private readonly Func<Guid, string, bool> m_setPassword;
        private readonly Action<string, string> m_send;
        private readonly Action m_warn;
        private readonly Uri m_publicRoot;
        private int m_pending, m_running;
        public const string NeutralResponse = "If the details match an eligible account, a recovery link will be emailed shortly";

        public WebUIPasswordRecovery(PasswordRecoveryStore store, Uri publicRoot,
            Func<string, string, RecoveryIdentity> lookup, Func<Guid, RecoveryIdentity> find,
            Func<Guid, string, bool> setPassword, Action<string, string> send, Action warn = null)
        {
            if (publicRoot == null || !publicRoot.IsAbsoluteUri || publicRoot.Query.Length != 0 || publicRoot.Fragment.Length != 0
                || publicRoot.UserInfo.Length != 0 || (publicRoot.Scheme != "https" && !(publicRoot.Scheme == "http" && publicRoot.IsLoopback)))
                throw new ArgumentException("Recovery URL must be an HTTPS portal root (HTTP loopback is permitted for isolated testing)");
            m_store = store;
            m_publicRoot = new UriBuilder(publicRoot) { Path = publicRoot.AbsolutePath.TrimEnd('/') + "/" }.Uri;
            m_lookup = lookup; m_find = find;
            m_setPassword = setPassword; m_send = send; m_warn = warn ?? (() => { });
        }
        public string Request(string name, string email)
        {
            if (String.IsNullOrWhiteSpace(name) || name.Length > 128 || String.IsNullOrWhiteSpace(email) || email.Length > 254)
                return NeutralResponse;
            if (Interlocked.Increment(ref m_pending) > 32)
            { Interlocked.Decrement(ref m_pending); return NeutralResponse; }
            m_queue.Enqueue((name.Trim(), email.Trim())); Schedule();
            return NeutralResponse;
        }
        private void Schedule()
        {
            if (Interlocked.CompareExchange(ref m_running, 1, 0) == 0)
                ThreadPool.QueueUserWorkItem(_ => Drain());
        }
        private void Drain()
        {
            try
            {
                while (m_queue.TryDequeue(out var request))
                {
                    try
                    {
                        var identity = m_lookup(request.Name, request.Email);
                        if (identity == null) continue;
                        string token = m_store.Issue(identity);
                        if (token == null) continue;
                        var link = new UriBuilder(m_publicRoot) { Query = "page=password-recovery", Fragment = "reset=" + token };
                        m_send(identity.Email, link.Uri.AbsoluteUri);
                    }
                    catch { m_warn(); } // do not log credentials, mailbox addresses or recovery links
                    finally { Interlocked.Decrement(ref m_pending); }
                }
            }
            finally { Volatile.Write(ref m_running, 0); if (!m_queue.IsEmpty) Schedule(); }
        }
        public bool Reset(string token, string password, string confirmation)
        {
            if (password == null || password.Length < 8 || password.Length > 128 || password != confirmation)
                return false;
            Guid account = m_store.Consume(token, m_find);
            return account != Guid.Empty && m_setPassword(account, password);
        }
    }
}
