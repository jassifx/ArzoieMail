using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using ArzoieMail.Management;
using Microsoft.Extensions.DependencyInjection;

// ACME end to end against Pebble, Let's Encrypt's test server, with its DNS test server
// resolving every name to this machine. The real API answers the HTTP-01 challenges on its own
// port, the real mail service generates the keys and installs the certificates, and the renewal
// task renews without an administrator.
static class AcmeLiveTests
{
    const string Host = "mail.arzoie-ci.example.com";
    const int ChallengePort = 5002;

    public static async Task Run(Action<bool, string> check)
    {
        string username = Required("ARZOIEMAIL_TEST_USERNAME"), password = Required("ARZOIEMAIL_TEST_PASSWORD");
        string pebble = Required("ARZOIEMAIL_TEST_PEBBLE"), challengeServer = Required("ARZOIEMAIL_TEST_PEBBLE_CHALLTESTSRV");
        string work = Path.Combine(Path.GetTempPath(), "arzoie-pebble-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var processes = new List<Process>();
        try
        {
            // Pebble's own HTTPS certificate, from a root the API is told to trust for this test.
            using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var rootRequest = new CertificateRequest("CN=ArzoieMail Pebble test root", rootKey, HashAlgorithmName.SHA256);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            using var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
            using var serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var serverRequest = new CertificateRequest("CN=localhost", serverKey, HashAlgorithmName.SHA256);
            var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback);
            serverRequest.CertificateExtensions.Add(names.Build());
            serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
            using var server = serverRequest.Create(root, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(6), RandomNumberGenerator.GetBytes(12));
            string rootFile = Path.Combine(work, "root.pem");
            File.WriteAllText(rootFile, root.ExportCertificatePem());
            File.WriteAllText(Path.Combine(work, "cert.pem"), server.ExportCertificatePem() + "\n" + root.ExportCertificatePem());
            File.WriteAllText(Path.Combine(work, "key.pem"), serverKey.ExportPkcs8PrivateKeyPem());
            // Certificates valid for 20 days, so a fresh one is already due for renewal.
            File.WriteAllText(Path.Combine(work, "pebble.json"), JsonSerializer.Serialize(new
            {
                pebble = new
                {
                    listenAddress = "127.0.0.1:14000", managementListenAddress = "127.0.0.1:15000",
                    certificate = Path.Combine(work, "cert.pem"), privateKey = Path.Combine(work, "key.pem"),
                    httpPort = ChallengePort, tlsPort = 5001, ocspResponderURL = "", externalAccountBindingRequired = false,
                    retryAfter = new { authz = 1, order = 1 }, certificateValidityPeriod = 1728000,
                    profiles = new { @default = new { description = "CI", validityPeriod = 1728000 } }
                }
            }));
            processes.Add(Start(challengeServer, "-defaultIPv4 127.0.0.1 -defaultIPv6 \"\" -dnsserver 127.0.0.1:8053 -http01 \"\" -https01 \"\" -tlsalpn01 \"\" -doh \"\" -management 127.0.0.1:8055", work, []));
            processes.Add(Start(pebble, $"-config \"{Path.Combine(work, "pebble.json")}\" -dnsserver 127.0.0.1:8053", work,
                [("PEBBLE_VA_NOSLEEP", "1"), ("PEBBLE_WFE_NONCEREJECT", "0")]));
            await WaitForDirectory(rootFile);

            await using var app = ManagementHost.Build(["--environment", "Development", "--urls", "http://127.0.0.1:0",
                "--Acme:HttpPort", ChallengePort.ToString(), "--Acme:TrustedRootFile", rootFile, "--Acme:RenewalCheckMinutes", "0"]);
            await app.StartAsync();
            using var admin = new LiveRestTests.BrowserSession(new Uri(app.Urls.Single()), check);
            await admin.Login(username, password);
            var before = await admin.Send("/acme", HttpMethod.Get);
            string? certificateId = null;
            try
            {
                // The challenge port answers challenges and nothing else.
                using (var plain = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{ChallengePort}") })
                {
                    using var panel = await plain.GetAsync("/api/v1/session");
                    using var unknown = await plain.GetAsync("/.well-known/acme-challenge/unknown-token");
                    check(panel.StatusCode == HttpStatusCode.NotFound && unknown.StatusCode == HttpStatusCode.NotFound, "The challenge port serves no panel and no unknown challenge.");
                }
                // Without accepted terms nothing is requested from the certificate authority.
                await admin.Send("/acme", HttpMethod.Patch, new { directoryUrl = "https://localhost:14000/dir", contact = "", termsAgreed = false });
                var refusedJob = await admin.Send("/certificates/acme", HttpMethod.Post, new { name = "Pebble refused", hostnames = new[] { Host } }, status: 202);
                var refused = await Wait(admin, refusedJob.GetProperty("id").GetString()!);
                check(refused.GetProperty("status").GetString() == "failed" && refused.GetProperty("message").GetString()!.Contains("terms", StringComparison.Ordinal),
                    "Issuance refuses until the certificate authority's terms are accepted.");
                await admin.Send("/certificates/acme", HttpMethod.Post, new { name = "Bad", hostnames = new[] { Host }, path = "x" }, status: 422);

                await admin.Send("/acme", HttpMethod.Patch, new { directoryUrl = "https://localhost:14000/dir", contact = "", termsAgreed = true });
                var job = await admin.Send("/certificates/acme", HttpMethod.Post, new { name = "Pebble " + Guid.NewGuid().ToString("N")[..6], hostnames = new[] { Host }, autoRenew = true }, status: 202);
                var done = await Wait(admin, job.GetProperty("id").GetString()!);
                check(done.GetProperty("status").GetString() == "succeeded", "Pebble issues a certificate after validating HTTP-01 through the API: " + done.GetProperty("message").GetString());
                if (done.GetProperty("status").GetString() != "succeeded") return;
                var certificate = done.GetProperty("certificate");
                certificateId = certificate.GetProperty("id").GetString();
                string first = certificate.GetProperty("fingerprintSha256").GetString()!;
                check(certificate.GetProperty("dnsNames").EnumerateArray().Any(name => name.GetString() == Host) && certificate.GetProperty("acme").GetProperty("autoRenew").GetBoolean(),
                    "The issued certificate names the host and renews automatically.");
                check((await admin.Send("/acme", HttpMethod.Get)).GetProperty("accountUrl").GetString()!.StartsWith("https://localhost:14000/", StringComparison.Ordinal),
                    "The ACME account was registered and recorded.");

                // Renewal without an administrator: the certificate is valid for 20 days, so it is due.
                var (renewed, failed) = await app.Services.GetRequiredService<AcmeRenewalService>().RunOnceAsync(CancellationToken.None);
                check(renewed >= 1 && failed == 0, $"The renewal task renews due certificates ({renewed} renewed, {failed} failed).");
                var after = await admin.Send($"/certificates/{certificateId}", HttpMethod.Get);
                string second = after.GetProperty("fingerprintSha256").GetString()!;
                check(second != first && after.GetProperty("previousAvailable").GetBoolean(), "Renewal installs a new certificate and keeps the previous one.");

                // A manual renewal from the panel.
                job = await admin.Send($"/certificates/{certificateId}/renew", HttpMethod.Post, new { }, status: 202);
                done = await Wait(admin, job.GetProperty("id").GetString()!);
                check(done.GetProperty("status").GetString() == "succeeded"
                    && done.GetProperty("certificate").GetProperty("fingerprintSha256").GetString() != second, "Renew now issues another certificate: " + done.GetProperty("message").GetString());
                await admin.Send("/certificates/acme/jobs/" + new string('0', 32), HttpMethod.Get, status: 404);
            }
            finally
            {
                if (certificateId is not null) await admin.Send($"/certificates/{certificateId}", HttpMethod.Delete);
                await admin.Send("/acme", HttpMethod.Patch, new { directoryUrl = before.GetProperty("directoryUrl").GetString(),
                    contact = before.GetProperty("contact").GetString(), termsAgreed = before.GetProperty("termsAgreed").GetBoolean() });
                await app.StopAsync();
            }
        }
        finally
        {
            foreach (var process in processes) { try { if (!process.HasExited) process.Kill(true); process.Dispose(); } catch (Exception) { } }
            try { Directory.Delete(work, true); } catch (IOException) { }
        }
    }

    static Process Start(string file, string arguments, string directory, (string Name, string Value)[] environment)
    {
        var start = new ProcessStartInfo(file, arguments) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var (name, value) in environment) start.Environment[name] = value;
        var process = Process.Start(start) ?? throw new Exception("Could not start " + Path.GetFileName(file));
        process.OutputDataReceived += (_, line) => { if (line.Data is not null) Console.WriteLine("[" + Path.GetFileNameWithoutExtension(file) + "] " + line.Data); };
        process.ErrorDataReceived += (_, line) => { if (line.Data is not null) Console.WriteLine("[" + Path.GetFileNameWithoutExtension(file) + "] " + line.Data); };
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        return process;
    }

    static async Task WaitForDirectory(string rootFile)
    {
        using var handler = AcmeIssuer.CreateHandler(new AcmeOptions { TrustedRootFile = rootFile });
        using var client = new HttpClient(handler);
        AcmeIssuer.ConfigureClient(client);
        string last = "no response";
        for (int attempt = 0; attempt < 60; ++attempt)
        {
            try
            {
                using var response = await client.GetAsync("https://localhost:14000/dir");
                if (response.IsSuccessStatusCode) return;
                string body = await response.Content.ReadAsStringAsync();
                last = "HTTP " + (int)response.StatusCode + " from " + response.RequestMessage?.RequestUri + ": " + (body.Length > 400 ? body[..400] : body);
            }
            catch (HttpRequestException error) { last = error.Message + (error.InnerException is null ? "" : " / " + error.InnerException.Message); }
            await Task.Delay(500);
        }
        throw new Exception("Pebble's directory did not answer: " + last);
    }

    static async Task<JsonElement> Wait(LiveRestTests.BrowserSession admin, string id)
    {
        for (int attempt = 0; attempt < 240; ++attempt)
        {
            var job = await admin.Send("/certificates/acme/jobs/" + id, HttpMethod.Get);
            if (job.GetProperty("status").GetString() != "running") return job;
            await Task.Delay(500);
        }
        throw new Exception("The ACME job did not finish.");
    }

    static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new Exception("Set " + name + ".");
}
