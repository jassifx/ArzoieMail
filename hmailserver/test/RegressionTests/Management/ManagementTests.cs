using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.IO.Pipes;
using System.Security;
using System.Text.Json;
using ArzoieMail.Management;
using Microsoft.Extensions.DependencyInjection;

int assertions = 0;
void Check(bool condition, string message)
{
    ++assertions;
    if (!condition) throw new Exception(message);
}

if (args.Contains("--browser-host"))
{
    await using var preview = ManagementHost.Build(["--environment", "Development", "--urls", "http://127.0.0.1:18086"]);
    await preview.RunAsync();
    return;
}

if (args.Contains("--squatting"))
{
    using var impostor = new NamedPipeServerStream(ManagementChannel.PipeName, PipeDirection.InOut, 1,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var connected = impostor.WaitForConnectionAsync(timeout.Token);
    var request = new ManagementChannel().SendAsync("authenticate", "must-not-be-sent", "must-not-be-sent", null, timeout.Token);
    await connected;
    try { await request; throw new Exception("Impostor pipe server accepted."); }
    catch (SecurityException) { Check(true, "Pipe server must be the SCM service process."); }
    byte[] probe = new byte[1];
    Check(await impostor.ReadAsync(probe, timeout.Token) == 0, "No credential bytes may be sent to an impostor pipe server.");
    Console.WriteLine($"Passed {assertions} pipe-squatting security assertions.");
    return;
}

if (args.Contains("--live"))
{
    string name = Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_USERNAME") ?? throw new Exception("Set test username.");
    string password = Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_PASSWORD") ?? throw new Exception("Set test password.");
    IManagementChannel native = new ManagementChannel();
    var identity = await native.SendAsync("authenticate", name, password, null, CancellationToken.None);
    Check(identity.GetProperty("level").GetInt32() == 2, "Live test requires a server administrator.");
    // Exercise the native parser directly, rather than relying only on HTTP validation.
    using (var raw = new NamedPipeClientStream(".", ManagementChannel.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await raw.ConnectAsync(timeout.Token);
        byte[] length = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, 1);
        // Size one is rejected before allocation; connection must be closed without a response.
        await raw.WriteAsync(length, timeout.Token);
        byte[] probe = new byte[1];
        bool refused;
        try { refused = await raw.ReadAsync(probe, timeout.Token) == 0; }
        catch (IOException) { refused = true; }
        Check(refused, "Native framing refuses invalid lengths.");
    }
    using (var raw = new NamedPipeClientStream(".", ManagementChannel.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await raw.ConnectAsync(timeout.Token);
        byte[] length = new byte[4];
        byte[] malformed = System.Text.Encoding.UTF8.GetBytes("{broken");
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, malformed.Length);
        await raw.WriteAsync(length, timeout.Token); await raw.WriteAsync(malformed, timeout.Token);
        await raw.ReadExactlyAsync(length, timeout.Token);
        byte[] response = new byte[System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(length)];
        await raw.ReadExactlyAsync(response, timeout.Token);
        await raw.WriteAsync(new byte[] { 6 }, timeout.Token);
        Check(JsonSerializer.Deserialize<JsonElement>(response).GetProperty("status").GetInt32() == 422, "Native parser refuses malformed JSON without crashing.");
    }
    if (Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_DOMAIN_USERNAME") is { Length: > 0 } domainUser)
    {
        var limited = await native.SendAsync("authenticate", domainUser, password, null, CancellationToken.None);
        long ownDomain = limited.GetProperty("domainId").GetInt64();
        Check(limited.GetProperty("level").GetInt32() == 1, "Live domain administrator role.");
        var own = await native.SendAsync("domains", domainUser, password, null, CancellationToken.None);
        Check(own.GetProperty("items").GetArrayLength() == 1 && own.GetProperty("items")[0].GetProperty("id").GetInt64() == ownDomain, "Native domain isolation.");
        await native.SendAsync("domain", domainUser, password, new { id = ownDomain }, CancellationToken.None);
        foreach (var denied in new[] { ("status", (object)new { }), ("antispam.update", (object)new { UseSPF = false }), ("domain", (object)new { id = long.MaxValue }) })
        {
            try { await native.SendAsync(denied.Item1, domainUser, password, denied.Item2, CancellationToken.None); throw new Exception("Native permission boundary bypassed."); }
            catch (ManagementError e) { Check(e.Status == 403, "Native domain administrator restrictions."); }
        }
        string ordinaryUser = Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_USER_USERNAME")!;
        try { await native.SendAsync("domain", ordinaryUser, password, new { id = ownDomain }, CancellationToken.None); throw new Exception("Account user may not inspect domain DNS."); }
        catch (ManagementError e) { Check(e.Status == 403, "Native ordinary-user restrictions."); }
    }
    var settings = await native.SendAsync("antispam.read", name, password, null, CancellationToken.None);
    Check(settings.TryGetProperty("SpamAssassinHost", out _), "Read native configuration.");
    var originalSpf = settings.GetProperty("UseSPF").GetBoolean();
    long? id = null;
    try
    {
        await native.SendAsync("antispam.update", name, password, new { UseSPF = !originalSpf }, CancellationToken.None);
        var updated = await native.SendAsync("antispam.read", name, password, null, CancellationToken.None);
        Check(updated.GetProperty("UseSPF").GetBoolean() != originalSpf, "Native write did not persist.");
        try
        {
            await native.SendAsync("antispam.update", name, password, new { UseSPF = originalSpf, Unexpected = true }, CancellationToken.None);
            throw new Exception("Unknown fields must be refused.");
        }
        catch (ManagementError e) { Check(e.Status == 422, "Native validation status."); }
        updated = await native.SendAsync("antispam.read", name, password, null, CancellationToken.None);
        Check(updated.GetProperty("UseSPF").GetBoolean() != originalSpf, "Invalid request partially applied.");
        var created = await native.SendAsync("dnsbl.create", name, password,
            new { Active = false, DNSHost = "test-" + Guid.NewGuid().ToString("N") + ".invalid", ExpectedResult = "127.0.0.*", RejectMessage = "API regression fixture", Score = 3 }, CancellationToken.None);
        id = created.GetProperty("id").GetInt64();
        Check(id > 0 && !created.GetProperty("Active").GetBoolean(), "Create disabled native DNSBL.");
        var changed = await native.SendAsync("dnsbl.update", name, password, new { id, fields = new { Score = 7 } }, CancellationToken.None);
        Check(changed.GetProperty("Score").GetInt32() == 7, "Update native DNSBL.");
        var listed = await native.SendAsync("dnsbl.list", name, password, null, CancellationToken.None);
        Check(listed.GetProperty("items").EnumerateArray().Any(v => v.GetProperty("id").GetInt64() == id), "List native DNSBL.");
        var status = await native.SendAsync("status", name, password, null, CancellationToken.None);
        Check(status.GetProperty("state").GetInt32() == 3, "Service should remain running.");
        try { await native.SendAsync("authenticate", name, Guid.NewGuid().ToString(), null, CancellationToken.None); throw new Exception("Invalid password accepted."); }
        catch (ManagementError e) { Check(e.Status == 401, "Invalid password status."); }
    }
    finally
    {
        await native.SendAsync("antispam.update", name, password, new { UseSPF = originalSpf }, CancellationToken.None);
        if (id.HasValue) await native.SendAsync("dnsbl.delete", name, password, new { id }, CancellationToken.None);
    }
    Console.WriteLine($"Passed {assertions} live native management assertions (no COM).");
    return;
}

await DnsTests.Run();
using (var vault = new CredentialVault())
{
    string id = vault.Add(new("admin", "test-password"));
    Check(vault.Get(id).Password == "test-password", "Encrypted vault round trip.");
    vault.Remove(id);
    try { vault.Get(id); throw new Exception("Removed session accepted."); }
    catch (ManagementError e) { Check(e.Status == 401, "Removed session denied."); }
}

var fake = new FakeChannel();
await using var app = ManagementHost.Build(["--environment", "Development", "--urls", "http://127.0.0.1:0"],
    services => services.AddSingleton<IManagementChannel>(fake));
await app.StartAsync();
try
{
    Uri address = new(app.Urls.Single());
    using var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };
    using var client = new HttpClient(handler) { BaseAddress = address };
    async Task<JsonElement> Send(string path, HttpMethod method, object? body = null, string? csrf = null, int status = 200, string? origin = null)
    {
        using var request = new HttpRequestMessage(method, "/api/v1" + path);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (csrf is not null) request.Headers.Add("X-CSRF-Token", csrf);
        if (origin is not null) request.Headers.Add("Origin", origin);
        using var response = await client.SendAsync(request);
        Check((int)response.StatusCode == status, $"{method} {path}: {(int)response.StatusCode}, expected {status}.");
        Check(response.Headers.CacheControl?.NoStore == true, "No-store on API responses.");
        if (response.Content.Headers.ContentLength == 0) return default;
        var raw = await response.Content.ReadAsStringAsync();
        if (raw.Length == 0) return default;
        var payload = JsonSerializer.Deserialize<JsonElement>(raw);
        return payload.TryGetProperty("data", out var data) ? data.Clone() : payload.Clone();
    }
    var anonymous = await Send("/session", HttpMethod.Get);
    Check(anonymous.GetProperty("user").ValueKind == JsonValueKind.Null, "Anonymous bootstrap.");
    string token = anonymous.GetProperty("csrf").GetString()!;
    foreach (var invalid in new[] { ("text/plain", "{}", 415), ("application/json", "{broken", 400), ("application/json", "{\"username\":\"" + new string('x', 20000) + "\"}", 413) })
    {
        using var bad = new HttpRequestMessage(HttpMethod.Post, "/api/v1/session");
        bad.Headers.Add("X-CSRF-Token", token);
        bad.Content = new StringContent(invalid.Item2, System.Text.Encoding.UTF8, invalid.Item1);
        using var response = await client.SendAsync(bad);
        Check((int)response.StatusCode == invalid.Item3, "Reject invalid content type, malformed JSON and oversized requests.");
    }
    await Send("/status", HttpMethod.Get, status: 401);
    await Send("/session", HttpMethod.Post, new { username = "admin", password = "test-password" }, "wrong", 403);
    Check(fake.Calls == 0, "CSRF must fail before contacting the native service.");
    await Send("/session", HttpMethod.Post, new { username = "admin", password = "test-password" }, token, 403, "https://evil.example");
    var login = await Send("/session", HttpMethod.Post, new { username = "admin", password = "test-password" }, token);
    Check(login.GetProperty("user").GetProperty("level").GetInt32() == 2, "Native role mapped to HTTP identity.");
    token = login.GetProperty("csrf").GetString()!;
    Check(handler.CookieContainer.GetCookies(address)["ArzoieMail.Session"]!.HttpOnly, "HttpOnly session cookie.");
    Check(!handler.CookieContainer.GetCookies(address)["ArzoieMail.Session"]!.Value.Contains("test-password"), "No password in cookies.");
    await Send("/status", HttpMethod.Get);
    fake.Unavailable = true;
    await Send("/status", HttpMethod.Get, status: 503);
    fake.Unavailable = false;
    await Send("/antispam", HttpMethod.Patch, new { UseSPF = true }, token);
    Check(fake.LastOperation == "antispam.update", "Route calls explicit native command.");
    fake.Level = 1;
    var current = await Send("/session", HttpMethod.Get);
    Check(current.GetProperty("user").GetProperty("level").GetInt32() == 1, "Refresh roles from the native service.");
    await Send("/status", HttpMethod.Get, status: 403);
    await Send("/domains", HttpMethod.Get);
    fake.Valid = false;
    await Send("/status", HttpMethod.Get, status: 401);
    current = await Send("/session", HttpMethod.Get);
    Check(current.GetProperty("user").ValueKind == JsonValueKind.Null, "Revoked identity cleared.");
    fake.Valid = true; fake.Level = 2;
    token = current.GetProperty("csrf").GetString()!;
    login = await Send("/session", HttpMethod.Post, new { username = "admin", password = "test-password" }, token);
    token = login.GetProperty("csrf").GetString()!;
    await Send("/session", HttpMethod.Delete, new { }, token);
    await Send("/status", HttpMethod.Get, status: 401);
    using var page = await client.GetAsync("/");
    // Test host uses API project's content root supplied by build/test-management.ps1.
    Check(page.IsSuccessStatusCode, "Modern panel is served.");
    Check(!page.Headers.GetValues("Content-Security-Policy").Single().Contains("unsafe-inline"), "No inline scripts in CSP.");
    var finalSession = await Send("/session", HttpMethod.Get);
    token = finalSession.GetProperty("csrf").GetString()!;
    bool limited = false;
    for (int attempt = 0; attempt < 12; ++attempt)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/session");
        request.Headers.Add("X-CSRF-Token", token);
        request.Content = JsonContent.Create(new { username = "admin", password = "invalid" });
        using var response = await client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            Check(response.Headers.RetryAfter is not null, "Login throttling supplies retry guidance.");
            limited = true;
            break;
        }
        Check(response.StatusCode == HttpStatusCode.Unauthorized, "Bad login receives generic authentication failure.");
    }
    Check(limited, "Login rate limit is enforced.");
}
finally { await app.StopAsync(); }
Console.WriteLine($"Passed {assertions} management security assertions.");

await using var production = ManagementHost.Build(["--environment", "Production", "--urls", "http://127.0.0.1:0", "--AllowedHosts", "admin.example"]);
await production.StartAsync();
try
{
    using var client = new HttpClient();
    using var request = new HttpRequestMessage(HttpMethod.Get, production.Urls.Single() + "/api/v1/session");
    request.Headers.Host = "admin.example";
    using var response = await client.SendAsync(request);
    Check(response.StatusCode == HttpStatusCode.BadRequest, "Production must refuse unencrypted HTTP.");
    Check(!response.Headers.Contains("Set-Cookie"), "Do not issue session cookies over unencrypted production connections.");
}
finally { await production.StopAsync(); }
Console.WriteLine("Passed production HTTPS boundary checks.");

sealed class FakeChannel : IManagementChannel
{
    public int Calls { get; private set; }
    public string LastOperation { get; private set; } = "";
    public int Level { get; set; } = 2;
    public bool Valid { get; set; } = true;
    public bool Unavailable { get; set; }
    public Task<JsonElement> SendAsync(string operation, string username, string password, object? arguments, CancellationToken cancellationToken)
    {
        ++Calls; LastOperation = operation;
        if (Unavailable) throw new IOException("Simulated unavailable native service.");
        if (!Valid || password != "test-password") throw new ManagementError(401, "Incorrect username or password.");
        if (operation == "authenticate") return Task.FromResult(JsonSerializer.SerializeToElement(new { address = username, level = Level, domainId = 5, accountId = 7 }));
        if (operation != "domains" && Level != 2) throw new ManagementError(403, "Access denied.");
        return Task.FromResult(JsonSerializer.SerializeToElement(new { ok = true }));
    }
}
