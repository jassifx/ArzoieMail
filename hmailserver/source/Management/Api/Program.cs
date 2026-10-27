using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using ArzoieMail.Management;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;

var app = ManagementHost.Build(args);
await app.RunAsync();

public static class ManagementHost
{
    public static WebApplication Build(string[] args, Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders().AddSimpleConsole();
        if (!builder.Environment.IsDevelopment()
            && (string.IsNullOrWhiteSpace(builder.Configuration["AllowedHosts"]) || builder.Configuration["AllowedHosts"]!.Contains('*')))
            throw new InvalidOperationException("Configure explicit AllowedHosts for the administration hostname.");
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 16384;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
        });
        builder.Services.AddSingleton<IManagementChannel, ManagementChannel>();
        builder.Services.AddSingleton<CredentialVault>();
        builder.Services.AddSingleton<ITxtResolver, WindowsTxtResolver>();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow);
        // All web sessions deliberately end on restart. No reusable credentials or keys on disk.
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-Token";
            options.Cookie.Name = "ArzoieMail.Csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = "ArzoieMail.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
            options.Events.OnValidatePrincipal = async context =>
            {
                var vault = context.HttpContext.RequestServices.GetRequiredService<CredentialVault>();
                string? id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                try
                {
                    var login = vault.Get(id);
                    var channel = context.HttpContext.RequestServices.GetRequiredService<IManagementChannel>();
                    var identity = await channel.SendAsync("authenticate", login.Username, login.Password, null, context.HttpContext.RequestAborted);
                    context.ReplacePrincipal(Principal(id!, identity));
                }
                catch (ManagementError error) when (error.Status == 401)
                {
                    vault.Remove(id);
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync();
                }
            };
        });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetConcurrencyLimiter("management", _ => new ConcurrencyLimiterOptions { PermitLimit = 16, QueueLimit = 0 }));
            options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0, AutoReplenishment = true }));
            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "300";
                await context.HttpContext.Response.WriteAsJsonAsync(new { error = new { message = "Too many requests. Try again later." } }, token);
            };
        });
        configure?.Invoke(builder.Services);
        var app = builder.Build();
        if (!app.Environment.IsDevelopment()) app.UseHsts();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; base-uri 'none'; form-action 'self'; frame-ancestors 'none'; object-src 'none'";
            context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            try
            {
                bool localDevelopment = app.Environment.IsDevelopment() && context.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip);
                if (!context.Request.IsHttps && !localDevelopment)
                    throw new ManagementError(400, "HTTPS is required.");
                await next(context);
            }
            catch (Exception error) when (!context.Response.HasStarted)
            {
                int status = error switch
                {
                    ManagementError management => management.Status,
                    AntiforgeryValidationException => 403,
                    BadHttpRequestException bad => bad.StatusCode,
                    JsonException => 400,
                    _ => 503
                };
                if (error is not (ManagementError or AntiforgeryValidationException or BadHttpRequestException or JsonException))
                    app.Logger.LogWarning("Management operation failed ({ExceptionType}).", error.GetType().Name);
                context.Response.StatusCode = status;
                await context.Response.WriteAsJsonAsync(new { error = new { message = error is ManagementError known ? known.Message :
                    status == 403 ? "The security token expired. Reload and try again." : status == 400 ? "Invalid request." : "The management service is unavailable." } });
            }
        });
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                string origin = context.Request.Headers.Origin.ToString();
                if (origin.Length != 0 && origin != $"{context.Request.Scheme}://{context.Request.Host}")
                    throw new ManagementError(403, "Cross-origin requests are not allowed.");
                await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
                if (!context.Request.HasJsonContentType()) throw new ManagementError(415, "Use application/json.");
            }
            await next(context);
        });
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapGet("/api/v1/session", (HttpContext context, IAntiforgery antiforgery) =>
            Data(new { user = UserView(context.User), csrf = antiforgery.GetAndStoreTokens(context).RequestToken }));
        app.MapPost("/api/v1/session", async (HttpContext context, CredentialVault vault, IManagementChannel channel, IAntiforgery antiforgery) =>
        {
            var login = await context.Request.ReadFromJsonAsync<Login>(context.RequestAborted);
            if (login is null || string.IsNullOrWhiteSpace(login.Username) || login.Username.Length > 320
                || string.IsNullOrEmpty(login.Password) || login.Password.Length > 1024)
                throw new ManagementError(422, "A username and nonempty password are required.");
            vault.Remove(context.User.FindFirstValue(ClaimTypes.NameIdentifier));
            await context.SignOutAsync();
            var identity = await channel.SendAsync("authenticate", login.Username, login.Password, null, context.RequestAborted);
            string id = vault.Add(login);
            context.User = Principal(id, identity);
            // Persist only the opaque vault ID in the encrypted cookie ticket. Roles and names
            // are reloaded from the native service, never accepted from a browser-held snapshot.
            await context.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, id)
            ], CookieAuthenticationDefaults.AuthenticationScheme)));
            return Data(new { user = UserView(context.User), csrf = antiforgery.GetAndStoreTokens(context).RequestToken });
        }).RequireRateLimiting("login");
        app.MapDelete("/api/v1/session", async (HttpContext context, CredentialVault vault, IAntiforgery antiforgery) =>
        {
            vault.Remove(context.User.FindFirstValue(ClaimTypes.NameIdentifier));
            await context.SignOutAsync();
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
            return Data(new { user = (object?)null, csrf = antiforgery.GetAndStoreTokens(context).RequestToken });
        });
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapGet("/status", (HttpContext c, CredentialVault v, IManagementChannel p) => Forward(c, v, p, "status"));
        api.MapGet("/domains", (HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "domains.list"));
        api.MapPost("/domains", (HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "domains.create", true));
        api.MapGet("/domains/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Forward(c, v, p, "domain", false, id));
        api.MapPatch("/domains/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "domains.update", true, id));
        api.MapDelete("/domains/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "domains.delete", false, id));
        api.MapGet("/domains/{domainId:long:min(1)}/accounts", (long domainId, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "accounts.list", domainId: domainId));
        api.MapPost("/domains/{domainId:long:min(1)}/accounts", (long domainId, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "accounts.create", true, domainId: domainId));
        api.MapGet("/domains/{domainId:long:min(1)}/accounts/{id:long:min(1)}", (long domainId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "accounts.read", false, id, domainId));
        api.MapPatch("/domains/{domainId:long:min(1)}/accounts/{id:long:min(1)}", (long domainId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "accounts.update", true, id, domainId));
        api.MapDelete("/domains/{domainId:long:min(1)}/accounts/{id:long:min(1)}", (long domainId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "accounts.delete", false, id, domainId));
        api.MapGet("/domains/{domainId:long:min(1)}/accounts/{id:long:min(1)}/behavior", (long domainId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "accounts.behavior.read", false, id, domainId));
        api.MapPatch("/domains/{domainId:long:min(1)}/accounts/{id:long:min(1)}/behavior", (long domainId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "accounts.behavior.update", true, id, domainId));
        api.MapGet("/domains/{domainId:long:min(1)}/accounts/{id:long:min(1)}/signature", (long domainId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "accounts.signature.read", false, id, domainId));
        api.MapPatch("/domains/{domainId:long:min(1)}/accounts/{id:long:min(1)}/signature", (long domainId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "accounts.signature.update", true, id, domainId));
        foreach (string resource in new[] { "aliases", "lists" })
        {
            string command = resource;
            var group = api.MapGroup("/domains/{domainId:long:min(1)}/" + resource);
            group.MapGet("", (long domainId, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, command + ".list", domainId: domainId));
            group.MapPost("", (long domainId, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, command + ".create", true, domainId: domainId));
            group.MapGet("/{id:long:min(1)}", (long domainId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, command + ".read", false, id, domainId));
            group.MapPatch("/{id:long:min(1)}", (long domainId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, command + ".update", true, id, domainId));
            group.MapDelete("/{id:long:min(1)}", (long domainId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, command + ".delete", false, id, domainId));
        }
        var members = api.MapGroup("/domains/{domainId:long:min(1)}/lists/{listId:long:min(1)}/members");
        members.MapGet("", (long domainId, long listId, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "members.list", domainId: domainId, listId: listId));
        members.MapPost("", (long domainId, long listId, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "members.create", true, domainId: domainId, listId: listId));
        members.MapGet("/{id:long:min(1)}", (long domainId, long listId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "members.read", false, id, domainId, listId));
        members.MapPatch("/{id:long:min(1)}", (long domainId, long listId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "members.update", true, id, domainId, listId));
        members.MapDelete("/{id:long:min(1)}", (long domainId, long listId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "members.delete", false, id, domainId, listId));
        api.MapPost("/domains/{id:long:min(1)}/dns", async (long id, HttpContext c, CredentialVault vault, IManagementChannel channel, ITxtResolver resolver) =>
        {
            var credentials = vault.Get(c.User.FindFirstValue(ClaimTypes.NameIdentifier));
            var domain = await channel.SendAsync("domain", credentials.Username, credentials.Password, new { id }, c.RequestAborted);
            var input = await c.Request.ReadFromJsonAsync<DnsInput>(c.RequestAborted) ?? new();
            if (string.IsNullOrEmpty(input.Selector) && domain.GetProperty("selector").GetString() is { Length: > 0 } selector)
                input = input with { Selector = selector };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            return Data(await new DnsAdvisor(resolver).InspectAsync(domain.GetProperty("name").GetString()!, input, timeout.Token));
        });
        api.MapGet("/antispam", (HttpContext c, CredentialVault v, IManagementChannel p) => Forward(c, v, p, "antispam.read"));
        api.MapPatch("/antispam", (HttpContext c, CredentialVault v, IManagementChannel p) => Forward(c, v, p, "antispam.update", true));
        api.MapGet("/dnsbl", (HttpContext c, CredentialVault v, IManagementChannel p) => Forward(c, v, p, "dnsbl.list"));
        api.MapPost("/dnsbl", (HttpContext c, CredentialVault v, IManagementChannel p) => Forward(c, v, p, "dnsbl.create", true));
        api.MapPatch("/dnsbl/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Forward(c, v, p, "dnsbl.update", true, id));
        api.MapDelete("/dnsbl/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Forward(c, v, p, "dnsbl.delete", false, id));
        api.MapPost("/spamassassin/test", (HttpContext c, CredentialVault v, IManagementChannel p) => Forward(c, v, p, "spamassassin.test"));
        api.MapGet("/diagnostics", async (HttpContext c, CredentialVault v, IManagementChannel p) =>
        {
            await Forward(c, v, p, "status"); // Native authorization, not a browser-provided role.
            return Data(new { checks = new[] {
                new { safe = true, message = "ASP.NET Core / .NET " + Environment.Version },
                new { safe = c.Request.IsHttps, message = "HTTPS transport (HTTP only permitted for local development)." },
                new { safe = true, message = "Native management channel connected; no COM dependency." }
            }});
        });
        return app;
    }

    private static IResult Data(object data) => Results.Json(new { data });
    private static async Task<IResult> Objects(HttpContext context, CredentialVault vault, IManagementChannel channel,
        string operation, bool readBody = false, long? id = null, long? domainId = null, long? listId = null)
    {
        var arguments = new Dictionary<string, object?>();
        if (id.HasValue) arguments["id"] = id.Value;
        if (domainId.HasValue) arguments["domainId"] = domainId.Value;
        if (listId.HasValue) arguments["listId"] = listId.Value;
        if (operation.EndsWith(".list", StringComparison.Ordinal))
        {
            foreach (string key in context.Request.Query.Keys)
            {
                var values = context.Request.Query[key];
                if (values.Count != 1) throw new ManagementError(422, "Supply one value per filter.");
                string value = values.ToString();
                if (key == "search" && value.Length <= 255) arguments[key] = value;
                else if ((key == "offset" || key == "limit") && long.TryParse(value, out long number)
                         && number >= (key == "limit" ? 1 : 0) && number <= (key == "limit" ? 100 : int.MaxValue)) arguments[key] = number;
                else throw new ManagementError(422, "Invalid list filter. Use search, offset and limit (1–100).");
            }
        }
        if (readBody)
        {
            var fields = await context.Request.ReadFromJsonAsync<JsonElement>(context.RequestAborted);
            if (fields.ValueKind != JsonValueKind.Object) throw new ManagementError(422, "Supply a JSON object.");
            arguments["fields"] = fields;
        }
        var credentials = vault.Get(context.User.FindFirstValue(ClaimTypes.NameIdentifier));
        return Data(await channel.SendAsync(operation, credentials.Username, credentials.Password, arguments, context.RequestAborted));
    }
    private static ClaimsPrincipal Principal(string id, JsonElement user) => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Name, user.GetProperty("address").GetString()!),
        new Claim("level", user.GetProperty("level").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new Claim("domainId", user.GetProperty("domainId").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new Claim("accountId", user.GetProperty("accountId").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture))
    ], CookieAuthenticationDefaults.AuthenticationScheme));
    private static object? UserView(ClaimsPrincipal user) => user.Identity?.IsAuthenticated != true ? null : new
    { address = user.Identity.Name, level = int.Parse(user.FindFirstValue("level")!), domainId = long.Parse(user.FindFirstValue("domainId")!), accountId = long.Parse(user.FindFirstValue("accountId")!) };
    private static async Task<IResult> Forward(HttpContext context, CredentialVault vault, IManagementChannel channel,
        string operation, bool readBody = false, long? id = null)
    {
        var credentials = vault.Get(context.User.FindFirstValue(ClaimTypes.NameIdentifier));
        JsonElement? fields = readBody ? await context.Request.ReadFromJsonAsync<JsonElement>(context.RequestAborted) : null;
        if (fields.HasValue && fields.Value.ValueKind != JsonValueKind.Object) throw new ManagementError(422, "Supply a JSON object.");
        object? arguments = id.HasValue ? new { id, fields } : fields;
        var result = await channel.SendAsync(operation, credentials.Username, credentials.Password, arguments, context.RequestAborted);
        return Data(result);
    }
}
