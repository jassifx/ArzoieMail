using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

namespace ArzoieMail.Management;

// Certificates for the mail listeners: import (PEM or PFX), replacement, rollback, ACME
// issuance, listener assignment, and a TLS probe that shows which certificate a listener
// actually serves. Server administrators only. Private keys pass through this process only on
// import, in owned buffers cleared afterwards; ACME keys never do.
public static class CertificateEndpoints
{
    private const int MaximumImportBytes = 1048576;

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/certificates", (HttpContext c, CredentialVault v, IManagementChannel p) => Native(c, v, p, "certificates.list", null));
        api.MapPost("/certificates", (HttpContext c, CredentialVault v, IManagementChannel p) => Import(c, v, p, null)).RequireRateLimiting("certificate-inspection");
        api.MapGet("/certificates/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Native(c, v, p, "certificates.read", new { id }));
        api.MapPatch("/certificates/{id:long:min(1)}", async (long id, HttpContext c, CredentialVault v, IManagementChannel p) =>
        {
            var body = await Body(c);
            var arguments = new Dictionary<string, object?> { ["id"] = id };
            foreach (var property in body.EnumerateObject())
            {
                if (property.Name is not ("name" or "autoRenew")) throw new ManagementError(422, "Only name and autoRenew can be changed.");
                arguments[property.Name] = property.Value;
            }
            return await Native(c, v, p, "certificates.update", arguments);
        });
        api.MapPost("/certificates/{id:long:min(1)}/replace", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Import(c, v, p, id)).RequireRateLimiting("certificate-inspection");
        api.MapPost("/certificates/{id:long:min(1)}/rollback", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Native(c, v, p, "certificates.rollback", new { id }));
        api.MapDelete("/certificates/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Native(c, v, p, "certificates.delete", new { id }));

        api.MapGet("/acme", async (HttpContext c, CredentialVault v, IManagementChannel p, AcmeChallenges challenges) =>
        {
            // The account, and whether this process can answer HTTP-01 challenges.
            ServerAdministrator(c);
            var (username, password) = Credentials(c, v);
            var account = System.Text.Json.Nodes.JsonNode.Parse((await p.SendAsync("acme.account.read", username, password, null, c.RequestAborted)).GetRawText())!.AsObject();
            account["httpChallenge"] = new System.Text.Json.Nodes.JsonObject { ["port"] = challenges.Port, ["listening"] = challenges.Listening, ["message"] = challenges.Unavailable };
            return Results.Json(new { data = account });
        });
        api.MapPatch("/acme", async (HttpContext c, CredentialVault v, IManagementChannel p) => await Native(c, v, p, "acme.account.update", new { fields = await Body(c) }));
        api.MapPost("/certificates/acme", async (HttpContext c, CredentialVault v, IManagementChannel p, AcmeJobs jobs) =>
        {
            var body = await Body(c);
            foreach (var property in body.EnumerateObject())
                if (property.Name is not ("name" or "hostnames" or "autoRenew")) throw new ManagementError(422, "Supply a name, host names and optionally autoRenew.");
            return StartJob(c, v, p, jobs, body);
        });
        api.MapPost("/certificates/{id:long:min(1)}/renew", async (long id, HttpContext c, CredentialVault v, IManagementChannel p, AcmeJobs jobs) =>
        {
            var body = await Body(c);
            foreach (var property in body.EnumerateObject())
                if (property.Name is not ("hostnames" or "autoRenew")) throw new ManagementError(422, "Supply host names and optionally autoRenew.");
            var arguments = new Dictionary<string, object?> { ["certificateId"] = id.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            foreach (var property in body.EnumerateObject()) arguments[property.Name] = property.Value;
            return StartJob(c, v, p, jobs, arguments);
        });
        api.MapGet("/certificates/acme/jobs/{jobId}", (string jobId, HttpContext c, AcmeJobs jobs) =>
        {
            ServerAdministrator(c);
            var job = jobId.Length == 32 ? jobs.Find(jobId) : null;
            if (job is null) throw new ManagementError(404, "No such certificate request.");
            return Results.Json(new { data = new { id = job.Id, status = job.Status, message = job.Message, certificate = job.Certificate } });
        });

        api.MapPost("/listeners", async (HttpContext c, CredentialVault v, IManagementChannel p) => await Native(c, v, p, "listeners.create", new { fields = await Body(c) }));
        api.MapPatch("/listeners/{id:long:min(1)}", async (long id, HttpContext c, CredentialVault v, IManagementChannel p) => await Native(c, v, p, "listeners.update", new { id, fields = await Body(c) }));
        api.MapDelete("/listeners/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Native(c, v, p, "listeners.delete", new { id }));
        api.MapPost("/listeners/{id:long:min(1)}/probe", Probe);
    }

    private static void ServerAdministrator(HttpContext context)
    {
        if (context.User.FindFirstValue("level") != "2") throw new ManagementError(403, "Server administration is required.");
    }

    private static (string Username, string Password) Credentials(HttpContext context, CredentialVault vault)
    {
        var login = vault.Get(context.User.FindFirstValue(ClaimTypes.NameIdentifier));
        return (login.Username, login.Password);
    }

    private static async Task<IResult> Native(HttpContext context, CredentialVault vault, IManagementChannel channel, string operation, object? arguments)
    {
        ServerAdministrator(context);
        var (username, password) = Credentials(context, vault);
        return Results.Json(new { data = await channel.SendAsync(operation, username, password, arguments, context.RequestAborted) });
    }

    private static async Task<JsonElement> Body(HttpContext context)
    {
        ServerAdministrator(context);
        var body = await context.Request.ReadFromJsonAsync<JsonElement>(context.RequestAborted);
        if (body.ValueKind != JsonValueKind.Object) throw new ManagementError(422, "Supply a JSON object.");
        return body;
    }

    private static IResult StartJob(HttpContext context, CredentialVault vault, IManagementChannel channel, AcmeJobs jobs, object prepareArguments)
    {
        var (username, password) = Credentials(context, vault);
        var job = jobs.Start(AcmeNative.Administrator(channel, username, password), prepareArguments);
        return Results.Json(new { data = new { id = job.Id, status = job.Status } }, statusCode: 202);
    }

    // PEM ({certificatePem, privateKeyPem}) or PFX ({pfx: base64, password}); a name for a new
    // certificate. The PFX is opened here, bounded as in PFX inspection, and passed on as PEM.
    private static async Task<IResult> Import(HttpContext context, CredentialVault vault, IManagementChannel channel, long? id)
    {
        ServerAdministrator(context);
        if (context.Request.ContentLength > MaximumImportBytes) throw new ManagementError(413, "Certificate uploads must not exceed 1 MiB.");
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is null || feature.IsReadOnly) throw new ManagementError(503, "This host cannot accept certificate uploads.");
        feature.MaxRequestBodySize = MaximumImportBytes;
        byte[] wire = new byte[MaximumImportBytes + 1];
        string? keyPem = null;
        try
        {
            int length = 0;
            while (length < wire.Length)
            {
                int read = await context.Request.Body.ReadAsync(wire.AsMemory(length), context.RequestAborted);
                if (read == 0) break;
                length += read;
            }
            if (length > MaximumImportBytes) throw new ManagementError(413, "Certificate uploads must not exceed 1 MiB.");
            using var document = JsonDocument.Parse(wire.AsMemory(0, length), new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new ManagementError(422, "Supply a JSON object.");
            string? name = null, certificatePem = null;
            byte[]? pfx = null; string? pfxPassword = null;
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String) throw new ManagementError(422, "Certificate fields are text.");
                switch (property.Name)
                {
                    case "name" when id is null: name = property.Value.GetString(); break;
                    case "certificatePem": certificatePem = property.Value.GetString(); break;
                    case "privateKeyPem": keyPem = property.Value.GetString(); break;
                    case "pfx": pfx = property.Value.GetBytesFromBase64(); break;
                    case "password": pfxPassword = property.Value.GetString(); break;
                    default: throw new ManagementError(422, "Supply PEM text, or a PFX file and its password.");
                }
            }
            if (pfx is not null)
            {
                if (certificatePem is not null || keyPem is not null) throw new ManagementError(422, "Supply PEM text or a PFX file, not both.");
                try { (certificatePem, keyPem) = PfxToPem(pfx, pfxPassword ?? ""); }
                finally { CryptographicOperations.ZeroMemory(pfx); }
            }
            if (string.IsNullOrEmpty(certificatePem) || string.IsNullOrEmpty(keyPem)) throw new ManagementError(422, "Supply the certificate and its private key.");
            var (username, password) = Credentials(context, vault);
            var result = id is null
                ? await channel.SendAsync("certificates.import", username, password, new { name = name ?? "", certificatePem, privateKeyPem = keyPem }, context.RequestAborted)
                : await channel.SendAsync("certificates.replace", username, password, new { id, certificatePem, privateKeyPem = keyPem }, context.RequestAborted);
            return Results.Json(new { data = result });
        }
        catch (ManagementError error) when (error.Status == 422)
        {
            throw new ManagementError(422, "The certificate was refused. Check that the private key matches, is RSA 2048 or larger or EC P-256 or larger and not encrypted, the certificate is current and not a CA, the chain is in order, and the name is not already used.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wire);
            // Managed strings cannot be cleared reliably; this drops the reference promptly.
            keyPem = null;
        }
    }

    private static (string Certificate, string Key) PfxToPem(byte[] pfx, string password)
    {
        if (pfx.Length > CertificateInspection.MaximumPfxBytes) throw new ManagementError(413, "PFX files must not exceed 512 KiB.");
        X509Certificate2Collection certificates;
        try
        {
            certificates = X509CertificateLoader.LoadPkcs12Collection(pfx, password,
                X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable, CertificateInspection.Limits);
        }
        catch (CryptographicException) { throw new ManagementError(422, "The PFX file could not be opened. Check the password."); }
        try
        {
            var leaf = certificates.Cast<X509Certificate2>().SingleOrDefault(item => item.HasPrivateKey)
                ?? throw new ManagementError(422, "The PFX file must contain one certificate with its private key.");
            // Leaf first, then each issuer in turn; certificates outside that chain are left out.
            var chain = new StringBuilder(leaf.ExportCertificatePem()).Append('\n');
            var current = leaf;
            for (int depth = 0; depth < 8; ++depth)
            {
                var issuer = certificates.Cast<X509Certificate2>().FirstOrDefault(item => !ReferenceEquals(item, current)
                    && item.SubjectName.RawData.AsSpan().SequenceEqual(current.IssuerName.RawData) && !ReferenceEquals(item, leaf));
                if (issuer is null || current.SubjectName.RawData.AsSpan().SequenceEqual(current.IssuerName.RawData)) break;
                chain.Append(issuer.ExportCertificatePem()).Append('\n');
                current = issuer;
            }
            string key = (AsymmetricAlgorithm?)leaf.GetRSAPrivateKey() is RSA rsa ? rsa.ExportPkcs8PrivateKeyPem()
                : leaf.GetECDsaPrivateKey() is ECDsa ec ? ec.ExportPkcs8PrivateKeyPem()
                : throw new ManagementError(422, "Only RSA and EC keys are supported.");
            return (chain.ToString(), key);
        }
        finally { foreach (var certificate in certificates) certificate.Dispose(); }
    }

    // Connects to the listener as a client would, upgrading with STARTTLS where configured, and
    // reports the certificate it serves and whether that is the configured one.
    private static async Task<IResult> Probe(long id, HttpContext context, CredentialVault vault, IManagementChannel channel)
    {
        ServerAdministrator(context);
        var (username, password) = Credentials(context, vault);
        var inventory = await channel.SendAsync("certificates.list", username, password, null, context.RequestAborted);
        var listener = inventory.GetProperty("listeners").EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("id").GetString() == id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (listener.ValueKind != JsonValueKind.Object) throw new ManagementError(404, "No such listener.");
        int security = listener.GetProperty("securityCode").GetInt32();
        if (security == 0) throw new ManagementError(409, "This listener does not use TLS.");
        string address = listener.GetProperty("address").GetString() ?? "";
        address = address is "" or "0.0.0.0" ? "127.0.0.1" : address == "::" ? "::1" : address;
        int port = listener.GetProperty("port").GetInt32();
        string protocol = listener.GetProperty("protocol").GetString() ?? "";
        string? configured = null;
        if (listener.GetProperty("certificateId").GetString() is string certificateId)
            foreach (var item in inventory.GetProperty("items").EnumerateArray())
                if (item.GetProperty("id").GetString() == certificateId && item.TryGetProperty("fingerprintSha256", out var print)) configured = print.GetString();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(System.Net.IPAddress.Parse(address), port, timeout.Token);
            var stream = client.GetStream();
            if (security != 1) await StartTlsAsync(stream, protocol, timeout.Token);
            X509Certificate2? served = null;
            await using var tls = new SslStream(stream, false, (_, certificate, _, _) =>
            {
                if (certificate is not null) served = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
                return true; // Inspect whatever is served; trust is not the question here.
            });
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost" }, timeout.Token);
            using (served)
            {
                if (served is null) throw new ManagementError(502, "The listener served no certificate.");
                string fingerprint = Convert.ToHexString(SHA256.HashData(served.RawData));
                return Results.Json(new { data = new { listenerId = id.ToString(System.Globalization.CultureInfo.InvariantCulture), protocol = tls.SslProtocol.ToString(),
                    subject = served.GetNameInfo(X509NameType.SimpleName, false), notAfter = served.NotAfter.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    fingerprintSha256 = fingerprint, matchesConfigured = configured is not null && string.Equals(configured, fingerprint, StringComparison.OrdinalIgnoreCase) } });
            }
        }
        catch (Exception error) when (error is IOException or SocketException or AuthenticationException or OperationCanceledException)
        {
            throw new ManagementError(502, "The listener did not complete a TLS handshake. If it was just changed, restart the mail services first.");
        }
    }

    private static async Task StartTlsAsync(NetworkStream stream, string protocol, CancellationToken token)
    {
        var reader = new StreamReader(stream, Encoding.ASCII, false, 1, leaveOpen: true);
        var writer = new StreamWriter(stream, Encoding.ASCII, 1, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
        async Task<string> Line() => await reader.ReadLineAsync(token) ?? throw new IOException("Disconnected.");
        async Task<string> Reply() { string line; do { line = await Line(); } while (line.Length > 3 && line[3] == '-'); return line; }
        switch (protocol)
        {
            case "SMTP":
                await Reply();
                await writer.WriteLineAsync("EHLO certificate-probe.localhost"); await Reply();
                await writer.WriteLineAsync("STARTTLS");
                if (!(await Reply()).StartsWith("220", StringComparison.Ordinal)) throw new IOException("STARTTLS refused.");
                break;
            case "IMAP":
                await Line();
                await writer.WriteLineAsync("P1 STARTTLS");
                string line;
                do { line = await Line(); } while (!line.StartsWith("P1 ", StringComparison.Ordinal));
                if (!line.StartsWith("P1 OK", StringComparison.Ordinal)) throw new IOException("STARTTLS refused.");
                break;
            case "POP3":
                await Line();
                await writer.WriteLineAsync("STLS");
                if (!(await Line()).StartsWith("+OK", StringComparison.Ordinal)) throw new IOException("STLS refused.");
                break;
            default: throw new IOException("Unknown protocol.");
        }
    }
}
