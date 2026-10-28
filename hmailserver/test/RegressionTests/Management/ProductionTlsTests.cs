using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using ArzoieMail.Management;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

static class ProductionTlsTests
{
    public static async Task Run(Action<bool, string> check, bool liveNative = false)
    {
        foreach (string allowed in new[] { "*", "" })
        {
            bool refused = false;
            try { await using var invalid = ManagementHost.Build(["--environment", "Production", "--AllowedHosts", allowed]); }
            catch (InvalidOperationException) { refused = true; }
            check(refused, "Production startup refuses missing or wildcard AllowedHosts.");
        }
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=ArzoieMail isolated CI root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        using var key = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=admin.example", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("admin.example");
        leafRequest.CertificateExtensions.Add(names.Build());
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, true));
        using var signed = leafRequest.Create(root, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(30), RandomNumberGenerator.GetBytes(16));
        using var ephemeral = signed.CopyWithPrivateKey(key);
        // Windows Schannel needs a Windows-backed key handle for the server credential.
        // Import only this generated fixture key; no certificate is installed in a trust store.
        byte[] pfx = ephemeral.Export(X509ContentType.Pfx);
        X509Certificate2 imported;
        try { imported = X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
        using var certificate = imported;
        await using var app = ManagementHost.Build(["--environment", "Production", "--urls", "https://127.0.0.1:0", "--AllowedHosts", "admin.example"], services =>
        {
            if (!liveNative) services.AddSingleton<IManagementChannel>(new FakeChannel());
            services.Configure<KestrelServerOptions>(options => options.ConfigureHttpsDefaults(https => https.ServerCertificate = certificate));
        });
        await app.StartAsync();
        try
        {
            int port = new Uri(app.Urls.Single()).Port;
            using var handler = CreateHandler(root);
            using var client = new HttpClient(handler) { BaseAddress = new Uri($"https://admin.example:{port}"), Timeout = TimeSpan.FromSeconds(15) };
            using var bootstrap = await client.GetAsync("/api/v1/session");
            check(bootstrap.IsSuccessStatusCode, "Production API serves a real TLS connection with hostname and private-root validation.");
            check(bootstrap.Headers.Contains("Strict-Transport-Security"), "Production HTTPS advertises HSTS.");
            check(bootstrap.Headers.GetValues("Set-Cookie").Any(SecureCookie), "Production CSRF cookie is Secure, HttpOnly and SameSite Strict.");
            var session = JsonSerializer.Deserialize<JsonElement>(await bootstrap.Content.ReadAsStringAsync()).GetProperty("data");
            string username = liveNative ? Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_USERNAME") ?? throw new Exception("Missing TLS native fixture username.") : "tls-fixture";
            string password = liveNative ? Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_PASSWORD") ?? throw new Exception("Missing TLS native fixture password.") : "test-password";
            using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/session") { Content = JsonContent.Create(new { username, password }) };
            login.Headers.Add("X-CSRF-Token", session.GetProperty("csrf").GetString());
            using var loggedIn = await client.SendAsync(login);
            check(loggedIn.IsSuccessStatusCode, "Production login accepts same-origin antiforgery over actual HTTPS.");
            check(loggedIn.Headers.GetValues("Set-Cookie").Any(cookie => cookie.StartsWith("ArzoieMail.Session=", StringComparison.Ordinal) && SecureCookie(cookie)), "Production authentication cookie has all required security attributes.");
            using var status = await client.GetAsync("/api/v1/status");
            check(status.IsSuccessStatusCode, "Secure session authenticates a subsequent HTTPS request.");
            if (liveNative) check(JsonSerializer.Deserialize<JsonElement>(await status.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("version").GetString()!.StartsWith("6.", StringComparison.Ordinal), "Production HTTPS authentication reaches the actual ArzoieMail 6 native service.");
            // Keep TLS SNI/name validation on admin.example while independently forging HTTP Host.
            using (var connection = new TcpClient())
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                await connection.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
                using var tls = new SslStream(connection.GetStream());
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "admin.example", CertificateChainPolicy = TrustRoot(root) }, deadline.Token);
                await tls.WriteAsync(System.Text.Encoding.ASCII.GetBytes("GET /api/v1/session HTTP/1.1\r\nHost: foreign.invalid\r\nConnection: close\r\n\r\n"), deadline.Token);
                using var reader = new StreamReader(tls);
                string? line = await reader.ReadLineAsync(deadline.Token);
                check(line?.StartsWith("HTTP/1.1 400 ", StringComparison.Ordinal) == true, "HTTPS still enforces the configured HTTP Host allowlist.");
            }

            using var untrustedHandler = CreateHandler(null);
            using var untrusted = new HttpClient(untrustedHandler);
            try { using var unexpected = await untrusted.GetAsync(client.BaseAddress + "api/v1/session"); throw new Exception("Untrusted TLS certificate accepted."); }
            catch (HttpRequestException error) { check(error.HttpRequestError == HttpRequestError.SecureConnectionError, "OS trust rejects the private test certificate without a custom trust root."); }
            try { using var unexpected = await client.GetAsync($"https://wrong.example:{port}/api/v1/session"); throw new Exception("Wrong TLS hostname accepted."); }
            catch (HttpRequestException error) { check(error.HttpRequestError == HttpRequestError.SecureConnectionError, "Trusted-chain TLS still rejects a certificate hostname mismatch."); }
        }
        finally { await app.StopAsync(); }
    }

    static bool SecureCookie(string cookie) => cookie.Contains("secure", StringComparison.OrdinalIgnoreCase)
        && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) && cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase);

    static SocketsHttpHandler CreateHandler(X509Certificate2? root)
    {
        var handler = new SocketsHttpHandler { UseProxy = false, CookieContainer = new CookieContainer() };
        handler.ConnectCallback = async (context, token) =>
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(IPAddress.Loopback, context.DnsEndPoint.Port, token); return new NetworkStream(socket, ownsSocket: true); }
            catch { socket.Dispose(); throw; }
        };
        if (root is not null)
        {
            handler.SslOptions.CertificateChainPolicy = TrustRoot(root);
        }
        return handler;
    }

    static X509ChainPolicy TrustRoot(X509Certificate2 root)
    {
        var policy = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck };
        policy.CustomTrustStore.Add(root); return policy;
    }
}
