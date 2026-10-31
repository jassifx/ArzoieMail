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

public static partial class ManagementHost
{
    public static WebApplication Build(string[] args, Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders().AddSimpleConsole();
        // Installed, the panel runs as the ArzoieMailAdmin service under its own virtual account;
        // setup registers the event source it logs to. Elsewhere this changes nothing.
        builder.Host.UseWindowsService(options => options.ServiceName = "ArzoieMailAdmin");
        if (OperatingSystem.IsWindows() && Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
            AddServiceLog(builder.Logging);
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
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ITxtResolver, WindowsTxtResolver>();
        // ACME: the challenge answers, issuance jobs and renewal task, and the challenge listener.
        builder.Services.Configure<AcmeOptions>(builder.Configuration.GetSection("Acme"));
        builder.Services.AddSingleton<AcmeChallenges>();
        builder.Services.AddSingleton<AcmeIssuer>();
        builder.Services.AddSingleton<AcmeJobs>();
        builder.Services.AddSingleton<AcmeRenewalService>();
        builder.Services.AddHostedService(services => services.GetRequiredService<AcmeRenewalService>());
        builder.Services.AddHostedService<AcmeChallengeServer>();
        builder.Services.AddHttpClient(AcmeIssuer.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(services => AcmeIssuer.CreateHandler(services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AcmeOptions>>().Value));
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
                    if (!vault.MatchesStamp(id, SecurityStamp(identity))) throw new ManagementError(401, "Please sign in again.");
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
        builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<TimeProvider>((options, clock) => options.TimeProvider = clock);
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetConcurrencyLimiter("management", _ => new ConcurrencyLimiterOptions { PermitLimit = 16, QueueLimit = 0 }));
            options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0, AutoReplenishment = true }));
            options.AddPolicy("certificate-inspection", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
            options.OnRejected = async (context, token) =>
            {
                long retrySeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
                    ? Math.Clamp((long)Math.Ceiling(retry.TotalSeconds), 1, 3600) : 1;
                context.HttpContext.Response.Headers.RetryAfter = retrySeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
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
                    status == 403 ? "The security token expired. Reload and try again." : status == 400 ? "Invalid request." : status == 413 ? "The request is too large for this operation." : "The management service is unavailable." } });
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
            string id = vault.Add(login, SecurityStamp(identity));
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
        api.MapGet("/self", (HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "self.read"));
        foreach (string area in new[] { "profile", "behavior", "signature" })
        {
            api.MapGet("/self/" + area, (HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "self." + area + ".read"));
            api.MapPatch("/self/" + area, (HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "self." + area + ".update", true));
        }
        api.MapGet("/self/rules", (HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "self.rules.list"));
        api.MapPost("/self/rules", (HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "self.rules.create", true));
        api.MapGet("/self/rules/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "self.rules.read", id: id));
        api.MapPatch("/self/rules/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "self.rules.update", true, id));
        api.MapDelete("/self/rules/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "self.rules.delete", id: id));
        api.MapPost("/self/password", async (HttpContext c, CredentialVault v, IManagementChannel p, IAntiforgery antiforgery) =>
        {
            await Objects(c, v, p, "self.password.update", true);
            v.Remove(c.User.FindFirstValue(ClaimTypes.NameIdentifier));
            await c.SignOutAsync();
            c.User = new ClaimsPrincipal(new ClaimsIdentity());
            return Data(new { changed = true, user = (object?)null, csrf = antiforgery.GetAndStoreTokens(c).RequestToken });
        }).RequireRateLimiting("login");
        api.MapGet("/settings/{section}", (string section, HttpContext c, CredentialVault v, IManagementChannel p) => Settings(c, v, p, section, false));
        api.MapPatch("/settings/{section}", (string section, HttpContext c, CredentialVault v, IManagementChannel p) => Settings(c, v, p, section, true));
        api.MapGet("/transport/listeners", (HttpContext c, CredentialVault v, IManagementChannel p) => TransportInventory(c, v, p, "transport.listeners.list"));
        api.MapGet("/transport/certificates", (HttpContext c, CredentialVault v, IManagementChannel p) => TransportInventory(c, v, p, "transport.certificates.list"));
        api.MapPost("/transport/certificates/inspect", CertificateInspectionEndpoint.Handle).RequireRateLimiting("certificate-inspection");
        CertificateEndpoints.Map(api);
        api.MapPost("/policies/securityranges/match", (HttpContext c, CredentialVault v, IManagementChannel p) => Forward(c, v, p, "securityranges.match", true));
        foreach (string resource in new[] { "surbl", "blockedattachments", "incomingrelays", "securityranges", "spamallowlist", "greylistexceptions" })
        {
            var policy = api.MapGroup("/policies/" + resource);
            policy.MapGet("", (HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, resource + ".list"));
            policy.MapPost("", (HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, resource + ".create", true));
            policy.MapGet("/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, resource + ".read", id: id));
            policy.MapPatch("/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, resource + ".update", true, id));
            policy.MapDelete("/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, resource + ".delete", true, id));
        }
        api.MapGet("/queue", (HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "queue.list"));
        api.MapGet("/queue/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "queue.read", id: id));
        api.MapPost("/queue/{id:long:min(1)}/retry", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "queue.retry", id: id));
        api.MapDelete("/queue/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "queue.delete", id: id));
        api.MapPost("/queue/wake", (HttpContext c, CredentialVault v, IManagementChannel p) => Forward(c, v, p, "queue.wake", true));
        foreach (var resource in new[] { ("routes", "routes"), ("rules", "rules") })
        {
            string command = resource.Item1; var group = api.MapGroup("/" + resource.Item2);
            group.MapGet("", (HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, command + ".list"));
            group.MapPost("", (HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, command + ".create", true));
            group.MapGet("/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, command + ".read", id: id));
            group.MapPatch("/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, command + ".update", true, id));
            group.MapDelete("/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, command + ".delete", id: id));
        }
        api.MapGet("/routes/{routeId:long:min(1)}/addresses", (long routeId, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "routeaddresses.list", routeId: routeId));
        api.MapPost("/routes/{routeId:long:min(1)}/addresses", (long routeId, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "routeaddresses.create", true, routeId: routeId));
        api.MapGet("/routes/{routeId:long:min(1)}/addresses/{id:long:min(1)}", (long routeId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "routeaddresses.read", id: id, routeId: routeId));
        api.MapDelete("/routes/{routeId:long:min(1)}/addresses/{id:long:min(1)}", (long routeId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "routeaddresses.delete", id: id, routeId: routeId));
        api.MapGet("/domains", (HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "domains.list"));
        api.MapPost("/domains", (HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "domains.create", true));
        api.MapGet("/domains/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Forward(c, v, p, "domain", false, id));
        api.MapPatch("/domains/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "domains.update", true, id));
        api.MapDelete("/domains/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "domains.delete", false, id));
        api.MapGet("/domains/{id:long:min(1)}/settings", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "domains.settings.read", false, id));
        api.MapPatch("/domains/{id:long:min(1)}/settings", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "domains.settings.update", true, id));
        api.MapGet("/domains/{domainId:long:min(1)}/names", (long domainId, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "domainaliases.list", domainId: domainId));
        api.MapPost("/domains/{domainId:long:min(1)}/names", (long domainId, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "domainaliases.create", true, domainId: domainId));
        api.MapGet("/domains/{domainId:long:min(1)}/names/{id:long:min(1)}", (long domainId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "domainaliases.read", false, id, domainId));
        api.MapDelete("/domains/{domainId:long:min(1)}/names/{id:long:min(1)}", (long domainId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "domainaliases.delete", false, id, domainId));
        api.MapGet("/domains/{domainId:long:min(1)}/accounts", (long domainId, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "accounts.list", domainId: domainId));
        api.MapPost("/domains/{domainId:long:min(1)}/accounts", (long domainId, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "accounts.create", true, domainId: domainId));
        api.MapGet("/domains/{domainId:long:min(1)}/accounts/{id:long:min(1)}", (long domainId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "accounts.read", false, id, domainId));
        api.MapPatch("/domains/{domainId:long:min(1)}/accounts/{id:long:min(1)}", (long domainId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "accounts.update", true, id, domainId));
        api.MapDelete("/domains/{domainId:long:min(1)}/accounts/{id:long:min(1)}", (long domainId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "accounts.delete", false, id, domainId));
        api.MapGet("/domains/{domainId:long:min(1)}/accounts/{id:long:min(1)}/behavior", (long domainId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "accounts.behavior.read", false, id, domainId));
        api.MapPatch("/domains/{domainId:long:min(1)}/accounts/{id:long:min(1)}/behavior", (long domainId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "accounts.behavior.update", true, id, domainId));
        api.MapGet("/domains/{domainId:long:min(1)}/accounts/{id:long:min(1)}/signature", (long domainId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "accounts.signature.read", false, id, domainId));
        api.MapPatch("/domains/{domainId:long:min(1)}/accounts/{id:long:min(1)}/signature", (long domainId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "accounts.signature.update", true, id, domainId));
        api.MapGet("/domains/{domainId:long:min(1)}/accounts/{id:long:min(1)}/profile", (long domainId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "accounts.profile.read", false, id, domainId));
        api.MapPatch("/domains/{domainId:long:min(1)}/accounts/{id:long:min(1)}/profile", (long domainId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "accounts.profile.update", true, id, domainId));
        var mailboxRules = api.MapGroup("/domains/{domainId:long:min(1)}/accounts/{accountId:long:min(1)}/rules");
        mailboxRules.MapGet("", (long domainId, long accountId, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "rules.list", domainId: domainId, accountId: accountId));
        mailboxRules.MapPost("", (long domainId, long accountId, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "rules.create", true, domainId: domainId, accountId: accountId));
        mailboxRules.MapGet("/{id:long:min(1)}", (long domainId, long accountId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "rules.read", id: id, domainId: domainId, accountId: accountId));
        mailboxRules.MapPatch("/{id:long:min(1)}", (long domainId, long accountId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "rules.update", true, id, domainId, accountId: accountId));
        mailboxRules.MapDelete("/{id:long:min(1)}", (long domainId, long accountId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "rules.delete", id: id, domainId: domainId, accountId: accountId));
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
        // External POP3 fetch accounts: server and domain administrators, checked again natively.
        var fetch = api.MapGroup("/domains/{domainId:long:min(1)}/accounts/{accountId:long:min(1)}/fetchaccounts");
        fetch.MapGet("", (long domainId, long accountId, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "fetchaccounts.list", domainId: domainId, accountId: accountId));
        fetch.MapPost("", (long domainId, long accountId, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "fetchaccounts.create", true, domainId: domainId, accountId: accountId));
        fetch.MapGet("/{id:long:min(1)}", (long domainId, long accountId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "fetchaccounts.read", id: id, domainId: domainId, accountId: accountId));
        fetch.MapPatch("/{id:long:min(1)}", (long domainId, long accountId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "fetchaccounts.update", true, id, domainId, accountId: accountId));
        fetch.MapDelete("/{id:long:min(1)}", (long domainId, long accountId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "fetchaccounts.delete", id: id, domainId: domainId, accountId: accountId));
        fetch.MapPost("/{id:long:min(1)}/run", (long domainId, long accountId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "fetchaccounts.run", id: id, domainId: domainId, accountId: accountId));
        // Groups, public folders and their permissions: server administrators.
        api.MapGet("/groups", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerObjects(c, v, p, "groups.list"));
        api.MapPost("/groups", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerObjects(c, v, p, "groups.create", true));
        api.MapGet("/groups/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => ServerObjects(c, v, p, "groups.read", id: id));
        api.MapPatch("/groups/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => ServerObjects(c, v, p, "groups.update", true, id));
        api.MapDelete("/groups/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => ServerObjects(c, v, p, "groups.delete", id: id));
        api.MapGet("/groups/{groupId:long:min(1)}/members", (long groupId, HttpContext c, CredentialVault v, IManagementChannel p) => ServerObjects(c, v, p, "groupmembers.list", groupId: groupId));
        api.MapPost("/groups/{groupId:long:min(1)}/members", (long groupId, HttpContext c, CredentialVault v, IManagementChannel p) => ServerObjects(c, v, p, "groupmembers.create", true, groupId: groupId));
        api.MapDelete("/groups/{groupId:long:min(1)}/members/{id:long:min(1)}", (long groupId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => ServerObjects(c, v, p, "groupmembers.delete", id: id, groupId: groupId));
        api.MapGet("/publicfolders", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerObjects(c, v, p, "publicfolders.list"));
        api.MapPost("/publicfolders", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerObjects(c, v, p, "publicfolders.create", true));
        api.MapPatch("/publicfolders/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => ServerObjects(c, v, p, "publicfolders.update", true, id));
        api.MapDelete("/publicfolders/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => ServerObjects(c, v, p, "publicfolders.delete", id: id));
        api.MapGet("/publicfolders/{folderId:long:min(1)}/permissions", (long folderId, HttpContext c, CredentialVault v, IManagementChannel p) => ServerObjects(c, v, p, "folderpermissions.list", folderId: folderId));
        api.MapPost("/publicfolders/{folderId:long:min(1)}/permissions", (long folderId, HttpContext c, CredentialVault v, IManagementChannel p) => ServerObjects(c, v, p, "folderpermissions.create", true, folderId: folderId));
        api.MapPatch("/publicfolders/{folderId:long:min(1)}/permissions/{id:long:min(1)}", (long folderId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => ServerObjects(c, v, p, "folderpermissions.update", true, id, folderId: folderId));
        api.MapDelete("/publicfolders/{folderId:long:min(1)}/permissions/{id:long:min(1)}", (long folderId, long id, HttpContext c, CredentialVault v, IManagementChannel p) => ServerObjects(c, v, p, "folderpermissions.delete", id: id, folderId: folderId));
        // Existing antivirus providers: status and tests of the configured scanners only.
        api.MapGet("/antivirus", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "antivirus.status"));
        api.MapPost("/antivirus/test", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "antivirus.test", true));
        // The native Bayesian filter: statistics, training from a mailbox folder, and reset. Its settings are in /antispam.
        api.MapGet("/bayes", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "bayes.status"));
        api.MapPost("/bayes/train", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "bayes.train", true));
        api.MapPost("/bayes/reset", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "bayes.reset", true));
        // DKIM signing. Domain administrators may read their own domain; changes need a server administrator.
        api.MapGet("/domains/{domainId:long:min(1)}/dkim", (long domainId, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "dkim.read", domainId: domainId));
        api.MapPatch("/domains/{domainId:long:min(1)}/dkim", (long domainId, HttpContext c, CredentialVault v, IManagementChannel p) => Objects(c, v, p, "dkim.update", true, domainId: domainId));
        api.MapPost("/domains/{domainId:long:min(1)}/dkim/keys", async (long domainId, HttpContext c, CredentialVault vault, IManagementChannel channel) =>
        {
            if (c.User.FindFirstValue("level") != "2") throw new ManagementError(403, "Server administration is required.");
            var body = await c.Request.ReadFromJsonAsync<JsonElement>(c.RequestAborted);
            if (body.ValueKind != JsonValueKind.Object || body.EnumerateObject().Any(p => p.Name is not ("selector" or "bits"))
                || !body.TryGetProperty("selector", out var selector) || selector.ValueKind != JsonValueKind.String)
                throw new ManagementError(422, "Supply a selector; keys are RSA 2048.");
            var arguments = new Dictionary<string, object?> { ["domainId"] = domainId, ["selector"] = selector.GetString() };
            if (body.TryGetProperty("bits", out var bits)) arguments["bits"] = bits;
            var credentials = vault.Get(c.User.FindFirstValue(ClaimTypes.NameIdentifier));
            return Data(await channel.SendAsync("dkim.generate", credentials.Username, credentials.Password, arguments, c.RequestAborted));
        });
        api.MapPost("/domains/{domainId:long:min(1)}/dkim/keys/{selector}/activate", async (long domainId, string selector, HttpContext c, CredentialVault vault, IManagementChannel channel, ITxtResolver resolver) =>
        {
            if (c.User.FindFirstValue("level") != "2") throw new ManagementError(403, "Server administration is required.");
            var body = await c.Request.ReadFromJsonAsync<JsonElement>(c.RequestAborted);
            bool skip = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("skipDnsCheck", out var flag) && flag.ValueKind == JsonValueKind.True;
            var credentials = vault.Get(c.User.FindFirstValue(ClaimTypes.NameIdentifier));
            var state = await channel.SendAsync("dkim.read", credentials.Username, credentials.Password, new { domainId }, c.RequestAborted);
            var key = state.GetProperty("keys").EnumerateArray().FirstOrDefault(k => k.GetProperty("selector").GetString() == selector);
            if (key.ValueKind != JsonValueKind.Object) throw new ManagementError(404, "No key has that selector.");
            if (!skip)
            {
                // Publishing comes first: activating a key whose record is missing makes every signature fail.
                string name = key.GetProperty("dnsName").GetString()!, expected = DkimPublicKey(key.GetProperty("dnsRecord").GetString());
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                string[] published;
                try { published = await resolver.QueryAsync(name, timeout.Token); }
                catch (Exception error) when (error is not OperationCanceledException || !c.RequestAborted.IsCancellationRequested)
                { throw new ManagementError(409, $"The DNS record {name} could not be looked up. Publish it, or activate without the DNS check."); }
                if (expected.Length == 0 || !published.Any(record => DkimPublicKey(record) == expected))
                    throw new ManagementError(409, $"The DNS record {name} does not publish this key yet. Publish it and try again, or activate without the DNS check.");
            }
            return Data(await channel.SendAsync("dkim.activate", credentials.Username, credentials.Password, new { domainId, selector }, c.RequestAborted));
        });
        api.MapDelete("/domains/{domainId:long:min(1)}/dkim/keys/{selector}", async (long domainId, string selector, HttpContext c, CredentialVault vault, IManagementChannel channel) =>
        {
            if (c.User.FindFirstValue("level") != "2") throw new ManagementError(403, "Server administration is required.");
            var credentials = vault.Get(c.User.FindFirstValue(ClaimTypes.NameIdentifier));
            return Data(await channel.SendAsync("dkim.delete", credentials.Username, credentials.Password, new { domainId, selector }, c.RequestAborted));
        });
        // Backup, restore and the installation key.
        api.MapGet("/backup", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "backup.status"));
        api.MapPost("/backup/start", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "backup.start"));
        api.MapPost("/backup/restore", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "backup.restore", true));
        api.MapPut("/settings/backup/passphrase", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "settings.backup.passphrase", true));
        api.MapGet("/secretkey", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "secretkey.read"));
        api.MapPost("/secretkey/export", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "secretkey.export", true));
        api.MapPost("/secretkey/import", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "secretkey.import", true));
        // Server operations. The native dispatcher authorizes each one again.
        api.MapPut("/settings/smtp/smart-host-credential", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "settings.smarthost.credential", true));
        api.MapPost("/settings/administrator-password", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "settings.administrator.password", true));
        api.MapPost("/settings/performance/message-index/clear", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "settings.messageindex.clear"));
        api.MapPost("/server/start", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "server.start", true));
        api.MapPost("/server/stop", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "server.stop", true));
        api.MapGet("/servermessages", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "servermessages.list"));
        api.MapGet("/servermessages/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "servermessages.read", id: id));
        api.MapPatch("/servermessages/{id:long:min(1)}", (long id, HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "servermessages.update", true, id));
        api.MapPost("/sendout/preview", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "sendout.preview", true));
        api.MapPost("/sendout", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "sendout.send", true));
        api.MapPost("/diagnostics/run", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "diagnostics.start", true));
        api.MapPost("/diagnostics/mail-servers", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "diagnostics.mailservers", true));
        api.MapGet("/diagnostics/results", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "diagnostics.read"));
        api.MapGet("/logs", (HttpContext c, CredentialVault v, IManagementChannel p) => ServerOnly(c, v, p, "logs.list"));
        api.MapGet("/logs/{name}", async (string name, HttpContext c, CredentialVault vault, IManagementChannel channel) =>
        {
            if (c.User.FindFirstValue("level") != "2") throw new ManagementError(403, "Server administration is required.");
            if (!LogName().IsMatch(name)) throw new ManagementError(422, "Choose a log file from the list.");
            var arguments = new Dictionary<string, object?> { ["name"] = name };
            foreach (string key in c.Request.Query.Keys)
            {
                var values = c.Request.Query[key];
                if (key != "before" || values.Count != 1 || !long.TryParse(values.ToString(), System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out long before))
                    throw new ManagementError(422, "The only log filter is before, a byte offset.");
                arguments["before"] = before;
            }
            var credentials = vault.Get(c.User.FindFirstValue(ClaimTypes.NameIdentifier));
            return Data(await channel.SendAsync("logs.read", credentials.Username, credentials.Password, arguments, c.RequestAborted));
        });
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
    // The p= value of a DKIM TXT record, without whitespace; empty when absent.
    internal static string DkimPublicKey(string? record)
    {
        if (string.IsNullOrEmpty(record)) return "";
        foreach (string tag in record.Split(';'))
        {
            string part = tag.Trim();
            if (part.StartsWith("p=", StringComparison.Ordinal)) return string.Concat(part[2..].Where(ch => !char.IsWhiteSpace(ch)));
        }
        return "";
    }
    // Must match the native allowlist; the native side checks the name again.
    [System.Text.RegularExpressions.GeneratedRegex(@"^(ERROR_)?hmailserver(_[A-Za-z0-9]{1,16})?_[0-9]{4}-[0-9]{2}-[0-9]{2}\.log\z|^hmailserver_(awstats|backup|events)\.log\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex LogName();
    private static Task<IResult> ServerObjects(HttpContext context, CredentialVault vault, IManagementChannel channel,
        string operation, bool readBody = false, long? id = null, long? groupId = null, long? folderId = null)
    {
        if (context.User.FindFirstValue("level") != "2") throw new ManagementError(403, "Server administration is required.");
        return Objects(context, vault, channel, operation, readBody, id, groupId: groupId, folderId: folderId);
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void AddServiceLog(ILoggingBuilder logging) =>
        logging.AddEventLog(settings => settings.SourceName = "ArzoieMail Administration");
    private static Task<IResult> ServerOnly(HttpContext context, CredentialVault vault, IManagementChannel channel,
        string operation, bool readBody = false, long? id = null)
    {
        if (context.User.FindFirstValue("level") != "2") throw new ManagementError(403, "Server administration is required.");
        return id.HasValue ? Objects(context, vault, channel, operation, readBody, id) : Forward(context, vault, channel, operation, readBody);
    }
    private static string SecurityStamp(JsonElement identity)
    {
        if (!identity.TryGetProperty("securityStamp", out var field) || field.ValueKind != JsonValueKind.String
            || field.GetString() is not string stamp || stamp.Length != 64 || !stamp.All(char.IsAsciiHexDigit))
            throw new ManagementError(503, "The native identity version is unavailable.");
        return stamp;
    }
    private static async Task<IResult> Settings(HttpContext context, CredentialVault vault, IManagementChannel channel,
        string section, bool update)
    {
        if (context.User.FindFirstValue("level") != "2") throw new ManagementError(403, "Server administrator access is required.");
        var arguments = new Dictionary<string, object?> { ["section"] = section };
        if (update)
        {
            var body = await context.Request.ReadFromJsonAsync<JsonElement>(context.RequestAborted);
            if (body.ValueKind != JsonValueKind.Object || body.EnumerateObject().Count() != 2
                || !body.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object
                || !body.TryGetProperty("expected", out var expected) || expected.ValueKind != JsonValueKind.Object)
                throw new ManagementError(422, "Supply fields and their expected previous values. Reload after a conflict.");
            arguments["fields"] = fields;
            arguments["expected"] = expected;
        }
        var credentials = vault.Get(context.User.FindFirstValue(ClaimTypes.NameIdentifier));
        return Data(await channel.SendAsync(update ? "settings.update" : "settings.read", credentials.Username,
            credentials.Password, arguments, context.RequestAborted));
    }
    private static async Task<IResult> Objects(HttpContext context, CredentialVault vault, IManagementChannel channel,
        string operation, bool readBody = false, long? id = null, long? domainId = null, long? listId = null, long? routeId = null, long? accountId = null,
        long? groupId = null, long? folderId = null)
    {
        var arguments = new Dictionary<string, object?>();
        if (id.HasValue) arguments["id"] = id.Value;
        if (groupId.HasValue) arguments["groupId"] = groupId.Value;
        if (folderId.HasValue) arguments["folderId"] = folderId.Value;
        if (domainId.HasValue) arguments["domainId"] = domainId.Value;
        if (listId.HasValue) arguments["listId"] = listId.Value;
        if (routeId.HasValue) arguments["routeId"] = routeId.Value;
        if (accountId.HasValue) arguments["accountId"] = accountId.Value;
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
    private static Task<IResult> TransportInventory(HttpContext context, CredentialVault vault, IManagementChannel channel, string operation)
    {
        if (context.User.FindFirstValue("level") != "2") throw new ManagementError(403, "Server administration is required.");
        if (context.Request.Query.TryGetValue("limit", out var value) && (!int.TryParse(value.ToString(), out int limit) || limit is < 1 or > 25))
            throw new ManagementError(422, "Transport inventory page size must be between 1 and 25.");
        return Objects(context, vault, channel, operation);
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
