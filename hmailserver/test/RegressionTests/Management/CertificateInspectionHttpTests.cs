using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArzoieMail.Management;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

static class CertificateInspectionHttpTests
{
    public static async Task Run(HttpClient client, string token, Action<bool, string> check)
    {
        const string path = "/api/v1/transport/certificates/inspect", password = "PFX-http-fixture-\u03c0!9";
        byte[] pfx = CertificateInspectionTests.CreatePfxFixture(password), large = CertificateInspectionTests.CreatePfxFixture(password, large: true);
        var wires = new List<byte[]>();
        async Task<JsonElement> Send(object body, int status = 200, string? csrf = null, string? origin = null, bool raw = false)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Add("X-CSRF-Token", csrf ?? token);
            if (origin is not null) request.Headers.Add("Origin", origin);
            if (raw)
            {
                byte[] bytes = Encoding.UTF8.GetBytes((string)body); wires.Add(bytes);
                request.Content = new ByteArrayContent(bytes);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                request.Headers.ExpectContinue = true;
            }
            else request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request);
            check((int)response.StatusCode == status, $"Certificate inspection HTTP returned {(int)response.StatusCode}, expected {status}.");
            check(response.Headers.CacheControl?.NoStore == true, "Certificate inspection replies are not cached.");
            string text = await response.Content.ReadAsStringAsync();
            check(!text.Contains(password, StringComparison.Ordinal) && !text.Contains(Convert.ToBase64String(pfx), StringComparison.Ordinal), "HTTP replies omit uploaded PFX/password data.");
            var json = JsonSerializer.Deserialize<JsonElement>(text);
            return json.TryGetProperty("data", out var data) ? data.Clone() : json.Clone();
        }
        try
        {
            var report = await Send(new { pfx, password, hosts = new[] { "mail.example.test", "wrong.example.test" } });
            check(report.GetProperty("sha256").GetString()!.Length == 64 && report.GetProperty("hosts")[0].GetProperty("matches").GetBoolean(), "HTTP inspection returns a public fingerprint and actual SAN match.");
            check(!report.GetProperty("hosts")[1].GetProperty("matches").GetBoolean() && report.GetProperty("revocationStatus").GetString() == "NotChecked", "Mismatch and skipped revocation remain distinct visible results.");
            check(large.Length > 16384, "The large PFX fixture exceeds ordinary management request limits.");
            await Send(new { pfx = large, password, hosts = new[] { "mail.example.test" } });
            await Send(new { pfx, password = "incorrect", hosts = new[] { "mail.example.test" } }, 422);
            await Send(new { pfx, hosts = new[] { "mail.example.test" } }, 422);
            await Send(new { pfx, password, hosts = new[] { "mail.example.test" }, path = "C:\\Windows\\win.ini" }, 422);
            await Send(new { pfx, password, hosts = new[] { "mail.example.test" }, now = "2100-01-01T00:00:00Z" }, 422);
            await Send(new { pfx, password, hosts = Array.Empty<string>() }, 422);
            string base64 = Convert.ToBase64String(pfx);
            await Send("{\"pfx\":\"" + base64 + "\",\"password\":\"first\",\"pa\\u0073sword\":\"second\",\"hosts\":[\"mail.example.test\"]}", 422, raw: true);
            string valid = JsonSerializer.Serialize(new { pfx, password, hosts = new[] { "mail.example.test" } });
            await Send(valid + " {}", 400, raw: true);
            await Send("{\"pfx\":\"not base64!\",\"password\":\"\",\"hosts\":[\"mail.example.test\"]}", 422, raw: true);
            await Send("{\"pfx\":\"\",\"password\":\"\\uD800\",\"hosts\":[\"mail.example.test\"]}", 422, raw: true);
            await Send(new { pfx = new byte[524289], password, hosts = new[] { "mail.example.test" } }, 422);
            await Send(new { pfx, password, hosts = new[] { "mail.example.test" } }, 403, csrf: "incorrect");
            await Send(new { pfx, password, hosts = new[] { "mail.example.test" } }, 403, origin: "https://evil.example");
            await Send("{\"pfx\":\"" + new string('A', 1100000) + "\",\"password\":\"\",\"hosts\":[\"mail.example.test\"]}", 413, raw: true);
            using (var ordinary = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/settings/pop3"))
            {
                ordinary.Headers.Add("X-CSRF-Token", token);
                ordinary.Content = JsonContent.Create(new { fields = new { welcomeMessage = new string('x', 20000) }, expected = new { welcomeMessage = "" } });
                using var response = await client.SendAsync(ordinary);
                check((int)response.StatusCode == 413, "Allowing certificate uploads does not enlarge other endpoints' request limits.");
            }
            await RunWorkGuards(check);
            await RunRateLimit(check);
            await Send(new { pfx, password, hosts = new[] { "mail.example.test" } });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfx); CryptographicOperations.ZeroMemory(large);
            foreach (byte[] wire in wires) CryptographicOperations.ZeroMemory(wire);
        }
    }

    private static async Task RunRateLimit(Action<bool, string> check)
    {
        await using var app = ManagementHost.Build(["--environment", "Development", "--urls", "http://127.0.0.1:0"],
            services => services.AddSingleton<IManagementChannel>(new FakeChannel()));
        await app.StartAsync();
        try
        {
            using var handler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(app.Urls.Single()) };
            var session = (await client.GetFromJsonAsync<JsonElement>("/api/v1/session")).GetProperty("data");
            using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/session")
                { Content = JsonContent.Create(new { username = "admin", password = "test-password" }) };
            login.Headers.Add("X-CSRF-Token", session.GetProperty("csrf").GetString());
            using var loggedIn = await client.SendAsync(login);
            check(loggedIn.IsSuccessStatusCode, "Certificate-rate fixture authenticates through the real HTTP pipeline.");
            string token = (await loggedIn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("csrf").GetString()!;
            for (int index = 0; index <= 30; index++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transport/certificates/inspect") { Content = JsonContent.Create(new { }) };
                request.Headers.Add("X-CSRF-Token", token);
                using var response = await client.SendAsync(request);
                check((int)response.StatusCode == (index < 30 ? 422 : 429), "Certificate inspection enforces its independent 30/minute request budget.");
                if (index == 30) check(response.Headers.RetryAfter?.Delta?.TotalSeconds is >= 1 and <= 60, "Certificate rate-limit retry guidance reflects its own window.");
            }
        }
        finally { await app.StopAsync(); }
    }

    private static async Task RunWorkGuards(Action<bool, string> check)
    {
        using var first = new HeldBody(); using var second = new HeldBody(); using var third = new HeldBody();
        var one = CertificateInspectionEndpoint.Handle(Context(first), TimeProvider.System);
        var two = CertificateInspectionEndpoint.Handle(Context(second), TimeProvider.System);
        Exception? failure = null;
        try
        {
            await Task.WhenAll(first.Started.Task, second.Started.Task).WaitAsync(TimeSpan.FromSeconds(5));
            var context = Context(third);
            await Refuse(CertificateInspectionEndpoint.Handle(context, TimeProvider.System), 429, check);
            check(context.Response.Headers.RetryAfter == "1", "Concurrent certificate work supplies bounded retry guidance.");
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            first.Release.TrySetResult(); second.Release.TrySetResult();
            try { await Refuse(one, 422, check); await Refuse(two, 422, check); }
            catch (Exception cleanup) when (failure is not null) { throw new AggregateException("Certificate concurrency assertions and cleanup failed.", failure, cleanup); }
        }
        using var timed = new HeldBody(); var clock = new DeadlineClock();
        var upload = CertificateInspectionEndpoint.Handle(Context(timed), clock);
        await timed.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        check(clock.Timer?.DueTime == TimeSpan.FromSeconds(30), "Certificate upload deadline is finite and driven by the trusted clock.");
        clock.Timer!.Fire();
        await Refuse(upload, 408, check);
        using var aborted = new HeldBody(); using var cancellation = new CancellationTokenSource();
        var disconnected = Context(aborted); disconnected.RequestAborted = cancellation.Token;
        var canceled = CertificateInspectionEndpoint.Handle(disconnected, TimeProvider.System);
        await aborted.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        try { await canceled; throw new Exception("Disconnected certificate upload continued."); }
        catch (OperationCanceledException) { check(true, "Caller cancellation stops certificate body transfer and releases work capacity."); }
    }

    private static async Task Refuse(Task<IResult> work, int status, Action<bool, string> check)
    {
        try { await work.WaitAsync(TimeSpan.FromSeconds(5)); throw new Exception("Certificate work guard unexpectedly succeeded."); }
        catch (ManagementError error) { check(error.Status == status, "Certificate work limit/deadline is enforced."); }
    }
    private static DefaultHttpContext Context(Stream body)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("level", "2")], "test")) };
        context.Request.Body = body; context.Features.Set<IHttpMaxRequestBodySizeFeature>(new BodyLimit());
        return context;
    }
    private sealed class BodyLimit : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;
        public long? MaxRequestBodySize { get; set; }
    }
    private sealed class DeadlineClock : TimeProvider
    {
        public DeadlineTimer? Timer { get; private set; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => Timer = new(callback, state, dueTime);
    }
    private sealed class DeadlineTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
    {
        private bool disposed;
        public TimeSpan DueTime => dueTime;
        public void Fire() { if (!disposed) callback(state); }
        public bool Change(TimeSpan dueTime, TimeSpan period) => !disposed;
        public void Dispose() => disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
    private sealed class HeldBody : Stream
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool sent;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (sent) return 0;
            Started.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); sent = true;
            byte[] data = "{\"pfx\":\"\",\"password\":\"\",\"hosts\":[\"mail.example.test\"]}"u8.ToArray();
            data.AsSpan().CopyTo(buffer.Span); return data.Length;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
