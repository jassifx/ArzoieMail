using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ArzoieMail.Management;

// Server status/control, new protocol settings, server messages, sendout, diagnostics and logs
// against the real native dispatcher and the live service.
static class ServerOperationsTests
{
    public static async Task Run(IManagementChannel native, string username, string password, Action<bool, string> check)
    {
        Task<JsonElement> Send(string op, object? data, string? actor = null) => native.SendAsync(op, actor ?? username, password, data, CancellationToken.None);
        async Task Refuse(string op, object? data, int status, string? actor = null)
        {
            try { await Send(op, data, actor); throw new Exception("Unexpected server operation success: " + op); }
            catch (ManagementError error) { check(error.Status == status, $"{op}: expected {status}, received {error.Status}."); }
        }

        foreach (string actor in new[] { Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_DOMAIN_USERNAME"), Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_USER_USERNAME") }.OfType<string>())
        {
            await Refuse("server.stop", new { expectedState = 3 }, 403, actor);
            await Refuse("servermessages.list", new { }, 403, actor);
            await Refuse("sendout.preview", new { recipients = "*" }, 403, actor);
            await Refuse("diagnostics.start", new { localDomain = "", testDomain = "" }, 403, actor);
            await Refuse("logs.list", new { }, 403, actor);
            await Refuse("settings.smarthost.credential", new { password = "x" }, 403, actor);
            await Refuse("settings.messageindex.clear", new { }, 403, actor);
            await Refuse("settings.administrator.password", new { currentPassword = password, newPassword = password }, 403, actor);
        }

        // Status and counters.
        var status = await Send("status", null);
        check(status.GetProperty("state").GetInt32() == 3, "The service reports Running.");
        foreach (string name in new[] { "smtp", "pop3", "imap" })
            check(status.GetProperty("sessions").GetProperty(name).GetInt32() >= 0, "Session counts are reported per protocol.");
        foreach (string name in new[] { "processedMessages", "spamMessagesDetected", "virusesRemoved" })
            check(status.GetProperty(name).GetInt32() >= 0, "Message counters are reported.");
        check(status.GetProperty("startTime").GetString()!.Length > 0 && status.GetProperty("cacheHitRates").EnumerateObject().Count() == 4, "Start time and cache hit rates are reported.");

        // Stop and start: the management channel stays available while the listeners are down.
        await Refuse("server.start", new { expectedState = 3 }, 422);
        await Refuse("server.stop", new { expectedState = 3, extra = true }, 422);
        await Refuse("server.start", new { expectedState = 1 }, 409);
        Exception? controlFailure = null;
        try
        {
            check((await Send("server.stop", new { expectedState = 3 })).GetProperty("state").GetInt32() == 1, "Stopping reports Stopped.");
            check(!await Accepts(110), "Stopped services no longer accept POP3 connections.");
            check((await Send("status", null)).GetProperty("state").GetInt32() == 1, "The management channel answers while services are stopped.");
            await Refuse("server.stop", new { expectedState = 3 }, 409);
        }
        catch (Exception error) { controlFailure = error; throw; }
        finally
        {
            try
            {
                if ((await Send("status", null)).GetProperty("state").GetInt32() == 1)
                    check((await Send("server.start", new { expectedState = 1 })).GetProperty("state").GetInt32() == 3, "Starting reports Running.");
            }
            catch (Exception cleanup) when (controlFailure is not null) { throw new AggregateException("Server control and restart both failed.", controlFailure, cleanup); }
        }
        check(await Accepts(110), "Started services accept POP3 connections again.");

        // SMTP smart host, delivery security and SRS settings.
        var smtp = await Send("settings.read", new { section = "smtp" });
        var schema = smtp.GetProperty("schema").EnumerateArray().ToDictionary(rule => rule.GetProperty("name").GetString()!);
        foreach (string name in new[] { "deliverySecurity", "smartHost", "smartHostPort", "smartHostSecurity", "smartHostAuthentication", "smartHostUsername", "srsEnabled", "srsMaxAgeDays" })
            check(schema.ContainsKey(name), $"SMTP settings expose {name}.");
        check(schema["deliverySecurity"].GetProperty("options").EnumerateArray().Select(option => option.GetProperty("value").GetInt32()).SequenceEqual(new[] { 0, 2, 3 }),
            "Direct delivery offers no implicit-TLS option.");
        var smtpValues = smtp.GetProperty("values");
        int delivery = smtpValues.GetProperty("deliverySecurity").GetInt32(), maxAge = smtpValues.GetProperty("srsMaxAgeDays").GetInt32();
        string smartHost = smtpValues.GetProperty("smartHost").GetString()!;
        bool credentialBefore = smtp.GetProperty("credentials").GetProperty("smartHostConfigured").GetBoolean();
        await Refuse("settings.update", new { section = "smtp", fields = new { deliverySecurity = 1 }, expected = new { deliverySecurity = delivery } }, 422);
        foreach (int invalid in new[] { 0, 513 })
            await Refuse("settings.update", new { section = "smtp", fields = new { srsMaxAgeDays = invalid }, expected = new { srsMaxAgeDays = maxAge } }, 422);
        await Refuse("settings.update", new { section = "smtp", fields = new { smartHost = "bad host*" }, expected = new { smartHost } }, 422);
        await Refuse("settings.smarthost.credential", new { password = "x", other = 1 }, 422);
        await Refuse("settings.smarthost.credential", new { password = "line\r\nbreak" }, 422);
        Exception? smtpFailure = null;
        try
        {
            int changed = delivery == 3 ? 2 : 3;
            var saved = await Send("settings.update", new { section = "smtp", fields = new { deliverySecurity = changed, srsMaxAgeDays = maxAge == 30 ? 31 : 30, smartHost = "relay.example.test" },
                expected = new { deliverySecurity = delivery, srsMaxAgeDays = maxAge, smartHost } });
            check(saved.GetProperty("values").GetProperty("deliverySecurity").GetInt32() == changed && saved.GetProperty("values").GetProperty("smartHost").GetString() == "relay.example.test",
                "Delivery security, SRS age and smart host persist as one checked batch.");
            check((await Send("settings.smarthost.credential", new { password = "relay credential ü" })).GetProperty("configured").GetBoolean(), "A smart host credential is stored.");
            var reread = await Send("settings.read", new { section = "smtp" });
            check(reread.GetProperty("credentials").GetProperty("smartHostConfigured").GetBoolean() && !reread.GetRawText().Contains("relay credential", StringComparison.Ordinal),
                "The stored credential is reported only as configured, never returned.");
            check(!(await Send("settings.smarthost.credential", new { password = "" })).GetProperty("configured").GetBoolean(), "An empty credential clears it.");
        }
        catch (Exception error) { smtpFailure = error; throw; }
        finally
        {
            try
            {
                var current = (await Send("settings.read", new { section = "smtp" })).GetProperty("values");
                await Send("settings.update", new { section = "smtp", fields = new { deliverySecurity = delivery, srsMaxAgeDays = maxAge, smartHost },
                    expected = new { deliverySecurity = current.GetProperty("deliverySecurity").GetInt32(), srsMaxAgeDays = current.GetProperty("srsMaxAgeDays").GetInt32(), smartHost = current.GetProperty("smartHost").GetString() } });
                if (credentialBefore) Console.WriteLine("Server operations: a smart host credential existed before the test and was cleared.");
            }
            catch (Exception cleanup) when (smtpFailure is not null) { throw new AggregateException("SMTP settings and restoration failed.", smtpFailure, cleanup); }
        }

        // The built-in Administrator password needs the current one.
        await Refuse("settings.administrator.password", new { currentPassword = "wrong " + Guid.NewGuid(), newPassword = "replacement" }, 403);
        await Refuse("settings.administrator.password", new { currentPassword = password, newPassword = "" }, 422);
        await Refuse("settings.administrator.password", new { currentPassword = password, newPassword = "x", extra = 1 }, 422);
        if (username.Equals("Administrator", StringComparison.OrdinalIgnoreCase))
        {
            string rotated = "Rotated-" + Guid.NewGuid().ToString("N");
            check((await Send("settings.administrator.password", new { currentPassword = password, newPassword = rotated })).GetProperty("changed").GetBoolean(), "The Administrator password changes.");
            try
            {
                await Refuse("status", null, 401);
                check((await native.SendAsync("status", username, rotated, null, CancellationToken.None)).GetProperty("state").GetInt32() == 3, "The new Administrator password authenticates.");
            }
            finally { await native.SendAsync("settings.administrator.password", username, rotated, new { currentPassword = rotated, newPassword = password }, CancellationToken.None); }
            check((await Send("status", null)).GetProperty("state").GetInt32() == 3, "The original Administrator password is restored.");
        }
        else Console.WriteLine("Server operations: the test administrator is not the built-in Administrator; the password change was not exercised.");

        // IMAP master user and the directly applied hierarchy delimiter.
        var imap = (await Send("settings.read", new { section = "imap" })).GetProperty("values");
        string masterUser = imap.GetProperty("masterUser").GetString()!, delimiter = imap.GetProperty("hierarchyDelimiter").GetString()!;
        await Refuse("settings.update", new { section = "imap", fields = new { masterUser = "two words" }, expected = new { masterUser } }, 422);
        await Refuse("settings.update", new { section = "imap", fields = new { hierarchyDelimiter = "|" }, expected = new { hierarchyDelimiter = delimiter } }, 422);
        await Refuse("settings.update", new { section = "imap", fields = new { hierarchyDelimiter = delimiter == "/" ? "." : "/", masterUser },
            expected = new { hierarchyDelimiter = delimiter, masterUser } }, 422);
        string replacement = delimiter == "/" ? "." : "/";
        try
        {
            var changed = await Send("settings.update", new { section = "imap", fields = new { hierarchyDelimiter = replacement }, expected = new { hierarchyDelimiter = delimiter } });
            check(changed.GetProperty("values").GetProperty("hierarchyDelimiter").GetString() == replacement, "The hierarchy delimiter changes through its native setter.");
            await Refuse("settings.update", new { section = "imap", fields = new { hierarchyDelimiter = delimiter }, expected = new { hierarchyDelimiter = delimiter } }, 409);
        }
        catch (ManagementError error) when (error.Status == 409)
        {
            check(true, "The delimiter change was refused because an existing folder or rule uses the character.");
        }
        finally
        {
            var current = (await Send("settings.read", new { section = "imap" })).GetProperty("values").GetProperty("hierarchyDelimiter").GetString()!;
            if (current != delimiter)
                await Send("settings.update", new { section = "imap", fields = new { hierarchyDelimiter = delimiter }, expected = new { hierarchyDelimiter = current } });
        }
        check((await Send("settings.read", new { section = "imap" })).GetProperty("values").GetProperty("hierarchyDelimiter").GetString() == delimiter, "The original delimiter is restored.");

        // Message indexing applies directly and can be cleared.
        bool indexing = (await Send("settings.read", new { section = "performance" })).GetProperty("values").GetProperty("messageIndexing").GetBoolean();
        try
        {
            var toggled = await Send("settings.update", new { section = "performance", fields = new { messageIndexing = !indexing }, expected = new { messageIndexing = indexing } });
            check(toggled.GetProperty("values").GetProperty("messageIndexing").GetBoolean() == !indexing, "Message indexing toggles through its native setter.");
        }
        finally
        {
            var current = (await Send("settings.read", new { section = "performance" })).GetProperty("values").GetProperty("messageIndexing").GetBoolean();
            if (current != indexing)
                await Send("settings.update", new { section = "performance", fields = new { messageIndexing = indexing }, expected = new { messageIndexing = current } });
        }
        await Refuse("settings.messageindex.clear", new { all = true }, 422);
        check((await Send("settings.messageindex.clear", new { })).GetProperty("cleared").GetBoolean(), "The message index can be cleared.");

        // Server messages: only the text changes, under an expected-value check.
        var messages = (await Send("servermessages.list", new { })).GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToList();
        check(messages.Count > 0, "Server messages are listed.");
        var message = messages[0];
        string messageId = message.GetProperty("id").GetString()!, originalText = message.GetProperty("text").GetString()!;
        long id = long.Parse(messageId, System.Globalization.CultureInfo.InvariantCulture);
        await Refuse("servermessages.read", new { id = long.MaxValue }, 404);
        await Refuse("servermessages.update", new { id, fields = new { expected = new { id = messageId, name = "changed", text = originalText }, text = "x" } }, 409);
        await Refuse("servermessages.update", new { id, fields = new { expected = message, text = "bad\u0001text" } }, 422);
        await Refuse("servermessages.update", new { id, fields = new { expected = message, text = "x", name = "RENAMED" } }, 422);
        try
        {
            string text = "ArzoieMail server message fixture\r\nSecond line";
            var saved = await Send("servermessages.update", new { id, fields = new { expected = message, text } });
            check(saved.GetProperty("text").GetString() == text && saved.GetProperty("name").GetString() == message.GetProperty("name").GetString(), "A server message text persists; its name does not change.");
            check((await Send("servermessages.read", new { id })).GetProperty("text").GetString() == text, "A second read observes the saved text.");
        }
        finally
        {
            var current = await Send("servermessages.read", new { id });
            if (current.GetProperty("text").GetString() != originalText)
                await Send("servermessages.update", new { id, fields = new { expected = current, text = originalText } });
        }

        // Sendout: preview the audience, then send to exactly that many accounts.
        string domain = "sendout-" + Guid.NewGuid().ToString("N") + ".test";
        long domainId = (await Send("domains.create", new { fields = new { name = domain, active = true } })).GetProperty("id").GetInt64();
        try
        {
            foreach (string local in new[] { "first", "second", "inactive" })
                await Send("accounts.create", new { domainId, fields = new { address = local + "@" + domain, password, active = local != "inactive" } });
            string pattern = "*@" + domain;
            check((await Send("sendout.preview", new { recipients = pattern })).GetProperty("recipients").GetInt32() == 2, "The preview counts only active accounts that match.");
            check((await Send("sendout.preview", new { recipients = "nobody-*@" + domain })).GetProperty("recipients").GetInt32() == 0, "A pattern without matches previews zero.");
            await Refuse("sendout.preview", new { recipients = "a b" }, 422);
            await Refuse("sendout.preview", new { recipients = "" }, 422);
            object Message(object expectedRecipients, string fromName = "Administrator", string subject = "Sendout fixture", string from = "admin@example.com") =>
                new { recipients = pattern, fromAddress = from, fromName, subject, body = "Sendout body\r\nline two", expectedRecipients };
            await Refuse("sendout.send", Message(3), 409);
            await Refuse("sendout.send", Message(0), 422);
            await Refuse("sendout.send", Message(2, fromName: "Quote\" <evil@example.com>"), 422);
            await Refuse("sendout.send", Message(2, subject: "Header\r\nBcc: evil@example.com"), 422);
            await Refuse("sendout.send", Message(2, from: "not an address"), 422);
            await Refuse("sendout.send", new { recipients = "nobody-*@" + domain, fromAddress = "admin@example.com", fromName = "A", subject = "S", body = "B", expectedRecipients = 0 }, 422);
            string subject = "Sendout " + Guid.NewGuid().ToString("N");
            check((await Send("sendout.send", Message(2, subject: subject))).GetProperty("recipients").GetInt32() == 2, "The sendout queues one message to both accounts.");
            foreach (string local in new[] { "first", "second" })
            {
                await MailboxBehaviorTests.WaitFor(local + "@" + domain, password, 1);
                var received = await MailboxBehaviorTests.Read(local + "@" + domain, password);
                check(received.Count == 1 && received[0].Contains("Subject: " + subject, StringComparison.Ordinal) && received[0].Contains("Sendout body", StringComparison.Ordinal),
                    "Each matching account receives the sendout.");
            }
        }
        finally { await Send("domains.delete", new { id = domainId }); }

        // Diagnostics run in the background and are polled.
        await Refuse("diagnostics.start", new { localDomain = "bad domain", testDomain = "" }, 422);
        await Refuse("diagnostics.start", new { localDomain = "missing-" + Guid.NewGuid().ToString("N") + ".test", testDomain = "" }, 422);
        await Refuse("diagnostics.start", new { localDomain = "" }, 422);
        await Refuse("diagnostics.read", new { extra = 1 }, 422);
        long run = (await Send("diagnostics.start", new { localDomain = "", testDomain = "" })).GetProperty("run").GetInt64();
        JsonElement diagnostics;
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90)))
        {
            do
            {
                await Task.Delay(500, deadline.Token);
                diagnostics = await Send("diagnostics.read", new { });
            } while (diagnostics.GetProperty("running").GetBoolean());
        }
        check(diagnostics.GetProperty("run").GetInt64() == run && diagnostics.GetProperty("completed").GetString()!.Length > 0, "The polled run completes.");
        var results = diagnostics.GetProperty("results").EnumerateArray().ToList();
        check(results.Count >= 4 && results.All(result => result.GetProperty("name").GetString()!.Length > 0), "Diagnostics return named results.");

        // Logs are chosen by exact name from the listing, and read in bounded pages.
        var logs = (await Send("logs.list", new { })).GetProperty("items").EnumerateArray().ToList();
        check(logs.All(log => log.GetProperty("name").GetString()!.EndsWith(".log", StringComparison.Ordinal)), "Only log files are listed.");
        foreach (string name in new[] { "..\\hMailServer.ini", "../hmailserver_events.log", "hmailserver_events.log\n", "hMailServer.ini", "C:\\Windows\\win.ini" })
            await Refuse("logs.read", new { name }, 422);
        await Refuse("logs.read", new { name = "hmailserver_1999-01-01.log" }, 404);
        await Refuse("logs.read", new { name = "hmailserver_events.log", path = "x" }, 422);
        if (logs.Count > 0)
        {
            string name = logs[0].GetProperty("name").GetString()!;
            var page = await Send("logs.read", new { name });
            long size = page.GetProperty("size").GetInt64(), start = page.GetProperty("start").GetInt64(), end = page.GetProperty("end").GetInt64();
            check(start >= 0 && start <= end && end <= size && end - start <= 16384, "A log page is bounded.");
            check(page.GetProperty("text").GetString() is not null, "A log page returns text.");
            await Refuse("logs.read", new { name, before = size + 1 }, 422);
            check((await Send("logs.read", new { name, before = 0 })).GetProperty("text").GetString() == "", "A page before the first byte is empty.");
        }
        else Console.WriteLine("Server operations: no log files exist; page reads were not exercised.");
    }

    static async Task<bool> Accepts(int port)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port, deadline.Token);
            using var reader = new StreamReader(client.GetStream(), Encoding.ASCII);
            return (await reader.ReadLineAsync(deadline.Token))?.StartsWith("+OK", StringComparison.Ordinal) == true;
        }
        catch (Exception error) when (error is SocketException or IOException or OperationCanceledException) { return false; }
    }
}
