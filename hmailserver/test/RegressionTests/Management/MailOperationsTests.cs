using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ArzoieMail.Management;

static class MailOperationsTests
{
    public static async Task Run(IManagementChannel native, string username, string password, Action<bool, string> check)
    {
        Task<JsonElement> Send(string op, object data, string? actor = null) => native.SendAsync(op, actor ?? username, password, data, CancellationToken.None);
        async Task Refuse(string op, object data, int status, string? actor = null)
        {
            try { await Send(op, data, actor); throw new Exception("Unexpected mail-operations success: " + op); }
            catch (ManagementError error) { check(error.Status == status, $"{op}: expected {status}, got {error.Status}."); }
        }
        string tag = "mailops-" + Guid.NewGuid().ToString("N"), domain = tag + ".test";
        long domainId = (await Send("domains.create", new { fields = new { name = domain, active = true } })).GetProperty("id").GetInt64();
        long? routeId = null, globalRuleId = null;
        try
        {
            string sender = "sender@" + domain, sink = "sink@" + domain, manager = "manager@" + domain;
            async Task<long> Create(string address, int level = 0) => (await Send("accounts.create", new { domainId, fields = new { address, password, active = true, level } })).GetProperty("id").GetInt64();
            await Create(sender); long accountId = await Create(sink); await Create(manager, 1); long protectedId = await Create("protected@" + domain, 2);
            foreach (string actor in new[] { sender, manager })
                foreach (string op in new[] { "queue.list", "queue.wake", "queue.read", "queue.retry", "queue.delete", "routes.list", "routes.create", "routes.update", "routes.delete", "routeaddresses.list", "rules.list" })
                    await Refuse(op, new { id = 1, routeId = 1, fields = new { } }, 403, actor);
            await Refuse("queue.list", new { limit = 101 }, 422);
            long storedId = long.Parse(Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_STORED_ID") ?? throw new Exception("Seed the stored-message queue fixture."));
            foreach (string op in new[] { "queue.read", "queue.retry", "queue.delete" })
            {
                await Refuse(op, new { id = long.MaxValue }, 404);
                await Refuse(op, new { id = storedId }, 404);
            }
            await Refuse("queue.wake", new { unexpected = true }, 422);
            await Send("queue.wake", new { }); check(true, "Queue wake accepts an explicit empty request.");
            foreach (object fields in new object[] { new { domain, host = "https://bad.test/" }, new { domain, host = "bad\r\nhost" },
                new { domain, host = "127.0.0.1", port = 0 }, new { domain, host = "127.0.0.1", security = 4 },
                new { domain, host = "127.0.0.1", requiresAuth = true }, new { domain, host = "127.0.0.1", unexpected = true } })
                await Refuse("routes.create", new { fields }, 422);
            var route = await Send("routes.create", new { fields = new { domain = "relay-" + domain, host = "127.0.0.1" } });
            routeId = route.GetProperty("id").GetInt64();
            check(!route.GetProperty("allAddresses").GetBoolean() && route.GetProperty("security").GetInt32() == 3, "Route defaults require STARTTLS and listed recipients.");
            string secret = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            await Send("routes.update", new { id = routeId, fields = new { requiresAuth = true, username = "relay-user", password = secret } });
            route = await Send("routes.read", new { id = routeId });
            check(route.GetProperty("passwordConfigured").GetBoolean() && !route.TryGetProperty("password", out _) && !route.GetRawText().Contains(secret), "Route password is write-only.");
            await Refuse("routes.update", new { id = routeId, fields = new { security = 0 } }, 422);
            await Refuse("routes.update", new { id = routeId, fields = new { security = 2 } }, 422);
            check((await Send("routes.read", new { id = routeId })).GetProperty("security").GetInt32() == 3, "Invalid route updates are not saved.");
            await Send("routes.update", new { id = routeId, fields = new { description = "Updated" } });
            check((await Send("routes.read", new { id = routeId })).GetProperty("passwordConfigured").GetBoolean(), "Omitted route passwords are preserved.");
            await Refuse("routes.update", new { id = routeId, fields = new { domain = "rename.test" } }, 422);
            var address = await Send("routeaddresses.create", new { routeId, fields = new { address = sink } }); long addressId = address.GetProperty("id").GetInt64();
            await Refuse("routeaddresses.create", new { routeId, fields = new { address = sink.ToUpperInvariant() } }, 422);
            await Refuse("routeaddresses.create", new { routeId, fields = new { address = "bad\r\nx@test" } }, 422);
            await Refuse("routeaddresses.read", new { routeId = long.MaxValue, id = addressId }, 404);
            check((await Send("routeaddresses.list", new { routeId, search = "SINK@" })).GetProperty("total").GetInt64() == 1, "Route recipient list/search persists.");
            await Send("routeaddresses.delete", new { routeId, id = addressId });
            await Refuse("routeaddresses.read", new { routeId, id = addressId }, 404);
            await Send("routes.update", new { id = routeId, fields = new { requiresAuth = false, password = "" } });
            check(!(await Send("routes.read", new { id = routeId })).GetProperty("passwordConfigured").GetBoolean(), "Route secrets can be explicitly cleared when authentication is off.");

            object Scope(long id) => new { domainId, accountId, id };
            async Task<JsonElement> RuleUpdate(long id, object fields) => await Send("rules.update", new { domainId, accountId, id, fields }, manager);
            foreach (string op in new[] { "rules.list", "rules.create", "rules.read", "rules.update", "rules.delete" })
            {
                await Refuse(op, new { domainId, accountId, id = 1, fields = new { name = tag } }, 403, sender);
                await Refuse(op, new { domainId = long.MaxValue, accountId, id = 1, fields = new { name = tag } }, 403, manager);
                await Refuse(op, new { domainId, accountId = protectedId, id = 1, fields = new { name = tag } }, 403, manager);
            }
            var draft = await Send("rules.create", new { domainId, accountId, fields = new { name = tag } }, manager); long ruleId = draft.GetProperty("id").GetInt64();
            check(!draft.GetProperty("active").GetBoolean(), "Rules start as disabled drafts.");
            await Refuse("rules.update", new { domainId, accountId, id = ruleId, fields = new { active = true } }, 422, manager);
            object criterion = new { field = 4, header = "", match = 2, value = tag };
            foreach (object fields in new object[] { new { criteria = "bad" }, new { actions = new[] { new { type = 5 } } }, new { actions = new[] { new { type = 10 } } },
                new { criteria = new[] { new { field = 4, match = 5, value = "(a+)+$" } } },
                new { actions = new[] { new { type = 7, header = "X-hMailServer-LoopCount", value = "0" } } },
                new { actions = new[] { new { type = 7, header = "Subject", value = "bad\r\nBcc:x" } } },
                new { actions = new[] { new { type = 2, to = sink } } },
                new { criteria = Enumerable.Repeat(criterion, 21).ToArray() } })
                await Refuse("rules.update", new { domainId, accountId, id = ruleId, fields }, 422, manager);
            await Refuse("rules.update", new { domainId, accountId, id = ruleId, fields = new { actions = new[] { new { type = 8, routeId } } } }, 403, manager);
            check((await Send("rules.read", Scope(ruleId), manager)).GetProperty("criteria").GetArrayLength() == 0, "Invalid draft changes are not partially persisted.");
            var actionDraft = await RuleUpdate(ruleId, new { actions = new object[] {
                new { type = 1 }, new { type = 2, to = sender, abortSpam = true },
                new { type = 3, fromAddress = sink, fromName = "Mail operations", subject = "Reply", body = "First line\r\nSecond line", abortSpam = true },
                new { type = 4, folder = "INBOX.Processed" }, new { type = 9 }, new { type = 6 } } });
            check(actionDraft.GetProperty("actions").EnumerateArray().Select(a => a.GetProperty("type").GetInt32()).SequenceEqual(new[] { 1, 2, 3, 4, 9, 6 })
                && actionDraft.GetProperty("actions")[2].GetProperty("body").GetString() == "First line\r\nSecond line", "Supported draft actions retain order and reply text without activation.");
            await RuleUpdate(ruleId, new { criteria = new[] { criterion }, actions = new object[] { new { type = 7, header = "X-MailOps-Local", value = "yes" }, new { type = 6 } } });
            await RuleUpdate(ruleId, new { active = true });
            await Refuse("rules.update", new { domainId, accountId, id = ruleId, fields = new { name = "active edit" } }, 409, manager);
            await Refuse("rules.read", new { id = ruleId }, 404);
            var global = await Send("rules.create", new { fields = new { name = tag, criteria = new[] { criterion }, actions = new[] { new { type = 7, header = "X-MailOps-Global", value = "yes" } } } });
            globalRuleId = global.GetProperty("id").GetInt64(); await Send("rules.update", new { id = globalRuleId, fields = new { active = true } });
            await MailboxBehaviorTests.Submit(sender, sink, tag + "-one"); await MailboxBehaviorTests.WaitFor(sink, password, 1);
            string mail = (await MailboxBehaviorTests.Read(sink, password))[0];
            check(mail.Contains("X-MailOps-Global: yes", StringComparison.OrdinalIgnoreCase) && mail.Contains("X-MailOps-Local: yes", StringComparison.OrdinalIgnoreCase), "Enabled global and mailbox rules modify real local SMTP mail.");
            await RuleUpdate(ruleId, new { active = false }); await MailboxBehaviorTests.Submit(sender, sink, tag + "-two"); await MailboxBehaviorTests.WaitFor(sink, password, 2);
            mail = (await MailboxBehaviorTests.Read(sink, password)).Single(message => message.Contains(tag + "-two"));
            check(!mail.Contains("X-MailOps-Local:", StringComparison.OrdinalIgnoreCase) && mail.Contains("X-MailOps-Global:", StringComparison.OrdinalIgnoreCase), "Rule deactivation invalidates processing caches.");
            await Send("rules.delete", Scope(ruleId), manager); await Refuse("rules.read", Scope(ruleId), 404, manager);
            await Send("rules.delete", new { id = globalRuleId }); globalRuleId = null;
            await ExerciseQueue(Send, Refuse, sender, domain, check);
        }
        finally
        {
            if (globalRuleId.HasValue) await Send("rules.delete", new { id = globalRuleId.Value });
            if (routeId.HasValue) await Send("routes.delete", new { id = routeId.Value });
            await Send("domains.delete", new { id = domainId });
        }
    }
    private static async Task ExerciseQueue(Func<string, object, string?, Task<JsonElement>> send, Func<string, object, int, string?, Task> refuse, string sender, string domain, Action<bool,string> check)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Backend()
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token); using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII); using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 loopback.test");
            while (await reader.ReadLineAsync(timeout.Token) is { } line)
            {
                if (line.StartsWith("RCPT", StringComparison.OrdinalIgnoreCase)) { entered.TrySetResult(); await release.Task.WaitAsync(timeout.Token); await writer.WriteLineAsync("451 Controlled test retry"); }
                else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase)) { await writer.WriteLineAsync("221 Bye"); break; }
                else await writer.WriteLineAsync("250 loopback.test");
            }
        }
        var backend = Backend(); long? routeId = null; long? id = null;
        try
        {
            string queueDomain = "queue-" + domain;
            routeId = (await send("routes.create", new { fields = new { domain = queueDomain, host = "127.0.0.1", port = ((IPEndPoint)listener.LocalEndpoint).Port, security = 0, allAddresses = true, recipientLocal = true, tries = 10, retryMinutes = 60 } }, null)).GetProperty("id").GetInt64();
            await MailboxBehaviorTests.Submit(sender, "destination@" + queueDomain, "queue-boundary-probe"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(40));
            var page = await send("queue.list", new { search = sender, limit = 10 }, null); var item = page.GetProperty("items").EnumerateArray().Single();
            id = long.Parse(item.GetProperty("id").GetString()!); check(item.GetProperty("busy").GetBoolean(), "Queue exposes busy delivery state and exact string IDs.");
            var detail = await send("queue.read", new { id }, null);
            check(detail.GetProperty("recipients")[0].GetString() == "destination@" + queueDomain && !detail.TryGetProperty("filename", out _), "Queue detail shows recipients without body or storage paths.");
            await refuse("queue.delete", new { id }, 409, null); await refuse("queue.retry", new { id }, 409, null);
            release.TrySetResult(); await backend.WaitAsync(TimeSpan.FromSeconds(30)); listener.Stop();
            async Task WaitIdle()
            {
                while ((await send("queue.read", new { id }, null)).GetProperty("busy").GetBoolean()) await Task.Delay(100, timeout.Token);
            }
            await WaitIdle(); await send("queue.retry", new { id }, null); check(true, "An idle queued item accepts explicit retry.");
            while (true)
            {
                await WaitIdle();
                try { await send("queue.delete", new { id }, null); break; }
                catch (ManagementError error) when (error.Status == 409) { await Task.Delay(100, timeout.Token); }
            }
            await refuse("queue.read", new { id }, 404, null); await Task.Delay(300, timeout.Token);
            check((await send("queue.list", new { search = sender }, null)).GetProperty("total").GetInt64() == 0, "Queue removal invalidates pending delivery snapshots."); id = null;
        }
        finally
        {
            release.TrySetResult(); listener.Stop(); timeout.Cancel();
            try { await backend; } catch (Exception error) when (error is OperationCanceledException or SocketException or IOException or ObjectDisposedException) { }
            if (id.HasValue) { try { await send("queue.delete", new { id }, null); } catch (ManagementError) { } }
            if (routeId.HasValue) await send("routes.delete", new { id = routeId.Value }, null);
        }
    }
}
