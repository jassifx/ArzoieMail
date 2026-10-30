using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArzoieMail.Management;

// Spam allowlist, greylisting exceptions, existing antivirus providers, SpamAssassin testing and
// DKIM keys, settings and signatures, against the real native dispatcher and service.
static class SpamDkimTests
{
    public static async Task Run(IManagementChannel native, string username, string password, Action<bool, string> check)
    {
        Task<JsonElement> Send(string op, object? data, string? actor = null) => native.SendAsync(op, actor ?? username, password, data, CancellationToken.None);
        async Task Refuse(string op, object? data, int status, string? actor = null)
        {
            try { await Send(op, data, actor); throw new Exception("Unexpected success: " + op); }
            catch (ManagementError error) { check(error.Status == status, $"{op}: expected {status}, received {error.Status}."); }
        }
        string? manager = Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_DOMAIN_USERNAME"), ordinary = Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_USER_USERNAME");
        foreach (string actor in new[] { manager, ordinary }.OfType<string>())
        {
            await Refuse("spamallowlist.list", new { }, 403, actor);
            await Refuse("greylistexceptions.list", new { }, 403, actor);
            await Refuse("antivirus.test", new { scanner = "clamav" }, 403, actor);
        }

        // Spam allowlist: several entries may share an IP range with different address patterns.
        object Allow(object fields) => new { fields };
        await Refuse("spamallowlist.create", Allow(new { lowerAddress = "0.0.0.0", upperAddress = "255.255.255.255", address = "no-at-sign", description = "x" }), 422);
        await Refuse("spamallowlist.create", Allow(new { lowerAddress = "192.0.2.10", upperAddress = "192.0.2.1", address = "*@partner.example", description = "x" }), 422);
        await Refuse("spamallowlist.create", Allow(new { lowerAddress = "192.0.2.1", upperAddress = "2001:db8::1", address = "*@partner.example", description = "x" }), 422);
        await Refuse("spamallowlist.create", Allow(new { lowerAddress = "192.0.2.1", upperAddress = "192.0.2.9", address = "a b@partner.example", description = "x" }), 422);
        var ids = new List<string>();
        try
        {
            foreach (string pattern in new[] { "*@partner.example", "billing@vendor.example" })
            {
                var created = await Send("spamallowlist.create", Allow(new { lowerAddress = "192.0.2.1", upperAddress = "192.0.2.9", address = pattern, description = "Fixture" }));
                ids.Add(created.GetProperty("id").GetString()!);
            }
            check(ids.Count == 2, "Two allowlist entries can share one IP range with different address patterns.");
            var first = await Send("spamallowlist.read", new { id = long.Parse(ids[0]) });
            var updated = await Send("spamallowlist.update", new { id = long.Parse(ids[0]), fields = new { expected = first, description = "Updated fixture" } });
            check(updated.GetProperty("description").GetString() == "Updated fixture", "An allowlist entry is updated under its expected value.");
            await Refuse("spamallowlist.update", new { id = long.Parse(ids[0]), fields = new { expected = first, description = "stale" } }, 409);
        }
        finally
        {
            foreach (string id in ids)
            {
                var current = await Send("spamallowlist.read", new { id = long.Parse(id) });
                await Send("spamallowlist.delete", new { id = long.Parse(id), fields = new { expected = current } });
            }
        }

        // Greylisting exceptions take IP patterns with * and ?.
        await Refuse("greylistexceptions.create", new { fields = new { address = "mail.example.com", description = "x" } }, 422);
        await Refuse("greylistexceptions.create", new { fields = new { address = "", description = "x" } }, 422);
        string exception = (await Send("greylistexceptions.create", new { fields = new { address = "198.51.100.*", description = "Large sender" } })).GetProperty("id").GetString()!;
        try
        {
            await Refuse("greylistexceptions.create", new { fields = new { address = "198.51.100.*", description = "duplicate" } }, 409);
            var listed = await Send("greylistexceptions.list", new { });
            check(listed.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("address").GetString() == "198.51.100.*"), "Greylisting exceptions keep their wildcard form.");
        }
        finally
        {
            var current = await Send("greylistexceptions.read", new { id = long.Parse(exception) });
            await Send("greylistexceptions.delete", new { id = long.Parse(exception), fields = new { expected = current } });
        }

        // Existing antivirus providers: status, and tests of the configured scanners only.
        var antivirus = await Send("antivirus.status", new { });
        check(antivirus.TryGetProperty("clamAv", out _) && antivirus.TryGetProperty("customScanner", out _) && antivirus.TryGetProperty("clamWin", out _), "All three existing providers are reported.");
        await Refuse("antivirus.test", new { scanner = "defender" }, 422);
        await Refuse("antivirus.test", new { scanner = "clamav", host = "attacker.example" }, 422);
        try
        {
            var tested = await Send("antivirus.test", new { scanner = "clamav" });
            check(tested.GetProperty("scanner").GetString() == "clamav" && tested.TryGetProperty("passed", out _), "The configured ClamAV connection can be tested.");
        }
        catch (ManagementError error) when (error.Status == 412) { check(true, "Without a configured ClamAV host the test is refused."); }

        // SpamAssassin: CI has no spamd, so only the result shape is checked.
        var spamAssassin = await Send("spamassassin.test", new { });
        check(spamAssassin.TryGetProperty("connected", out _) && spamAssassin.TryGetProperty("gtubeDetected", out var gtube) && (spamAssassin.GetProperty("connected").GetBoolean() || !gtube.GetBoolean()),
            "The SpamAssassin test reports connection and GTUBE detection separately.");

        await Dkim();

        async Task Dkim()
        {
            string domain = "dkim-" + Guid.NewGuid().ToString("N") + ".test";
            long domainId = (await Send("domains.create", new { fields = new { name = domain, active = true } })).GetProperty("id").GetInt64();
            try
            {
                async Task Create(string local, int level = 0) =>
                    await Send("accounts.create", new { domainId, fields = new { address = local + "@" + domain, password, active = true, level } });
                await Create("sender"); await Create("sink"); await Create("manager", 1);
                string domainManager = "manager@" + domain;
                var state = await native.SendAsync("dkim.read", domainManager, password, new { domainId }, CancellationToken.None);
                check(state.GetProperty("keys").GetArrayLength() == 0 && !state.GetProperty("signEnabled").GetBoolean(), "A domain administrator reads their domain's DKIM state.");
                if (manager is not null) await Refuse("dkim.read", new { domainId }, 403, manager);
                try { await native.SendAsync("dkim.generate", domainManager, password, new { domainId, selector = "s1" }, CancellationToken.None); throw new Exception("Domain administrator generated a key."); }
                catch (ManagementError error) { check(error.Status == 403, "Only server administrators generate DKIM keys."); }
                await Refuse("dkim.update", new { domainId, fields = new { signEnabled = true } }, 409);
                foreach (string bad in new[] { "bad_selector", "-lead", "a..b", "x" + new string('y', 70) })
                    await Refuse("dkim.generate", new { domainId, selector = bad }, 422);
                foreach (int bits in new[] { 1024, 3072, 4096 }) await Refuse("dkim.generate", new { domainId, selector = "s1", bits }, 422);
                await Refuse("dkim.generate", new { domainId, selector = "s1", path = "C:\\keys\\x.pem" }, 422);
                var generated = await Send("dkim.generate", new { domainId, selector = "s1" });
                var key = generated.GetProperty("keys").EnumerateArray().Single();
                string record = key.GetProperty("dnsRecord").GetString()!;
                check(record.StartsWith("v=DKIM1; k=rsa; p=", StringComparison.Ordinal) && key.GetProperty("dnsName").GetString() == "s1._domainkey." + domain && !key.GetProperty("active").GetBoolean(),
                    "A generated key is inactive and reports its DNS record.");
                check(!generated.GetRawText().Contains("PRIVATE KEY", StringComparison.Ordinal), "The private key is never returned.");
                await Refuse("dkim.generate", new { domainId, selector = "s1" }, 409);
                await Refuse("dkim.activate", new { domainId, selector = "missing" }, 404);
                var active = await Send("dkim.activate", new { domainId, selector = "s1" });
                check(active.GetProperty("selector").GetString() == "s1" && active.GetProperty("keyManaged").GetBoolean() && active.GetProperty("keyConfigured").GetBoolean(), "Activating a key sets the selector and key.");
                await Refuse("dkim.update", new { domainId, fields = new { algorithm = 3 } }, 422);
                await Refuse("dkim.update", new { domainId, fields = new { headerCanonicalization = 0 } }, 422);
                var signing = await Send("dkim.update", new { domainId, fields = new { signEnabled = true, algorithm = 2, headerCanonicalization = 2, bodyCanonicalization = 2 } });
                check(signing.GetProperty("signEnabled").GetBoolean(), "Signing is enabled once a key is active.");
                await Refuse("dkim.delete", new { domainId, selector = "s1" }, 409);
                await Send("dkim.generate", new { domainId, selector = "s2", bits = 2048 });
                check((await Send("dkim.delete", new { domainId, selector = "s2" })).GetProperty("keys").GetArrayLength() == 1, "An inactive key is deleted.");

                // Conformance: the server signs, an independent RFC 6376 check verifies.
                string subject = "DKIM " + Guid.NewGuid().ToString("N");
                await MailboxBehaviorTests.Submit("sender@" + domain, "sink@" + domain, subject, body: "Line one\r\nLine  two with  spaces   \r\n\r\n");
                await MailboxBehaviorTests.WaitFor("sink@" + domain, password, 1);
                string message = (await MailboxBehaviorTests.Read("sink@" + domain, password)).Single();
                check(message.Contains("DKIM-Signature:", StringComparison.OrdinalIgnoreCase), "Mail from a signing domain carries a DKIM signature.");
                check(VerifyDkim(message, record, out string reason), "The DKIM signature verifies against the published key: " + reason);
                check(!VerifyDkim(message.Replace(subject, subject + "x"), record, out _), "Changing a signed header breaks the signature.");
                await Send("dkim.update", new { domainId, fields = new { signEnabled = false } });
            }
            finally { await Send("domains.delete", new { id = domainId }); }
        }
    }

    // Verifies one rsa-sha256 DKIM-Signature (simple or relaxed canonicalization) per RFC 6376.
    static bool VerifyDkim(string message, string dnsRecord, out string reason)
    {
        int split = message.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (split < 0) { reason = "no header/body separator"; return false; }
        string headerBlock = message[..(split + 2)], body = message[(split + 4)..];
        var headers = new List<(string Name, string Raw)>();
        foreach (Match match in Regex.Matches(headerBlock, @"(?m)^([^\s:]+):[^\r\n]*(?:\r\n[ \t][^\r\n]*)*\r\n"))
            headers.Add((match.Groups[1].Value, match.Value));
        var signature = headers.FirstOrDefault(header => header.Name.Equals("DKIM-Signature", StringComparison.OrdinalIgnoreCase));
        if (signature.Raw is null) { reason = "no signature"; return false; }
        var tags = new Dictionary<string, string>();
        foreach (string part in Regex.Replace(signature.Raw[(signature.Raw.IndexOf(':') + 1)..], @"\s+", "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        { int equals = part.IndexOf('='); if (equals > 0) tags[part[..equals]] = part[(equals + 1)..]; }
        if (!tags.TryGetValue("a", out var algorithm) || algorithm != "rsa-sha256") { reason = "algorithm " + algorithm; return false; }
        string[] canonical = (tags.GetValueOrDefault("c") ?? "simple/simple").Split('/');
        bool relaxedHeader = canonical[0] == "relaxed", relaxedBody = canonical.Length > 1 && canonical[1] == "relaxed";
        string canonicalBody;
        if (relaxedBody)
        {
            var lines = body.Split("\r\n").Select(line => Regex.Replace(line, @"[ \t]+", " ").TrimEnd(' ')).ToList();
            while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
            canonicalBody = lines.Count == 0 ? "" : string.Join("\r\n", lines) + "\r\n";
        }
        else
        {
            canonicalBody = body;
            while (canonicalBody.EndsWith("\r\n\r\n", StringComparison.Ordinal)) canonicalBody = canonicalBody[..^2];
            if (canonicalBody.Length == 0) canonicalBody = "\r\n";
        }
        string bodyHash = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(canonicalBody)));
        if (bodyHash != tags.GetValueOrDefault("bh")) { reason = "body hash mismatch"; return false; }
        string Canonical(string raw)
        {
            if (!relaxedHeader) return raw;
            int colon = raw.IndexOf(':');
            string value = Regex.Replace(raw[(colon + 1)..].Replace("\r\n", ""), @"[ \t]+", " ").Trim();
            return raw[..colon].Trim().ToLowerInvariant() + ":" + value + "\r\n";
        }
        var used = new HashSet<int>(); var signed = new StringBuilder();
        foreach (string name in tags["h"].Split(':'))
        {
            for (int index = headers.Count - 1; index >= 0; --index)
                if (!used.Contains(index) && headers[index].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { used.Add(index); signed.Append(Canonical(headers[index].Raw)); break; }
        }
        // The signature header is signed with an empty b= value; bh= keeps its value.
        string unsigned = Regex.Replace(signature.Raw, @"(?<=[;\s:])b\s*=\s*[^;]*", "b=");
        string canonicalSignature = Canonical(unsigned);
        signed.Append(canonicalSignature.EndsWith("\r\n", StringComparison.Ordinal) ? canonicalSignature[..^2] : canonicalSignature);
        string publicKey = Regex.Match(dnsRecord, @"p=([^;\s]+)").Groups[1].Value;
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
        bool valid = rsa.VerifyData(Encoding.ASCII.GetBytes(signed.ToString()), Convert.FromBase64String(tags["b"]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        reason = valid ? "valid" : "signature mismatch";
        return valid;
    }
}
