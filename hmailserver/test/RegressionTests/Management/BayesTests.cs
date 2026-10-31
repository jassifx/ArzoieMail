using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ArzoieMail.Management;

// The native Bayesian filter against the real native dispatcher and service: training from IMAP
// copies into and out of a Junk folder and from a mailbox folder, scoring of new mail over SMTP,
// statistics, reset, and the settings and permission boundaries.
static class BayesTests
{
    public static async Task Run(IManagementChannel native, string username, string password, Action<bool, string> check)
    {
        Task<JsonElement> Send(string op, object? data) => native.SendAsync(op, username, password, data, CancellationToken.None);
        async Task Refuse(string op, object? data, int status, string? actor = null)
        {
            try { await native.SendAsync(op, actor ?? username, password, data, CancellationToken.None); throw new Exception("Unexpected success: " + op); }
            catch (ManagementError error) { check(error.Status == status, $"{op}: expected {status}, received {error.Status}."); }
        }
        string? manager = Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_DOMAIN_USERNAME"), ordinary = Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_USER_USERNAME");
        foreach (string actor in new[] { manager, ordinary }.OfType<string>())
        {
            await Refuse("bayes.status", new { }, 403, actor);
            await Refuse("bayes.reset", new { confirm = true }, 403, actor);
            await Refuse("bayes.train", new { accountId = 1, folder = "INBOX", @as = "spam" }, 403, actor);
        }
        await Refuse("bayes.status", new { extra = 1 }, 422);
        await Refuse("bayes.reset", new { }, 422);
        await Refuse("bayes.reset", new { confirm = false }, 422);
        await Refuse("bayes.reset", new { confirm = true, scope = "all" }, 422);
        await Refuse("bayes.train", new { accountId = 0, folder = "INBOX", @as = "spam" }, 422);
        await Refuse("bayes.train", new { accountId = 1, folder = "INBOX", @as = "maybe" }, 422);
        await Refuse("bayes.train", new { accountId = 1, folder = "INBOX", @as = "spam", path = "C:\\mail" }, 422);
        await Refuse("bayes.train", new { accountId = long.MaxValue, folder = "INBOX", @as = "spam" }, 404);
        await Refuse("antispam.update", new { BayesThreshold = 49 }, 422);
        await Refuse("antispam.update", new { BayesThreshold = 101 }, 422);
        await Refuse("antispam.update", new { BayesMinimumMessages = 0 }, 422);
        await Refuse("antispam.update", new { BayesEnabled = 1 }, 422);

        var settings = await Send("antispam.read", new { });
        check(settings.TryGetProperty("BayesEnabled", out _) && settings.TryGetProperty("BayesScore", out _) && settings.TryGetProperty("BayesThreshold", out _)
            && settings.TryGetProperty("BayesMinimumMessages", out _) && settings.TryGetProperty("BayesLearnFromJunk", out _), "The Bayesian settings are part of the antispam section.");
        string[] changed = { "BayesEnabled", "BayesLearnFromJunk", "BayesMinimumMessages", "BayesThreshold", "BayesScore", "SpamMarkThreshold", "SpamDeleteThreshold", "AddHeaderReason", "AddHeaderSpam" };
        var original = changed.ToDictionary(key => key, key => settings.GetProperty(key).Clone());

        string domain = "bayes-" + Guid.NewGuid().ToString("N") + ".test";
        long domainId = (await Send("domains.create", new { fields = new { name = domain, active = true } })).GetProperty("id").GetInt64();
        JsonElement? range = null;
        try
        {
            async Task<long> Create(string local) =>
                (await Send("accounts.create", new { domainId, fields = new { address = local + "@" + domain, password, active = true } })).GetProperty("id").GetInt64();
            await Create("sender");
            long userId = await Create("user");
            string user = "user@" + domain, sender = "sender@" + domain;
            // A score far above anything the other tests add marks only what the filter flags.
            await Send("antispam.update", new { BayesEnabled = true, BayesLearnFromJunk = true, BayesMinimumMessages = 2, BayesThreshold = 90, BayesScore = 1000,
                SpamMarkThreshold = 999, SpamDeleteThreshold = 0, AddHeaderReason = true, AddHeaderSpam = true });
            var status = await Send("bayes.reset", new { confirm = true });
            check(status.GetProperty("spamMessages").GetInt64() == 0 && status.GetProperty("hamMessages").GetInt64() == 0 && status.GetProperty("tokens").GetInt64() == 0
                && !status.GetProperty("ready").GetBoolean(), "A reset filter has no trained messages and is not ready.");

            string[] spam = { "Claim your lottery prize now", "Cheap pharmacy pills winner", "Bitcoin jackpot claim today" };
            string[] ham = { "Quarterly budget review agenda", "Project schedule meeting notes", "Team report for the planning meeting" };
            const string spamBody = "Congratulations winner! Claim your free lottery prize and cheap pharmacy pills now. Bitcoin jackpot casino bonus. Visit http://prize-claim.example/now";
            const string hamBody = "Hello team, attached is the quarterly budget review and the project schedule. Please read the agenda before our planning meeting on Thursday. Regards";
            foreach (string subject in spam) await MailboxBehaviorTests.Submit(sender, user, "bayes-spam " + subject, body: spamBody + " " + subject);
            foreach (string subject in ham) await MailboxBehaviorTests.Submit(sender, user, "bayes-ham " + subject, body: hamBody + " " + subject);
            await MailboxBehaviorTests.WaitFor(user, password, spam.Length + ham.Length);

            // Spam: copied into a Junk folder by the user. Ham: copied to an ordinary folder, which
            // trains nothing, then learned from that folder by the administrator.
            await Imap(user, password, async command =>
            {
                await command("CREATE Junk (USE (\\Junk))", true);
                await command("CREATE Keep", false);
                await command("SELECT INBOX", false);
                await command("COPY " + Search(await command("SEARCH SUBJECT \"bayes-spam\"", false)) + " Junk", false);
                await command("COPY " + Search(await command("SEARCH SUBJECT \"bayes-ham\"", false)) + " Keep", false);
            });
            status = await WaitForTraining(s => s.GetProperty("spamMessages").GetInt64() == spam.Length);
            check(status.GetProperty("spamMessages").GetInt64() == spam.Length && status.GetProperty("hamMessages").GetInt64() == 0,
                "Copying messages into a Junk folder trains them as spam, and copying elsewhere trains nothing.");
            await Refuse("bayes.train", new { accountId = userId, folder = "Missing", @as = "ham" }, 404);
            var queued = await Send("bayes.train", new { accountId = userId, folder = "Keep", @as = "ham" });
            check(queued.GetProperty("queued").GetInt64() == ham.Length && queued.GetProperty("dropped").GetInt64() == 0, "Training a folder queues each of its messages.");
            status = await WaitForTraining(s => s.GetProperty("hamMessages").GetInt64() == ham.Length);
            check(status.GetProperty("hamMessages").GetInt64() == ham.Length && status.GetProperty("ready").GetBoolean() && status.GetProperty("tokens").GetInt64() > 10,
                "Folder training counts the messages as ham, and the filter is ready once both minimums are met.");

            // Scoring needs spam checks for the test's own address; a temporary higher-priority copy
            // of the matching IP policy turns them on and leaves everything else as it was.
            var matched = (await Send("securityranges.match", new { address = "127.0.0.1" })).GetProperty("matched");
            var fields = new Dictionary<string, object?>();
            foreach (var property in matched.EnumerateObject())
                if (property.Name is not ("id" or "name" or "priority" or "expires" or "expiresAt" or "acknowledgeUnauthenticatedRelay")) fields[property.Name] = property.Value.Clone();
            fields["name"] = "Bayes test " + Guid.NewGuid().ToString("N")[..8];
            fields["priority"] = Math.Min(matched.GetProperty("priority").GetInt64() + 1, 1000000);
            fields["spamProtection"] = true;
            if (matched.GetProperty("externalToExternal").GetBoolean() && !matched.GetProperty("authExternalToExternal").GetBoolean())
                fields["acknowledgeUnauthenticatedRelay"] = true;
            range = await Send("securityranges.create", new { fields });

            await MailboxBehaviorTests.Submit(sender, user, "bayes-new-spam Lottery winner", body: spamBody + " Final notice");
            await MailboxBehaviorTests.Submit(sender, user, "bayes-new-ham Budget meeting", body: hamBody + " See you then");
            await MailboxBehaviorTests.WaitFor(user, password, spam.Length + ham.Length + 2);
            // Headers may be folded; unfold them before looking for the reason text.
            var messages = (await MailboxBehaviorTests.Read(user, password)).Select(message => message.Replace("\r\n ", " ").Replace("\r\n\t", " ")).ToList();
            string scoredSpam = messages.Single(message => message.Contains("bayes-new-spam", StringComparison.Ordinal));
            string scoredHam = messages.Single(message => message.Contains("bayes-new-ham", StringComparison.Ordinal));
            check(scoredSpam.Contains("Bayesian filter: spam probability", StringComparison.Ordinal) && scoredSpam.Contains("X-hMailServer-Spam: YES", StringComparison.OrdinalIgnoreCase),
                "A message like the trained spam is scored by the Bayesian filter and marked as spam.");
            check(!scoredHam.Contains("Bayesian filter", StringComparison.Ordinal), "A message like the trained ham is not scored by the Bayesian filter.");

            // Out of Junk: to Trash says nothing, to another folder trains ham.
            await Imap(user, password, async command =>
            {
                await command("CREATE Trash (USE (\\Trash))", true);
                await command("SELECT Junk", false);
                await command("COPY 1 Trash", false);
                await command("COPY 1 Keep", false);
            });
            status = await WaitForTraining(s => s.GetProperty("hamMessages").GetInt64() == ham.Length + 1);
            check(status.GetProperty("hamMessages").GetInt64() == ham.Length + 1 && status.GetProperty("spamMessages").GetInt64() == spam.Length,
                "Copying out of Junk trains ham, except into Trash.");

            status = await Send("bayes.reset", new { confirm = true });
            check(status.GetProperty("spamMessages").GetInt64() == 0 && status.GetProperty("tokens").GetInt64() == 0, "Reset forgets every trained message.");
        }
        finally
        {
            if (range.HasValue)
            {
                long id = long.Parse(range.Value.GetProperty("id").GetString()!);
                var expected = await Send("securityranges.read", new { id });
                await Send("securityranges.delete", new { id, fields = new { expected } });
            }
            await Send("antispam.update", original);
            await Send("bayes.reset", new { confirm = true });
            await Send("domains.delete", new { id = domainId });
        }

        async Task<JsonElement> WaitForTraining(Func<JsonElement, bool> done)
        {
            var deadline = DateTime.UtcNow.AddSeconds(60);
            for (;;)
            {
                var current = await Send("bayes.status", new { });
                bool idle = current.GetProperty("pending").GetInt64() == 0 && !current.GetProperty("training").GetBoolean();
                if ((idle && done(current)) || DateTime.UtcNow > deadline) return current;
                await Task.Delay(250);
            }
        }
    }

    // The sequence numbers of an untagged SEARCH response, as a COPY set.
    static string Search(List<string> lines)
    {
        string numbers = lines.Single(line => line.StartsWith("* SEARCH", StringComparison.Ordinal))[8..].Trim();
        if (numbers.Length == 0) throw new IOException("IMAP SEARCH found nothing.");
        return string.Join(',', numbers.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // Runs IMAP commands as the user. A command marked optional may fail, for a folder that exists already.
    static async Task Imap(string address, string password, Func<Func<string, bool, Task<List<string>>>, Task> commands)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var client = new TcpClient(); await client.ConnectAsync("127.0.0.1", 143, deadline.Token);
        using var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII);
        using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
        int sequence = 0;
        async Task<List<string>> Command(string text, bool optional)
        {
            string tag = "B" + ++sequence;
            await writer.WriteLineAsync(tag + " " + text); var lines = new List<string>();
            for (string? line; (line = await reader.ReadLineAsync(deadline.Token)) is not null;) { lines.Add(line); if (line.StartsWith(tag + " ", StringComparison.Ordinal)) break; }
            if (!optional && !lines.Last().StartsWith(tag + " OK", StringComparison.Ordinal)) throw new IOException($"IMAP {text}: {lines.Last()}");
            return lines;
        }
        await reader.ReadLineAsync(deadline.Token);
        await Command($"LOGIN \"{address}\" \"{password.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"", false);
        await commands(Command);
        await Command("LOGOUT", true);
    }
}
