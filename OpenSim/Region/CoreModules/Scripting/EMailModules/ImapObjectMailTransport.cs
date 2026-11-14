/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * Redistribution and use are permitted under the conditions in LICENSE.txt.
 *
 * Shared-inbox object mail adapted from opensim-lickx archive
 * 6614599b7506b63861763e6cae5eefde861f8749. Continuum adds bounded background
 * processing, exact routing, backpressure and selective acknowledgement.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;

[assembly: InternalsVisibleTo("Continuum.Lickx.Tests")]

namespace OpenSim.Region.CoreModules.Scripting.EmailModules
{
    internal sealed class ImapObjectMailOptions
    {
        public string Host, User, Password, Folder = "INBOX", Domain;
        public int Port = 143, PollSeconds = 5, TimeoutMilliseconds = 10000;
        public int MaxMessageBytes = 65536, MaxPendingSends = 128, MaxMailboxes = 1024;
        public int UidWindow = 256, BatchSize = 8, RetrySeconds = 15, SendLifetimeSeconds = 3600;
        public SecureSocketOptions Security = SecureSocketOptions.StartTls;

        public static ImapObjectMailOptions Read(IConfig config, string domain)
        {
            var options = new ImapObjectMailOptions
            {
                Host = config.GetString("IMAP_SERVER_HOSTNAME", "").Trim(),
                User = config.GetString("IMAP_SERVER_LOGIN", ""),
                Password = config.GetString("IMAP_SERVER_PASSWORD", ""),
                Folder = config.GetString("IMAP_FOLDER", "INBOX"),
                Domain = domain,
                Port = config.GetInt("IMAP_SERVER_PORT", 143),
                PollSeconds = config.GetInt("IMAP_PollSeconds", 5),
                TimeoutMilliseconds = config.GetInt("IMAP_TimeoutMilliseconds", 10000),
                MaxMessageBytes = config.GetInt("IMAP_MaxMessageBytes", 65536),
                MaxPendingSends = config.GetInt("IMAP_MaxPendingSends", 128),
                MaxMailboxes = config.GetInt("IMAP_MaxMailboxes", 1024),
                UidWindow = config.GetInt("IMAP_UidWindow", 256),
                BatchSize = config.GetInt("IMAP_BatchSize", 8),
                RetrySeconds = config.GetInt("IMAP_RetrySeconds", 15),
                SendLifetimeSeconds = config.GetInt("IMAP_SendLifetimeSeconds", 3600)
            };
            string security = config.GetString("IMAP_SERVER_SECURITY",
                config.GetBoolean("IMAP_SERVER_TLS", true) ? "StartTls" : "None");
            if (!Enum.TryParse(security, true, out options.Security)
                || (options.Security != SecureSocketOptions.StartTls
                    && options.Security != SecureSocketOptions.SslOnConnect && options.Security != SecureSocketOptions.None))
                throw new ArgumentException("IMAP_SERVER_SECURITY must be StartTls, SslOnConnect or None");
            options.Validate();
            return options;
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Host) || Host.Length > 255 || string.IsNullOrEmpty(User)
                || string.IsNullOrEmpty(Password) || string.IsNullOrWhiteSpace(Folder) || Folder.Length > 255
                || string.IsNullOrWhiteSpace(Domain) || Domain.Length > 253
                || !MailboxAddress.TryParse("object@" + Domain, out MailboxAddress _)
                || Port < 1 || Port > 65535 || PollSeconds < 1 || PollSeconds > 300
                || TimeoutMilliseconds < 100 || TimeoutMilliseconds > 60000
                || MaxMessageBytes < 1024 || MaxMessageBytes > 1048576
                || MaxPendingSends < 1 || MaxPendingSends > 1024 || MaxMailboxes < 1 || MaxMailboxes > 10000
                || UidWindow < 1 || UidWindow > 1024 || BatchSize < 1 || BatchSize > 64
                || RetrySeconds < 1 || RetrySeconds > 300 || SendLifetimeSeconds < 1 || SendLifetimeSeconds > 86400)
                throw new ArgumentException("Invalid IMAP object-mail configuration or bounds");
        }
    }

    internal sealed class ImapObjectMailHeader
    {
        public uint Uid;
        public uint? Size;
        public bool Deleted;
        public string[] Recipients = Array.Empty<string>();
    }

    internal interface IImapObjectMailStore : IDisposable
    {
        void Open(CancellationToken token);
        uint UidValidity { get; }
        uint NextUid { get; }
        IList<ImapObjectMailHeader> FetchAfter(uint afterUid, int maximum, CancellationToken token);
        MimeMessage Read(uint uid, int maxBytes, CancellationToken token);
        void Append(MimeMessage message, CancellationToken token);
        void Delete(uint uid, CancellationToken token);
    }

    internal sealed class MailKitObjectMailStore : IImapObjectMailStore
    {
        private readonly ImapObjectMailOptions m_options;
        private readonly ImapClient m_client = new ImapClient();
        private IMailFolder m_folder;
        private long m_mailboxVersion;
        public MailKitObjectMailStore(ImapObjectMailOptions options) { m_options = options; }
        public uint UidValidity => m_folder.UidValidity;
        public uint NextUid => m_folder.UidNext?.Id ?? 1;
        public void Open(CancellationToken token)
        {
            m_client.Timeout = m_options.TimeoutMilliseconds;
            // MailKit's default certificate validation remains enabled.
            m_client.Connect(m_options.Host, m_options.Port, m_options.Security, token);
            m_client.Authenticate(m_options.User, m_options.Password, token);
            if ((m_client.Capabilities & ImapCapabilities.UidPlus) == 0)
                throw new NotSupportedException("IMAP object mail requires UIDPLUS");
            m_folder = m_options.Folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
                ? m_client.Inbox : m_client.GetFolder(m_options.Folder, token);
            m_folder.Open(FolderAccess.ReadWrite, token);
            m_folder.CountChanged += (_, _) => Interlocked.Increment(ref m_mailboxVersion);
            m_folder.MessageExpunged += (_, _) => Interlocked.Increment(ref m_mailboxVersion);
            m_folder.UidValidityChanged += (_, _) => Interlocked.Increment(ref m_mailboxVersion);
        }
        public IList<ImapObjectMailHeader> FetchAfter(uint afterUid, int maximum, CancellationToken token)
        {
            long version = Volatile.Read(ref m_mailboxVersion);
            void CheckVersion()
            {
                if (version != Volatile.Read(ref m_mailboxVersion))
                    throw new IOException("IMAP mailbox changed during message-page fetch");
            }
            // Seek by UID using single-message probes, then fetch a bounded page
            // of actual messages. Numeric UID windows can take hours to cross
            // gaps left by expunged mail in a long-lived shared inbox.
            int low = 0, high = m_folder.Count;
            while (low < high)
            {
                token.ThrowIfCancellationRequested();
                int middle = low + (high - low) / 2;
                var probe = m_folder.Fetch(middle, middle, MessageSummaryItems.UniqueId, token);
                CheckVersion();
                if (probe.Count != 1) throw new IOException("IMAP mailbox changed during UID seek");
                if (probe[0].UniqueId.Id <= afterUid) low = middle + 1;
                else high = middle;
            }
            if (low >= m_folder.Count) return Array.Empty<ImapObjectMailHeader>();
            int end = (int)Math.Min((long)m_folder.Count - 1, (long)low + maximum - 1);
            var messages = m_folder.Fetch(low, end,
                MessageSummaryItems.UniqueId | MessageSummaryItems.Size | MessageSummaryItems.Flags | MessageSummaryItems.Envelope, token);
            CheckVersion();
            return messages.Select(message => new ImapObjectMailHeader
            {
                Uid = message.UniqueId.Id, Size = message.Size,
                Deleted = (message.Flags.GetValueOrDefault() & MessageFlags.Deleted) != 0,
                Recipients = message.Envelope?.To?.Mailboxes.Select(address => address.Address).ToArray() ?? Array.Empty<string>()
            }).ToList();
        }
        public MimeMessage Read(uint uid, int maxBytes, CancellationToken token)
        {
            // Partial fetch bounds raw MIME bytes even if RFC822.SIZE was false.
            using Stream source = m_folder.GetStream(new UniqueId(uid), 0, maxBytes + 1, token);
            using var data = new MemoryStream();
            byte[] buffer = new byte[4096];
            int count;
            while ((count = source.Read(buffer, 0, Math.Min(buffer.Length, maxBytes + 1 - (int)data.Length))) > 0)
            {
                token.ThrowIfCancellationRequested();
                data.Write(buffer, 0, count);
                if (data.Length > maxBytes) throw new InvalidDataException("IMAP message exceeds byte limit");
            }
            data.Position = 0;
            return MimeMessage.Load(data, token);
        }
        public void Append(MimeMessage message, CancellationToken token) => m_folder.Append(message, MessageFlags.None, token);
        public void Delete(uint uid, CancellationToken token)
        {
            var ids = new[] { new UniqueId(uid) };
            m_folder.AddFlags(ids, MessageFlags.Deleted, true, token);
            // Never use mailbox-wide EXPUNGE or expunge-on-disconnect.
            m_folder.Expunge(ids, token);
        }
        public void Dispose() => m_client.Dispose();
    }

    internal sealed class ImapObjectMailTransport : IDisposable
    {
        private sealed class Mailbox
        {
            public UUID Object;
            public uint Cursor, Validity, Pending;
            public DateTime LastPoll;
        }
        private sealed class Outbound
        {
            public MimeMessage Message;
            public DateTime Created, Retry;
        }
        private readonly object m_gate = new object();
        private readonly ImapObjectMailOptions m_options;
        private readonly Func<IImapObjectMailStore> m_factory;
        private readonly Func<UUID, Email, bool> m_deliver;
        private readonly Action<string> m_warn;
        private readonly Dictionary<UUID, Mailbox> m_mailboxes = new Dictionary<UUID, Mailbox>();
        private readonly Queue<Mailbox> m_poll = new Queue<Mailbox>();
        private readonly Queue<Outbound> m_outbox = new Queue<Outbound>();
        private readonly CancellationTokenSource m_stop = new CancellationTokenSource();
        private Timer m_timer;
        private IImapObjectMailStore m_active;
        private bool m_closed;
        private int m_running;
        private int m_pendingSends;

        public ImapObjectMailTransport(ImapObjectMailOptions options, Func<UUID, Email, bool> deliver,
            Action<string> warn, Func<IImapObjectMailStore> factory = null)
        {
            options.Validate();
            m_options = options; m_deliver = deliver; m_warn = warn;
            m_factory = factory ?? (() => new MailKitObjectMailStore(options));
        }
        public void Start()
        {
            lock (m_gate)
                if (!m_closed && m_timer == null)
                    m_timer = new Timer(_ => ProcessOnce(), null, m_options.PollSeconds * 1000, m_options.PollSeconds * 1000);
        }
        public void RequestPoll(UUID id)
        {
            if (id.IsZero()) return;
            lock (m_gate)
            {
                if (m_closed) return;
                if (m_mailboxes.TryGetValue(id, out Mailbox box)) { box.LastPoll = DateTime.UtcNow; return; }
                if (m_mailboxes.Count >= m_options.MaxMailboxes) return;
                box = new Mailbox { Object = id, LastPoll = DateTime.UtcNow };
                m_mailboxes.Add(id, box); m_poll.Enqueue(box);
            }
        }
        public void RemoveMailbox(UUID id)
        {
            lock (m_gate)
            {
                m_mailboxes.Remove(id);
                // Compact to keep repeated remove/re-add bounded between timer passes.
                var live = m_poll.Where(IsCurrent).ToArray();
                m_poll.Clear();
                foreach (var box in live) m_poll.Enqueue(box);
            }
        }
        public bool Enqueue(UUID recipient, Email email)
        {
            if (recipient.IsZero() || email == null || string.IsNullOrWhiteSpace(email.sender)
                || Encoding.UTF8.GetByteCount((email.subject ?? "") + (email.message ?? "")) > m_options.MaxMessageBytes)
                return false;
            var message = new MimeMessage { Date = DateTimeOffset.UtcNow, Subject = email.subject ?? "",
                MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId(), Body = new TextPart("plain") { Text = email.message ?? "" } };
            if (!MailboxAddress.TryParse(email.sender, out MailboxAddress sender)) { message.Dispose(); return false; }
            message.From.Add(sender);
            message.To.Add(new MailboxAddress("", recipient + "@" + m_options.Domain));
            try
            {
                var format = FormatOptions.Default.Clone();
                format.NewLineFormat = NewLineFormat.Dos;
                using var limit = new MimeByteLimit(m_options.MaxMessageBytes);
                message.WriteTo(format, limit);
            }
            catch (InvalidDataException) { message.Dispose(); return false; }
            lock (m_gate)
            {
                if (m_closed || m_pendingSends >= m_options.MaxPendingSends) { message.Dispose(); return false; }
                m_outbox.Enqueue(new Outbound { Message = message, Created = DateTime.UtcNow });
                m_pendingSends++;
                return true;
            }
        }
        private bool IsCurrent(Mailbox box) => !m_closed && m_mailboxes.TryGetValue(box.Object, out Mailbox current) && ReferenceEquals(box, current);
        public void ProcessOnce()
        {
            if (Interlocked.CompareExchange(ref m_running, 1, 0) != 0) return;
            IImapObjectMailStore store = null;
            try
            {
                lock (m_gate)
                {
                    int queued = m_outbox.Count;
                    for (int i = 0; i < queued; i++)
                    {
                        var send = m_outbox.Dequeue();
                        if ((DateTime.UtcNow - send.Created).TotalSeconds > m_options.SendLifetimeSeconds)
                        { send.Message.Dispose(); m_pendingSends--; m_warn("[EMAIL]: IMAP queued send expired"); }
                        else m_outbox.Enqueue(send);
                    }
                    int tracked = m_poll.Count;
                    for (int i = 0; i < tracked; i++)
                    {
                        var box = m_poll.Dequeue();
                        if (!IsCurrent(box)) continue;
                        if ((DateTime.UtcNow - box.LastPoll).TotalMinutes > 30) m_mailboxes.Remove(box.Object);
                        else m_poll.Enqueue(box);
                    }
                    if (m_closed || (m_poll.Count == 0 && m_outbox.Count == 0)) return;
                }
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(m_stop.Token);
                deadline.CancelAfter(m_options.TimeoutMilliseconds);
                var token = deadline.Token;
                store = m_factory();
                lock (m_gate)
                {
                    if (m_closed) return;
                    m_active = store;
                }
                store.Open(token);
                int sendCount;
                lock (m_gate) sendCount = Math.Min(m_options.BatchSize, m_outbox.Count);
                for (int i = 0; i < sendCount; i++)
                {
                    Outbound outbound;
                    lock (m_gate)
                    {
                        if (m_closed || m_outbox.Count == 0) break;
                        outbound = m_outbox.Dequeue();
                    }
                    bool retry = true;
                    bool attempted = false;
                    try
                    {
                        if ((DateTime.UtcNow - outbound.Created).TotalSeconds > m_options.SendLifetimeSeconds)
                        { retry = false; m_warn("[EMAIL]: IMAP queued send expired"); }
                        else if (outbound.Retry <= DateTime.UtcNow)
                        { attempted = true; store.Append(outbound.Message, token); retry = false; }
                    }
                    finally
                    {
                        lock (m_gate)
                        {
                            if (retry && !m_closed)
                            {
                                if (attempted) outbound.Retry = DateTime.UtcNow.AddSeconds(m_options.RetrySeconds);
                                m_outbox.Enqueue(outbound);
                            }
                            else { outbound.Message.Dispose(); m_pendingSends--; }
                        }
                    }
                }
                int pollCount;
                lock (m_gate) pollCount = Math.Min(m_options.BatchSize, m_poll.Count);
                for (int i = 0; i < pollCount; i++)
                {
                    Mailbox box;
                    lock (m_gate)
                    {
                        if (m_closed || m_poll.Count == 0) break;
                        box = m_poll.Dequeue();
                    }
                    try
                    {
                        lock (m_gate)
                        {
                            if (!IsCurrent(box)) continue;
                            if ((DateTime.UtcNow - box.LastPoll).TotalMinutes > 30)
                            { m_mailboxes.Remove(box.Object); continue; }
                        }
                        Receive(store, box, token);
                    }
                    finally { lock (m_gate) if (IsCurrent(box)) m_poll.Enqueue(box); }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                // Do not log credentials, server replies or message contents.
                if (!m_stop.IsCancellationRequested) m_warn("[EMAIL]: IMAP object-mail cycle failed; pending work will retry");
            }
            finally
            {
                lock (m_gate) if (ReferenceEquals(m_active, store)) m_active = null;
                try { store?.Dispose(); } catch { }
                Volatile.Write(ref m_running, 0);
            }
        }
        private void Receive(IImapObjectMailStore store, Mailbox box, CancellationToken token)
        {
            if (box.Validity != store.UidValidity)
            { box.Validity = store.UidValidity; box.Cursor = 0; box.Pending = 0; }
            if (box.Pending != 0)
            {
                token.ThrowIfCancellationRequested();
                if (store.UidValidity != box.Validity) throw new IOException("IMAP UIDVALIDITY changed");
                store.Delete(box.Pending, token);
                box.Cursor = box.Pending; box.Pending = 0;
            }
            uint last = store.NextUid > 0 ? store.NextUid - 1 : 0;
            if (box.Cursor >= last) return;
            var headers = store.FetchAfter(box.Cursor, m_options.UidWindow, token);
            foreach (var header in headers.OrderBy(h => h.Uid).Take(m_options.UidWindow))
            {
                token.ThrowIfCancellationRequested();
                lock (m_gate) if (!IsCurrent(box)) return;
                if (header.Uid <= box.Cursor || header.Uid > last) continue;
                if (header.Deleted || !ExactRecipient(header.Recipients, box.Object)
                    || !header.Size.HasValue || header.Size > m_options.MaxMessageBytes)
                { box.Cursor = header.Uid; continue; }
                MimeMessage message;
                try { message = store.Read(header.Uid, m_options.MaxMessageBytes, token); }
                catch (InvalidDataException) { box.Cursor = header.Uid; continue; }
                catch (FormatException) { box.Cursor = header.Uid; continue; }
                using (message)
                {
                    if (!ExactRecipient(message.To.Mailboxes.Select(a => a.Address).ToArray(), box.Object)
                        || message.Cc.Count != 0 || message.Bcc.Count != 0 || message.TextBody == null)
                    { box.Cursor = header.Uid; continue; }
                    var email = new Email { time = message.Date.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                        subject = message.Subject ?? "", sender = message.From.Mailboxes.FirstOrDefault()?.Address ?? message.Sender?.Address ?? "",
                        message = message.TextBody };
                    if (Encoding.UTF8.GetByteCount(email.subject + email.message) > m_options.MaxMessageBytes)
                    { box.Cursor = header.Uid; continue; }
                    token.ThrowIfCancellationRequested();
                    lock (m_gate) if (!IsCurrent(box)) return;
                    if (store.UidValidity != box.Validity) throw new IOException("IMAP UIDVALIDITY changed");
                    if (!m_deliver(box.Object, email)) return; // backpressure: leave UID and message untouched
                    box.Pending = header.Uid; // retain across ack failure, without delivering twice
                    token.ThrowIfCancellationRequested();
                    if (store.UidValidity != box.Validity) throw new IOException("IMAP UIDVALIDITY changed");
                    store.Delete(header.Uid, token);
                    box.Pending = 0; box.Cursor = header.Uid;
                }
            }
            if (headers.Count == 0) box.Cursor = last;
        }
        private bool ExactRecipient(string[] recipients, UUID id) => recipients.Length == 1
            && recipients[0].Equals(id + "@" + m_options.Domain, StringComparison.OrdinalIgnoreCase);

        public void Dispose()
        {
            IImapObjectMailStore active;
            lock (m_gate)
            {
                if (m_closed) return;
                m_closed = true; m_timer?.Dispose(); m_mailboxes.Clear(); m_poll.Clear();
                while (m_outbox.Count > 0) { m_outbox.Dequeue().Message.Dispose(); m_pendingSends--; }
                active = m_active;
            }
            m_stop.Cancel();
            try { active?.Dispose(); } catch { }
        }
    }

    // Measure the actual serialized MIME size without allocating an unbounded buffer.
    internal sealed class MimeByteLimit : Stream
    {
        private readonly int m_max;
        private long m_length;
        public MimeByteLimit(int max) { m_max = max; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => m_length;
        public override long Position { get => m_length; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (m_length + count > m_max) throw new InvalidDataException("MIME byte limit exceeded");
            m_length += count;
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
