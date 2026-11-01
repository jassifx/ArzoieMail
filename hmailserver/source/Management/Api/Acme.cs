using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArzoieMail.Management;

// ACME (RFC 8555) issuance and renewal for the mail listeners' certificates.
//
// The private keys never come here. The mail service generates each certificate's key and
// returns only a certificate signing request, and it keeps the ACME account key and signs this
// client's requests (JWS ES256). This process talks HTTP to the ACME directory and answers the
// HTTP-01 challenges, on a separate port-80 listener that serves nothing else.
public sealed class AcmeOptions
{
    // Port for HTTP-01 challenge answers; 0 turns the challenge listener off.
    public int HttpPort { get; set; }
    // How often the renewal task looks for certificates due; 0 turns it off.
    public int RenewalCheckMinutes { get; set; } = 360;
    // Testing only: a PEM root the ACME directory's HTTPS certificate must chain to.
    public string? TrustedRootFile { get; set; }
}

// Key authorizations waiting for the ACME server to fetch, by token, and whether the challenge
// listener is answering.
public sealed class AcmeChallenges
{
    private readonly ConcurrentDictionary<string, string> answers = new(StringComparer.Ordinal);
    public int Port { get; private set; }
    public bool Listening { get; private set; }
    public void SetListener(int port, bool listening) { Port = port; Listening = listening; }

    // Why HTTP-01 cannot be answered, or null when it can.
    public string? Unavailable => Listening ? null : Port <= 0
        ? "ACME checks each host name over HTTP on port 80, and the challenge listener is off. Set Acme:HttpPort in appsettings.Production.json and restart the ArzoieMailAdmin service."
        : $"The ACME challenge listener could not start on port {Port} because another program is using it. Free the port and restart the ArzoieMailAdmin service.";
    public void Add(string token, string keyAuthorization) => answers[token] = keyAuthorization;
    public void Remove(string token) => answers.TryRemove(token, out _);
    public string? Find(string token) => answers.TryGetValue(token, out var answer) ? answer : null;
}

// The native calls an issuance needs: an administrator's (interactive) or the renewal scope's.
public sealed record AcmeNative(Func<string, object?, CancellationToken, Task<JsonElement>> Call, bool Automation)
{
    public static AcmeNative Administrator(IManagementChannel channel, string username, string password) =>
        new((operation, arguments, token) => channel.SendAsync(operation, username, password, arguments, token), false);
    public static AcmeNative Renewal(IManagementChannel channel) =>
        new((operation, arguments, token) => channel.SendAutomationAsync(operation, arguments, token), true);
}

public sealed class AcmeException(string message) : Exception(message);

public sealed class AcmeIssuer(IHttpClientFactory clients, AcmeChallenges challenges, ILogger<AcmeIssuer> log)
{
    public const string HttpClientName = "acme";
    private static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(90);

    // Issues a certificate and installs it through the mail service. prepareArguments go to the
    // native prepare operation (host names and name, or the certificate being renewed).
    public async Task<JsonElement> IssueAsync(AcmeNative native, object prepareArguments, CancellationToken token)
    {
        if (challenges.Unavailable is string unavailable) throw new AcmeException(unavailable);
        var account = await native.Call("acme.account.read", null, token);
        string directoryUrl = account.GetProperty("directoryUrl").GetString() ?? "";
        if (directoryUrl.Length == 0) throw new AcmeException("Choose an ACME certificate authority first.");
        if (!account.GetProperty("termsAgreed").GetBoolean()) throw new AcmeException("Agree to the certificate authority's terms first.");
        var session = new Session(clients.CreateClient(HttpClientName), native, account.GetProperty("jwk"));
        await session.LoadDirectoryAsync(directoryUrl, token);

        string accountUrl = account.GetProperty("accountUrl").GetString() ?? "";
        if (accountUrl.Length == 0)
        {
            if (native.Automation) throw new AcmeException("The ACME account is not registered; issue the certificate again from the panel.");
            string contact = account.GetProperty("contact").GetString() ?? "";
            var payload = new JsonObject { ["termsOfServiceAgreed"] = true };
            if (contact.Length != 0) payload["contact"] = new JsonArray($"mailto:{contact}");
            using var created = await session.PostAsync(session.Endpoint("newAccount"), payload, token, useJwk: true);
            accountUrl = created.Headers.Location?.ToString() ?? throw new AcmeException("The certificate authority did not return an account URL.");
            await native.Call("acme.account.registered", new { directoryUrl, accountUrl }, token);
        }
        session.AccountUrl = accountUrl;

        JsonElement prepared;
        try { prepared = await native.Call(native.Automation ? "acme.renewals.prepare" : "certificates.acme.prepare", prepareArguments, token); }
        catch (ManagementError error) when (error.Status == 409)
        {
            throw new AcmeException("This certificate is already being renewed. Try again in a few minutes.");
        }
        string pendingId = prepared.GetProperty("pendingId").GetString()!;
        bool installed = false;
        var tokens = new List<string>();
        try
        {
            var hostnames = prepared.GetProperty("hostnames").EnumerateArray().Select(item => item.GetString()!).ToArray();
            var order = new JsonObject { ["identifiers"] = new JsonArray(hostnames.Select(host => (JsonNode)new JsonObject { ["type"] = "dns", ["value"] = host }).ToArray()) };
            using var orderResponse = await session.PostAsync(session.Endpoint("newOrder"), order, token);
            string orderUrl = orderResponse.Headers.Location?.ToString() ?? throw new AcmeException("The certificate authority did not return an order URL.");
            var orderBody = await ReadJsonAsync(orderResponse, token);

            foreach (var authorization in orderBody.GetProperty("authorizations").EnumerateArray())
                await AuthorizeAsync(session, authorization.GetString()!, tokens, token);
            using (await session.PostAsync(orderBody.GetProperty("finalize").GetString()!, new JsonObject { ["csr"] = prepared.GetProperty("csr").GetString() }, token)) { }
            var finished = await PollAsync(session, orderUrl, "valid", token);
            string certificateUrl = finished.GetProperty("certificate").GetString() ?? throw new AcmeException("The order has no certificate.");
            using var download = await session.PostAsync(certificateUrl, null, token, accept: "application/pem-certificate-chain");
            string chain = await download.Content.ReadAsStringAsync(token);
            var certificate = await native.Call(native.Automation ? "acme.renewals.complete" : "certificates.acme.complete", new { pendingId, certificatePem = chain }, token);
            installed = true;
            log.LogInformation("ACME certificate issued for {Count} host name(s).", hostnames.Length);
            return certificate;
        }
        finally
        {
            foreach (string item in tokens) challenges.Remove(item);
            if (!installed) await DiscardAsync(native, pendingId);
        }
    }

    // Lets the certificate be ordered again at once after a failure. A completion the service
    // refused has already dropped the order, so a missing one is expected.
    private static async Task DiscardAsync(AcmeNative native, string pendingId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await native.Call(native.Automation ? "acme.renewals.discard" : "certificates.acme.discard", new { pendingId }, timeout.Token);
        }
        catch (Exception) { }
    }

    private async Task AuthorizeAsync(Session session, string url, List<string> tokens, CancellationToken token)
    {
        using var response = await session.PostAsync(url, null, token);
        var authorization = await ReadJsonAsync(response, token);
        if (authorization.GetProperty("status").GetString() == "valid") return;
        var challenge = authorization.GetProperty("challenges").EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("type").GetString() == "http-01");
        if (challenge.ValueKind != JsonValueKind.Object) throw new AcmeException("The certificate authority offered no HTTP-01 challenge.");
        string challengeToken = challenge.GetProperty("token").GetString()!;
        if (challengeToken.Length is 0 or > 256 || !challengeToken.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new AcmeException("The certificate authority sent an invalid challenge token.");
        challenges.Add(challengeToken, challengeToken + "." + session.Thumbprint);
        tokens.Add(challengeToken);
        using (await session.PostAsync(challenge.GetProperty("url").GetString()!, new JsonObject(), token)) { }
        await PollAsync(session, url, "valid", token);
    }

    // Polls an order or authorization until it reaches the wanted status, or fails with the
    // certificate authority's own explanation.
    private static async Task<JsonElement> PollAsync(Session session, string url, string wanted, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + ValidationTimeout;
        for (;;)
        {
            using var response = await session.PostAsync(url, null, token);
            var body = await ReadJsonAsync(response, token);
            string status = body.GetProperty("status").GetString() ?? "";
            if (status == wanted) return body;
            if (status is "invalid" or "revoked" or "deactivated" or "expired")
                throw new AcmeException("The certificate authority refused the request: " + Problem(body));
            if (DateTime.UtcNow > deadline) throw new AcmeException("The certificate authority did not finish in time.");
            var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
            await Task.Delay(delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : delay, token);
        }
    }

    private static string Problem(JsonElement body)
    {
        if (body.TryGetProperty("error", out var orderError) && orderError.TryGetProperty("detail", out var orderDetail)) return Bounded(orderDetail.GetString());
        if (body.TryGetProperty("challenges", out var challenges))
            foreach (var challenge in challenges.EnumerateArray())
                if (challenge.TryGetProperty("error", out var error) && error.TryGetProperty("detail", out var detail)) return Bounded(detail.GetString());
        return "no reason given.";
    }

    private static string Bounded(string? text) => (text ?? "").Length > 300 ? text![..300] : text ?? "";

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken token)
    {
        string text = await response.Content.ReadAsStringAsync(token);
        if (text.Length > 1_000_000) throw new AcmeException("The certificate authority's response is too large.");
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });
        return document.RootElement.Clone();
    }

    private sealed class Session(HttpClient http, AcmeNative native, JsonElement jwk)
    {
        private JsonElement directory;
        private string? nonce;
        public string? AccountUrl { get; set; }
        public string Thumbprint { get; } = ComputeThumbprint(jwk);

        // RFC 7638: SHA-256 over the required members in lexicographic order, no whitespace.
        private static string ComputeThumbprint(JsonElement key)
        {
            string canonical = $"{{\"crv\":\"{key.GetProperty("crv").GetString()}\",\"kty\":\"{key.GetProperty("kty").GetString()}\",\"x\":\"{key.GetProperty("x").GetString()}\",\"y\":\"{key.GetProperty("y").GetString()}\"}}";
            return Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        }

        public async Task LoadDirectoryAsync(string url, CancellationToken token)
        {
            using var response = await http.GetAsync(url, token);
            if (!response.IsSuccessStatusCode) throw new AcmeException($"The ACME directory answered {(int)response.StatusCode}.");
            directory = await ReadJsonAsync(response, token);
        }

        public string Endpoint(string name) =>
            directory.TryGetProperty(name, out var value) && value.GetString() is string url ? url : throw new AcmeException($"The ACME directory has no {name}.");

        private async Task<string> NonceAsync(CancellationToken token)
        {
            if (nonce is { } current) { nonce = null; return current; }
            using var request = new HttpRequestMessage(HttpMethod.Head, Endpoint("newNonce"));
            using var response = await http.SendAsync(request, token);
            return response.Headers.TryGetValues("Replay-Nonce", out var values) ? values.First() : throw new AcmeException("The certificate authority sent no nonce.");
        }

        // A JWS-signed POST; a null payload is a POST-as-GET. Retries a rejected nonce.
        public async Task<HttpResponseMessage> PostAsync(string url, JsonNode? payload, CancellationToken token, bool useJwk = false, string? accept = null)
        {
            for (int attempt = 0; ; ++attempt)
            {
                var header = new JsonObject { ["alg"] = "ES256", ["nonce"] = await NonceAsync(token), ["url"] = url };
                if (useJwk) header["jwk"] = JsonNode.Parse(jwk.GetRawText());
                else header["kid"] = AccountUrl ?? throw new AcmeException("The ACME account is not registered.");
                string protectedHeader = Base64Url(Encoding.UTF8.GetBytes(header.ToJsonString()));
                string body = payload is null ? "" : Base64Url(Encoding.UTF8.GetBytes(payload.ToJsonString()));
                var signed = await native.Call("acme.sign", new { input = protectedHeader + "." + body }, token);
                var jws = new JsonObject { ["protected"] = protectedHeader, ["payload"] = body, ["signature"] = signed.GetProperty("signature").GetString() };
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(jws.ToJsonString(), Encoding.UTF8) };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/jose+json");
                if (accept is not null) request.Headers.Accept.ParseAdd(accept);
                var response = await http.SendAsync(request, token);
                if (response.Headers.TryGetValues("Replay-Nonce", out var values)) nonce = values.First();
                if (response.IsSuccessStatusCode) return response;
                string problem = await response.Content.ReadAsStringAsync(token);
                int status = (int)response.StatusCode;
                response.Dispose();
                if (attempt < 3 && problem.Contains("urn:ietf:params:acme:error:badNonce", StringComparison.Ordinal)) continue;
                string detail = "";
                try { using var document = JsonDocument.Parse(problem); detail = document.RootElement.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : ""; }
                catch (JsonException) { }
                throw new AcmeException($"The certificate authority answered {status}: {Bounded(detail)}");
            }
        }
    }

    internal static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // RFC 8555 6.1: every request names the client.
    public static void ConfigureClient(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        // The product version; the assembly version is kept fixed for identity.
        string version = (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(AcmeIssuer).Assembly)
            ?.InformationalVersion ?? "6").Split('+')[0];
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ArzoieMail-ACME/" + version);
    }

    // The HTTP client for the ACME directory. A configured test root replaces the system roots.
    public static HttpMessageHandler CreateHandler(AcmeOptions options)
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        if (string.IsNullOrEmpty(options.TrustedRootFile)) return handler;
        var root = X509Certificate2.CreateFromPem(File.ReadAllText(options.TrustedRootFile));
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
        {
            if (certificate is null || (errors & ~System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors) != 0) return false;
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(root);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return chain.Build(new X509Certificate2(certificate));
        };
        return handler;
    }
}

// Interactive issuance runs in the background and is polled, because ACME validation can take
// longer than a request should. One job runs at a time.
public sealed class AcmeJobs(AcmeIssuer issuer, AcmeChallenges challenges, ILogger<AcmeJobs> log)
{
    public sealed class Job
    {
        public required string Id { get; init; }
        public string Status { get; set; } = "running";
        public string Message { get; set; } = "";
        public JsonElement? Certificate { get; set; }
    }
    private readonly ConcurrentDictionary<string, Job> jobs = new(StringComparer.Ordinal);
    private int running;

    public Job Start(AcmeNative native, object prepareArguments)
    {
        if (challenges.Unavailable is string unavailable) throw new ManagementError(409, unavailable);
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0) throw new ManagementError(409, "A certificate is already being issued. Wait for it to finish.");
        foreach (var old in jobs.Values.Where(job => job.Status != "running").ToArray()) jobs.TryRemove(old.Id, out _);
        var job = new Job { Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant() };
        jobs[job.Id] = job;
        _ = Task.Run(async () =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                job.Certificate = await issuer.IssueAsync(native, prepareArguments, timeout.Token);
                job.Status = "succeeded";
            }
            catch (Exception error)
            {
                job.Status = "failed";
                job.Message = error is AcmeException or ManagementError ? error.Message : "Issuing the certificate failed.";
                if (error is not (AcmeException or ManagementError)) log.LogWarning("ACME issuance failed ({ExceptionType}).", error.GetType().Name);
            }
            finally { Interlocked.Exchange(ref running, 0); }
        });
        return job;
    }

    public Job? Find(string id) => jobs.TryGetValue(id, out var job) ? job : null;
}

// Renews certificates the mail service reports as due, without an administrator signed in.
public sealed class AcmeRenewalService(IManagementChannel channel, AcmeIssuer issuer, Microsoft.Extensions.Options.IOptions<AcmeOptions> options,
    ILogger<AcmeRenewalService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        int minutes = options.Value.RenewalCheckMinutes;
        if (minutes <= 0) return;
        // Let the mail service finish starting after a reboot.
        try { await Task.Delay(TimeSpan.FromMinutes(2), stopping); } catch (OperationCanceledException) { return; }
        while (!stopping.IsCancellationRequested)
        {
            try { await RunOnceAsync(stopping); }
            catch (Exception error) when (!stopping.IsCancellationRequested) { log.LogWarning("Certificate renewal check failed ({ExceptionType}).", error.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromMinutes(minutes), stopping); } catch (OperationCanceledException) { return; }
        }
    }

    // Renews every certificate that is due; returns how many succeeded and failed.
    public async Task<(int Renewed, int Failed)> RunOnceAsync(CancellationToken token)
    {
        var native = AcmeNative.Renewal(channel);
        var due = await channel.SendAutomationAsync("acme.renewals.due", null, token);
        int renewed = 0, failed = 0;
        foreach (var item in due.GetProperty("items").EnumerateArray())
        {
            string certificateId = item.GetProperty("certificateId").GetString()!;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromMinutes(5));
                await issuer.IssueAsync(native, new { certificateId }, timeout.Token);
                ++renewed;
            }
            catch (Exception error) when (!token.IsCancellationRequested)
            {
                ++failed;
                string message = error is AcmeException or ManagementError ? error.Message : "Renewal failed.";
                log.LogWarning("Certificate {CertificateId} could not be renewed: {Message}", certificateId, message);
                try { await channel.SendAutomationAsync("acme.renewals.report", new { certificateId, error = message }, token); }
                catch (Exception) { }
            }
        }
        return (renewed, failed);
    }
}

// The port-80 listener for HTTP-01. It answers /.well-known/acme-challenge/{token} for pending
// challenges and nothing else, so the administration panel is never served over plain HTTP.
// When the port is taken the panel still starts; issuance then says why it cannot run.
public sealed class AcmeChallengeServer(AcmeChallenges challenges, Microsoft.Extensions.Options.IOptions<AcmeOptions> options,
    ILogger<AcmeChallengeServer> log) : IHostedService, IAsyncDisposable
{
    private WebApplication? server;

    public async Task StartAsync(CancellationToken token)
    {
        int port = options.Value.HttpPort;
        challenges.SetListener(port, false);
        if (port <= 0) return;
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ListenAnyIP(port);
            kestrel.Limits.MaxRequestBodySize = 0;
            kestrel.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
        });
        server = builder.Build();
        server.MapGet("/.well-known/acme-challenge/{token}", (string token) =>
            challenges.Find(token) is string answer ? Results.Text(answer, "application/octet-stream") : Results.NotFound());
        server.MapFallback(() => Results.NotFound());
        try { await server.StartAsync(token); }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            await server.DisposeAsync();
            server = null;
            log.LogWarning("The ACME HTTP-01 challenge listener could not start on port {Port} ({ExceptionType}); certificates cannot be issued until the port is free.",
                port, error.GetType().Name);
            return;
        }
        challenges.SetListener(port, true);
        log.LogInformation("ACME HTTP-01 challenge listener started on port {Port}.", port);
    }

    public async Task StopAsync(CancellationToken token)
    {
        if (server is not null) await server.StopAsync(token);
    }

    public async ValueTask DisposeAsync()
    {
        if (server is not null) await server.DisposeAsync();
    }
}
