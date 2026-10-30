using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ArzoieMail.Management;

static class SecurityRangeTests
{
    public static async Task Run(IManagementChannel native, string username, string password, Action<bool, string> check)
    {
        Task<JsonElement> Send(string operation, object data, string? actor = null) => native.SendAsync(operation, actor ?? username, password, data, CancellationToken.None);
        async Task Refuse(string operation, object data, int status, string? actor = null)
        {
            try { await Send(operation, data, actor); throw new Exception("Unexpected IP policy success: " + operation); }
            catch (ManagementError error) { check(error.Status == status, $"IP policy {operation}: expected {status}, received {error.Status}."); }
        }
        foreach (string actor in new[] { Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_DOMAIN_USERNAME"), Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_USER_USERNAME") }.OfType<string>())
            foreach (string action in new[] { "list", "read", "create", "update", "delete", "match" })
                await Refuse("securityranges." + action, new { id = 1, fields = new { }, address = "127.0.0.1" }, 403, actor);
        await Refuse("securityranges.match", new { address = "not-an-ip" }, 422);
        await Refuse("securityranges.match", new { address = "127.0.0.1", arbitrary = true }, 422);
        var schema = await Send("securityranges.list", new { limit = 1 });
        check(schema.GetProperty("schema").GetArrayLength() == 21, "IP policies enumerate permissions, expiry and the transient relay-risk acknowledgement.");
        check(!schema.GetProperty("defaults").GetProperty("allowSmtp").GetBoolean(), "New IP policies deny mail connections by default.");
        string name = "Management IP fixture " + Guid.NewGuid().ToString("N");
        JsonElement? current = null;
        Exception? bodyFailure = null;
        async Task Patch(object values)
        {
            var fields = JsonSerializer.SerializeToElement(values).EnumerateObject().ToDictionary(property => property.Name, property => (object)property.Value.Clone());
            fields["expected"] = current!.Value;
            current = await Send("securityranges.update", new { id = long.Parse(current.Value.GetProperty("id").GetString()!), fields });
        }
        async Task<string?> SmtpGreeting()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", 25, deadline.Token);
            using var reader = new StreamReader(client.GetStream());
            try { return await reader.ReadLineAsync(deadline.Token); }
            catch (IOException) { return null; } // Native range rejection may close/reset the socket.
        }
        try
        {
            current = await Send("securityranges.create", new { fields = new { name, lowerAddress = "127.0.0.1", upperAddress = "127.0.0.1", priority = 1000000 } });
            long id = long.Parse(current.Value.GetProperty("id").GetString()!);
            check(!(await SmtpGreeting() ?? "").StartsWith("220", StringComparison.Ordinal), "A higher-priority deny policy blocks a real new SMTP connection.");
            var match = await Send("securityranges.match", new { address = "127.0.0.1" });
            check(match.GetProperty("matched").GetProperty("id").GetString() == id.ToString(), "Effective policy inspection resolves native priority selection.");
            check((await Send("status", new { })).TryGetProperty("version", out _), "Web/native management remains available to recover mail protocol lockout.");
            foreach (var fields in new object[] {
                new { expected = current.Value, externalToExternal = true, authExternalToExternal = false },
                new { expected = current.Value, localToExternal = true, authLocalToExternal = false },
                new { expected = current.Value, upperAddress = "::1" },
                new { expected = current.Value, expiresAt = "2099-02-30 25:00:00" },
                new { expected = current.Value, expires = true, expiresAt = "2001-01-01 00:00:00" } })
                await Refuse("securityranges.update", new { id, fields }, 422);
            await Patch(new { allowSmtp = true, externalToExternal = true, authExternalToExternal = true, requireTlsForAuth = true });
            check((await SmtpGreeting() ?? "").StartsWith("220", StringComparison.Ordinal), "A checked allow policy restores real new SMTP connections.");
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (var client = new TcpClient())
            {
                await client.ConnectAsync("127.0.0.1", 25, deadline.Token);
                using var reader = new StreamReader(client.GetStream());
                using var writer = new StreamWriter(client.GetStream(), Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
                await reader.ReadLineAsync(deadline.Token);
                await writer.WriteLineAsync("EHLO ip-policy.example");
                string? line;
                do { line = await reader.ReadLineAsync(deadline.Token); } while (line is { Length: > 3 } && line[3] == '-');
                await writer.WriteLineAsync("MAIL FROM:<sender@external.example>");
                line = await reader.ReadLineAsync(deadline.Token);
                if (line?.StartsWith("250", StringComparison.Ordinal) == true)
                {
                    await writer.WriteLineAsync("RCPT TO:<recipient@another-external.example>");
                    line = await reader.ReadLineAsync(deadline.Token);
                }
                check(line?.StartsWith("530", StringComparison.Ordinal) == true, "External relay remains authentication-gated on the real SMTP listener.");
            }
            await Patch(new { authExternalToExternal = false, acknowledgeUnauthenticatedRelay = true });
            check(!current.Value.GetProperty("authExternalToExternal").GetBoolean()
                && !current.Value.GetProperty("acknowledgeUnauthenticatedRelay").GetBoolean(),
                "An explicit trusted-relay decision persists while its acknowledgement remains transient.");
            await Refuse("securityranges.update", new { id, fields = new { expected = current.Value, name = "Unacknowledged relay edit" } }, 422);
            await Patch(new { lowerAddress = "2001:db8::10", upperAddress = "2001:db8::20", authExternalToExternal = true, expires = true, expiresAt = "2099-01-01 12:34:56" });
            match = await Send("securityranges.match", new { address = "2001:db8::15" });
            check(match.GetProperty("matched").GetProperty("id").GetString() == id.ToString(), "IPv6 interval matching and future expiry persist through native policy selection.");
            check(current.Value.GetProperty("expiresAt").GetString() == "2099-01-01 12:34:56", "Expiry preserves the explicit server-local timestamp.");
        }
        catch (Exception error) { bodyFailure = error; throw; }
        finally
        {
            if (current.HasValue)
            {
                try
                {
                    long id = long.Parse(current.Value.GetProperty("id").GetString()!);
                    var expected = await Send("securityranges.read", new { id });
                    await Send("securityranges.delete", new { id, fields = new { expected } });
                    await Refuse("securityranges.read", new { id }, 404);
                    check((await SmtpGreeting() ?? "").StartsWith("220", StringComparison.Ordinal), "Deleting the temporary policy restores original SMTP access.");
                }
                catch (Exception cleanup) when (bodyFailure is not null) { throw new AggregateException("IP policy test and recovery failed.", bodyFailure, cleanup); }
            }
        }
    }
}
