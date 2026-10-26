using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArzoieMail.Management;

public sealed record DnsInput(string[]? Addresses = null, string[]? Includes = null, string? Selector = null, string? PublicKey = null);
public sealed record DnsRecord(string Name, string Type, object? Published, string? Proposed, string Status, string[] Errors, string[] Warnings);
public sealed record DnsAdvice(string Domain, DnsRecord Spf, DnsRecord Dkim, DnsRecord Dmarc, string Notice);

public sealed class DnsAdvisor(ITxtResolver resolver)
{
    private readonly Dictionary<string, string[]?> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> errors = [], warnings = [];
    private int queries, lookups;
    private static readonly Regex DomainPattern = new(@"^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));
    private static string Domain(string name)
    {
        name = name.TrimEnd('.').ToLowerInvariant();
        if (name.Length > 253 || !DomainPattern.IsMatch(name)) throw new ManagementError(422, "Supply a valid ASCII domain (punycode for international names).");
        return name;
    }
    private async Task<string[]?> Txt(string name, CancellationToken token)
    {
        if (cache.TryGetValue(name, out var found)) return found;
        if (++queries > 32) { warnings.Add("DNS query budget exceeded; inspection is inconclusive."); return null; }
        try
        {
            var records = await resolver.QueryAsync(name, token);
            if (records.Length > 64 || records.Any(record => record.Length > 8192))
            {
                warnings.Add("DNS response exceeds the inspection budget; result is inconclusive.");
                return cache[name] = null;
            }
            return cache[name] = records;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        { warnings.Add("DNS lookup failed for " + name + ". Retry before changing records."); return cache[name] = null; }
    }
    private static bool IsSpf(string text) => text.Length >= 6
        && text.AsSpan(0, 6).Equals("v=spf1", StringComparison.OrdinalIgnoreCase)
        && (text.Length == 6 || char.IsWhiteSpace(text[6]));
    private async Task<string?> Spf(string domain, HashSet<string> path, CancellationToken token)
    {
        if (path.Contains(domain)) { errors.Add("SPF include/redirect cycle at " + domain + "."); return null; }
        if (path.Count > 10) { errors.Add("SPF recursion exceeds ten levels."); return null; }
        var records = await Txt(domain, token);
        if (records is null) return null;
        var policies = records.Where(IsSpf).ToArray();
        if (policies.Length != 1) { errors.Add(policies.Length == 0 ? "No SPF record at " + domain + "." : "Multiple SPF records at " + domain + "."); return null; }
        var modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? redirect = null;
        bool all = false;
        foreach (string term in policies[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            if (term.Contains('='))
            {
                var pair = term.Split('=', 2);
                if (!modifiers.Add(pair[0])) errors.Add("Duplicate SPF modifier " + pair[0] + ".");
                if (pair[0].Equals("redirect", StringComparison.OrdinalIgnoreCase)) redirect = pair[1];
                else if (!pair[0].Equals("exp", StringComparison.OrdinalIgnoreCase)) warnings.Add("Unknown SPF modifier is preserved: " + pair[0]);
                continue;
            }
            if (all) { warnings.Add("Mechanism after all is unreachable: " + term); continue; }
            string unqualified = term.Length > 0 && "+-~?".Contains(term[0]) ? term[1..] : term;
            var match = Regex.Match(unqualified, @"^(all|include|ip4|ip6|a|mx|ptr|exists)([:/].*)?$", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));
            if (!match.Success) { errors.Add("Invalid SPF mechanism: " + term); continue; }
            string kind = match.Groups[1].Value.ToLowerInvariant(), suffix = match.Groups[2].Value;
            if (kind == "all")
            {
                all = true;
                if (suffix.Length != 0) errors.Add("Invalid all mechanism.");
                if (term is "all" or "+all") warnings.Add("SPF allows every sender (+all).");
            }
            else if (kind is "ip4" or "ip6")
            {
                var bits = suffix.TrimStart(':').Split('/');
                if (!suffix.StartsWith(':') || !IPAddress.TryParse(bits[0], out var ip) || bits.Length > 2
                    || (kind == "ip4") != (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    || (bits.Length == 2 && (!int.TryParse(bits[1], out int cidr) || cidr < 0 || cidr > (kind == "ip4" ? 32 : 128))))
                    errors.Add("Invalid SPF address: " + term);
            }
            else
            {
                if (++lookups > 10) { errors.Add("SPF exceeds the ten DNS-lookup mechanism limit."); break; }
                if (kind == "include")
                {
                    if (!suffix.StartsWith(':') || suffix.Length < 2) errors.Add("An SPF include requires a domain.");
                    else await Follow(suffix[1..], new(path) { domain }, token);
                }
                else
                {
                    warnings.Add("Sender-dependent mechanism requires message-context evaluation: " + term);
                    if (kind is "a" or "mx")
                    {
                        var cidr = Regex.Match(suffix, @"^(?::[^/]+)?(?:/(\d{1,3}))?(?://(\d{1,3}))?$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));
                        if (!cidr.Success || (cidr.Groups[1].Success && int.Parse(cidr.Groups[1].Value) > 32)
                            || (cidr.Groups[2].Success && int.Parse(cidr.Groups[2].Value) > 128)) errors.Add("Invalid SPF address mechanism: " + term);
                    }
                    if (kind == "exists" && (!suffix.StartsWith(':') || suffix.Length < 2)) errors.Add("An exists mechanism requires a domain.");
                }
            }
        }
        if (!all && redirect is not null)
        {
            if (++lookups > 10) errors.Add("SPF exceeds the ten DNS-lookup mechanism limit.");
            else await Follow(redirect, new(path) { domain }, token);
        }
        return policies[0];
    }
    private async Task Follow(string name, HashSet<string> path, CancellationToken token)
    {
        if (name.Contains('%')) { warnings.Add("SPF macros require message context; this branch is inconclusive."); return; }
        try { await Spf(Domain(name), path, token); }
        catch (ManagementError) { errors.Add("Invalid SPF include/redirect domain."); }
    }
    private static Dictionary<string, string> Tags(string record)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string part in record.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || !result.TryAdd(pair[0], pair[1])) throw new ManagementError(422, "Malformed or duplicate DNS record tag.");
        }
        return result;
    }
    public async Task<DnsAdvice> InspectAsync(string name, DnsInput input, CancellationToken token)
    {
        string domain = Domain(name);
        if ((input.Addresses?.Length ?? 0) > 10 || (input.Includes?.Length ?? 0) > 10) throw new ManagementError(422, "Supply up to ten addresses and ten includes.");
        var additions = new List<string>();
        foreach (string address in input.Addresses ?? [])
        {
            if (!IPAddress.TryParse(address, out var ip) || ip.ToString() != address.ToLowerInvariant()) throw new ManagementError(422, "Supply canonical IPv4 or IPv6 sending addresses.");
            additions.Add((ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "ip4:" : "ip6:") + address);
        }
        foreach (string include in input.Includes ?? []) additions.Add("include:" + Domain(include));
        string? original = await Spf(domain, [], token);
        string? proposal = null;
        bool missingOnly = errors.SequenceEqual(["No SPF record at " + domain + "."]);
        if (cache[domain] is not null && (errors.Count == 0 || missingOnly))
        {
            var terms = (original ?? "v=spf1 ~all").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
            int before = terms.FindIndex(term => term.TrimStart('+', '-', '~', '?').Equals("all", StringComparison.OrdinalIgnoreCase));
            if (before < 0) before = terms.Count;
            terms.InsertRange(before, additions.Distinct(StringComparer.OrdinalIgnoreCase).Except(terms, StringComparer.OrdinalIgnoreCase));
            proposal = string.Join(' ', terms);
            var checker = new DnsAdvisor(new ProposedResolver(domain, proposal, resolver));
            await checker.Spf(domain, [], token);
            if (checker.errors.Count != 0) proposal = null;
            errors.AddRange(checker.errors); warnings.AddRange(checker.warnings);
        }
        var spf = new DnsRecord(domain, "TXT", original, proposal, cache[domain] is null ? "inconclusive" : errors.Count > 0 ? "invalid" : warnings.Count > 0 ? "inconclusive" : "valid",
            errors.Distinct().ToArray(), warnings.Distinct().ToArray());
        string selector = input.Selector ?? "arzoie";
        if (selector.Length > 253 || selector.Split('.').Any(label => label.Length is 0 or > 63)
            || !Regex.IsMatch(selector, @"^[a-zA-Z0-9][a-zA-Z0-9_.-]*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1))) throw new ManagementError(422, "Invalid DKIM selector.");
        string keyName = selector + "._domainkey." + domain;
        var keys = await Txt(keyName, token);
        var keyRecords = (keys ?? []).Where(record => Regex.IsMatch(record, @"(?:^|;)\s*p=", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1))).ToArray();
        var keyErrors = new List<string>();
        string? publicKey = input.PublicKey;
        if (publicKey?.Length > 4096) throw new ManagementError(422, "DKIM public key is too large.");
        if (!string.IsNullOrEmpty(publicKey) && !RsaKey(publicKey, 2048)) throw new ManagementError(422, "Supply a base64 RSA public key of at least 2048 bits, never a private key.");
        if (keyRecords.Length != 1) keyErrors.Add("Expected one DKIM key record for this selector.");
        else
        {
            try
            {
                var tags = Tags(keyRecords[0]);
                string key = tags.GetValueOrDefault("p", "");
                string type = tags.GetValueOrDefault("k", "rsa");
                if (tags.GetValueOrDefault("v", "DKIM1") != "DKIM1" || key.Length == 0) keyErrors.Add("Invalid or revoked DKIM key.");
                else if (type == "rsa" ? !RsaKey(key, 1024) : type != "ed25519" || !EdKey(key)) keyErrors.Add("Malformed or unsupported DKIM public key.");
                if (!string.IsNullOrEmpty(publicKey) && key != publicKey) keyErrors.Add("Published DKIM key differs from the supplied public key.");
            }
            catch (ManagementError e) { keyErrors.Add(e.Message); }
        }
        var dkim = new DnsRecord(keyName, "TXT", keyRecords, string.IsNullOrEmpty(publicKey) ? null : "v=DKIM1; k=rsa; p=" + publicKey,
            keys is null ? "inconclusive" : keyErrors.Count == 0 ? "valid" : "invalid", keyErrors.ToArray(), []);
        string dmarcName = "_dmarc." + domain;
        var dmarcRecords = await Txt(dmarcName, token);
        var policies = (dmarcRecords ?? []).Where(record => record.StartsWith("v=DMARC1", StringComparison.Ordinal)).ToArray();
        var dmarcErrors = new List<string>();
        if (policies.Length != 1) dmarcErrors.Add("Expected one DMARC policy here; organizational-domain inheritance is not checked.");
        else
        {
            try
            {
                var tags = Tags(policies[0]);
                if (tags.GetValueOrDefault("v") != "DMARC1" || tags.GetValueOrDefault("p") is not ("none" or "quarantine" or "reject")) dmarcErrors.Add("Invalid DMARC version or policy.");
                foreach (string tag in new[] { "adkim", "aspf" }) if (tags.TryGetValue(tag, out var mode) && mode is not ("r" or "s")) dmarcErrors.Add("Invalid alignment mode.");
                if (tags.TryGetValue("pct", out var pct) && (!int.TryParse(pct, out int percentage) || percentage is < 0 or > 100)) dmarcErrors.Add("DMARC pct must be between 0 and 100.");
            }
            catch (ManagementError e) { dmarcErrors.Add(e.Message); }
        }
        var dmarc = new DnsRecord(dmarcName, "TXT", policies, policies.Length == 0 && dmarcRecords is not null ? "v=DMARC1; p=none; adkim=r; aspf=r" : null,
            dmarcRecords is null ? "inconclusive" : dmarcErrors.Count == 0 ? "valid" : "invalid", dmarcErrors.ToArray(), []);
        return new(domain, spf, dkim, dmarc, "DNS inspection only. Review proposals at your DNS provider. Test messages from every sender for SPF/DKIM/DMARC alignment before increasing DMARC enforcement.");
    }
    private static bool RsaKey(string value, int bits)
    {
        try { using var rsa = RSA.Create(); rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(value), out _); return rsa.KeySize >= bits; }
        catch (Exception e) when (e is FormatException or CryptographicException) { return false; }
    }
    private static bool EdKey(string value) { try { return Convert.FromBase64String(value).Length == 32; } catch (FormatException) { return false; } }
    private sealed class ProposedResolver(string domain, string proposal, ITxtResolver inner) : ITxtResolver
    {
        public Task<string[]> QueryAsync(string name, CancellationToken token) => name == domain ? Task.FromResult(new[] { proposal }) : inner.QueryAsync(name, token);
    }
}
