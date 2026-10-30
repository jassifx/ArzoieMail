using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ArzoieMail.Management;

public sealed record CertificateHostMatch(string Host, bool Matches);
public sealed record CertificateInspectionResult(string Sha256, string Subject, string Issuer,
    DateTimeOffset NotBefore, DateTimeOffset NotAfter, string KeyAlgorithm, int KeyBits,
    bool MeetsKeyStrengthMinimum, bool IsCurrentlyValid, bool IsCertificateAuthority,
    bool ServerAuthenticationEku, string? KeyUsage, string[] DnsNames, string[] IpAddresses,
    CertificateHostMatch[] Hosts, bool ChainTrustedWithoutRevocationCheck, string[] ChainStatus,
    string RevocationStatus, string[] Warnings);

// In-memory preparation for the guided-import workflow, not an installation or
// activation API. Never return a certificate handle, PFX bytes or a private key.
public static class CertificateInspection
{
    public const int MaximumPfxBytes = 524288;
    private static readonly Pkcs12LoaderLimits Limits = CreateLimits();
    private static Pkcs12LoaderLimits CreateLimits()
    {
        var limits = new Pkcs12LoaderLimits
        {
            MacIterationLimit = 300000, IndividualKdfIterationLimit = 300000,
            TotalKdfIterationLimit = 1000000, MaxKeys = 1, MaxCertificates = 16,
            PreserveStorageProvider = false, PreserveKeyName = false,
            PreserveCertificateAlias = false, PreserveUnknownAttributes = false
        };
        limits.MakeReadOnly();
        return limits;
    }

    public static CertificateInspectionResult Inspect(ReadOnlySpan<byte> pfx, ReadOnlySpan<char> password,
        IReadOnlyList<string> expectedHosts, TimeProvider? clock = null)
    {
        if (pfx.IsEmpty || pfx.Length > MaximumPfxBytes)
            throw new ManagementError(422, "Select a PFX file between 1 byte and 512 KiB.");
        if (password.Contains('\0') || Encoding.UTF8.GetByteCount(password) > 1024)
            throw new ManagementError(422, "The PFX password must fit 1024 UTF-8 bytes and contain no null characters.");
        if (expectedHosts is null || expectedHosts.Count is < 1 or > 32)
            throw new ManagementError(422, "Supply between 1 and 32 mail hostnames or IP addresses.");
        string[] hosts = expectedHosts.Select(NormalizeHost).Distinct(StringComparer.Ordinal).ToArray();
        X509Certificate2Collection? certificates = null;
        try
        {
            certificates = X509CertificateLoader.LoadPkcs12Collection(pfx, password, X509KeyStorageFlags.EphemeralKeySet, Limits);
            var leaves = certificates.Cast<X509Certificate2>().Where(item => item.HasPrivateKey).ToArray();
            if (leaves.Length != 1) throw new ManagementError(422, "The PFX must contain exactly one certificate with its private key.");
            var leaf = leaves[0];
            if (leaf.Extensions.Cast<X509Extension>().GroupBy(extension => extension.Oid?.Value).Any(group => group.Count() > 1))
                throw new ManagementError(422, "The certificate contains ambiguous duplicate extensions.");
            var (algorithm, bits, matches) = InspectKey(leaf);
            if (!matches) throw new ManagementError(422, "The private key does not match the certificate.");
            var names = Extension(leaf, "2.5.29.17");
            var alternativeNames = names is null ? null : new X509SubjectAlternativeNameExtension(names.RawData, names.Critical);
            string[] dns = alternativeNames?.EnumerateDnsNames().Take(129).ToArray() ?? [];
            string[] ips = alternativeNames?.EnumerateIPAddresses().Take(129).Select(item => item.ToString()).ToArray() ?? [];
            if (dns.Length + ips.Length > 128 || dns.Any(name => name.Length is < 1 or > 253 || name.Any(character => char.IsControl(character) || char.IsWhiteSpace(character))))
                throw new ManagementError(422, "The certificate contains too many or malformed alternative names (maximum 128).");
            var constraints = Extension(leaf, "2.5.29.19");
            bool authority = constraints is not null && new X509BasicConstraintsExtension(constraints, constraints.Critical).CertificateAuthority;
            var usage = Extension(leaf, "2.5.29.15");
            string? keyUsage = usage is null ? null : new X509KeyUsageExtension(usage, usage.Critical).KeyUsages.ToString();
            var eku = Extension(leaf, "2.5.29.37");
            bool serverAuthentication = eku is null || new X509EnhancedKeyUsageExtension(eku, eku.Critical).EnhancedKeyUsages
                .Cast<Oid>().Any(item => item.Value is "1.3.6.1.5.5.7.3.1" or "2.5.29.37.0");
            DateTimeOffset now = (clock ?? TimeProvider.System).GetUtcNow();
            DateTimeOffset before = leaf.NotBefore.ToUniversalTime(), after = leaf.NotAfter.ToUniversalTime();
            bool valid = before <= now && now <= after;
            bool strong = algorithm == "ECDSA" ? bits >= 256 : bits >= 2048;
            var hostMatches = hosts.Select(host => new CertificateHostMatch(host,
                leaf.MatchesHostname(host, allowWildcards: true, allowCommonName: false))).ToArray();
            using var chain = new X509Chain();
            chain.ChainPolicy.DisableCertificateDownloads = true;
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationTime = now.UtcDateTime;
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
            foreach (var certificate in certificates.Cast<X509Certificate2>().Where(item => !ReferenceEquals(item, leaf)))
                chain.ChainPolicy.ExtraStore.Add(certificate);
            bool trusted = chain.Build(leaf);
            string[] status = chain.ChainStatus.Select(item => item.Status.ToString()).Distinct().ToArray();
            var warnings = new List<string> { "Revocation status was not checked. Inspection does not install or activate a certificate." };
            if (!trusted) warnings.Add("The chain is not trusted for server authentication by the current Windows identity using available certificates.");
            if (!valid) warnings.Add("The certificate is expired or not yet valid.");
            else if (after - now < TimeSpan.FromDays(30)) warnings.Add("The certificate expires within 30 days.");
            if (!strong) warnings.Add("The key is below the inspection policy minimum (RSA/DSA 2048 bits or ECDSA 256 bits).");
            if (authority) warnings.Add("This is a certificate-authority certificate; select a mail-server leaf certificate.");
            if (!serverAuthentication) warnings.Add("The certificate does not permit server authentication.");
            if (hostMatches.Any(item => !item.Matches)) warnings.Add("Some requested hosts do not match the certificate alternative names. Common-name fallback is disabled.");
            if (algorithm == "DSA") warnings.Add("DSA is a legacy algorithm and does not support TLS 1.3 server authentication.");
            if (leaf.Subject.Length > 1024 || leaf.Issuer.Length > 1024) warnings.Add("Long subject or issuer display names were shortened; identify this certificate by its SHA-256 fingerprint.");
            return new(leaf.GetCertHashString(HashAlgorithmName.SHA256), Display(leaf.Subject), Display(leaf.Issuer),
                before, after, algorithm, bits, strong, valid, authority, serverAuthentication, keyUsage,
                dns, ips, hostMatches, trusted, status, "NotChecked", warnings.ToArray());
        }
        catch (Pkcs12LoadLimitExceededException)
        {
            throw new ManagementError(422, "The PFX exceeds safe loading limits: one key, 16 certificates, 300000 iterations per MAC/KDF and 1000000 total KDF iterations. Re-export a smaller bundle with a supported work factor.");
        }
        catch (CryptographicException)
        {
            throw new ManagementError(422, "The PFX or certificate could not be read. Check its password, format and supported key provider.");
        }
        finally
        {
            if (certificates is not null) foreach (var certificate in certificates) certificate.Dispose();
        }
    }

    private static X509Extension? Extension(X509Certificate2 certificate, string oid) =>
        certificate.Extensions.Cast<X509Extension>().SingleOrDefault(item => item.Oid?.Value == oid);

    private static string Display(string value)
    {
        if (value.Length <= 1024) return value;
        int length = char.IsHighSurrogate(value[1023]) ? 1023 : 1024;
        return value[..length];
    }

    private static string NormalizeHost(string host)
    {
        if (string.IsNullOrEmpty(host) || host.Length > 1024 || host.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)) || host.IndexOfAny(['%', '[', ']']) >= 0)
            throw new ManagementError(422, "Use a DNS hostname or canonical IP address without a URL, port or scope identifier.");
        if (IPAddress.TryParse(host, out var ip))
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork && host != ip.ToString())
                throw new ManagementError(422, "Use canonical dotted-decimal IPv4 addresses.");
            return ip.ToString();
        }
        try
        {
            string dns = host.EndsWith('.') ? host[..^1] : host;
            string ascii = new IdnMapping { UseStd3AsciiRules = true }.GetAscii(dns).ToLowerInvariant();
            if (ascii.Length is < 1 or > 253 || ascii.Split('.').Any(label => label.Length is < 1 or > 63
                || label[0] == '-' || label[^1] == '-' || label.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')))
                throw new ArgumentException();
            return ascii;
        }
        catch (ArgumentException) { throw new ManagementError(422, "Use a valid DNS hostname or IP address."); }
    }

    private static (string Algorithm, int Bits, bool Matches) InspectKey(X509Certificate2 certificate)
    {
        using var rsa = certificate.GetRSAPrivateKey();
        if (rsa is not null)
        {
            using var key = certificate.GetRSAPublicKey();
            return ("RSA", rsa.KeySize, key is not null && CryptographicOperations.FixedTimeEquals(rsa.ExportSubjectPublicKeyInfo(), key.ExportSubjectPublicKeyInfo()));
        }
        using var ec = certificate.GetECDsaPrivateKey();
        if (ec is not null)
        {
            using var key = certificate.GetECDsaPublicKey();
            return ("ECDSA", ec.KeySize, key is not null && CryptographicOperations.FixedTimeEquals(ec.ExportSubjectPublicKeyInfo(), key.ExportSubjectPublicKeyInfo()));
        }
        using var dsa = certificate.GetDSAPrivateKey();
        if (dsa is not null)
        {
            using var key = certificate.GetDSAPublicKey();
            return ("DSA", dsa.KeySize, key is not null && CryptographicOperations.FixedTimeEquals(dsa.ExportSubjectPublicKeyInfo(), key.ExportSubjectPublicKeyInfo()));
        }
        throw new ManagementError(422, "This inspection workflow supports RSA, ECDSA and DSA private keys.");
    }
}
