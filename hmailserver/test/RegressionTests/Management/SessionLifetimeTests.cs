using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ArzoieMail.Management;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

static class SessionLifetimeTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var clock = new ControlledClock();
        using (var vault = new CredentialVault(clock))
        {
            string idle = vault.Add(new("admin", "test-password"), new string('A', 64));
            clock.Advance(TimeSpan.FromMinutes(31));
            try { vault.Get(idle); throw new Exception("Idle vault entry survived expiry."); }
            catch (ManagementError error) { check(error.Status == 401, "Vault idle expiry rejects credentials."); }
            string active = vault.Add(new("admin", "test-password"), new string('A', 64));
            for (int index = 0; index < 24; ++index)
            {
                clock.Advance(TimeSpan.FromMinutes(20));
                check(vault.Get(active).Username == "admin", "Active sessions survive below their absolute limit.");
            }
            clock.Advance(TimeSpan.FromMinutes(1));
            try { vault.Get(active); throw new Exception("Vault absolute lifetime was renewed by activity."); }
            catch (ManagementError error) { check(error.Status == 401, "Eight-hour absolute expiry cannot slide indefinitely."); }
        }
        var fake = new FakeChannel();
        await using var app = ManagementHost.Build(["--environment", "Development", "--urls", "http://127.0.0.1:0"], services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IManagementChannel>(fake);
        });
        await app.StartAsync();
        try
        {
            var address = new Uri(app.Urls.Single());
            using var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };
            using var client = new HttpClient(handler) { BaseAddress = address };
            async Task<JsonElement> Session()
            {
                using var response = await client.GetAsync("/api/v1/session");
                check(response.StatusCode == HttpStatusCode.OK, "Session bootstrap remains available across lifecycle changes.");
                return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").Clone();
            }
            async Task Login(int expected = 200)
            {
                var session = await Session();
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/session") { Content = JsonContent.Create(new { username = "admin", password = "test-password" }) };
                request.Headers.Add("X-CSRF-Token", session.GetProperty("csrf").GetString());
                using var response = await client.SendAsync(request);
                check((int)response.StatusCode == expected, "Login uses the expected native identity binding contract.");
            }
            string Cookie() => handler.CookieContainer.GetCookies(address)["ArzoieMail.Session"]?.Value ?? throw new Exception("Missing session cookie.");
            void OnlyOpaqueIdentity()
            {
                var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
                var ticket = options.TicketDataFormat.Unprotect(Cookie()) ?? throw new Exception("Cannot inspect protected test cookie.");
                var claims = ticket.Principal.Claims.ToArray();
                check(claims.Length == 1 && claims[0].Type == ClaimTypes.NameIdentifier && claims[0].Value.Length == 64,
                    "Initial and renewed cookies contain only an opaque vault ID, never credentials, roles or native bindings.");
            }
            await Login();
            OnlyOpaqueIdentity();
            string originalCookie = Cookie();
            clock.Advance(TimeSpan.FromMinutes(16));
            fake.Level = 1;
            check((await Session()).GetProperty("user").GetProperty("level").GetInt32() == 1, "Renewal refreshes roles from native identity.");
            check(Cookie() != originalCookie, "Cookie lifetime slides after half its window under the injected clock.");
            OnlyOpaqueIdentity();
            clock.Advance(TimeSpan.FromMinutes(31));
            check((await Session()).GetProperty("user").ValueKind == JsonValueKind.Null, "Idle cookie expiry rejects the browser session.");
            await Login();
            fake.SecurityStamp = new('B', 64);
            check((await Session()).GetProperty("user").ValueKind == JsonValueKind.Null, "A changed native binding revokes a session even while its password remains valid.");
            fake.SecurityStamp = "";
            await Login(503);
            check((await Session()).GetProperty("user").ValueKind == JsonValueKind.Null, "Missing native binding cannot create a partially authenticated browser session.");
        }
        finally { await app.StopAsync(); }
    }

    sealed class ControlledClock : TimeProvider
    {
        long ticks = DateTimeOffset.UtcNow.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void Advance(TimeSpan amount) => Interlocked.Add(ref ticks, amount.Ticks);
    }
}
