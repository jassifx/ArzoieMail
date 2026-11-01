using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using ArzoieMail.Management;

// Mail listener certificates against the real native dispatcher and service: import checks,
// replacement and rollback, ACME orders completed by a test certificate authority, renewal
// automation, and a real TLS listener serving each generation after a restart.
static class CertificateTests
{
    public static async Task Run(IManagementChannel native, string username, string password, Action<bool, string> check)
    {
        Task<JsonElement> Send(string op, object? data, string? actor = null) => native.SendAsync(op, actor ?? username, password, data, CancellationToken.None);
        async Task Refuse(string op, object? data, int status, string? actor = null)
        {
            try { await Send(op, data, actor); throw new Exception("Unexpected success: " + op); }
            catch (ManagementError error) { check(error.Status == status, $"{op}: expected {status}, received {error.Status}."); }
        }
        async Task RefuseAutomation(string op, object? data, int status)
        {
            try { await native.SendAutomationAsync(op, data, CancellationToken.None); throw new Exception("Unexpected automation success: " + op); }
            catch (ManagementError error) { check(error.Status == status, $"Automation {op}: expected {status}, received {error.Status}."); }
        }
        string? manager = Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_DOMAIN_USERNAME"), ordinary = Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_USER_USERNAME");
        foreach (string actor in new[] { manager, ordinary }.OfType<string>())
        {
            await Refuse("certificates.list", new { }, 403, actor);
            await Refuse("listeners.create", new { fields = new { } }, 403, actor);
            await Refuse("acme.sign", new { input = "x" }, 403, actor);
        }
        // The automation scope renews and nothing else.
        await RefuseAutomation("certificates.list", null, 403);
        await RefuseAutomation("certificates.import", null, 403);
        await RefuseAutomation("listeners.delete", new { id = 1 }, 403);
        check((await native.SendAutomationAsync("acme.renewals.due", null, CancellationToken.None)).TryGetProperty("items", out _), "The renewal task can list due certificates.");

        string suffix = Guid.NewGuid().ToString("N")[..8];
        var created = new List<string>();
        string? listenerId = null;
        var acmeBefore = await Send("acme.account.read", new { });
        try
        {
            // Import refusals: each leaves nothing behind.
            var good = SelfSigned("mail-" + suffix + ".test", TimeSpan.FromDays(90));
            var other = SelfSigned("other-" + suffix + ".test", TimeSpan.FromDays(90));
            await Refuse("certificates.import", new { name = "Mismatch " + suffix, certificatePem = good.Pem, privateKeyPem = other.Key }, 422);
            await Refuse("certificates.import", new { name = "Expired " + suffix, certificatePem = SelfSigned("old.test", TimeSpan.FromDays(-1)).Pem, privateKeyPem = "" }, 422);
            var expired = SelfSigned("old-" + suffix + ".test", TimeSpan.FromDays(10), DateTimeOffset.UtcNow.AddDays(-30));
            await Refuse("certificates.import", new { name = "Expired " + suffix, certificatePem = expired.Pem, privateKeyPem = expired.Key }, 422);
            var authority = Authority("Test CA " + suffix);
            await Refuse("certificates.import", new { name = "CA " + suffix, certificatePem = authority.Pem, privateKeyPem = authority.Key }, 422);
            var weak = SelfSigned("weak-" + suffix + ".test", TimeSpan.FromDays(90), bits: 1024);
            await Refuse("certificates.import", new { name = "Weak " + suffix, certificatePem = weak.Pem, privateKeyPem = weak.Key }, 422);
            using (var rsa = RSA.Create(2048))
            {
                string encrypted = rsa.ExportEncryptedPkcs8PrivateKeyPem("secret", new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 10000));
                await Refuse("certificates.import", new { name = "Encrypted " + suffix, certificatePem = good.Pem, privateKeyPem = encrypted }, 422);
            }
            await Refuse("certificates.import", new { name = "Garbage " + suffix, certificatePem = "not a certificate", privateKeyPem = good.Key }, 422);
            await Refuse("certificates.import", new { name = "Path " + suffix, certificatePem = good.Pem, privateKeyPem = good.Key, path = "C:\\x.pem" }, 422);

            var imported = await Send("certificates.import", new { name = "Imported " + suffix, certificatePem = good.Pem, privateKeyPem = good.Key });
            string id = imported.GetProperty("id").GetString()!;
            created.Add(id);
            check(imported.GetProperty("managed").GetBoolean() && imported.GetProperty("readable").GetBoolean()
                && imported.GetProperty("fingerprintSha256").GetString() == Fingerprint(good.Certificate), "An imported certificate is stored as a managed certificate and reported by fingerprint.");
            check(imported.GetProperty("dnsNames").EnumerateArray().Any(name => name.GetString() == "mail-" + suffix + ".test"), "The certificate's host names are reported.");
            check(!imported.GetRawText().Contains("PRIVATE KEY", StringComparison.Ordinal) && !imported.GetRawText().Contains("\\\\", StringComparison.Ordinal), "No key or path is returned.");
            await Refuse("certificates.import", new { name = "Imported " + suffix, certificatePem = other.Pem, privateKeyPem = other.Key }, 409);
            await Send("certificates.update", new { id, name = "Renamed " + suffix });
            check((await Send("certificates.read", new { id })).GetProperty("name").GetString() == "Renamed " + suffix, "A certificate can be renamed.");
            await Refuse("certificates.update", new { id, autoRenew = true }, 409);
            await Refuse("certificates.rollback", new { id }, 409);

            // A real TLS listener on a free port serves the certificate after a restart.
            int port = FreePort();
            await Refuse("listeners.create", new { fields = new { protocolCode = 5, address = "127.0.0.1", port, securityCode = 1 } }, 422);
            await Refuse("listeners.create", new { fields = new { protocolCode = 5, address = "127.0.0.1", port, securityCode = 1, certificateId = "999999999" } }, 404);
            await Refuse("listeners.create", new { fields = new { protocolCode = 1, address = "0.0.0.0", port = 25, securityCode = 0 } }, 409);
            await Refuse("listeners.create", new { fields = new { protocolCode = 2, address = "127.0.0.1", port, securityCode = 0 } }, 422);
            var listener = await Send("listeners.create", new { fields = new { protocolCode = 5, address = "127.0.0.1", port, securityCode = 1, certificateId = id } });
            listenerId = listener.GetProperty("id").GetString();
            check(listener.GetProperty("restartRequired").GetBoolean(), "A listener change says it needs a restart.");
            await Refuse("certificates.delete", new { id }, 409);
            await Restart(Send);
            check(await Served(port) == Fingerprint(good.Certificate), "After a restart the new TLS listener serves the imported certificate.");

            // Replacement keeps one previous generation; rollback returns to it.
            var next = SelfSigned("mail-" + suffix + ".test", TimeSpan.FromDays(120));
            var replaced = await Send("certificates.replace", new { id, certificatePem = next.Pem, privateKeyPem = next.Key });
            check(replaced.GetProperty("previousAvailable").GetBoolean() && replaced.GetProperty("restartRequired").GetBoolean(), "A replaced certificate in use keeps its previous generation and needs a restart.");
            await Restart(Send);
            check(await Served(port) == Fingerprint(next.Certificate), "After a restart the listener serves the replacement.");
            await Send("certificates.rollback", new { id });
            await Restart(Send);
            check(await Served(port) == Fingerprint(good.Certificate), "After a rollback and restart the listener serves the previous certificate again.");

            // ACME settings and signing. The account key never leaves the service.
            await Refuse("acme.account.update", new { fields = new { directoryUrl = "http://acme.example/directory" } }, 422);
            await Refuse("acme.account.update", new { fields = new { contact = "not an address" } }, 422);
            var account = await Send("acme.account.update", new { fields = new { directoryUrl = "https://acme.invalid/directory", contact = "admin@example.com", termsAgreed = true } });
            check(account.GetProperty("termsAgreed").GetBoolean() && account.GetProperty("accountUrl").GetString() == "", "ACME settings are saved; a new directory has no account yet.");
            var jwk = account.GetProperty("jwk");
            const string signingInput = "eyJhbGciOiJFUzI1NiJ9.eyJ0ZXN0Ijp0cnVlfQ";
            string signature = (await Send("acme.sign", new { input = signingInput })).GetProperty("signature").GetString()!;
            using (var verifier = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = FromBase64Url(jwk.GetProperty("x").GetString()!), Y = FromBase64Url(jwk.GetProperty("y").GetString()!) } }))
                check(verifier.VerifyData(Encoding.ASCII.GetBytes(signingInput), FromBase64Url(signature), HashAlgorithmName.SHA256), "The service signs ACME requests with the account key it publishes.");
            check(!account.GetRawText().Contains("\"d\"", StringComparison.Ordinal), "The account's private key is never returned.");
            await Refuse("acme.account.registered", new { directoryUrl = "https://other.invalid/directory", accountUrl = "https://acme.invalid/acct/1" }, 409);
            await Send("acme.account.registered", new { directoryUrl = "https://acme.invalid/directory", accountUrl = "https://acme.invalid/acct/1" });

            // An ACME order completed by a test authority: the key stays in the service, which
            // checks that the issued certificate uses it and names every host.
            string host = "acme-" + suffix + ".test";
            foreach (var bad in new object[] { new[] { "*.wild.test" }, new[] { "UPPER_case.test" }, new[] { "192.0.2.1" }, Array.Empty<string>() })
                await Refuse("certificates.acme.prepare", new { hostnames = bad, name = "Bad " + suffix }, 422);
            var order = await Send("certificates.acme.prepare", new { hostnames = new[] { host }, name = "ACME " + suffix, autoRenew = true });
            var (chain, _) = Sign(order, authority, TimeSpan.FromDays(20));
            await Refuse("certificates.acme.complete", new { pendingId = "0123456789abcdef0123456789abcdef", certificatePem = chain }, 404);
            await Refuse("certificates.acme.complete", new { pendingId = order.GetProperty("pendingId").GetString(), certificatePem = good.Pem }, 422);
            order = await Send("certificates.acme.prepare", new { hostnames = new[] { host }, name = "ACME " + suffix, autoRenew = true });
            (chain, var issued) = Sign(order, authority, TimeSpan.FromDays(20));
            var acmeCertificate = await Send("certificates.acme.complete", new { pendingId = order.GetProperty("pendingId").GetString(), certificatePem = chain });
            string acmeId = acmeCertificate.GetProperty("id").GetString()!;
            created.Add(acmeId);
            check(acmeCertificate.GetProperty("acme").GetProperty("autoRenew").GetBoolean() && acmeCertificate.GetProperty("fingerprintSha256").GetString() == Fingerprint(issued),
                "A completed ACME order installs the issued certificate with its renewal settings.");
            await RefuseAutomation("acme.renewals.prepare", new { certificateId = id }, 403);

            // Renewal automation: due within 30 days, renewed without an administrator, and the
            // mail services restart because the certificate is in use.
            await Send("listeners.update", new { id = listenerId, fields = new { certificateId = acmeId } });
            await Restart(Send);
            var due = await native.SendAutomationAsync("acme.renewals.due", null, CancellationToken.None);
            check(due.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("certificateId").GetString() == acmeId), "A certificate within 30 days of expiry is due for renewal.");
            var renewal = await native.SendAutomationAsync("acme.renewals.prepare", new { certificateId = acmeId }, CancellationToken.None);
            // One order per certificate: a renewal in progress blocks Renew now and another renewal.
            await Refuse("certificates.acme.prepare", new { certificateId = acmeId }, 409);
            await RefuseAutomation("acme.renewals.prepare", new { certificateId = acmeId }, 409);
            check(!(await native.SendAutomationAsync("acme.renewals.due", null, CancellationToken.None)).GetProperty("items").EnumerateArray()
                .Any(item => item.GetProperty("certificateId").GetString() == acmeId), "A certificate with an order in progress is not offered for renewal again.");
            await RefuseAutomation("acme.renewals.complete", new { pendingId = order.GetProperty("pendingId").GetString(), certificatePem = chain }, 404);
            var (renewedChain, renewedCertificate) = Sign(renewal, authority, TimeSpan.FromDays(90));
            var renewed = await native.SendAutomationAsync("acme.renewals.complete", new { pendingId = renewal.GetProperty("pendingId").GetString(), certificatePem = renewedChain }, CancellationToken.None);
            check(renewed.GetProperty("restarted").GetBoolean() && renewed.GetProperty("previousAvailable").GetBoolean(), "A renewed certificate in use restarts the mail services and keeps the previous generation.");
            check(await Served(port) == Fingerprint(renewedCertificate), "After automated renewal the listener serves the renewed certificate.");
            check(!(await native.SendAutomationAsync("acme.renewals.due", null, CancellationToken.None)).GetProperty("items").EnumerateArray()
                .Any(item => item.GetProperty("certificateId").GetString() == acmeId), "A renewed certificate is not due again.");
            await native.SendAutomationAsync("acme.renewals.report", new { certificateId = acmeId, error = "Simulated failure" }, CancellationToken.None);
            check((await Send("certificates.read", new { id = acmeId })).GetProperty("acme").GetProperty("lastError").GetString() == "Simulated failure", "A failed renewal is recorded for the panel.");
            // A failed order is discarded, so the certificate can be ordered again at once; each
            // scope discards only its own orders.
            var manual = await Send("certificates.acme.prepare", new { certificateId = acmeId });
            await RefuseAutomation("acme.renewals.discard", new { pendingId = manual.GetProperty("pendingId").GetString() }, 404);
            await Send("certificates.acme.discard", new { pendingId = manual.GetProperty("pendingId").GetString() });
            await Refuse("certificates.acme.discard", new { pendingId = manual.GetProperty("pendingId").GetString() }, 404);
            var again = await native.SendAutomationAsync("acme.renewals.prepare", new { certificateId = acmeId }, CancellationToken.None);
            await native.SendAutomationAsync("acme.renewals.discard", new { pendingId = again.GetProperty("pendingId").GetString() }, CancellationToken.None);

            // A host name too long for a common name is requested by its alternative name alone.
            string longHost = new string('l', 60) + "." + suffix + ".test";
            var longOrder = await Send("certificates.acme.prepare", new { hostnames = new[] { longHost, host }, name = "Long " + suffix, autoRenew = false });
            var longRequest = CertificateRequest.LoadSigningRequest(FromBase64Url(longOrder.GetProperty("csr").GetString()!), HashAlgorithmName.SHA256,
                CertificateRequestLoadOptions.UnsafeLoadCertificateExtensions);
            check(longRequest.SubjectName.Name == "CN=" + host && longRequest.CertificateExtensions.OfType<X509SubjectAlternativeNameExtension>().Single().EnumerateDnsNames().Contains(longHost),
                "A host name longer than 64 characters goes in the alternative names; the common name uses the first that fits.");
            var onlyLong = await Send("certificates.acme.prepare", new { hostnames = new[] { longHost }, name = "Long only " + suffix, autoRenew = false });
            check(CertificateRequest.LoadSigningRequest(FromBase64Url(onlyLong.GetProperty("csr").GetString()!), HashAlgorithmName.SHA256).SubjectName.RawData.Length <= 2,
                "A request whose host names are all too long has an empty subject.");
            await Send("certificates.acme.discard", new { pendingId = longOrder.GetProperty("pendingId").GetString() });
            await Send("certificates.acme.discard", new { pendingId = onlyLong.GetProperty("pendingId").GetString() });

            await Send("certificates.update", new { id = acmeId, autoRenew = false });
            await RefuseAutomation("acme.renewals.prepare", new { certificateId = acmeId }, 403);
        }
        finally
        {
            if (listenerId is not null) await Send("listeners.delete", new { id = listenerId });
            foreach (string id in created) { try { await Send("certificates.delete", new { id }); } catch (ManagementError) { } }
            await Send("acme.account.update", new { fields = new { directoryUrl = acmeBefore.GetProperty("directoryUrl").GetString(),
                contact = acmeBefore.GetProperty("contact").GetString(), termsAgreed = acmeBefore.GetProperty("termsAgreed").GetBoolean() } });
            await Restart(Send);
        }
        foreach (string id in created) await Refuse("certificates.read", new { id }, 404);
    }

    static async Task Restart(Func<string, object?, string?, Task<JsonElement>> send)
    {
        await send("server.stop", new { expectedState = 3 }, null);
        await send("server.start", new { expectedState = 1 }, null);
    }

    static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    // The SHA-256 fingerprint of the certificate a TLS client receives on the port.
    static async Task<string> Served(int port)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        for (int attempt = 0; ; ++attempt)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
                string? fingerprint = null;
                await using var tls = new SslStream(client.GetStream(), false, (_, certificate, _, _) =>
                {
                    if (certificate is not null) fingerprint = Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
                    return true;
                });
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost" }, timeout.Token);
                return fingerprint ?? "";
            }
            catch (Exception) when (attempt < 20 && !timeout.IsCancellationRequested) { await Task.Delay(250); }
        }
    }

    static string Fingerprint(X509Certificate2 certificate) => Convert.ToHexString(SHA256.HashData(certificate.RawData));

    internal static (string Pem, string Key, X509Certificate2 Certificate) SelfSigned(string host, TimeSpan lifetime, DateTimeOffset? from = null, int bits = 2048)
    {
        using var rsa = RSA.Create(bits);
        var request = new CertificateRequest("CN=" + host, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName(host);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        var start = from ?? DateTimeOffset.UtcNow.AddMinutes(-5);
        var end = lifetime > TimeSpan.Zero ? start + lifetime : DateTimeOffset.UtcNow.AddDays(-1);
        if (end <= start) start = end.AddDays(-1);
        var certificate = request.CreateSelfSigned(start, end);
        return (certificate.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem(), X509CertificateLoader.LoadCertificate(certificate.RawData));
    }

    sealed record TestAuthority(string Pem, string Key, X509Certificate2 Certificate);

    static TestAuthority Authority(string name)
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=" + name, ecdsa, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        return new TestAuthority(certificate.ExportCertificatePem(), ecdsa.ExportPkcs8PrivateKeyPem(), certificate);
    }

    // Issues a certificate for the service's CSR, as a certificate authority would.
    static (string Chain, X509Certificate2 Leaf) Sign(JsonElement order, TestAuthority authority, TimeSpan lifetime)
    {
        byte[] der = FromBase64Url(order.GetProperty("csr").GetString()!);
        var request = CertificateRequest.LoadSigningRequest(der, HashAlgorithmName.SHA256, CertificateRequestLoadOptions.UnsafeLoadCertificateExtensions);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(authority.Certificate, true, false));
        var leaf = request.Create(authority.Certificate, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow + lifetime, RandomNumberGenerator.GetBytes(12));
        return (leaf.ExportCertificatePem() + "\n" + authority.Pem, leaf);
    }

    static byte[] FromBase64Url(string text)
    {
        string padded = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded + new string('=', (4 - padded.Length % 4) % 4));
    }
}
