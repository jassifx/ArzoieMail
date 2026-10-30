using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using ArzoieMail.Management;

static class CertificateInspectionTests
{
    public static byte[] CreatePfxFixture(string password, bool large = false)
    {
        using var certificate = CreateCertificate(extensionBytes: large ? 20000 : 0);
        return new X509Certificate2Collection(certificate).ExportPkcs12(
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 2000), password);
    }

    public static void WriteBrowserFixture()
    {
        string path = Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_PFX_PATH") ?? throw new Exception("Set the protected VM fixture path.");
        string password = Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_PFX_PASSWORD") ?? throw new Exception("Set the VM fixture password.");
        byte[] pfx = CreatePfxFixture(password, large: true);
        try
        {
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            output.Write(pfx); output.Flush(flushToDisk: true);
        }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public static void Run(Action<bool, string> check)
    {
        const string password = "PFX-test-\u03c0\u6f22-!9";
        var buffers = new List<byte[]>();
        byte[] Bundle(X509Certificate2Collection certificates, int iterations = 2000)
        {
            var bytes = certificates.ExportPkcs12(new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, iterations), password);
            buffers.Add(bytes); return bytes;
        }
        byte[] Pfx(X509Certificate2 certificate, int iterations = 2000) => Bundle(new X509Certificate2Collection(certificate), iterations);
        void Refuse(byte[] data, string secret, string[] hosts, bool workLimit = false)
        {
            try { CertificateInspection.Inspect(data, secret, hosts); throw new Exception("Unsafe certificate inspection unexpectedly succeeded."); }
            catch (ManagementError error)
            {
                check(error.Status == 422, "PFX input errors are controlled validation failures.");
                check(!error.Message.Contains(password, StringComparison.Ordinal) && !error.Message.Contains("PRIVATE KEY", StringComparison.Ordinal),
                    "PFX errors omit passwords and private material.");
                if (workLimit) check(error.Message.Contains("loading limits", StringComparison.Ordinal), "PFX work/structure limits are enforced before platform loading.");
            }
        }
        try
        {
            using var leaf = CreateCertificate();
            byte[] pfx = Pfx(leaf);
            var now = DateTimeOffset.UtcNow;
            var report = CertificateInspection.Inspect(pfx, password,
                ["MAIL.EXAMPLE.TEST.", "one.wild.example.test", "deep.one.wild.example.test", "b\u00fccher.example", "127.0.0.1", "2001:db8::1"], new Clock(now));
            check(report.Sha256 == leaf.GetCertHashString(HashAlgorithmName.SHA256) && report.Sha256.Length == 64, "Inspection identifies the exact leaf by SHA-256.");
            check(report.KeyAlgorithm == "ECDSA" && report.KeyBits == 256 && report.MeetsKeyStrengthMinimum, "ECDSA key policy metadata is accurate.");
            check(report.IsCurrentlyValid && !report.IsCertificateAuthority && report.ServerAuthenticationEku, "Current server-leaf validity and usage are separate checks.");
            check(report.Hosts.Select(item => item.Matches).SequenceEqual(new[] { true, true, false, true, true, false }),
                "SAN matching covers case/FQDN normalization, wildcard depth, IDNA and DNS/IP separation.");
            check(report.Hosts[3].Host == "xn--bcher-kva.example", "Internationalized input is normalized to an ASCII DNS name.");
            check(report.DnsNames.Contains("*.wild.example.test") && report.IpAddresses.SequenceEqual(new[] { "127.0.0.1" }), "SAN metadata remains explicit.");
            check(!report.ChainTrustedWithoutRevocationCheck && report.RevocationStatus == "NotChecked" && report.ChainStatus.Length > 0,
                "A bundled self-signed certificate does not become trusted, and skipped revocation is explicit.");
            string json = JsonSerializer.Serialize(report);
            check(!json.Contains(password, StringComparison.Ordinal) && !json.Contains(Convert.ToBase64String(pfx), StringComparison.Ordinal)
                && !json.Contains("PrivateKey", StringComparison.Ordinal) && !json.Contains("RawData", StringComparison.Ordinal), "Inspection returns public metadata only.");
            using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadOnly);
                var installed = store.Certificates.Find(X509FindType.FindByThumbprint, leaf.Thumbprint, validOnly: false);
                try { check(installed.Count == 0, "Inspection does not install the certificate into the personal store."); }
                finally { foreach (var certificate in installed) certificate.Dispose(); }
            }
            Refuse(pfx, "incorrect-PFX-password", ["mail.example.test"]);
            Refuse([], password, ["mail.example.test"]);
            Refuse(new byte[524289], password, ["mail.example.test"]);
            Refuse([1, 2, 3], password, ["mail.example.test"]);
            Refuse(pfx, "bad\0password", ["mail.example.test"]);
            Refuse(pfx, new string('\u6f22', 400), ["mail.example.test"]);
            Refuse(pfx, password, []);
            Refuse(pfx, password, Enumerable.Repeat("mail.example.test", 33).ToArray());
            foreach (string host in new[] { "https://mail.example.test", "mail.example.test:443", "127.1", "mail.example.test..", "bad\n.example", "*.example.test", "fe80::1%12", "[::1]" })
                Refuse(pfx, password, [host]);
            using var publicOnly = X509CertificateLoader.LoadCertificate(leaf.RawData);
            Refuse(Pfx(publicOnly), password, ["mail.example.test"]);
            using var other = CreateCertificate(host: "other.example.test");
            Refuse(Bundle(new X509Certificate2Collection(new[] { leaf, other })), password, ["mail.example.test"], workLimit: true);
            Refuse(Pfx(leaf, iterations: 300001), password, ["mail.example.test"], workLimit: true);
            var excess = new X509Certificate2Collection(leaf);
            try
            {
                for (int index = 0; index < 16; index++)
                {
                    using var extra = CreateCertificate(host: $"extra-{index}.example.test");
                    excess.Add(X509CertificateLoader.LoadCertificate(extra.RawData));
                }
                Refuse(Bundle(excess), password, ["mail.example.test"], workLimit: true);
            }
            finally { foreach (var certificate in excess.Cast<X509Certificate2>().Where(item => !ReferenceEquals(item, leaf))) certificate.Dispose(); }
            var expired = CertificateInspection.Inspect(pfx, password, ["mail.example.test"], new Clock(now.AddYears(1)));
            check(!expired.IsCurrentlyValid && expired.Warnings.Any(message => message.Contains("expired", StringComparison.Ordinal)), "Expiry is evaluated against the trusted injected clock.");
            var future = CertificateInspection.Inspect(pfx, password, ["mail.example.test"], new Clock(now.AddYears(-1)));
            check(!future.IsCurrentlyValid, "Not-yet-valid certificates remain visibly invalid.");
            var expiring = CertificateInspection.Inspect(pfx, password, ["mail.example.test"], new Clock(now.AddDays(30)));
            check(expiring.IsCurrentlyValid && expiring.Warnings.Any(message => message.Contains("30 days", StringComparison.Ordinal)), "Approaching expiry has an explicit warning.");
            using var client = CreateCertificate(clientOnly: true);
            check(!CertificateInspection.Inspect(Pfx(client), password, ["mail.example.test"]).ServerAuthenticationEku, "Client-only EKU is not treated as server authentication.");
            using var authority = CreateCertificate(authority: true);
            check(CertificateInspection.Inspect(Pfx(authority), password, ["mail.example.test"]).IsCertificateAuthority, "A CA certificate is identified rather than silently approved as a server leaf.");
            using var subjectOnly = CreateCertificate(subjectOnly: true);
            check(!CertificateInspection.Inspect(Pfx(subjectOnly), password, ["mail.example.test"]).Hosts.Single().Matches, "Hostname matching does not fall back to the subject common name.");
            using var rsa = RSA.Create(2048);
            using var rsaCertificate = CreateCertificate(rsa: rsa);
            var rsaReport = CertificateInspection.Inspect(Pfx(rsaCertificate), password, ["mail.example.test"]);
            check(rsaReport.KeyAlgorithm == "RSA" && rsaReport.KeyBits == 2048 && rsaReport.MeetsKeyStrengthMinimum, "RSA metadata follows the actual private-key pair.");
            using var weak = RSA.Create(1024);
            using var weakCertificate = CreateCertificate(rsa: weak);
            check(!CertificateInspection.Inspect(Pfx(weakCertificate), password, ["mail.example.test"]).MeetsKeyStrengthMinimum, "Weak keys are visible as a failed strength check.");
        }
        finally { foreach (byte[] buffer in buffers) CryptographicOperations.ZeroMemory(buffer); }
    }

    private static X509Certificate2 CreateCertificate(bool clientOnly = false, bool authority = false,
        bool subjectOnly = false, string host = "mail.example.test", RSA? rsa = null, int extensionBytes = 0)
    {
        using var ec = rsa is null ? ECDsa.Create(ECCurve.NamedCurves.nistP256) : null;
        var request = rsa is null ? new CertificateRequest("CN=" + host, ec!, HashAlgorithmName.SHA256)
            : new CertificateRequest("CN=" + host, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(authority, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(authority ? X509KeyUsageFlags.KeyCertSign : X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
            { new(clientOnly ? "1.3.6.1.5.5.7.3.2" : "1.3.6.1.5.5.7.3.1") }, false));
        if (extensionBytes > 0)
            request.CertificateExtensions.Add(new X509Extension("1.3.6.1.4.1.57264.999", new byte[extensionBytes], false));
        if (!subjectOnly)
        {
            var names = new SubjectAlternativeNameBuilder(); names.AddDnsName(host);
            names.AddDnsName("*.wild.example.test"); names.AddDnsName("xn--bcher-kva.example"); names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
        }
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(45));
    }
}
