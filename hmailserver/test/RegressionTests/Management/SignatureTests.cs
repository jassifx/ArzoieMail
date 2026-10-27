using System.Text.Json;
using ArzoieMail.Management;

static class SignatureTests
{
    public static async Task Run(IManagementChannel native, string username, string password, Action<bool, string> check)
    {
        Task<JsonElement> Send(string operation, object data, string? actor = null) =>
            native.SendAsync(operation, actor ?? username, password, data, CancellationToken.None);
        async Task Refuse(string operation, object data, int status, string? actor = null)
        {
            try { await Send(operation, data, actor); throw new Exception("Unexpected signature operation success."); }
            catch (ManagementError error) { check(error.Status == status, $"Signature expected {status}, got {error.Status}."); }
        }
        string name = "signature-" + Guid.NewGuid().ToString("N") + ".test";
        long domainId = (await Send("domains.create", new { fields = new { name, active = true } })).GetProperty("id").GetInt64();
        try
        {
            async Task<long> Create(string local, int level = 0) => (await Send("accounts.create", new { domainId,
                fields = new { address = local + "@" + name, active = true, password, level } })).GetProperty("id").GetInt64();
            string sender = "sender@" + name, sink = "sink@" + name, manager = "manager@" + name;
            long id = await Create("sender"); await Create("sink"); await Create("manager", 1);
            long protectedId = await Create("protected", 2);
            var route = new { domainId, id };
            Task<JsonElement> Update(object fields) => Send("accounts.signature.update", new { domainId, id, fields }, manager);
            var original = await Send("accounts.signature.read", route, manager);
            check(!original.GetProperty("signatureEnabled").GetBoolean(), "New mailbox signature defaults disabled.");
            foreach (string operation in new[] { "accounts.signature.read", "accounts.signature.update" })
            {
                await Refuse(operation, new { domainId, id, fields = new { signatureEnabled = true } }, 403, sender);
                await Refuse(operation, new { domainId = long.MaxValue, id, fields = new { signatureEnabled = true } }, 403, manager);
                await Refuse(operation, new { domainId, id = protectedId, fields = new { signatureEnabled = true } }, 403, manager);
                await Refuse(operation, new { domainId, id = long.MaxValue, fields = new { signatureEnabled = true } }, 404, manager);
                await Refuse(operation, new { domainId = long.MaxValue, id, fields = new { signatureEnabled = true } }, 404);
            }
            check(!(await Send("accounts.signature.read", new { domainId, id = protectedId })).GetProperty("signatureEnabled").GetBoolean(), "Server administrator may read a protected mailbox signature.");
            foreach (object fields in new object[] { new { }, new { unknown = true }, new { signatureEnabled = 1 },
                new { signaturePlainText = false }, new { signatureHtml = 1 }, new { signaturePlainText = "a\0b" },
                new { signatureHtml = "a\u0001b" }, new { signaturePlainText = "a\u007fb" },
                new { signatureHtml = new string('x', 16001) }, new { signaturePlainText = new string('✓', 5334) },
                new { signatureEnabled = true, signaturePlainText = "valid", signatureHtml = "invalid\0" },
                new { password = "forbidden" }, new { level = 2 }, new { domainId = 1 } })
                await Refuse("accounts.signature.update", new { domainId, id, fields }, 422, manager);
            check((await Send("accounts.signature.read", route)).GetRawText() == original.GetRawText(), "Invalid signatures are not partially saved.");
            var behavior = await Send("accounts.behavior.read", route);
            const string literal = "<img src=x onerror=alert(1)><script>literal source</script>";
            await Update(new { signatureEnabled = true, signaturePlainText = "Thanks\r\n✓\t%User.Address%", signatureHtml = literal });
            var stored = await Send("accounts.signature.read", route);
            check(stored.GetProperty("signaturePlainText").GetString() == "Thanks\r\n✓\t%User.Address%" && stored.GetProperty("signatureHtml").GetString() == literal,
                "Unicode and HTML source round-trip without evaluation or rewriting.");
            await Update(new { signatureEnabled = false });
            stored = await Send("accounts.signature.read", route);
            check(!stored.GetProperty("signatureEnabled").GetBoolean() && stored.GetProperty("signatureHtml").GetString() == literal, "Disabling preserves both signature formats.");
            check((await Send("accounts.behavior.read", route)).GetRawText() == behavior.GetRawText(), "Signature changes preserve forwarding/reply settings.");
            check(!(await Send("accounts.read", route)).TryGetProperty("signatureHtml", out _), "Basic mailbox views exclude signature content.");
            // Exercise the existing MIME/delivery pipeline with only disposable local recipients.
            await Update(new { signatureEnabled = true, signaturePlainText = "PLAIN-SIGNATURE %User.Address%", signatureHtml = "<strong>HTML-SIGNATURE</strong>" });
            await MailboxBehaviorTests.Submit(sender, sink, "plain", "text/plain; charset=us-ascii", "Plain body");
            await MailboxBehaviorTests.WaitFor(sink, password, 1);
            var messages = await MailboxBehaviorTests.Read(sink, password);
            check(messages[0].Contains("PLAIN-SIGNATURE " + sender), "Plain SMTP delivery applies configured signature and address macro.");
            await MailboxBehaviorTests.Submit(sender, sink, "html", "text/html; charset=us-ascii", "<p>HTML body</p>");
            await MailboxBehaviorTests.WaitFor(sink, password, 2);
            messages = await MailboxBehaviorTests.Read(sink, password);
            check(messages.Any(message => message.Contains("<strong>HTML-SIGNATURE</strong>")), "HTML SMTP delivery applies the HTML signature.");
            const string multipart = "--sig-probe\r\nContent-Type: text/plain; charset=us-ascii\r\n\r\nPlain alternative\r\n--sig-probe\r\nContent-Type: text/html; charset=us-ascii\r\n\r\n<p>HTML alternative</p>\r\n--sig-probe--";
            await MailboxBehaviorTests.Submit(sender, sink, "multipart", "multipart/alternative; boundary=\"sig-probe\"", multipart);
            await MailboxBehaviorTests.WaitFor(sink, password, 3);
            messages = await MailboxBehaviorTests.Read(sink, password);
            check(messages.Any(message => message.Contains("Plain alternative") && message.Contains("PLAIN-SIGNATURE") && message.Contains("HTML-SIGNATURE")), "Multipart mail receives both configured formats.");
            await Update(new { signatureEnabled = false });
            await MailboxBehaviorTests.Submit(sender, sink, "disabled-signature", "text/plain; charset=us-ascii", "DISABLED-PROBE");
            await MailboxBehaviorTests.WaitFor(sink, password, 4);
            messages = await MailboxBehaviorTests.Read(sink, password);
            check(messages.Single(message => message.Contains("DISABLED-PROBE")).Contains("PLAIN-SIGNATURE") == false, "Disabling invalidates the sender cache and stops applying its signature.");
            await Update(new { signaturePlainText = "", signatureHtml = "", signatureEnabled = true });
            stored = await Send("accounts.signature.read", route);
            check(stored.GetProperty("signaturePlainText").GetString() == "" && stored.GetProperty("signatureHtml").GetString() == "", "Empty formats can be explicitly saved.");
        }
        finally { await Send("domains.delete", new { id = domainId }); }
    }
}
