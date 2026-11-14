using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using MailKit.Security;
using MimeKit;
using OpenMetaverse;
using OpenSim.Region.CoreModules.Scripting.EmailModules;

static class ImapWireChecks
{
    public static void Run(UUID recipient)
    {
        using (var server = new Fixture(recipient, true))
        {
            var options = ImapChecks.Options(); options.Host = "127.0.0.1"; options.Port = server.Port;
            options.Security = SecureSocketOptions.None;
            using (var store = new MailKitObjectMailStore(options))
            {
                store.Open(CancellationToken.None);
                ImapChecks.Check(store.UidValidity == 7 && store.NextUid == 2, "Real SELECT UID state");
                var headers = store.FetchAfter(0, 1, CancellationToken.None);
                ImapChecks.Check(headers.Count == 1 && headers[0].Recipients.Single() == recipient + "@objects.test.invalid",
                    "Real bounded FETCH envelope");
                using var message = store.Read(1, 4096, CancellationToken.None);
                ImapChecks.Check(message.TextBody.Contains("Ω"), "Real MIME literal parsing");
                using var outbound = ImapChecks.Message(recipient, "append");
                store.Append(outbound, CancellationToken.None);
                store.Delete(1, CancellationToken.None);
            }
            server.CheckComplete();
            ImapChecks.Check(server.Commands.Any(command => command.Contains("UID EXPUNGE 1"))
                && !server.Commands.Any(command => Regex.IsMatch(command, @"^[^ ]+ EXPUNGE$")),
                "Acknowledgement must use UID EXPUNGE, never mailbox-wide EXPUNGE");
            ImapChecks.Check(server.Appended != null, "Real APPEND literal");
        }
        using (var server = new Fixture(recipient, true, large: true))
        {
            var options = ImapChecks.Options(); options.Host = "127.0.0.1"; options.Port = server.Port;
            options.Security = SecureSocketOptions.None;
            using var store = new MailKitObjectMailStore(options);
            store.Open(CancellationToken.None);
            try { using var ignored = store.Read(1, 1024, CancellationToken.None); throw new Exception("Oversized MIME accepted"); }
            catch (InvalidDataException) { }
            ImapChecks.Check(server.LargestLiteral == 1025, "Raw download must stop at max bytes plus one");
        }
        using (var server = new Fixture(recipient, false))
        {
            var options = ImapChecks.Options(); options.Host = "127.0.0.1"; options.Port = server.Port;
            options.Security = SecureSocketOptions.None;
            using var store = new MailKitObjectMailStore(options);
            try { store.Open(CancellationToken.None); throw new Exception("Missing UIDPLUS accepted"); }
            catch (NotSupportedException) { }
            ImapChecks.Check(!server.Commands.Any(command => command.Contains("EXPUNGE")), "Missing UIDPLUS must not delete mail");
        }
        using (var server = new Fixture(recipient, true, changeDuringSeek: true))
        {
            var options = ImapChecks.Options(); options.Host = "127.0.0.1"; options.Port = server.Port;
            options.Security = SecureSocketOptions.None;
            using var store = new MailKitObjectMailStore(options);
            store.Open(CancellationToken.None);
            try { store.FetchAfter(0, 8, CancellationToken.None); throw new Exception("Changed sequence index accepted"); }
            catch (IOException) { }
            ImapChecks.Check(!server.Commands.Any(command => command.Contains("STORE")), "Expunge during seek must retry without consuming mail");
        }
    }

    sealed class Fixture : IDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly Task worker;
        readonly byte[] message;
        readonly UUID recipient;
        readonly bool uidPlus;
        bool changeDuringSeek;
        TcpClient client;
        volatile bool disposed;
        public List<string> Commands = new();
        public byte[] Appended;
        public int LargestLiteral;
        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
        public Fixture(UUID id, bool supportsUidPlus, bool large = false, bool changeDuringSeek = false)
        {
            recipient = id; uidPlus = supportsUidPlus;
            this.changeDuringSeek = changeDuringSeek;
            using var mail = ImapChecks.Message(id, "wire");
            if (large) mail.Body = new TextPart("plain") { Text = new string('x', 6000) };
            using var data = new MemoryStream(); mail.WriteTo(data); message = data.ToArray();
            listener.Start(); worker = Task.Run(Serve);
        }
        void Serve()
        {
            try
            {
                using var connection = listener.AcceptTcpClient(); client = connection;
                using var stream = connection.GetStream(); stream.ReadTimeout = 10000; stream.WriteTimeout = 10000;
                string capabilities = "IMAP4rev1" + (uidPlus ? " UIDPLUS" : "");
                Write(stream, "* OK [CAPABILITY " + capabilities + "] fixture\r\n");
                string line;
                while ((line = ReadLine(stream)) != null)
                {
                    Commands.Add(line);
                    string tag = line.Split(' ')[0];
                    string command = line.Substring(tag.Length + 1);
                    if (command.StartsWith("CAPABILITY"))
                        Write(stream, "* CAPABILITY " + capabilities + "\r\n" + tag + " OK capability\r\n");
                    else if (command.StartsWith("LOGIN"))
                        Write(stream, tag + " OK [CAPABILITY " + capabilities + "] authenticated\r\n");
                    else if (command.StartsWith("LIST"))
                        Write(stream, "* LIST (\\Inbox) \"/\" INBOX\r\n" + tag + " OK list\r\n");
                    else if (command.StartsWith("SELECT"))
                        Write(stream, "* FLAGS (\\Seen \\Deleted)\r\n* 1 EXISTS\r\n* 0 RECENT\r\n"
                            + "* OK [UIDVALIDITY 7] valid\r\n* OK [UIDNEXT 2] next\r\n"
                            + "* OK [PERMANENTFLAGS (\\Seen \\Deleted \\*)] flags\r\n" + tag + " OK [READ-WRITE] selected\r\n");
                    else if (command.StartsWith("UID FETCH") && command.Contains("BODY.PEEK"))
                    {
                        var range = Regex.Match(command, @"<(\d+)\.(\d+)>");
                        int maximum = range.Success ? int.Parse(range.Groups[2].Value) : message.Length;
                        int count = Math.Min(maximum, message.Length); LargestLiteral = Math.Max(LargestLiteral, count);
                        Write(stream, "* 1 FETCH (UID 1 BODY[]<0> {" + count + "}\r\n");
                        stream.Write(message, 0, count);
                        Write(stream, ")\r\n" + tag + " OK body\r\n");
                    }
                    else if (command.StartsWith("UID FETCH") || command.StartsWith("FETCH"))
                    {
                        if (changeDuringSeek)
                        {
                            changeDuringSeek = false;
                            Write(stream, "* 1 EXPUNGE\r\n" + tag + " OK changed\r\n");
                            continue;
                        }
                        string envelope = "(\"Tue, 14 Nov 2023 22:13:20 +0000\" \"wire\" "
                            + "((NIL NIL \"sender\" \"test.invalid\")) NIL NIL "
                            + "((NIL NIL \"" + recipient + "\" \"objects.test.invalid\")) NIL NIL NIL \"<fixture@test.invalid>\")";
                        Write(stream, "* 1 FETCH (UID 1 FLAGS () RFC822.SIZE " + message.Length + " ENVELOPE " + envelope
                            + ")\r\n" + tag + " OK headers\r\n");
                    }
                    else if (command.StartsWith("UID STORE")) Write(stream, tag + " OK stored\r\n");
                    else if (command.StartsWith("UID EXPUNGE")) Write(stream, "* 1 EXPUNGE\r\n" + tag + " OK expunged\r\n");
                    else if (command.StartsWith("APPEND"))
                    {
                        var literal = Regex.Match(command, @"\{(\d+)(\+)?\}$");
                        if (!literal.Success) throw new Exception("Missing APPEND literal");
                        if (!literal.Groups[2].Success) Write(stream, "+ ready\r\n");
                        int count = int.Parse(literal.Groups[1].Value);
                        Appended = new byte[count]; stream.ReadExactly(Appended); ReadLine(stream);
                        Write(stream, tag + " OK [APPENDUID 7 2] appended\r\n");
                    }
                    else if (command.StartsWith("LOGOUT")) { Write(stream, "* BYE\r\n" + tag + " OK logout\r\n"); break; }
                    else throw new Exception("Unexpected fixture command: " + command.Split(' ')[0]);
                }
            }
            catch (Exception) when (disposed) { }
        }
        static string ReadLine(Stream stream)
        {
            using var bytes = new MemoryStream();
            int next;
            while ((next = stream.ReadByte()) != -1)
            {
                if (next == '\n') return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
                bytes.WriteByte((byte)next);
                if (bytes.Length > 10000) throw new Exception("Fixture line too long");
            }
            return null;
        }
        static void Write(Stream stream, string text) { byte[] data = Encoding.ASCII.GetBytes(text); stream.Write(data); stream.Flush(); }
        public void CheckComplete()
        {
            ImapChecks.Check(worker.Wait(3000), "IMAP fixture connection closed");
            worker.GetAwaiter().GetResult();
        }
        public void Dispose()
        {
            disposed = true; client?.Dispose(); listener.Stop();
            if (!worker.Wait(3000)) throw new Exception("IMAP fixture did not stop");
        }
    }
}
