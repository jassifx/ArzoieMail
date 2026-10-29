using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ArzoieMail.Management;

static class MailboxBehaviorTests
{
    public static async Task Run(IManagementChannel native, string username, string password, Action<bool, string> check)
    {
        Task<JsonElement> Send(string op, object data, string? actor = null) =>
            native.SendAsync(op, actor ?? username, password, data, CancellationToken.None);
        async Task Refuse(object data, int status, string? actor = null, string op = "accounts.behavior.update")
        {
            try { await Send(op, data, actor); throw new Exception("Unexpected mailbox behavior success."); }
            catch (ManagementError error) { check(error.Status == status, $"Mailbox behavior expected {status}, got {error.Status}."); }
        }
        string name = "behavior-" + Guid.NewGuid().ToString("N") + ".test";
        long domainId = (await Send("domains.create", new { fields = new { name, active = true } })).GetProperty("id").GetInt64();
        Exception? bodyFailure = null;
        try
        {
            async Task<long> Create(string local, int level = 0) => (await Send("accounts.create", new { domainId,
                fields = new { address = local + "@" + name, password, active = true, level } })).GetProperty("id").GetInt64();
            string mailbox = "mailbox@" + name, sink = "sink@" + name, manager = "manager@" + name, sender = "sender@" + name;
            long id = await Create("mailbox"); await Create("sink"); await Create("manager", 1); await Create("sender");
            long protectedId = await Create("protected", 2);
            object Route(long accountId) => new { domainId, id = accountId };
            Task<JsonElement> Update(object fields, string? actor = null) => Send("accounts.behavior.update", new { domainId, id, fields }, actor);
            var initial = await Send("accounts.behavior.read", Route(id), manager);
            check(!initial.GetProperty("forwardEnabled").GetBoolean() && !initial.GetProperty("autoReplyEnabled").GetBoolean(), "New mailboxes have no active forwarding/replies.");
            foreach (string operation in new[] { "accounts.behavior.read", "accounts.behavior.update" })
            {
                await Refuse(new { domainId, id, fields = new { forwardEnabled = true } }, 403, mailbox, operation);
                await Refuse(new { domainId = long.MaxValue, id, fields = new { forwardEnabled = true } }, 403, manager, operation);
                await Refuse(new { domainId, id = protectedId, fields = new { forwardEnabled = true } }, 403, manager, operation);
                await Refuse(new { domainId, id = long.MaxValue, fields = new { forwardEnabled = true } }, 404, manager, operation);
            }
            foreach (object invalid in new object[] {
                new { }, new { unknown = true }, new { forwardEnabled = "yes" }, new { forwardEnabled = 1 },
                new { forwardEnabled = true }, new { forwardAddress = mailbox, forwardEnabled = true },
                new { forwardAddress = "one@example.test,two@example.test" }, new { forwardAddress = "x@example.test\r\nBcc:x" },
                new { forwardAddress = "\u0001x@example.test" }, new { autoReplySubject = "subject\r\nBcc:x" },
                new { autoReplyBody = "body\0" }, new { autoReplySubject = new string('x', 256) }, new { autoReplyBody = new string('x', 16001) },
                new { autoReplyExpiresDate = "2027-02-30" }, new { autoReplyExpiresDate = "2027-0x-01" },
                new { autoReplyExpiresDate = "0000-01-01" }, new { autoReplyExpiresDate = "", autoReplyExpires = true },
                new { forwardEnabled = true, forwardAddress = sink, autoReplySubject = "bad\nsubject" }
            }) await Refuse(new { domainId, id, fields = invalid }, 422, manager);
            check((await Send("accounts.behavior.read", Route(id))).GetRawText() == initial.GetRawText(), "Invalid updates leave persisted behavior unchanged.");
            var cleared = await Update(new { autoReplyExpiresDate = "", autoReplyExpires = false }, manager);
            check(!cleared.GetProperty("autoReplyExpires").GetBoolean() && cleared.GetProperty("autoReplyExpiresDate").GetString()!.Length == 10,
                "Clearing disabled expiry preserves a valid database timestamp.");
            var configured = await Update(new { forwardEnabled = true, forwardAddress = sink, forwardKeepOriginal = true,
                forwardAbortSpamFlagged = true, autoReplyEnabled = true, autoReplySubject = "Away",
                autoReplyBody = "Away\r\nPlease contact the team. ✓", autoReplyExpires = true, autoReplyExpiresDate = "2099-01-01", autoReplyAbortSpamFlagged = true }, manager);
            check(configured.GetProperty("autoReplyBody").GetString()!.EndsWith('✓') && configured.GetProperty("forwardAddress").GetString() == sink,
                "Unicode reply and forwarding settings round-trip.");
            // All SMTP destinations are local disposable mailboxes. No external delivery probes.
            Console.WriteLine("Mailbox behavior: testing local forwarding and automatic reply delivery.");
            await Submit(sender, mailbox, "keep-original");
            await WaitFor(mailbox, password, 1); await WaitFor(sink, password, 1); await WaitFor(sender, password, 1);
            check((await Read(sender, password))[0].Contains("Away"), "Native API reply settings produce an SMTP automatic reply.");
            check((await Read(mailbox, password))[0].Contains("keep-original") && (await Read(sink, password))[0].Contains("keep-original"), "Forwarding with keep-original delivers both copies.");
            await Update(new { forwardKeepOriginal = false }, manager);
            await Submit(sender, mailbox, "forward-only"); await WaitFor(sink, password, 2);
            check((await Read(mailbox, password)).Count == 1 && (await Read(sender, password)).Count == 1,
                "Forward-only removes the local copy and an enabled reply suppresses repeat senders.");
            await Update(new { forwardEnabled = false, autoReplyEnabled = false }, manager);
            await Submit(sender, mailbox, "disabled"); await WaitFor(mailbox, password, 2);
            check((await Read(sink, password)).Count == 2 && (await Read(sender, password)).Count == 1, "Disabling settings invalidates cached forwarding/reply behavior.");
            await Update(new { autoReplyEnabled = true, autoReplyExpiresDate = "2000-01-01" }, manager);
            await Submit(sender, mailbox, "expired"); await WaitFor(mailbox, password, 3);
            check((await Read(sender, password)).Count == 1, "Expired automatic reply does not send.");
            await Update(new { autoReplyEnabled = true, autoReplyExpires = false }, manager);
            await Submit(sender, mailbox, "new-generation"); await WaitFor(mailbox, password, 4); await WaitFor(sender, password, 2);
            check(true, "Re-enabled replies reset the existing per-sender history.");
        }
        catch (Exception error) { bodyFailure = error; throw; }
        finally
        {
            try { await Send("domains.delete", new { id = domainId }); }
            catch (Exception cleanup) when (bodyFailure is not null)
            {
                throw new AggregateException("Mailbox behavior and fixture cleanup both failed.", bodyFailure, cleanup);
            }
        }
    }

    internal static async Task Submit(string sender, string recipient, string marker, string? contentType = null, string? body = null, string? inReplyTo = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var client = new TcpClient(); await client.ConnectAsync("127.0.0.1", 25, timeout.Token);
        using var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII);
        using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
        async Task Expect(int code)
        {
            string line;
            do { line = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("SMTP disconnected."); }
            while (line.Length > 3 && line[3] == '-');
            if (!line.StartsWith(code.ToString(), StringComparison.Ordinal)) throw new IOException("SMTP probe: " + line);
        }
        await Expect(220);
        foreach (string command in new[] { "EHLO behavior.test", $"MAIL FROM:<{sender}>", $"RCPT TO:<{recipient}>" })
        { await writer.WriteLineAsync(command); await Expect(250); }
        await writer.WriteLineAsync("DATA"); await Expect(354);
        string mimeHeader = contentType is null ? "" : $"MIME-Version: 1.0\r\nContent-Type: {contentType}\r\n";
        if (inReplyTo is not null) mimeHeader += $"In-Reply-To: {inReplyTo}\r\n";
        await writer.WriteLineAsync($"From: {sender}\r\nTo: {recipient}\r\nSubject: {marker}\r\n{mimeHeader}\r\n{body ?? marker}\r\n."); await Expect(250);
        await writer.WriteLineAsync("QUIT"); await Expect(221);
    }

    internal static async Task WaitFor(string address, string password, int count)
    {
        // Forward-only work can wait for the existing delivery manager's one-minute poll;
        // unlike an automatic reply, forwarding does not explicitly wake that manager.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        try { while ((await Read(address, password)).Count < count) await Task.Delay(1000, timeout.Token); }
        catch (OperationCanceledException error) { throw new IOException($"Timed out waiting for {count} local messages at {address}.", error); }
        // A mailbox delivery can precede completion of generated local forward/reply work.
        await Task.Delay(300, timeout.Token);
    }
    internal static async Task<List<string>> Read(string address, string password)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new TcpClient(); await client.ConnectAsync("127.0.0.1", 110, timeout.Token);
        using var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII);
        using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
        async Task<string> Reply()
        {
            string line = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("POP3 disconnected.");
            if (!line.StartsWith("+OK", StringComparison.Ordinal)) throw new IOException("POP3 probe: " + line);
            return line;
        }
        await Reply(); await writer.WriteLineAsync("USER " + address); await Reply();
        await writer.WriteLineAsync("PASS " + password); await Reply();
        await writer.WriteLineAsync("STAT"); int count = int.Parse((await Reply()).Split(' ')[1]);
        var messages = new List<string>();
        for (int index = 1; index <= count; ++index)
        {
            await writer.WriteLineAsync("RETR " + index); await Reply(); var text = new StringBuilder();
            string? line;
            while ((line = await reader.ReadLineAsync(timeout.Token)) != ".")
            { if (line is null) throw new IOException("Truncated POP3 message."); text.AppendLine(line); }
            messages.Add(text.ToString());
        }
        await writer.WriteLineAsync("QUIT"); await Reply(); return messages;
    }
}
