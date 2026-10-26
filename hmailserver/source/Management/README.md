# ArzoieMail management API and panel (Phase 1b, in progress)

The replacement panel is an ASP.NET Core / .NET 10 application. It communicates with the native
server over a restricted local Windows named pipe, never through COM or direct SQL. The old
PHP application and COM API remain available as compatibility surfaces during migration.

Read [the requirements](../../../docs/webadmin-requirements.md) and
[architecture/security boundaries](../../../docs/management-api-architecture.md).

## Build on the development host

```
dotnet build hmailserver/source/Management/Api/ArzoieMail.Api.csproj -c Release
dotnet build hmailserver/source/Management/Tests/ArzoieMail.Management.Tests.csproj -c Release
```

Build the C++ server using `build/build.ps1`. A full server build is required for changes to
the native management channel. Do not register it or install it on the development host.

**Run all test executables, application previews and browser tests only in the disposable VM
or CI.** `build/test-management.ps1` checks for a VM before executing tests. Test sources live
in `hmailserver/test/RegressionTests/Management`; they compile into a separate test executable.

## Deployment design

- Publish `Api/ArzoieMail.Api.csproj` for Windows x64. The ASP.NET Core runtime is required,
  or publish a self-contained bundle. The Desktop Runtime alone does not provide ASP.NET Core.
- Use a separate restricted service or IIS application-pool identity. Do not grant DCOM,
  administrator, database or mail-store access to this identity.
- Set `[Management] ApiClientSID` in the mail server's protected `hMailServer.ini` to that
  identity's SID, then restart the mail service. Leave the value absent to disable the pipe.
- Configure an explicit administration hostname with `AllowedHosts` and a valid HTTPS binding.
  No wildcard hosts, permissive CORS policy or automatic trust of forwarded headers is enabled.
- The API checks the named-pipe server PID against the running mail service before sending
  credentials. Both native and HTTP layers enforce the management boundary.
- The API identity needs permission to query the mail service's status for that verification;
  it does not need service start/stop or administrator permission.
- Web credentials are kept encrypted in bounded ephemeral memory; cookies hold only opaque
  session IDs. Logout, process restart, inactivity and credential revocation invalidate sessions.
- Installer/service provisioning and complete legacy-page parity are still being implemented.
  This preview is not yet selected by the installer as the replacement for existing WebAdmin.

## REST v1 contract

Responses use `{ "data": ... }`, or `{ "error": { "message": "..." } }` for errors.
POST/PATCH/DELETE requests require JSON, a same-origin request and an `X-CSRF-Token` header.
GET `/api/v1/session` supplies the initial antiforgery token. Obtain the new token after login
or logout. Cookies are HttpOnly, SameSite=Strict and Secure in production. Sessions expire after
30 minutes of inactivity, with an eight-hour absolute limit and no persistence across restart.

| Method | Endpoint | Native operation / permissions |
|---|---|---|
| GET | `/api/v1/session` | Anonymous bootstrap or current authenticated identity |
| POST | `/api/v1/session` | Sign in with username/password; rate limited |
| DELETE | `/api/v1/session` | Sign out |
| GET | `/api/v1/status` | Server administrator |
| GET | `/api/v1/domains` | Server scope or the caller's own domain |
| POST | `/api/v1/domains/{id}/dns` | Native domain authorization, then DNS advice; domain/server administrator |
| GET / PATCH | `/api/v1/antispam` | Server administrator; explicit allowlisted fields |
| GET / POST | `/api/v1/dnsbl` | List/create providers; server administrator |
| PATCH / DELETE | `/api/v1/dnsbl/{id}` | Update/delete provider; server administrator |
| POST | `/api/v1/spamassassin/test` | Test saved host/port; server administrator |
| GET | `/api/v1/diagnostics` | Server administrator |

DNSBL creation accepts `Active` (boolean), `DNSHost`, `ExpectedResult`, `RejectMessage`
(strings), and `Score` (integer 0..10000). All five fields are required. PATCH accepts a subset.
Unknown properties and incorrect types are refused before writes. DNSBLs created through the
panel start disabled. Spam settings persist individually in the existing server; validation
finishes before writes, but multi-setting updates are not a database transaction.

DNS inspection accepts `addresses` and `includes` arrays (up to ten each), `selector`, and an
optional base64 RSA `publicKey` (at least 2048 bits). It preserves existing SPF policies when
proposing a merge, detects include/redirect cycles and lookup-budget overflow, and labels
sender-dependent/macros or DNS failures inconclusive. It does not claim full message-context
SPF evaluation, organizational-domain DMARC inheritance, or proof of outgoing DKIM signing.
DNS is read through the Windows resolver, not a third-party public service. Review proposals
and test messages from all providers before strengthening DMARC.
