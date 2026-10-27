using System.Text.Json;
using ArzoieMail.Management;

static class AdvancedAdministrationTests
{
    public static async Task Run(IManagementChannel native, string username, string password, Action<bool, string> check)
    {
        Task<JsonElement> Send(string op, object data, string? actor = null) => native.SendAsync(op, actor ?? username, password, data, CancellationToken.None);
        async Task Refuse(string op, object data, int status, string? actor = null)
        {
            try { await Send(op, data, actor); throw new Exception("Unexpected advanced administration success: " + op); }
            catch (ManagementError error) { check(error.Status == status, $"{op}: expected {status}, got {error.Status}."); }
        }
        string name = "advanced-" + Guid.NewGuid().ToString("N") + ".test";
        long domainId = (await Send("domains.create", new { fields = new { name, active = true } })).GetProperty("id").GetInt64();
        try
        {
            async Task<long> Create(string local, int level = 0) => (await Send("accounts.create", new { domainId, fields = new { address = local + "@" + name, active = true, password, level } })).GetProperty("id").GetInt64();
            string sender = "sender@" + name, sink = "sink@" + name, manager = "manager@" + name;
            long accountId = await Create("sender"), sinkId = await Create("sink"), protectedId = await Create("protected", 2);
            await Create("manager", 1);
            var accountRoute = new { domainId, id = accountId }; var domainRoute = new { id = domainId };
            Task<JsonElement> Settings(object fields, string? actor = null) => Send("domains.settings.update", new { id = domainId, fields }, actor);
            Task<JsonElement> Profile(object fields) => Send("accounts.profile.update", new { domainId, id = accountId, fields }, manager);
            foreach (string op in new[] { "domains.settings.read", "domains.settings.update" })
            {
                await Refuse(op, new { id = domainId, fields = new { greylistingEnabled = false } }, 403, sender);
                await Refuse(op, new { id = long.MaxValue, fields = new { greylistingEnabled = false } }, 403, manager);
                await Refuse(op, new { id = long.MaxValue, fields = new { greylistingEnabled = false } }, 404);
            }
            foreach (string op in new[] { "accounts.profile.read", "accounts.profile.update" })
            {
                await Refuse(op, new { domainId, id = accountId, fields = new { firstName = "x" } }, 403, sender);
                await Refuse(op, new { domainId = long.MaxValue, id = accountId, fields = new { firstName = "x" } }, 403, manager);
                await Refuse(op, new { domainId, id = protectedId, fields = new { firstName = "x" } }, 403, manager);
                await Refuse(op, new { domainId, id = long.MaxValue, fields = new { firstName = "x" } }, 404);
            }
            var originalProfile = await Send("accounts.profile.read", accountRoute, manager);
            foreach (object fields in new object[] { new { }, new { firstName = 1 }, new { firstName = "bad\nname" }, new { lastName = new string('x', 256) },
                new { firstName = "valid", lastName = "bad\0" }, new { directoryBacked = true }, new { lastLogon = "fake" }, new { password = "forbidden" } })
                await Refuse("accounts.profile.update", new { domainId, id = accountId, fields }, 422, manager);
            check((await Send("accounts.profile.read", accountRoute)).GetRawText() == originalProfile.GetRawText(), "Invalid profiles do not partially save.");
            var profile = await Profile(new { firstName = "Ada ✓", lastName = "Team" });
            check(profile.GetProperty("firstName").GetString() == "Ada ✓" && !profile.GetProperty("directoryBacked").GetBoolean(), "Unicode profile and read-only directory status round-trip.");
            await Profile(new { firstName = "Ada" });
            check((await Send("accounts.profile.read", accountRoute)).GetProperty("lastName").GetString() == "Team", "Profile PATCH preserves omitted names.");

            var original = await Send("domains.settings.read", domainRoute, manager);
            foreach (object fields in new object[] { new { }, new { unknown = true }, new { plusAddressingEnabled = 1 }, new { plusAddressingCharacter = "++" },
                new { plusAddressingEnabled = true, plusAddressingCharacter = "" }, new { plusAddressingCharacter = "@" }, new { catchAllAddress = "unknown@" + name },
                new { catchAllAddress = "outside@example.test" }, new { signatureMethod = 0 }, new { signatureMethod = 4 }, new { signatureEnabled = "yes" },
                new { signatureHtml = "bad\0" }, new { signaturePlainText = new string('x', 16001) }, new { maxAccounts = -1 }, new { maxAliases = true },
                new { greylistingEnabled = false, signatureMethod = 9 } })
                await Refuse("domains.settings.update", new { id = domainId, fields }, 422);
            foreach (object fields in new object[] { new { maxAccounts = 0 }, new { maxAliases = 1 }, new { maxLists = 1 }, new { catchAllAddress = "protected@" + name } })
                await Refuse("domains.settings.update", new { id = domainId, fields }, 403, manager);
            check((await Send("domains.settings.read", domainRoute)).GetRawText() == original.GetRawText(), "Invalid domain settings do not partially save.");
            await Send("accounts.update", new { domainId, id = sinkId, fields = new { active = false } });
            await Refuse("domains.settings.update", new { id = domainId, fields = new { catchAllAddress = sink } }, 422, manager);
            await Send("accounts.update", new { domainId, id = sinkId, fields = new { active = true } });
            check(await AddressingTests.RecipientStatus("missing@" + name) == 550, "Unknown recipient initially rejects.");
            await Settings(new { catchAllAddress = sink, plusAddressingEnabled = true, plusAddressingCharacter = "+", greylistingEnabled = false }, manager);
            check(await AddressingTests.RecipientStatus("missing@" + name) == 250, "Catch-all settings invalidate routing cache.");
            await Settings(new { catchAllAddress = "" }, manager);
            check(await AddressingTests.RecipientStatus("missing@" + name) == 550 && await AddressingTests.RecipientStatus("sink+tag@" + name) == 250, "Catch-all clearing preserves plus addressing.");
            await Settings(new { plusAddressingEnabled = false }, manager);
            check(await AddressingTests.RecipientStatus("sink+tag@" + name) == 550, "Disabling plus addressing invalidates routing cache.");

            await Settings(new { maxAccounts = 4, maxAliases = 1, maxLists = 1 });
            await Refuse("accounts.create", new { domainId, fields = new { address = "over@" + name, password, active = true } }, 422, manager);
            await Send("aliases.create", new { domainId, fields = new { address = "alias@" + name, target = sink } }, manager);
            await Refuse("aliases.create", new { domainId, fields = new { address = "over@" + name, target = sink } }, 422, manager);
            await Send("lists.create", new { domainId, fields = new { address = "list@" + name } }, manager);
            await Refuse("lists.create", new { domainId, fields = new { address = "over@" + name } }, 422, manager);
            await Settings(new { maxAccounts = 1 });
            check((await Send("accounts.list", new { domainId })).GetProperty("total").GetInt64() == 4, "Lowering limits keeps existing mailboxes.");
            await Settings(new { maxAccounts = 0, maxAliases = 0, maxLists = 0 });
            await Create("extra"); check(true, "Zero limit allows another mailbox.");

            string alternate = "alternate-" + Guid.NewGuid().ToString("N") + ".test";
            foreach (string op in new[] { "domainaliases.list", "domainaliases.read", "domainaliases.create", "domainaliases.delete" })
                await Refuse(op, new { domainId, id = 1, fields = new { name = alternate } }, 403, sender);
            await Refuse("domainaliases.create", new { domainId, fields = new { name = alternate } }, 403, manager);
            await Refuse("domainaliases.list", new { domainId = long.MaxValue }, 403, manager);
            foreach (string bad in new[] { "", "bad domain", "x..test", "-bad.test", "bad-.test", "test.", "x\r\ny.test", new string('a', 64) + ".test", name })
                await Refuse("domainaliases.create", new { domainId, fields = new { name = bad } }, 422);
            var alias = await Send("domainaliases.create", new { domainId, fields = new { name = alternate.ToUpperInvariant() } });
            long aliasId = alias.GetProperty("id").GetInt64();
            check(alias.GetProperty("name").GetString() == alternate, "Alternate names are canonicalized.");
            await Refuse("domainaliases.create", new { domainId, fields = new { name = alternate } }, 422);
            await Refuse("domainaliases.delete", new { domainId, id = aliasId }, 403, manager);
            await Refuse("domainaliases.read", new { domainId = long.MaxValue, id = aliasId }, 404);
            var names = await Send("domainaliases.list", new { domainId, search = "ALTERNATE-", limit = 1 }, manager);
            check(names.GetProperty("total").GetInt64() == 1 && names.GetProperty("items")[0].GetProperty("id").GetInt64() == aliasId, "Alternate name search/pagination and domain-admin reads.");
            check(await AddressingTests.RecipientStatus("sink@" + alternate) == 250, "Alternate name creation invalidates the native routing cache.");
            await MailboxBehaviorTests.Submit(sender, "sink@" + alternate, "alternate-delivery");
            await MailboxBehaviorTests.WaitFor(sink, password, 1);
            check((await MailboxBehaviorTests.Read(sink, password))[0].Contains("alternate-delivery"), "Alternate domain mail reaches the primary mailbox.");
            await Send("domainaliases.delete", new { domainId, id = aliasId });
            await Refuse("domainaliases.read", new { domainId, id = aliasId }, 404);
            check((await Send("domainaliases.list", new { domainId })).GetProperty("total").GetInt64() == 0, "Alternate name removal persists.");

            await Send("accounts.signature.update", new { domainId, id = accountId, fields = new { signatureEnabled = true, signaturePlainText = "USER %User.FirstName% %User.LastName%", signatureHtml = "<b>USER-HTML</b>" } }, manager);
            await Settings(new { signatureEnabled = true, signaturePlainText = "DOMAIN", signatureHtml = "<b>DOMAIN-HTML</b>", signatureMethod = 2, signaturesOnReplies = false, signaturesOnLocalMail = true }, manager);
            async Task<string> Deliver(string marker, int count, string? type = null, string? body = null, bool reply = false)
            {
                await MailboxBehaviorTests.Submit(sender, sink, marker, type, body, reply ? "<original@local.test>" : null);
                await MailboxBehaviorTests.WaitFor(sink, password, count + 1);
                return (await MailboxBehaviorTests.Read(sink, password)).Single(message => message.Contains("Subject: " + marker));
            }
            var mail = await Deliver("domain-replace", 1);
            check(mail.Contains("DOMAIN") && !mail.Contains("USER Ada"), "Domain replacement policy overrides mailbox signature.");
            await Settings(new { signatureMethod = 3 }, manager);
            mail = await Deliver("domain-append", 2);
            check(mail.Contains("USER Ada Team") && mail.Contains("DOMAIN"), "Domain append policy uses updated profile macros and both signatures.");
            await Settings(new { signatureMethod = 1 }, manager);
            mail = await Deliver("domain-fallback", 3);
            check(mail.Contains("USER Ada Team") && !mail.Contains("DOMAIN"), "Domain fallback preserves a supplied mailbox signature.");
            await Send("accounts.signature.update", new { domainId, id = accountId, fields = new { signatureEnabled = false } }, manager);
            mail = await Deliver("domain-html", 4, "text/html; charset=us-ascii", "<p>Body</p>");
            check(mail.Contains("DOMAIN-HTML") && !mail.Contains("USER-HTML"), "Domain HTML format applies when mailbox signature is disabled.");
            await Settings(new { signaturesOnLocalMail = false }, manager);
            mail = await Deliver("domain-local-off", 5);
            check(!mail.Contains("DOMAIN"), "Local-mail domain policy can suppress signatures.");
            await Settings(new { signaturesOnLocalMail = true, signatureEnabled = false }, manager);
            mail = await Deliver("domain-disabled", 6);
            check(!mail.Contains("DOMAIN"), "Disabling the domain signature invalidates cached policy.");
            await Settings(new { signatureEnabled = true, signaturesOnReplies = false }, manager);
            mail = await Deliver("reply-signature-off", 7, reply: true);
            check(!mail.Contains("DOMAIN"), "Reply policy suppresses domain signatures.");
            await Settings(new { signaturesOnReplies = true }, manager);
            mail = await Deliver("reply-signature-on", 8, reply: true);
            check(mail.Contains("DOMAIN"), "Reply signature policy updates reach the mail engine.");
        }
        finally { await Send("domains.delete", new { id = domainId }); }
    }
}
