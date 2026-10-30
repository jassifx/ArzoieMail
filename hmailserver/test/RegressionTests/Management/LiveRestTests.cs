using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArzoieMail.Management;

// Real Kestrel, cookies, authentication, pipe and persistence. No fake management channel.
static class LiveRestTests
{
    public static async Task Run(Action<bool, string> check)
    {
        string username = Required("ARZOIEMAIL_TEST_USERNAME"), password = Required("ARZOIEMAIL_TEST_PASSWORD");
        var native = new ManagementChannel();
        await using var app = ManagementHost.Build(["--environment", "Development", "--urls", "http://127.0.0.1:0"]);
        await app.StartAsync();
        using var admin = new BrowserSession(new Uri(app.Urls.Single()), check);
        long? domainId = null, routeId = null, ruleId = null;
        try
        {
            await admin.Send("/status", HttpMethod.Get, status: 401);
            var actor = await admin.Login(username, password);
            check(actor.GetProperty("level").GetInt32() == 2, "Real HTTP login obtains native server-administrator identity.");
            var status = await admin.Send("/status", HttpMethod.Get);
            check(status.GetProperty("version").GetString()!.StartsWith("6.", StringComparison.Ordinal), "Real API reports the installed ArzoieMail 6 service.");
            check(status.GetProperty("sessions").TryGetProperty("smtp", out _), "Real API reports session counts.");
            check((await admin.Send("/servermessages", HttpMethod.Get)).GetProperty("items").GetArrayLength() > 0, "Real API lists server messages.");
            await admin.Send("/logs", HttpMethod.Get);
            foreach (string path in new[] { "/logs/hMailServer.ini", "/logs/hmailserver_2026-10-30.log.bak", "/logs/hmailserver_events.log?before=abc", "/logs/hmailserver_events.log?before=-1", "/logs/hmailserver_events.log?offset=1" })
                await admin.Send(path, HttpMethod.Get, status: 422);
            // A stale expected state keeps this harmless even if the token check were bypassed.
            await admin.Send("/server/stop", HttpMethod.Post, new { expectedState = 1 }, status: 403, csrf: "wrong");
            check((await admin.Send("/sendout/preview", HttpMethod.Post, new { recipients = "nobody-" + Guid.NewGuid().ToString("N") + "@*" })).GetProperty("recipients").GetInt32() == 0, "Real API previews a sendout audience.");
            using (var page = await admin.Client.GetAsync("/"))
            {
                string html = await page.Content.ReadAsStringAsync();
                check(page.IsSuccessStatusCode && html.Contains("assets/panel.js", StringComparison.Ordinal), "Panel document is served by the real HTTP host.");
                check(page.Headers.GetValues("Content-Security-Policy").Single().Contains("frame-ancestors 'none'", StringComparison.Ordinal), "Live responses enforce the panel CSP.");
            }
            foreach (string asset in new[] { "/assets/panel.js", "/assets/panel.css", "/assets/vendor/bootstrap.min.css" })
                using (var response = await admin.Client.GetAsync(asset)) check(response.IsSuccessStatusCode, "Required panel asset is served.");

            string tag = "rest-" + Guid.NewGuid().ToString("N"), name = tag + ".test";
            await admin.Send("/domains", HttpMethod.Post, new { name, active = true }, status: 403, csrf: "wrong");
            await admin.Send("/domains", HttpMethod.Post, new { name, active = true }, status: 403, origin: "https://foreign.invalid");
            domainId = (await admin.Send("/domains", HttpMethod.Post, new { name, active = true })).GetProperty("id").GetInt64();
            string domainPath = "/domains/" + domainId;
            await admin.Send(domainPath, HttpMethod.Patch, new { unexpected = true }, status: 422);
            var account = await admin.Send(domainPath + "/accounts", HttpMethod.Post, new { address = "mail@" + name, password, active = true, level = 0 });
            long accountId = account.GetProperty("id").GetInt64(); string mailbox = domainPath + "/accounts/" + accountId;
            check(!account.TryGetProperty("password", out _), "Live HTTP account views never expose the password.");
            await admin.Send(mailbox + "/profile", HttpMethod.Patch, new { firstName = "REST", lastName = "Integration" });
            var persisted = await native.SendAsync("accounts.profile.read", username, password, new { domainId, id = accountId }, CancellationToken.None);
            check(persisted.GetProperty("firstName").GetString() == "REST", "HTTP profile change persists through the real native business layer.");
            await admin.Send(mailbox + "/signature", HttpMethod.Patch, new { signatureEnabled = false, signatureHtml = "<b>literal source</b>" });
            check((await admin.Send(mailbox + "/signature", HttpMethod.Get)).GetProperty("signatureHtml").GetString() == "<b>literal source</b>", "Live signature source round-trips without execution.");
            var alias = await admin.Send(domainPath + "/aliases", HttpMethod.Post, new { address = "alias@" + name, target = "mail@" + name, active = false });
            await admin.Send(domainPath + "/aliases/" + alias.GetProperty("id").GetInt64(), HttpMethod.Delete);
            var list = await admin.Send(domainPath + "/lists", HttpMethod.Post, new { address = "list@" + name });
            string listPath = domainPath + "/lists/" + list.GetProperty("id").GetInt64();
            var member = await admin.Send(listPath + "/members", HttpMethod.Post, new { address = "mail@" + name });
            await admin.Send(listPath + "/members/" + member.GetProperty("id").GetInt64(), HttpMethod.Delete);
            await admin.Send(listPath, HttpMethod.Delete);

            var draft = await admin.Send(mailbox + "/rules", HttpMethod.Post, new { name = tag });
            string draftPath = mailbox + "/rules/" + draft.GetProperty("id").GetInt64();
            check(!draft.GetProperty("active").GetBoolean(), "Live mailbox rule starts disabled.");
            await admin.Send(draftPath, HttpMethod.Patch, new { active = true }, status: 422);
            await admin.Send(draftPath, HttpMethod.Delete);
            ruleId = (await admin.Send("/rules", HttpMethod.Post, new { name = tag, criteria = new[] { new { field = 4, header = "", match = 2, value = tag } }, actions = new[] { new { type = 6 } } })).GetProperty("id").GetInt64();
            await admin.Send("/rules/" + ruleId, HttpMethod.Patch, new { active = true });
            await admin.Send("/rules/" + ruleId, HttpMethod.Patch, new { name = "active edit" }, status: 409);
            await admin.Send("/rules/" + ruleId, HttpMethod.Patch, new { active = false });

            routeId = (await admin.Send("/routes", HttpMethod.Post, new { domain = "relay-" + name, host = "127.0.0.1" })).GetProperty("id").GetInt64();
            string secret = Guid.NewGuid().ToString("N");
            await admin.Send("/routes/" + routeId, HttpMethod.Patch, new { requiresAuth = true, username = "relay", password = secret });
            var route = await admin.Send("/routes/" + routeId, HttpMethod.Get);
            check(route.GetProperty("passwordConfigured").GetBoolean() && !route.GetRawText().Contains(secret, StringComparison.Ordinal), "Live HTTP route credentials are write-only.");
            await admin.Send("/routes/" + routeId, HttpMethod.Patch, new { security = 0 }, status: 422);
            var recipient = await admin.Send("/routes/" + routeId + "/addresses", HttpMethod.Post, new { address = "mail@" + name });
            await admin.Send("/routes/" + routeId + "/addresses/" + recipient.GetProperty("id").GetInt64(), HttpMethod.Delete);
            long stored = long.Parse(Required("ARZOIEMAIL_TEST_STORED_ID"));
            await admin.Send("/queue/" + stored, HttpMethod.Get, status: 404);
            await admin.Send("/queue/" + stored + "/retry", HttpMethod.Post, status: 404);
            await admin.Send("/queue/" + stored, HttpMethod.Delete, status: 404);
            await admin.Send("/queue/wake", HttpMethod.Post);
            await admin.Send("/routes", HttpMethod.Post, new { description = new string('X', 20000) }, status: 413);

            using (var manager = new BrowserSession(admin.Client.BaseAddress!, check))
            {
                await manager.Login(Required("ARZOIEMAIL_TEST_DOMAIN_USERNAME"), password);
                await manager.Send("/domains/" + domainId, HttpMethod.Get, status: 403);
                foreach (string path in new[] { "/queue", "/routes", "/rules", "/status", "/servermessages", "/logs", "/diagnostics/results", "/logs/hmailserver_events.log" })
                    await manager.Send(path, HttpMethod.Get, status: 403);
                await manager.Send("/server/stop", HttpMethod.Post, new { expectedState = 3 }, status: 403);
                await manager.Send("/sendout/preview", HttpMethod.Post, new { recipients = "*" }, status: 403);
                await manager.Send("/settings/smtp/smart-host-credential", HttpMethod.Put, new { password = "x" }, status: 403);
                await manager.Send("/settings/administrator-password", HttpMethod.Post, new { currentPassword = password, newPassword = password }, status: 403);
                await manager.Send($"/domains/{actor.GetProperty("domainId").GetInt64()}/accounts/{actor.GetProperty("accountId").GetInt64()}/profile", HttpMethod.Patch, new { firstName = "blocked" }, status: 403);
            }
            using (var user = new BrowserSession(admin.Client.BaseAddress!, check))
            {
                await user.Login(Required("ARZOIEMAIL_TEST_USER_USERNAME"), password);
                await user.Send("/domains", HttpMethod.Get, status: 403);
                await user.Send("/queue", HttpMethod.Get, status: 403);
            }
            var revocable = await admin.Send(domainPath + "/accounts", HttpMethod.Post, new { address = "session@" + name, password, active = true, level = 2 });
            using (var resetSession = new BrowserSession(admin.Client.BaseAddress!, check))
            {
                var sessionUser = await resetSession.Login("session@" + name, password);
                check(!sessionUser.TryGetProperty("securityStamp", out _) && !sessionUser.TryGetProperty("password", out _),
                    "Browser identity responses contain neither native credential bindings nor passwords.");
                await resetSession.Send("/status", HttpMethod.Get);
                await admin.Send(domainPath + "/accounts/" + revocable.GetProperty("id").GetInt64(), HttpMethod.Patch, new { password });
                await resetSession.Send("/status", HttpMethod.Get, status: 401);
                check((await resetSession.Send("/session", HttpMethod.Get)).GetProperty("user").ValueKind == JsonValueKind.Null,
                    "Resetting even to the same password invalidates the earlier real browser session.");
            }
            using (var revoked = new BrowserSession(admin.Client.BaseAddress!, check))
            {
                await revoked.Login("session@" + name, password); await revoked.Send("/status", HttpMethod.Get);
                await admin.Send(domainPath + "/accounts/" + revocable.GetProperty("id").GetInt64(), HttpMethod.Patch, new { active = false });
                await revoked.Send("/status", HttpMethod.Get, status: 401);
                check((await revoked.Send("/session", HttpMethod.Get)).GetProperty("user").ValueKind == JsonValueKind.Null, "Real native revocation clears the browser session.");
            }
        }
        finally
        {
            if (ruleId.HasValue) await admin.Send("/rules/" + ruleId, HttpMethod.Delete);
            if (routeId.HasValue) await admin.Send("/routes/" + routeId, HttpMethod.Delete);
            if (domainId.HasValue) await admin.Send("/domains/" + domainId, HttpMethod.Delete);
            await app.StopAsync();
        }
        // A fresh process-equivalent host has a new vault and ephemeral keys. Old cookies cannot resume.
        await using var restarted = ManagementHost.Build(["--environment", "Development", "--urls", "http://127.0.0.1:0"]);
        await restarted.StartAsync();
        try
        {
            using var oldCookies = new BrowserSession(new Uri(restarted.Urls.Single()), check, admin.Cookies);
            await oldCookies.Send("/status", HttpMethod.Get, status: 401);
        }
        finally { await restarted.StopAsync(); }
    }

    static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new Exception("Missing live REST fixture variable: " + name);

    sealed class BrowserSession : IDisposable
    {
        readonly Action<bool, string> check;
        readonly HttpClientHandler handler;
        string? token;
        public HttpClient Client { get; }
        public CookieContainer Cookies => handler.CookieContainer;
        public BrowserSession(Uri address, Action<bool, string> check, CookieContainer? cookies = null)
        {
            this.check = check; handler = new HttpClientHandler { CookieContainer = cookies ?? new CookieContainer() };
            Client = new HttpClient(handler) { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) };
        }
        public async Task<JsonElement> Login(string username, string password)
        {
            token = (await Send("/session", HttpMethod.Get)).GetProperty("csrf").GetString();
            var login = await Send("/session", HttpMethod.Post, new { username, password });
            token = login.GetProperty("csrf").GetString();
            return login.GetProperty("user");
        }
        public async Task<JsonElement> Send(string path, HttpMethod method, object? fields = null, int status = 200, string? csrf = null, string? origin = null)
        {
            using var request = new HttpRequestMessage(method, "/api/v1" + path);
            if (method != HttpMethod.Get)
            {
                request.Content = JsonContent.Create(fields ?? new { });
                string? requestToken = csrf ?? token;
                if (requestToken is not null) request.Headers.Add("X-CSRF-Token", requestToken);
            }
            if (origin is not null) request.Headers.Add("Origin", origin);
            using var response = await Client.SendAsync(request);
            check((int)response.StatusCode == status, $"Live HTTP {method} {path}: expected {status}, got {(int)response.StatusCode}.");
            check(response.Headers.CacheControl?.NoStore == true, "Live API responses cannot be cached.");
            string raw = await response.Content.ReadAsStringAsync();
            if (raw.Length == 0) return default;
            var payload = JsonSerializer.Deserialize<JsonElement>(raw);
            return payload.TryGetProperty("data", out var data) ? data.Clone() : payload.Clone();
        }
        public void Dispose() { Client.Dispose(); handler.Dispose(); }
    }
}
