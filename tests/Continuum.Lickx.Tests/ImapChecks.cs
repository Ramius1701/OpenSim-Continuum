using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using MimeKit;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.CoreModules.Scripting.EmailModules;
using OpenSim.Region.Framework.Interfaces;

static class ImapChecks
{
    const string Domain = "objects.test.invalid";
    public static void Run()
    {
        var objectID = UUID.Random();
        var other = UUID.Random();
        var store = new FakeInbox();
        store.Add(1, Message(objectID, "accepted Ω"));
        var suffix = Message(objectID, "suffix");
        suffix.To.Clear(); suffix.To.Add(MailboxAddress.Parse(objectID + "@evil" + Domain));
        store.Add(2, suffix);
        var multiple = Message(objectID, "multiple"); multiple.To.Add(MailboxAddress.Parse(other + "@" + Domain));
        store.Add(3, multiple);
        store.Add(4, Message(objectID, "too big")); store.Sizes[4] = 100000;
        var html = Message(objectID, "html"); html.Body = new TextPart("html") { Text = "<p>html</p>" };
        store.Add(5, html);
        store.Add(6, Message(other, "other object"));
        var accepted = new List<Email>();
        using (var transport = Transport(store, (id, mail) => { Check(id == objectID, "Destination ID"); accepted.Add(mail); return true; }))
        {
            transport.RequestPoll(objectID);
            Check(store.Opens == 0, "RequestPoll must not perform network I/O");
            transport.ProcessOnce(); transport.ProcessOnce();
            Check(accepted.Count == 1 && accepted[0].subject == "accepted Ω", "Exact single-recipient routing");
            Check(long.Parse(accepted[0].time, CultureInfo.InvariantCulture) == 1700000000, "LSL email time is Unix seconds");
            Check(store.Deleted.SetEquals(new uint[] { 1 }), "Only accepted mail may be acknowledged");
            Check(store.Reads.SequenceEqual(new uint[] { 1, 5 }), "Oversized/unrelated messages must not download");
        }

        store = new FakeInbox(); store.Add(1, Message(objectID, "backpressure"));
        bool capacity = false;
        int delivered = 0;
        using (var transport = Transport(store, (id, mail) => { if (!capacity) return false; delivered++; return true; }))
        {
            transport.RequestPoll(objectID); transport.ProcessOnce();
            Check(store.Deleted.Count == 0, "Full local queue must leave message on server");
            capacity = true; transport.ProcessOnce();
            Check(delivered == 1 && store.Deleted.Contains(1), "Backpressured message must retry");
        }

        store = new FakeInbox { FailDelete = 1 }; store.Add(1, Message(objectID, "ack retry"));
        delivered = 0;
        using (var transport = Transport(store, (id, mail) => { delivered++; return true; }))
        {
            transport.RequestPoll(objectID); transport.ProcessOnce(); transport.ProcessOnce();
            Check(delivered == 1 && store.DeleteAttempts == 2, "Ack failure must not redeliver the same UID");
            store.Validity++; store.Deleted.Clear(); store.Add(1, Message(objectID, "new mailbox"));
            transport.ProcessOnce();
            Check(delivered == 2, "UIDVALIDITY change must reset UID state");
        }

        store = new FakeInbox(); store.Add(1, Message(objectID, "removed")); delivered = 0;
        using (var transport = Transport(store, (id, mail) => { delivered++; return true; }))
        {
            store.BeforeRead = () => transport.RemoveMailbox(objectID);
            transport.RequestPoll(objectID); transport.ProcessOnce();
            Check(delivered == 0 && store.Deleted.Count == 0, "Mailbox removal during download must not consume mail");
        }

        store = new FakeInbox(); store.Add(1, Message(objectID, "validity race")); delivered = 0;
        using (var transport = Transport(store, (_, _) => { delivered++; return true; }))
        {
            store.BeforeRead = () => { store.Validity++; store.BeforeRead = null; };
            transport.RequestPoll(objectID); transport.ProcessOnce();
            Check(delivered == 0 && store.Deleted.Count == 0, "UIDVALIDITY change during download must stop delivery/ack");
            transport.ProcessOnce();
            Check(delivered == 1, "New UID context must be retried safely");
        }

        store = new FakeInbox(); store.Add(1, Message(objectID, "bounded")); store.Add(2, Message(other, "second"));
        var options = Options(); options.MaxMailboxes = 1; options.UidWindow = 1;
        var destinations = new List<UUID>();
        using (var transport = Transport(store, (id, mail) => { destinations.Add(id); return true; }, options))
        {
            transport.RequestPoll(objectID); transport.RequestPoll(other); transport.ProcessOnce();
            Check(store.Ranges.All(range => range.Maximum == 1), "Message fetch pages must be bounded");
            Check(destinations.SequenceEqual(new[] { objectID }), "Tracked mailbox cap");
            transport.RemoveMailbox(objectID); transport.RequestPoll(other);
            transport.ProcessOnce(); transport.ProcessOnce();
            Check(destinations.SequenceEqual(new[] { objectID, other }), "Removed mailboxes must release capacity");
        }

        store = new FakeInbox(); store.Add(1000000, Message(objectID, "sparse UID")); delivered = 0;
        using (var transport = Transport(store, (_, _) => { delivered++; return true; }))
        {
            transport.RequestPoll(objectID); transport.ProcessOnce();
            Check(delivered == 1, "Expunged UID gaps must not delay pending mail");
        }

        store = new FakeInbox { FailAppend = 1 };
        options = Options(); options.MaxPendingSends = 1; options.RetrySeconds = 1;
        using (var transport = Transport(store, (_, _) => true, options))
        {
            var mail = new Email { sender = objectID + "@" + Domain, subject = "send", message = "body Ω" };
            Check(transport.Enqueue(other, mail) && !transport.Enqueue(other, mail), "Outbound cap includes pending mail");
            transport.ProcessOnce(); transport.ProcessOnce();
            Check(store.AppendAttempts == 1, "Failed appends must respect retry delay");
            Thread.Sleep(1100);
            transport.ProcessOnce();
            Check(store.AppendAttempts == 2 && store.Sent.Count == 1 && store.AttemptIds.Distinct().Count() == 1,
                "Retry must retain stable Message-ID and message content");
            Check(transport.Enqueue(other, mail), "Successful send must release queue capacity");
            transport.Dispose();
            Check(!transport.Enqueue(other, mail), "Close stops new sends");
        }

        store = new FakeInbox(); options = Options(); options.MaxMessageBytes = 1024;
        using (var transport = Transport(store, (_, _) => true, options))
            Check(!transport.Enqueue(other, new Email { sender = objectID + "@" + Domain, subject = "x", message = new string('Ω', 800) }),
                "UTF-8 byte limits must reject oversized output");

        store = new FakeInbox();
        using (var entered = new ManualResetEventSlim())
        using (var transport = Transport(store, (_, _) => { throw new Exception("Late delivery"); }))
        {
            store.WaitOnOpen = entered;
            transport.RequestPoll(objectID);
            var running = Task.Run(transport.ProcessOnce);
            Check(entered.Wait(3000), "Worker started");
            transport.ProcessOnce();
            Check(store.Opens == 1, "Worker must be single-flight");
            transport.Dispose();
            Check(running.Wait(3000), "Close must cancel blocked I/O");
        }

        ModuleChecks(objectID);
        IntegrationChecks(objectID, other);
        ImapWireChecks.Run(objectID);
        Console.WriteLine("PASS: IMAP exact routing, bounded scans/queues, backpressure, ack retry, UIDVALIDITY, removal/shutdown, outbound retry and MailKit wire protocol.");
    }

    static void ModuleChecks(UUID id)
    {
        foreach (string section in new[] { "SMTP", "Email" })
        {
            var config = new IniConfigSource();
            config.AddConfig("Startup").Set("emailmodule", "DefaultEmailModule");
            var mail = config.AddConfig(section); mail.Set("enabled", true); mail.Set("enableEmailToSMTP", false);
            // Missing IMAP credentials disable just the optional transport.
            mail.Set("IMAPEnabled", true);
            using var module = new ModuleLifetime();
            module.Value.Initialise(config); module.Value.AddPartMailBox(id);
            module.Value.InsertEmail(id, new Email { subject = "wrong", sender = "a@test.invalid" });
            module.Value.InsertEmail(id, new Email { subject = "wanted", sender = "b@test.invalid" });
            Check(module.Value.GetNextEmail(id, "", "wanted")?.sender == "b@test.invalid", "Subject-only filter must be honored");
            Check(module.Value.GetNextEmail(id, "b@test.invalid", "wrong") == null, "Sender and subject are combined");
            module.Value.Close(); module.Value.InsertEmail(id, new Email { subject = "late" });
            Check(module.Value.GetNextEmail(id, "", "") == null, "Closed module rejects mailbox delivery");
        }
    }

    static void IntegrationChecks(UUID source, UUID recipient)
    {
        var config = new IniConfigSource(); config.AddConfig("Startup").Set("emailmodule", "DefaultEmailModule");
        var settings = config.AddConfig("SMTP"); settings.Set("enabled", true); settings.Set("enableEmailToSMTP", false);
        settings.Set("internal_object_host", Domain);
        using var module = new ModuleLifetime(); module.Value.Initialise(config);
        var scene = new Scene(new OpenSim.Framework.RegionInfo { RegionID = UUID.Random(), RegionName = "Object mail fixture" });
        var graph = typeof(Scene).GetField("m_sceneGraph", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(scene);
        var parts = (Dictionary<UUID, SceneObjectPart>)graph.GetType()
            .GetField("m_scenePartsByID", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(graph);
        void AddObject(UUID id)
        {
            var group = new SceneObjectGroup(UUID.Random(), new Vector3(10, 10, 25), PrimitiveBaseShape.CreateBox());
            group.UUID = id;
            typeof(EntityBase).GetField("m_scene", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(group, scene);
            parts.Add(id, group.RootPart);
        }
        AddObject(source); module.Value.AddRegion(scene);
        var inbox = new FakeInbox();
        var accept = typeof(EmailModule).GetMethod("TryAcceptImapEmail", BindingFlags.Instance | BindingFlags.NonPublic);
        using var transport = Transport(inbox, (id, mail) => (bool)accept.Invoke(module.Value, new object[] { id, mail }));
        typeof(EmailModule).GetField("m_imapTransport", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(module.Value, transport);
        module.Value.SendEmail(source, UUID.Random(), recipient + "@" + Domain, "remote", "body Ω");
        module.Value.SendEmail(source, UUID.Random(), recipient + "@evil" + Domain, "spoof", "blocked");
        transport.ProcessOnce();
        Check(inbox.Sent.Count == 1, "Cross-simulator branch must append once; hostname suffixes are not object domains");
        using (var outbound = MimeMessage.Load(new MemoryStream(inbox.Sent[0])))
            Check(outbound.To.Mailboxes.Single().Address == recipient + "@" + Domain
                && outbound.From.Mailboxes.Single().Address == source + "@" + Domain && outbound.TextBody.Contains("Object-Name:"),
                "Outbound envelope and object context");
        AddObject(recipient); module.Value.AddPartMailBox(recipient);
        module.Value.SendEmail(source, UUID.Random(), recipient + "@" + Domain, "local", "local body");
        Check(module.Value.GetNextEmail(recipient, "", "local") != null, "Same-simulator mail remains local");
        inbox.Add(1, Message(recipient, "incoming"));
        transport.ProcessOnce();
        Check(module.Value.GetNextEmail(recipient, "", "incoming") != null && inbox.Deleted.Contains(1),
            "IMAP mail must enter the real local script queue before acknowledgement");
        module.Value.RemoveRegion(scene);
        inbox.Add(2, Message(recipient, "unloaded")); transport.RequestPoll(recipient); transport.ProcessOnce();
        Check(!inbox.Deleted.Contains(2), "Region unload must not acknowledge mail for departed objects");
    }

    internal static ImapObjectMailOptions Options() => new()
    { Host = "test.invalid", User = "test", Password = "fixture", Domain = Domain, UidWindow = 8, MaxMessageBytes = 4096 };
    static ImapObjectMailTransport Transport(FakeInbox inbox, Func<UUID, Email, bool> deliver, ImapObjectMailOptions options = null) =>
        new(options ?? Options(), deliver, _ => { }, () => new FakeStore(inbox));
    internal static MimeMessage Message(UUID id, string subject) => new()
    {
        Date = DateTimeOffset.FromUnixTimeSeconds(1700000000), Subject = subject, MessageId = "fixture-" + UUID.Random() + "@test.invalid",
        From = { MailboxAddress.Parse("sender@test.invalid") }, To = { MailboxAddress.Parse(id + "@" + Domain) },
        Body = new TextPart("plain") { Text = "body Ω" }
    };
    internal static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    sealed class ModuleLifetime : IDisposable { public EmailModule Value = new(); public void Dispose() => Value.Close(); }

    sealed class FakeInbox
    {
        public Dictionary<uint, byte[]> Messages = new();
        public Dictionary<uint, uint> Sizes = new();
        public HashSet<uint> Deleted = new();
        public List<uint> Reads = new();
        public List<(uint After, int Maximum)> Ranges = new();
        public List<string> AttemptIds = new();
        public List<byte[]> Sent = new();
        public uint Validity = 7, Next = 1;
        public int Opens, FailDelete, FailAppend, DeleteAttempts, AppendAttempts;
        public Action BeforeRead;
        public ManualResetEventSlim WaitOnOpen;
        public void Add(uint uid, MimeMessage message)
        {
            using (message)
            using (var bytes = new MemoryStream())
            { message.WriteTo(bytes); Messages[uid] = bytes.ToArray(); Sizes[uid] = (uint)bytes.Length; }
            Next = Math.Max(Next, uid + 1);
        }
    }
    sealed class FakeStore : IImapObjectMailStore
    {
        readonly FakeInbox inbox;
        public FakeStore(FakeInbox state) { inbox = state; }
        public void Open(CancellationToken token)
        {
            Interlocked.Increment(ref inbox.Opens);
            if (inbox.WaitOnOpen != null)
            { inbox.WaitOnOpen.Set(); token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); }
        }
        public uint UidValidity => inbox.Validity;
        public uint NextUid => inbox.Next;
        public IList<ImapObjectMailHeader> FetchAfter(uint after, int maximum, CancellationToken token)
        {
            inbox.Ranges.Add((after, maximum));
            return inbox.Messages.Where(entry => entry.Key > after).OrderBy(entry => entry.Key).Take(maximum).Select(entry =>
            {
                using var message = MimeMessage.Load(new MemoryStream(entry.Value));
                return new ImapObjectMailHeader { Uid = entry.Key, Size = inbox.Sizes[entry.Key],
                    Deleted = inbox.Deleted.Contains(entry.Key), Recipients = message.To.Mailboxes.Select(a => a.Address).ToArray() };
            }).ToArray();
        }
        public MimeMessage Read(uint uid, int maxBytes, CancellationToken token)
        { inbox.Reads.Add(uid); inbox.BeforeRead?.Invoke(); return MimeMessage.Load(new MemoryStream(inbox.Messages[uid])); }
        public void Append(MimeMessage message, CancellationToken token)
        {
            inbox.AppendAttempts++; inbox.AttemptIds.Add(message.MessageId);
            if (inbox.FailAppend-- > 0) throw new IOException("Injected append failure");
            using var data = new MemoryStream(); message.WriteTo(data); inbox.Sent.Add(data.ToArray());
        }
        public void Delete(uint uid, CancellationToken token)
        {
            inbox.DeleteAttempts++;
            if (inbox.FailDelete-- > 0) throw new IOException("Injected ack failure");
            inbox.Deleted.Add(uid);
        }
        public void Dispose() { }
    }
}
