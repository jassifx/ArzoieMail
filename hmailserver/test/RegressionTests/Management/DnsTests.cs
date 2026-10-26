using System.Security.Cryptography;
using ArzoieMail.Management;

static class DnsTests
{
    public static async Task<int> Run()
    {
        int assertions = 0;
        void Check(bool condition, string message) { ++assertions; if (!condition) throw new Exception(message); }
        async Task<DnsAdvice> Inspect(Dictionary<string, string[]?> records, DnsInput? input = null) =>
            await new DnsAdvisor(new FixtureResolver(records)).InspectAsync("example.com", input ?? new(), CancellationToken.None);
        var shared = await Inspect(new() { ["example.com"] = ["v=spf1 include:provider.example.net ip4:192.0.2.1 -all"], ["provider.example.net"] = ["v=spf1 ip4:198.51.100.0/24 ~all"] }, new(Addresses: ["203.0.113.10"]));
        Check(shared.Spf.Proposed == "v=spf1 include:provider.example.net ip4:192.0.2.1 ip4:203.0.113.10 -all", "Preserve existing provider policy when adding sender.");
        Check(shared.Spf.Status == "valid", "Literal/include policy validates.");
        var redirect = await Inspect(new() { ["example.com"] = ["v=spf1 redirect=provider.example.net"], ["provider.example.net"] = ["v=spf1 -all"] }, new(Addresses: ["203.0.113.10"]));
        Check(redirect.Spf.Proposed!.Contains("redirect=provider.example.net"), "Preserve redirect when merging.");
        var duplicates = await Inspect(new() { ["example.com"] = ["v=spf1 -all", "v=spf1 ~all"] });
        Check(duplicates.Spf.Proposed is null && duplicates.Spf.Status == "invalid", "Conflicting SPF policies must not be overwritten.");
        var cycle = await Inspect(new() { ["example.com"] = ["v=spf1 include:other.example.net -all"], ["other.example.net"] = ["v=spf1 include:example.com -all"] });
        Check(cycle.Spf.Status == "invalid", "Detect cyclic includes.");
        var failure = await Inspect(new() { ["example.com"] = null });
        Check(failure.Spf.Status == "inconclusive" && failure.Spf.Proposed is null, "Failed DNS lookup must not become a replacement policy.");
        var nestedFailure = await Inspect(new() { ["example.com"] = ["v=spf1 include:provider.example.net -all"], ["provider.example.net"] = null });
        Check(nestedFailure.Spf.Status == "inconclusive", "Nested DNS failure is inconclusive.");
        var macro = await Inspect(new() { ["example.com"] = ["v=spf1 include:%{i}.example.net -all"] });
        Check(macro.Spf.Status == "inconclusive", "Macros require message context.");
        var many = new Dictionary<string, string[]?> { ["example.com"] = ["v=spf1 " + string.Join(' ', Enumerable.Range(1,11).Select(i => $"include:p{i}.example.net")) + " -all"] };
        foreach (int i in Enumerable.Range(1,11)) many[$"p{i}.example.net"] = ["v=spf1 -all"];
        Check((await Inspect(many)).Spf.Status == "invalid", "Detect ten-lookup budget overflow.");
        var deep = new Dictionary<string, string[]?> { ["example.com"] = ["v=spf1 include:p1.example.net -all"] };
        foreach (int i in Enumerable.Range(1,9)) deep[$"p{i}.example.net"] = [$"v=spf1 include:p{i+1}.example.net -all"];
        deep["p10.example.net"] = ["v=spf1 -all"];
        Check((await Inspect(deep)).Spf.Status == "valid", "Exactly ten nested includes remain within the lookup budget.");
        Check((await Inspect(new() { ["example.com"] = ["v=spf1 a/99 -all"] })).Spf.Status == "invalid", "Validate CIDR limits.");
        Check((await Inspect(new() { ["example.com"] = ["v=spf1 --all"] })).Spf.Status == "invalid", "Refuse multiple SPF qualifiers.");
        Check((await Inspect(new() { ["example.com"] = [new string('x', 9000)] })).Spf.Status == "inconclusive", "Bound DNS record processing.");
        Check((await Inspect(new() { ["example.com"] = ["v=spf1 ip4:999.2.3.4 -all"] })).Spf.Proposed is null, "Invalid IP policies cannot be merged.");
        var proposedInclude = await Inspect(new() { ["example.com"] = ["v=spf1 -all"] }, new(Includes: ["missing.example.net"]));
        Check(proposedInclude.Spf.Proposed is null, "Validate added include providers.");
        var noDuplicates = await Inspect(new() { ["example.com"] = ["v=spf1 ip4:192.0.2.1 -all"] }, new(Addresses: ["192.0.2.1"]));
        Check(noDuplicates.Spf.Proposed == "v=spf1 ip4:192.0.2.1 -all", "Do not duplicate existing addresses.");
        var upper = await Inspect(new() { ["example.com"] = ["v=spf1\tINCLUDE:PROVIDER.EXAMPLE.NET -ALL"], ["provider.example.net"] = ["v=spf1 -all"] }, new(Addresses: ["203.0.113.10"], Includes: ["provider.example.net"]));
        Check(upper.Spf.Proposed == "v=spf1 INCLUDE:PROVIDER.EXAMPLE.NET ip4:203.0.113.10 -ALL", "Handle tabs, case-insensitive mechanisms and existing provider includes.");
        var missing = await Inspect(new());
        Check(missing.Spf.Proposed == "v=spf1 ~all", "Start with reviewed softfail policy.");
        Check(missing.Dmarc.Proposed == "v=DMARC1; p=none; adkim=r; aspf=r", "DMARC starts in monitoring mode.");
        var badDmarc = await Inspect(new() { ["_dmarc.example.com"] = ["v=DMARC1; p=none; p=reject"] });
        Check(badDmarc.Dmarc.Status == "invalid", "Reject duplicate DMARC tags.");
        var badKey = await Inspect(new() { ["arzoie._domainkey.example.com"] = ["v=DKIM1; p=not-a-key"] });
        Check(badKey.Dkim.Status == "invalid", "Published malformed keys must not validate.");
        using var rsa = RSA.Create(2048);
        string publicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        var goodKey = await Inspect(new() { ["arzoie._domainkey.example.com"] = ["v=DKIM1; k=rsa; p=" + publicKey] }, new(PublicKey: publicKey));
        Check(goodKey.Dkim.Status == "valid" && goodKey.Dkim.Proposed!.Contains(publicKey), "Match published RSA key.");
        var nestedSelector = await Inspect(new(), new(Selector: "mail.s1"));
        Check(nestedSelector.Dkim.Name == "mail.s1._domainkey.example.com", "Support hierarchical DKIM selectors.");
        foreach (var badInput in new[] { new DnsInput(Addresses: ["not-an-ip"]), new DnsInput(Selector: "../secret"), new DnsInput(PublicKey: "private-or-invalid") })
        {
            try { await Inspect(new(), badInput); throw new Exception("Invalid input accepted."); }
            catch (ManagementError e) { Check(e.Status == 422, "Invalid DNS input status."); }
        }
        Console.WriteLine($"Passed {assertions} deterministic DNS assertions.");
        return assertions;
    }
    private sealed class FixtureResolver(Dictionary<string, string[]?> records) : ITxtResolver
    {
        public Task<string[]> QueryAsync(string name, CancellationToken token)
        {
            if (records.TryGetValue(name, out var value)) return value is null ? throw new IOException("DNS unavailable") : Task.FromResult(value);
            return Task.FromResult(Array.Empty<string>());
        }
    }
}
