using System.Text.Json;
using System.Net.Sockets;
using ArzoieMail.Management;

static class ServerSettingsTests
{
    public static async Task Run(IManagementChannel native, string username, string password, Action<bool, string> check)
    {
        Task<JsonElement> Send(string op, object data, string? actor = null) => native.SendAsync(op, actor ?? username, password, data, CancellationToken.None);
        async Task Refuse(string op, object data, int status, string? actor = null)
        {
            try { await Send(op, data, actor); throw new Exception("Unexpected settings success: " + op); }
            catch (ManagementError error) { check(error.Status == status, $"{op} expected {status}, received {error.Status}."); }
        }
        foreach (string actor in new[] { Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_DOMAIN_USERNAME"), Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_USER_USERNAME") }.OfType<string>())
        {
            await Refuse("settings.read", new { section = "smtp" }, 403, actor);
            await Refuse("settings.update", new { section = "smtp", fields = new { maxConnections = 10 }, expected = new { maxConnections = 0 } }, 403, actor);
        }
        foreach (string section in new[] { "protocols", "smtp", "imap", "pop3", "autoban", "greylisting", "performance", "general", "antivirus", "passwordhash", "logging" })
        {
            var result = await Send("settings.read", new { section });
            var values = result.GetProperty("values"); var schema = result.GetProperty("schema");
            check(result.GetProperty("section").GetString() == section && schema.GetArrayLength() > 0, "Native section has a nonempty explicit schema.");
            foreach (var rule in schema.EnumerateArray())
            {
                string name = rule.GetProperty("name").GetString()!;
                check(values.TryGetProperty(name, out _), "Every schema field has a value.");
                check(!name.Contains("password", StringComparison.OrdinalIgnoreCase) && !name.Contains("secret", StringComparison.OrdinalIgnoreCase), "Server settings never expose secrets.");
            }
        }
        await Refuse("settings.read", new { section = "../../settings" }, 404);
        await Refuse("settings.read", new { section = "unknown" }, 404);
        await Refuse("settings.execute", new { section = "smtp" }, 404);
        var loggingOriginal = (await Send("settings.read", new { section = "logging" })).GetProperty("values").Clone();
        Exception? loggingFailure = null;
        try
        {
            bool smtp = !loggingOriginal.GetProperty("smtp").GetBoolean(), pop3 = !loggingOriginal.GetProperty("pop3").GetBoolean();
            var savedLogging = (await Send("settings.update", new { section = "logging", fields = new { smtp, pop3 },
                expected = new { smtp = !smtp, pop3 = !pop3 } })).GetProperty("values");
            check(savedLogging.GetProperty("smtp").GetBoolean() == smtp && savedLogging.GetProperty("pop3").GetBoolean() == pop3,
                "Multiple logging flags sharing one native property persist together without dropping either bit.");
            foreach (var property in loggingOriginal.EnumerateObject().Where(property => property.Name is not ("smtp" or "pop3")))
                check(savedLogging.GetProperty(property.Name).GetRawText() == property.Value.GetRawText(), "Logging patches preserve omitted flags and AWStats configuration.");
            await Refuse("settings.update", new { section = "logging", fields = new { smtp = !smtp }, expected = new { smtp = !smtp } }, 409);
            await Refuse("settings.update", new { section = "logging", fields = new { smtp = !smtp, rawMask = 0 }, expected = new { smtp, rawMask = 0 } }, 422);
            check((await Send("settings.read", new { section = "logging" })).GetProperty("values").GetProperty("smtp").GetBoolean() == smtp,
                "A mixed invalid log patch cannot change an earlier valid flag.");
        }
        catch (Exception error) { loggingFailure = error; throw; }
        finally
        {
            try
            {
                var expected = (await Send("settings.read", new { section = "logging" })).GetProperty("values");
                await Send("settings.update", new { section = "logging", fields = loggingOriginal, expected });
            }
            catch (Exception cleanup) when (loggingFailure is not null) { throw new AggregateException("Logging policy and restoration failed.", loggingFailure, cleanup); }
        }
        var hashOriginal = (await Send("settings.read", new { section = "passwordhash" })).GetProperty("values").Clone();
        async Task HashPatch(Dictionary<string, object> fields, int? failure = null)
        {
            var loaded = (await Send("settings.read", new { section = "passwordhash" })).GetProperty("values");
            var expected = fields.Keys.ToDictionary(key => key, key => loaded.GetProperty(key).Clone());
            var request = new { section = "passwordhash", fields, expected };
            if (failure.HasValue) await Refuse("settings.update", request, failure.Value);
            else await Send("settings.update", request);
        }
        Exception? hashFailure = null;
        try
        {
            await HashPatch(new() { ["memoryKiB"] = 4095 }, 422);
            await HashPatch(new() { ["algorithm"] = 2, ["iterations"] = 2 }, 422);
            await HashPatch(new() { ["algorithm"] = 1, ["iterations"] = 10000 }, 422);
            await HashPatch(new() { ["algorithm"] = 3 }, 422);
            var intact = (await Send("settings.read", new { section = "passwordhash" })).GetProperty("values");
            check(intact.GetRawText() == hashOriginal.GetRawText(), "Invalid cross-field hash policies leave every configured value unchanged.");
            foreach (int algorithm in new[] { 1, 2 })
            {
                await HashPatch(new() { ["algorithm"] = algorithm, ["iterations"] = 0, ["memoryKiB"] = 0, ["autoUpgrade"] = false });
                var savedHash = (await Send("settings.read", new { section = "passwordhash" })).GetProperty("values");
                check(savedHash.GetProperty("algorithm").GetInt32() == algorithm && savedHash.GetProperty("iterations").GetInt32() == 0,
                    "An algorithm change and default iteration policy persist as one checked batch.");
                await HashPatch(new() { ["iterations"] = algorithm == 1 ? 10000 : 2 }, 422);
            }
        }
        catch (Exception error) { hashFailure = error; throw; }
        finally
        {
            try { await HashPatch(hashOriginal.EnumerateObject().ToDictionary(property => property.Name, property => (object)property.Value.Clone())); }
            catch (Exception cleanup) when (hashFailure is not null) { throw new AggregateException("Hash policy and restoration failed.", hashFailure, cleanup); }
        }
        var original = (await Send("settings.read", new { section = "pop3" })).GetProperty("values").Clone();
        int oldLimit = original.GetProperty("maxConnections").GetInt32();
        string oldGreeting = original.GetProperty("welcomeMessage").GetString()!;
        int limit = oldLimit == 137 ? 138 : 137;
        string greeting = "ArzoieMail checked settings fixture";
        Exception? bodyFailure = null;
        try
        {
            await Refuse("settings.update", new { section = "pop3", fields = new { maxConnections = limit } }, 422);
            await Refuse("settings.update", new { section = "pop3", fields = new { maxConnections = limit }, expected = new { unexpected = oldLimit } }, 422);
            await Refuse("settings.update", new { section = "pop3", fields = new { maxConnections = limit }, expected = new { maxConnections = "not a number" } }, 422);
            await Refuse("settings.update", new { section = "pop3", fields = new { maxConnections = limit }, expected = new { maxConnections = oldLimit }, unexpected = true }, 422);
            await Refuse("settings.update", new { section = "pop3", fields = new { maxConnections = limit }, expected = new { maxConnections = oldLimit == 0 ? 1 : 0 } }, 409);
            foreach (object invalid in new object[] { -1, 1000001, true, "137", 1.5 })
                await Refuse("settings.update", new { section = "pop3", fields = new { maxConnections = invalid }, expected = new { maxConnections = oldLimit } }, 422);
            await Refuse("settings.update", new { section = "pop3", fields = new { welcomeMessage = "bad\r\nINJECTED" }, expected = new { welcomeMessage = oldGreeting } }, 422);
            await Refuse("settings.update", new { section = "pop3", fields = new { maxConnections = limit, arbitrarySql = "select 1" }, expected = new { maxConnections = oldLimit, arbitrarySql = "" } }, 422);
            check((await Send("settings.read", new { section = "pop3" })).GetProperty("values").GetProperty("maxConnections").GetInt32() == oldLimit, "Invalid mixed patches leave earlier valid fields unchanged.");
            Console.WriteLine("Server settings: saving checked numeric/string batch.");
            var saved = await Send("settings.update", new { section = "pop3", fields = new { maxConnections = limit, welcomeMessage = greeting }, expected = new { maxConnections = oldLimit, welcomeMessage = oldGreeting } });
            check(saved.GetProperty("values").GetProperty("maxConnections").GetInt32() == limit, "Checked numeric setting persists.");
            check(saved.GetProperty("values").GetProperty("welcomeMessage").GetString() == greeting, "Checked string setting persists in the same batch.");
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (var pop = new TcpClient())
            {
                await pop.ConnectAsync("127.0.0.1", 110, deadline.Token);
                using var reader = new StreamReader(pop.GetStream());
                string banner = await reader.ReadLineAsync(deadline.Token) ?? throw new IOException("POP3 disconnected before greeting.");
                check(banner.StartsWith("+OK", StringComparison.Ordinal) && banner.Contains(greeting, StringComparison.Ordinal), "Committed settings change the actual POP3 greeting on a new connection.");
            }
            await Refuse("settings.update", new { section = "pop3", fields = new { maxConnections = oldLimit }, expected = new { maxConnections = oldLimit } }, 409);
            var read = (await Send("settings.read", new { section = "pop3" })).GetProperty("values");
            check(read.GetProperty("maxConnections").GetInt32() == limit && read.GetProperty("welcomeMessage").GetString() == greeting, "A second request observes both committed values after a rejected stale patch.");
            async Task<int> ConcurrentEdit(string replacement)
            {
                try
                {
                    await Send("settings.update", new { section = "pop3", fields = new { welcomeMessage = replacement }, expected = new { welcomeMessage = greeting } });
                    return 200;
                }
                catch (ManagementError error) { return error.Status; }
            }
            var outcomes = await Task.WhenAll(ConcurrentEdit(greeting + " A"), ConcurrentEdit(greeting + " B"));
            check(outcomes.Count(status => status == 200) == 1 && outcomes.Count(status => status == 409) == 1,
                "Concurrent editors sharing a snapshot cannot both overwrite the greeting.");
            string winner = (await Send("settings.read", new { section = "pop3" })).GetProperty("values").GetProperty("welcomeMessage").GetString()!;
            check(winner == greeting + " A" || winner == greeting + " B", "Exactly one complete concurrent greeting is published.");
        }
        catch (Exception error) { bodyFailure = error; throw; }
        finally
        {
            try
            {
                var current = (await Send("settings.read", new { section = "pop3" })).GetProperty("values");
                await Send("settings.update", new { section = "pop3", fields = new { maxConnections = oldLimit, welcomeMessage = oldGreeting },
                    expected = new { maxConnections = current.GetProperty("maxConnections").GetInt32(), welcomeMessage = current.GetProperty("welcomeMessage").GetString() } });
            }
            catch (Exception cleanup) when (bodyFailure is not null)
            {
                throw new AggregateException("Server settings and fixture cleanup both failed.", bodyFailure, cleanup);
            }
        }
    }
}
