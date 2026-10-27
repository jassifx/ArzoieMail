# Phase 1b management architecture

## Decision (2026-10-26)

The maintainer requested that the new administration panel avoid a COM backend. Build the new
HTTP application on **ASP.NET Core / .NET 10**, alongside the existing .NET 10 tools. The new
panel and REST API have **no COM dependency** and do not access the mail database directly.
This supersedes the earlier PHP-hosted REST proposal and the PHP runtime requirement for the
replacement panel. The agreed end state removes PHP completely from administration delivery.
The existing legacy WebAdmin remains only until a tested migration retires its source, packaging
and deployment requirements; do not remove unrelated PHP applications from administrators' machines.

COM is not inherently insecure, and replacing it alone does not establish security. The goal
is an explicit, testable management boundary with limited operations and least privilege.
Retain the existing COM API separately for the Administrator, DBSetup, scripts and upgrade
compatibility. Removing those consumers and COM registration is a separate migration; this
change must not break them or silently expose them to the web application.

Keeping COM for compatibility retains that existing attack surface. Do not grant the API
identity DCOM access; audit legacy activation permissions independently. Migrating desktop tools,
DB setup and scripts to the new API must precede any eventual removal of COM registration.

## Boundaries

```text
Browser -> HTTPS REST /api/v1 -> ASP.NET Core administration process
                                     |
                          local Windows named pipe
                                     |
                          native management dispatcher
                                     |
                         C++ business / persistence layer

Existing desktop tools / scripts -> existing COM API -> same business layer
```

- Host the API with its own restricted Windows identity. It must not run as LocalSystem or
  an administrator, and needs no direct database credentials, mail-store access or DCOM grant.
- The native management pipe is opt-in through `[Management] ApiClientSID` in `hMailServer.ini`
  (or the mail service process environment setting `ARZOIEMAIL_MANAGEMENT_CLIENT_SID`, which
  takes precedence). Use the dedicated API service identity's SID in production.
  No network listener is added to the C++ mail service.
- The pipe permits only the configured API identity, the mail service identity and SYSTEM.
  Reject remote clients, reserve the first pipe instance, bound message size and depth, apply
  I/O deadlines and stop the worker before closing the database. The client verifies that
  the pipe server process is the running `hMailServer` service to resist name squatting.
- Use a versioned, length-prefixed JSON command protocol, a client receipt acknowledgement before
  pipe disconnect, and an explicit allowlist of operations.
  Never expose arbitrary object traversal, reflection, SQL, shell commands or filesystem reads.
- Authenticate credentials and authorize each operation in the native dispatcher. Share
  credential validation with the compatibility interface, not its COM wrappers. Domain and
  account boundaries must be enforced even if an HTTP handler is implemented incorrectly.

## HTTP security

- Use framework cookie protection and antiforgery, secure HttpOnly SameSite cookies, HTTPS,
  login rate limits, request limits, strict CSP, generic errors and no permissive CORS policy.
- Require explicit production `AllowedHosts` and HTTPS. Unencrypted HTTP is allowed only for
  loopback connections in the Development environment, inside the test VM.
- Cookies contain only opaque session IDs. Keep credentials encrypted in bounded, ephemeral
  server-side memory, reauthenticate native commands and expire sessions on restart, logout,
  inactivity or credential revocation. Never persist passwords in browser storage or cookies.
- Do not trust forwarded headers without an explicitly configured trusted proxy. Production
  deployments terminate HTTPS in the application or a configured trusted hosting layer.
- Publish versioned REST documentation and test unauthenticated access, CSRF, role changes,
  domain isolation, malformed IPC, pipe ACLs, unavailable services and credential revocation.

## Delivery status

The maintainer requires **all runtime testing inside the disposable Hyper-V VM or CI**, including
unit/security test executables, browser previews and integration tests. The development host is
for source editing, static inspection and compilation only. Do not start a local API preview or
run test executables there. This supersedes earlier local-testing notes and assumptions.

The native/API/panel foundation was merged in PR #31. Phase 1b remains in progress. The PHP/COM
prototype was withdrawn; reusable Bootstrap assets are retained under
`hmailserver/source/Management/Api/wwwroot`. New backend code is under `Management/Api` and
native management code belongs under `Server/Common/Management`.

Deliver and validate the native transport/authentication and HTTP foundation first, then migrate
administration capabilities and DNS guidance with regression coverage. Do not switch the installer
to the replacement panel or claim complete parity until endpoint, browser and upgrade tests pass.
SpamAssassin integration, DNSBL control and complex SPF/DKIM/DMARC guidance remain required.
Engine bundling and final service/installer provisioning remain explicit delivery work.

SQL Server Express modernization was merged separately in PR #33, with the full four-backend CI
matrix passing. It changes native database connectivity, not the panel's no-SQL/no-COM boundary.
The [documentation status](documentation-status.md) and ROADMAP track remaining feature guides,
manual technical review and publication; a prose rebrand does not complete panel migration.

The next authorized slice adds domain/mailbox CRUD, bounded list responses and panel workflows
on this same native boundary. See the [REST contract](../hmailserver/source/Management/README.md#rest-v1-contract)
for fields, units and the role matrix. Native commands load request-local business objects and
use existing persistence validation/cache invalidation. Persisted server-administrator roles
are checked before requested changes, preventing a domain administrator from demoting an account
and then replacing its password. No new schema, COM dependency or direct web SQL is introduced.
This slice remains under review until its PR is accepted; advanced account settings and full
administration parity are separate remaining work.

## Antivirus lifecycle and high-load boundary

Optional Microsoft Defender integration must meet the [Defender capacity and lifecycle requirements](defender-antivirus-feasibility.md#high-pressure-scanning-across-the-message-lifecycle).
Scan scheduling and verdict state belong to the native mail pipeline, independent of the web
host. Use durable work, bounded workers, backpressure and recovery across receiving, queued
sending, server-saved drafts and content changes. No normal delivery/retrieval may bypass the
required verdict. Validate protocol staging and per-message mitigation before enabling the feature;
Defender throughput and the necessary scheduling changes are not implemented by the API foundation.

## Implementation sequence

1. Establish the opt-in native management boundary, shared business authentication, ASP.NET Core
   session/security foundation, initial spam/DNSBL/DNS workflows and repeatable tests. This is
   the foundation merged in PR #31; it does not switch existing installations to the new panel.
2. Migrate the remaining domain/account, alias, distribution-list, delivery, rules, TLS and
   advanced administration capabilities to explicit native commands and REST endpoints, with
   server/domain/account permission tests for every operation.
3. Complete DNS setup workflows and protocol-conformance coverage, including DKIM key lifecycle
   and provider coexistence. Retain the distinction between DNS inspection and message-level
   authentication; do not replace the server's existing authentication evaluators with the wizard.
4. Finalize SpamAssassin engine packaging/lifecycle, API hosting under a restricted identity,
   HTTPS/certificate provisioning and offline installer/runtime delivery. Deliver the guided
   certificate import/issuance, listener assignment, renewal, validation and rollback requirements
   in [WebAdmin requirements](webadmin-requirements.md#certificate-setup-and-lifecycle). Keep mail
   listener certificates independent of IIS web bindings; automatic issuance is not yet implemented.
5. Switch the installer and end-user documentation only after parity, upgrade, security and
   supported-database acceptance tests pass. Retire the legacy PHP panel through that migration;
   remove PHP source, packaging and runtime requirements from administration delivery and verify
   that upgrades disable the old endpoint. Keep COM compatibility independent until its consumers
   have a supported replacement.
