# ArzoieMail management API and panel (Phase 1b, in progress)

The replacement panel is an ASP.NET Core / .NET 10 application. It communicates with the native
server over a restricted local Windows named pipe, never through COM or direct SQL. The old
PHP administration and COM API still exist in current code. Both retire before the 6.0
integrated validation baseline after accepted migration; optional PHP webmail is allowed.
The [6.0 scope](../../../docs/version-6-scope.md) and [migration register](../../../docs/breaking-changes-6.0.md)
require all supported desktop/DB tools, tests and automation to use secured management API
contracts. This documentation decision adds no routes and removes no current implementation.

Read [the requirements](../../../docs/webadmin-requirements.md) and
[architecture/security boundaries](../../../docs/management-api-architecture.md).
The [documentation authority index](../../../docs/README.md) and
[detailed Phase 1b ledger](../../../docs/phase-1b-status.md) identify the status owners,
remaining capabilities, legacy migration inventories and acceptance gates. #42's application/test
final head `28dd3df` passed all four CI backends and merged as `74eecc017`; later revisions need their own CI.
The foundation was merged in PR #31; SQL Express connectivity in PR #33 is a separate native
database improvement. See the [Phase 1b tracker](../../../ROADMAP.md#phase-1b-delivery-tracker)
and [documentation status](../../../docs/documentation-status.md) for remaining administration,
certificate, Defender, hosting and PHP-retirement work. Do not present this development implementation as full parity.

The [CI acceptance matrix](../../../docs/ci-acceptance-matrix.md) defines the test layers for
this implementation: .NET/ASP.NET Core 10 runtime, publish/asset checks, deterministic HTTP and
browser fixtures, actual REST/native persistence, real production TLS/cookies/trust/Host policy,
HTTPS/native authentication and published browser smoke with real roles/data. Every backend
job runs them; install/IIS/certificate-wizard/Defender capacity gates remain separate.

After compiling the management test project, use these **inside the disposable VM only**:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build/test-management.ps1 -Runtime -NoBuild
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build/test-management.ps1 -Tls -NoBuild
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build/test-management.ps1 -PipeIdentity -NoBuild
```

Pipe-identity testing requires the real management pipe to be disabled first; CI runs it
before fixture opt-in. `test-management-live.ps1` provisions temporary roles, runs native,
live HTTP and native HTTPS modes, and optionally tests a published API with
`-PublishedApi <publish-directory>`. It requires an AdministratorCredential supplied securely
by the caller and the installed backend. A framework-dependent publish requires ASP.NET Core
10 as well as .NET 10; the Desktop Runtime alone is insufficient. CI publishes from the same
revision before testing and retains redacted evidence in backend-specific artifacts.

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
- Setup installs the panel as the ArzoieMailAdmin service under its own virtual account and
  does all of the above itself; see "Listener certificates, ACME and the installed panel".
  Complete legacy-page parity and the retirement of the PHP WebAdmin are still to come (E08).

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
| GET | `/api/v1/status` | Server administrator; version, state, start time, per-protocol session counts, message counters, cache hit rates |
| POST | `/api/v1/server/stop`, `/api/v1/server/start` | Server administrator; `{ "expectedState": 3 }` to stop, `1` to start; a different current state returns 409 |
| GET | `/api/v1/servermessages` | Server administrator; list server-generated message texts |
| GET / PATCH | `/api/v1/servermessages/{id}` | Server administrator; read, or change only the text with `{ "expected": { ... }, "text": "..." }` |
| POST | `/api/v1/sendout/preview` | Server administrator; count the active accounts matching `recipients` |
| POST | `/api/v1/sendout` | Server administrator; queue one message to the previewed accounts; `expectedRecipients` must match |
| POST | `/api/v1/diagnostics/run` | Server administrator; start the native diagnostics in the background |
| GET | `/api/v1/diagnostics/results` | Server administrator; poll the latest diagnostics run |
| POST | `/api/v1/diagnostics/mail-servers` | Server administrator; the servers outgoing delivery would use for `domain` |
| GET | `/api/v1/logs` | Server administrator; list allowlisted log files (name, size, modified) |
| GET | `/api/v1/logs/{name}` | Server administrator; a page of at most 16 KiB ending at `before` (default: end of file) |
| GET / POST | `/api/v1/domains/{domainId}/accounts/{accountId}/fetchaccounts` | Server or own-domain administrator; list/create external POP3 fetch accounts for a mailbox |
| GET / PATCH / DELETE | `/api/v1/domains/{domainId}/accounts/{accountId}/fetchaccounts/{id}` | Read/change/delete a fetch account; the password is write-only; deleting a running download returns 409 |
| POST | `/api/v1/domains/{domainId}/accounts/{accountId}/fetchaccounts/{id}/run` | Request a download now (enabled accounts only; 409 otherwise) |
| GET / POST | `/api/v1/groups` | Server administrator; list/create groups |
| GET / PATCH / DELETE | `/api/v1/groups/{id}` | Read/rename/delete a group (deleting removes memberships and its folder permissions) |
| GET / POST | `/api/v1/groups/{groupId}/members` | List/add members by `accountId` |
| DELETE | `/api/v1/groups/{groupId}/members/{id}` | Remove a member |
| GET / POST | `/api/v1/publicfolders` | Server administrator; list the public folder tree / create a folder under `parentId` (0 = top level) |
| PATCH / DELETE | `/api/v1/publicfolders/{id}` | Rename / delete a folder with its messages (409 while it has subfolders) |
| GET / POST | `/api/v1/publicfolders/{folderId}/permissions` | List / grant a permission to a mailbox (`type` 0, `accountId`), group (1, `groupId`) or anyone (2) |
| PATCH / DELETE | `/api/v1/publicfolders/{folderId}/permissions/{id}` | Change named `rights` / remove a permission |
| GET / POST, GET / PATCH / DELETE | `/api/v1/policies/spamallowlist[/{id}]` | Server administrator; trusted senders that skip spam checks (IP range plus address pattern) |
| GET / POST, GET / PATCH / DELETE | `/api/v1/policies/greylistexceptions[/{id}]` | Server administrator; IP patterns that skip greylisting |
| GET | `/api/v1/antivirus` | Server administrator; status of the ClamAV, custom-scanner and ClamWin providers |
| POST | `/api/v1/antivirus/test` | Test the configured `scanner` (`clamav`, `custom`, `clamwin`) with EICAR and clean files |
| GET | `/api/v1/bayes` | Server administrator; native Bayesian filter statistics and training queue |
| POST | `/api/v1/bayes/train` | Server administrator; queue every message of one mailbox `folder` (`accountId`) for training `as` `spam` or `ham` |
| POST | `/api/v1/bayes/reset` | Server administrator; forget all training, with `confirm: true` |
| GET / POST | `/api/v1/certificates` | Server administrator; listener certificates and listeners, or import PEM/PFX (1 MiB) |
| GET / PATCH / DELETE | `/api/v1/certificates/{id}` | Server administrator; read, rename or set `autoRenew`, delete when unused |
| POST | `/api/v1/certificates/{id}/replace`, `/rollback`, `/renew` | Server administrator; new generation, previous generation, ACME renewal job |
| POST | `/api/v1/certificates/acme` | Server administrator; ACME issuance job for `hostnames` (202) |
| GET | `/api/v1/certificates/acme/jobs/{id}` | Server administrator; issuance job state |
| GET / PATCH | `/api/v1/acme` | Server administrator; ACME directory, contact and terms acceptance |
| POST | `/api/v1/listeners`; PATCH / DELETE `/api/v1/listeners/{id}` | Server administrator; mail listeners, TLS mode and certificate (restart applies) |
| POST | `/api/v1/listeners/{id}/probe` | Server administrator; the certificate the listener actually serves |
| GET / PATCH | `/api/v1/domains/{domainId}/dkim` | Read (own-domain administrator or server administrator) / change (server administrator) DKIM signing settings |
| POST | `/api/v1/domains/{domainId}/dkim/keys` | Server administrator; generate an RSA 2048 key for `selector` |
| POST | `/api/v1/domains/{domainId}/dkim/keys/{selector}/activate` | Activate after the published DNS record matches; `skipDnsCheck` true activates anyway |
| DELETE | `/api/v1/domains/{domainId}/dkim/keys/{selector}` | Delete an inactive key |
| GET | `/api/v1/backup` | Server administrator; running state, destination and the newest 100 archives |
| POST | `/api/v1/backup/start` | Start a backup with the configured contents (409 while one runs, 412 without a valid destination) |
| POST | `/api/v1/backup/restore` | Restore chosen parts of an archive named in the listing; `confirmName` must repeat the name |
| PUT | `/api/v1/settings/backup/passphrase` | Write-only backup passphrase (at least 12 characters; empty removes it) |
| GET | `/api/v1/secretkey` | Server administrator; the installation key identifier |
| POST | `/api/v1/secretkey/export`, `/api/v1/secretkey/import` | Export the key under a passphrase (at least 12 characters) / import an exported key |
| PUT | `/api/v1/settings/smtp/smart-host-credential` | Server administrator; write-only `{ "password": "..." }`; empty clears |
| POST | `/api/v1/settings/administrator-password` | Server administrator; `currentPassword` of the built-in Administrator and `newPassword` |
| POST | `/api/v1/settings/performance/message-index/clear` | Server administrator; empty JSON object |
| GET | `/api/v1/queue` | Server administrator; pending/held metadata, searchable sender or ID |
| GET / DELETE | `/api/v1/queue/{id}` | Inspect recipients or permanently remove an idle queued item; stored mail is excluded |
| POST | `/api/v1/queue/{id}/retry` | Server administrator; request retry of an idle queued item |
| POST | `/api/v1/queue/wake` | Server administrator; request a check for due mail, empty JSON object |
| GET / POST | `/api/v1/routes` | Server administrator; list/create delivery routes |
| GET / PATCH / DELETE | `/api/v1/routes/{id}` | Server administrator; inspect/change/remove a route |
| GET / POST | `/api/v1/routes/{routeId}/addresses` | Server administrator; list/add explicitly allowed recipients |
| GET / DELETE | `/api/v1/routes/{routeId}/addresses/{id}` | Server administrator; exact-parent recipient inspection/removal |
| GET / POST | `/api/v1/rules` | Server administrator; list/create global disabled rule drafts |
| GET / PATCH / DELETE | `/api/v1/rules/{id}` | Server administrator; read/edit/activate/remove global rules |
| GET / POST | `/api/v1/domains/{domainId}/accounts/{accountId}/rules` | Owning-domain/server administrator; protected mailbox checks apply |
| GET / PATCH / DELETE | `/api/v1/domains/{domainId}/accounts/{accountId}/rules/{id}` | Exact mailbox/parent scope; read/edit/activate/remove mailbox rules |
| GET / POST | `/api/v1/domains` | Paginated list (server/all or domain administrator/own); create requires server administrator |
| GET / PATCH / DELETE | `/api/v1/domains/{id}` | Read/update own domain for domain administrator; server administrator for creation, limits and deletion |
| GET / PATCH | `/api/v1/domains/{id}/settings` | Advanced policies/signatures; own domain administrator or server administrator, object limits require server administrator |
| GET / POST | `/api/v1/domains/{domainId}/names` | Alternate domain list/create; own domain administrator may list, server administrator required to create |
| GET / DELETE | `/api/v1/domains/{domainId}/names/{id}` | Read/remove matching alternate name; server administrator required to remove |
| GET / POST | `/api/v1/domains/{domainId}/accounts` | Paginated list/create; server administrator or administrator of that domain |
| GET / PATCH / DELETE | `/api/v1/domains/{domainId}/accounts/{id}` | Read/update/delete; owning domain must match, server administrator accounts require server administrator |
| GET / PATCH | `/api/v1/domains/{domainId}/accounts/{id}/behavior` | Forwarding/automatic replies; owning domain must match, server administrator accounts require server administrator |
| GET / PATCH | `/api/v1/domains/{domainId}/accounts/{id}/signature` | Mailbox signature; same administrator, owning-domain and protected-target checks |
| GET / PATCH | `/api/v1/domains/{domainId}/accounts/{id}/profile` | Profile names and read-only directory/last-logon state; same administrator/domain/protected-target checks |
| GET / POST | `/api/v1/domains/{domainId}/aliases` | Paginated list/create; server administrator or administrator of that domain |
| GET / PATCH / DELETE | `/api/v1/domains/{domainId}/aliases/{id}` | Read/update/delete within the owning domain |
| GET / POST | `/api/v1/domains/{domainId}/lists` | Paginated list/create distribution lists within the owning domain |
| GET / PATCH / DELETE | `/api/v1/domains/{domainId}/lists/{id}` | Read/update/delete list settings within the owning domain |
| GET / POST | `/api/v1/domains/{domainId}/lists/{listId}/members` | Paginated list/create recipients; domain and list ownership are checked natively |
| GET / PATCH / DELETE | `/api/v1/domains/{domainId}/lists/{listId}/members/{id}` | Read/update/delete a recipient belonging to that exact list |
| POST | `/api/v1/domains/{id}/dns` | Native domain authorization, then DNS advice; domain/server administrator |
| GET / PATCH | `/api/v1/antispam` | Server administrator; explicit allowlisted fields |
| GET / POST | `/api/v1/dnsbl` | List/create providers; server administrator |
| PATCH / DELETE | `/api/v1/dnsbl/{id}` | Update/delete provider; server administrator |
| POST | `/api/v1/spamassassin/test` | Test saved host/port; server administrator |
| GET | `/api/v1/diagnostics` | Server administrator |

Domain and mailbox lists accept `search` (case-insensitive substring), `offset` (default 0),
and `limit` (default 50, maximum 100). Responses contain `items`, `total`, `offset`, `limit`.
Unknown/duplicate HTTP filters and out-of-range native pagination are rejected. Collections are
loaded using existing business objects and filtered in memory; pagination bounds the response,
not database work. Indexed database-side paging remains a capacity follow-up.

### Queue, routing and rules

The mail-operations bundle adds the endpoints above; VM acceptance passed. Check its PR for required CI and review.
Queue `id` values in responses are **decimal strings**, preserving all signed 64-bit digits in
JavaScript. Keep them as strings when constructing URLs. Queue summaries include sender,
created/next-try timestamps, size, attempts, busy and held state. Detail adds recipient count
and the first 100 recipients; bodies and storage paths are never returned. Reads are snapshots.
Missing, completed or stored-mail IDs return 404; busy delivery or bulk clearing returns 409
for mutations. Retry can release held mail and only requests future delivery; wake does not
override next-try timestamps. Removal is permanent. Reservation and mutation share a native
guard and invalidate pending delivery snapshots. Legacy COM single-item queue operations use
the same stored-mail/busy guards. Bulk queue clearing is not exposed through REST.
Queue listing reads all queue metadata before filtering/paging; this is not a large-queue
capacity guarantee. Database-side paging and measured workload acceptance remain follow-ups.

Routes accept `domain` (required on creation, immutable), `host` (required), `description`,
`port` (1–65535), `tries` (1–1000), `retryMinutes` (1–10080), `allAddresses`, `recipientLocal`,
`senderLocal`, `security`, `requiresAuth`, `username` and write-only `password`. Exact domains,
`*.domain` and `*` are allowed; host is a DNS name or IP address, never a URL. Defaults are
port 25, four attempts, 60 minutes, listed recipients only, external sender/recipient
classification, no authentication and **required STARTTLS**. Security values: 0 plaintext,
1 implicit TLS, 2 optional STARTTLS, 3 required STARTTLS. Authentication requires 1 or 3,
a username and a password. Omit password to retain it; an explicit empty password clears it
only with authentication disabled. Responses return only `passwordConfigured`. Omitted legacy
zero attempts/delay values are preserved; new writes require the stated positive bounds.
Allowed recipient entries accept one full `address`, are case-insensitively unique per route,
and have no PATCH operation. Route changes affect delivery immediately; review wildcard and
local-classification settings carefully. Inspect rules before deleting a referenced route.

Rules accept `name` (nonempty, 255 UTF-8 bytes), `useAnd`, nonnegative `sortOrder`, `active`,
`criteria` and ordered `actions`. New rules are disabled and append after the last sort order;
explicit sort orders cannot collide in the same scope. Disable an active rule with only
`{"active":false}` before editing it. Enable separately with only `{"active":true}`; activation
revalidates a nonempty definition. Editing an active definition returns 409. Each array is
limited to 20 entries. Lower rule sort order runs first; already-processing mail can finish
using an earlier cached definition.

Conditions use `field`, `header`, `match`, `value`. Fields: 0 custom header, 1 From, 2 To,
3 Cc, 4 Subject, 5 body, 6 size, 7 recipient list, 8 delivery attempts. Matches: 1 equals,
2 contains, 3 less than, 4 greater than, 6 does not contain, 7 does not equal, 8 wildcard.
Regex (5) is unavailable. Header names must be valid tokens, up to 78 bytes; values are at
most 255 bytes. Size/attempt values must be nonnegative 32-bit decimal integers.

Actions use `type`: 1 delete, 2 forward (`to`, `abortSpam`), 3 reply (`fromAddress`,
`fromName`, `subject`, `body`, `abortSpam`), 4 move (`folder`), 6 stop processing,
7 set header (`header`, `value`), 8 send using route (`routeId`, server administrator only),
9 create copy. Forward targets cannot equal the scoped mailbox; mailbox reply senders must
equal that mailbox. Reply body is limited to 8000 UTF-8 bytes; names/subject/folder/header
values to 255. Spam-abort defaults true. Engine, authentication and MIME structural headers
cannot be overwritten. Script (5) and local-binding (10) actions are unavailable. Existing
unsupported rules can be read, disabled or deleted; rebuild a supported draft before activation.

Definition validation completes before writes. Persistence saves the rule disabled first,
checks every child write, then enables only after success. This is **not a transaction**:
a database error can leave a disabled partial draft. A 503 requires reading the draft and
repairing or deleting it before enabling. Failed deletion can also need child cleanup; retry
or inspect through supported maintenance tools. There is no SQL or file editor in the panel.

Domain POST accepts `name` (required), `active`, `maxMessageSizeKB`, `maxSizeMB`,
`maxAccountSizeMB`. PATCH accepts the same fields except `name`; domain administrators may
change only `active` on their own domain. Names/addresses are immutable in this slice.
Mailbox POST accepts `address` and a nonempty `password` (required), `active`, `maxSizeMB`,
and `level` (0 user, 1 domain administrator, 2 server administrator). PATCH accepts all except
`address`. Defaults follow the native business objects; the panel explicitly creates active
objects and normal-user mailboxes. Numeric sizes are nonnegative 32-bit integers; 0 means
unlimited subject to domain policy. Names/addresses, duplicate aliases/lists and quotas use the
shared native persistence validation. Invalid/duplicate/quota failures return 422 without
echoing submitted credentials. Infrastructure failures return 503.

Domain administrators cannot create/promote server administrators or read the detail, reset,
demote, disable or delete an existing server administrator account. Scoped listings show only
basic identity/status/role/quota information for these accounts. They cannot change their own role.
Mailbox users have no access to these administrative operations; account self-service remains
future work. The active account and its owning domain cannot be deleted by that session (409).
Deletion uses existing cascading persistence and permanently removes dependent mail data;
the panel confirms the impact first. There is no new undo facility.

Passwords are write-only, hashed using the configured native password hasher, and never included
in views. Omit `password` on PATCH to preserve it; blank passwords are rejected. AD-backed
password changes are rejected and must be made in the directory. No AD provisioning is added.
The next request reauthenticates and observes password, role, active-state or deletion changes.
The panel ends the session after changing its own password/role or disabling its own account.

Advanced domain settings GET/PATCH uses explicit `domains.settings.read/update` native commands.
Ordinary mailbox users are denied; domain administrators may access only their own domain.
PATCH fields are:

- `catchAllAddress`: empty to disable, or one valid address belonging to an active mailbox in
  this primary domain. Setting a protected server-administrator mailbox requires server admin.
  The mailbox is checked at save time; later disabling/deleting it can invalidate catch-all
  delivery. Existing external/alias catch-all configurations are preserved when omitted;
  new writes through this endpoint require a mailbox. External forwarding can be configured
  on that mailbox through the existing forwarding controls.
- `plusAddressingEnabled`, `plusAddressingCharacter`: boolean and one of `+`, `-`, `.`, `=`, `_`.
  Empty separator is allowed only while disabled. Existing unusual separators are preserved
  when omitted. Address matching continues to use the native engine's separator semantics.
- `greylistingEnabled`: this domain's flag; global greylisting policy remains authoritative.
- `maxAccounts`, `maxAliases`, `maxLists`: nonnegative 32-bit object counts; 0 means unlimited.
  Only server administrators may write these, even when submitting an unchanged value. Counts
  use existing native creation limits. Lowering a limit retains existing objects and blocks
  further creation; it does not delete data. The native distribution-list limit uses its count
  directly; the endpoint clears the count for unlimited and updates legacy enable flags together.
- `signatureEnabled`, `signaturePlainText`, `signatureHtml`: existing domain formats, with the
  same types, 16,000 UTF-8 byte limits and control-character rules as mailbox signatures below.
- `signatureMethod`: 1 uses domain formats where mailbox formats are empty; 2 replaces mailbox
  formats; 3 appends domain formats. `signaturesOnReplies` and `signaturesOnLocalMail` are booleans
  affecting signature application, including mailbox signatures, under existing engine rules.

Omitted settings are preserved. The entire request and effective plus-addressing combination
are checked before setters/persistence. Settings are saved together in the existing domain row
and invalidate the native domain cache. No schema or web SQL is added. The panel submits changed
fields, edits HTML as inert source, handles CRLF like mailbox signatures, and confirms catch-all
activation/changes and active signature-policy changes. Other domain controls (DKIM key lifecycle,
directory provisioning and further administration) remain separate delivery work.

Alternate domain names use `domainaliases.list/read/create/delete`; there is no rename/PATCH.
Listing accepts the standard search/offset/limit filters. POST accepts only `name`, up to 253
ASCII bytes with DNS labels up to 63 characters, letters/digits/interior hyphens and dots;
use ASCII IDN/punycode labels. Names are stored lowercase. Empty labels, trailing dot, controls,
invalid labels, duplicates and collisions with primary domains are refused. Creation/removal
requires server administrator because these names reserve global server namespace; an owning
domain administrator may only list/read. Every object ID is checked against its parent domain.
Mappings are active immediately and use existing native routing/cache invalidation. Adding a
name does not create mailboxes, provision DNS/certificates/DKIM or prove domain ownership.
The panel confirms activation/removal. Existing alias persistence's insert result is additionally
checked for a newly assigned ID before reporting success. Removal has no undo facility.

Mailbox profile GET/PATCH uses `accounts.profile.read/update`. Only `firstName` and `lastName`
are writable: strings up to 255 UTF-8 bytes, including empty strings, without ASCII control
characters. GET additionally returns `directoryBacked` and the engine's recorded `lastLogon`;
both are read-only. No directory credentials/bindings or password changes are exposed here.
Names feed existing signature macros; HTML/email sanitization behavior is unchanged. The panel
uses text/input values, not executable markup. Parent/role protections match mailbox signatures.

Mailbox behavior GET/PATCH is an admin-only operation. Server administrators may access any
mailbox; domain administrators only their own domain's non-server-admin accounts. Normal users
have no self-service access yet. Settings are excluded from mailbox collections and fetched
separately. PATCH accepts only these fields (omitted values are preserved):

- `forwardEnabled`, `forwardAddress`, `forwardKeepOriginal`, `forwardAbortSpamFlagged`.
- `autoReplyEnabled`, `autoReplySubject`, `autoReplyBody`, `autoReplyExpires`,
  `autoReplyExpiresDate`, `autoReplyAbortSpamFlagged`.

Flags must be JSON booleans. Forwarding accepts one local/external address (255 UTF-8 bytes);
an enabled forward needs a valid destination different from the mailbox. Indirect cycles are
not detected here. Subject is limited to 255 UTF-8 bytes without control characters; plain-text
body to 16,000 UTF-8 bytes, permitting CR/LF/tab but no other ASCII controls. Subject/body can
be blank; a blank subject uses the existing `Re:` behavior. Total HTTP request/frame limits still
apply. Expiry is a real `YYYY-MM-DD` date from 1900, or empty when expiration is disabled;
expiration requires a date and occurs at its start in server local time, evaluated during delivery.
An empty date while expiration is disabled is stored as today's server-local date because the
existing database column requires a timestamp; the disabled flag is preserved.
An expired enabled flag may remain visible until delivery evaluates it. Enabling with an elapsed
date is allowed and does not send a reply. Unknown fields and invalid combined settings return
422 before setters/persistence. Updates use existing persistence/cache invalidation; no schema
or COM changes. As in the desktop tools, disabling replies clears in-memory per-sender history;
editing an enabled reply does not reset it. History is not durable across service restart and
disabling follows existing BO behavior even if the subsequent persistence operation fails.
The panel confirms active forwarding/reply changes and warns about external destinations,
storage and client-visible semantics. Existing spam-abort flags and mail-engine suppression
remain authoritative; these controls do not add a new forwarding policy or vacation scheduler.

Mailbox signature GET/PATCH accepts `signatureEnabled` (boolean), `signaturePlainText` and
`signatureHtml` (strings). Only server administrators or the owning domain's administrator may
use it; protected server-administrator accounts require server administrator. Ordinary mailbox
self-service remains planned. Settings are fetched separately and excluded from basic mailbox
views. Omitted fields retain their values; blank formats explicitly clear them. Disabling keeps
both formats. Unknown fields, wrong types, NUL/other ASCII controls except CR/LF/tab, or more
than 16,000 UTF-8 bytes per format return 422 before writes. The 16 KiB HTTP request and native
frame limits also apply; the panel submits changed fields and checks serialized request size.
Unchanged formats retain their stored line endings when toggling the enabled flag. Plain-text
edits from the browser use CRLF so the existing HTML fallback recognizes their line breaks;
REST input is otherwise preserved as supplied.

The panel edits HTML as inert textarea source, never as a live preview; it does not sanitize
email HTML or remove markup on save. Domain policy can append/override account signatures or
omit them for local mail/replies, and large/header-only messages can skip signature addition.
Existing `%User.FirstName%`, `%User.LastName%`, `%User.Address%` macros, MIME-part handling and
plain-to-HTML fallback are preserved. These controls do not change domain signature policy or
the native mail engine. Existing persistence invalidates the sender-account cache after saving.

Aliases, lists and members use the same `search`/`offset`/`limit` contract. Server administrators
can manage every domain; domain administrators can manage only their own; ordinary mailbox
users cannot use these operations. Parent IDs come from routes, never from writable fields.
Foreign-domain requests are denied and mismatched object/list IDs return 404.

Alias POST requires `address` and `target`; `active` is optional and defaults to false. PATCH
accepts `target` and/or `active`. The source address must belong to the owning domain; the
target can be local or external. Domain/address duplicate checks and configured alias limits
use existing native persistence. Direct self-forwarding is rejected. Renaming is not exposed.

List POST requires `address`; optional fields are `active` (default false), `mode` (default 1),
`requireAuth` (default true) and `requireAddress` (default empty). PATCH accepts those fields
except `address`. Existing domain list limits and account/alias/list collisions remain enforced.

| Mode | Meaning |
|---|---|
| 0 | Public: accepts any envelope sender, subject to the other SMTP policies. |
| 1 | Membership: the envelope sender must be a saved member. |
| 2 | Announcements: the envelope sender must match `requireAddress`, which must be a valid nonempty address. |
| 3 | Domain senders: the envelope sender must use this domain; the mailbox need not exist. |

These are the existing mail engine's envelope-sender checks. `requireAuth` additionally requires
SMTP authentication; it does not bind the envelope sender to the authenticated account. Keep
SMTP sender policy appropriate to the deployment. The panel confirms saving an active public
list or an active list without required authentication. REST callers explicitly control both.

Member POST requires `address`; PATCH updates `address`. Local and external recipients are
supported. Case-insensitive duplicates within the same list and a direct self-member pointing
at the list address are rejected. Member changes invalidate the mail engine's cached list.
Deleting a list removes its member entries through existing persistence; deleting an alias stops
forwarding through it. These operations do not provide undo or full graph-cycle validation.

All new address fields accept one unquoted email address, up to 255 UTF-8 bytes. Empty values,
commas/semicolons, quotes and ASCII control characters are rejected; `requireAddress` can be empty outside
announcement mode. Special local-part characters supported by the mail parser remain accepted.
Unknown fields, wrong types, invalid mode values and duplicates return 422 before writes.
No schema, web SQL or COM backend dependency is added.

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

## Server settings (completion branch, acceptance in progress)

`GET /api/v1/settings/{section}` returns `section`, `values` and a typed `schema` for an
explicit native allowlist. Sections: `protocols`, `smtp`, `imap`, `pop3`, `autoban`, `greylisting`,
`performance`, `general`, `antivirus`, `passwordhash`, `logging`, `tls`, `backup`. The current implementation maps 97 fields. Server administrators only, checked in HTTP and native code.
No raw property names, SQL, process commands, secret values or filesystem paths are accepted.

`PATCH` requires exactly `{ "fields": { ... }, "expected": { ... } }`, with matching keys
and the previous values loaded for each changed field. Missing/unknown/incorrectly typed or
out-of-range fields return 422; a stale expected value returns 409. UTF-8 text is bounded and
rejects control characters; hostnames/IPs, default-domain names and mirror addresses are
validated. Update only changed fields. Reload after a conflict rather than overwriting another
administrator's changes. CSRF/origin/HTTPS/session rules apply as for all mutations.

The native PropertySet checked batch uses bound guarded updates and readback on one leased
transaction connection. No reconnect/retry occurs inside it. Cache values are published only
after successful commit and notifications follow. Legacy setters and batch writes share the
write mutex; normal cached value reads do not hold that mutex during database I/O. Numeric
cache values are atomic, string copies are protected by a per-property lock, and map publication
has its own short lock. This is not a simultaneous multi-field snapshot for protocol readers,
nor a conversion of legacy setters into checked transactions. Other external database writers
are not a supported configuration-management mechanism.

On database/commit/notification failure, 503 does not prove the database remained unchanged:
a commit acknowledgement may be lost or a post-commit observer may fail. Reconcile persisted
and effective configuration before retrying, using supported recovery/service reload where
needed. Do not claim an acknowledged success from the cache alone. Failed or exceptional
BEGIN/COMMIT/ROLLBACK retires the physical client instead of recycling uncertain transaction
state; its replacement opens before any SQL borrower uses it. Unsupported transactional storage
returns 412. For MySQL, the existing capability check requires all database tables to use InnoDB;
migrate with the service stopped and restart before retrying. Helper fault-injection regressions
and native TestInternals passed in the disposable VM. Real provider outage/acknowledgement-loss,
restart and concurrent legacy/new-tool acceptance remain pending full-phase validation.

Schema entries expose type, bounds, explanation and whether restart is required. Saving never
restarts the service automatically. Protocol enablement and networking thread-count changes
are marked for restart; other values retain existing native effective behavior. SMTP message
sizes and existing scanner limits are KB; cache lifetimes are seconds; retry/Auto-ban windows
and initial greylisting delay are minutes; both greylisting triplet retention values are hours.
ClamAV settings configure the existing provider, not an installed engine. Its legacy size-skip
policy is unchanged; these controls are not the required Defender verdict pipeline.

`passwordhash` adds algorithm (0/default or 1 Argon2id, 2 PBKDF2-SHA256), memoryKiB,
iterations and autoUpgrade. Zero cost values select existing native algorithm defaults. Explicit
memory is 4096-1048576 KiB; Argon2id iterations 1-20; PBKDF2 iterations 10000-10000000.
Validation uses the complete proposed policy before any write, so change algorithm and iteration
count in the same patch when needed. Omitted dependencies are also guarded against concurrent
changes. Existing hashes keep their self-described format; auto-upgrade follows the configured
target after successful authentication, including deliberately lowered costs. Defaults are
unchanged. Measure authentication workload before increasing costs. This addition passed
initial Compact VM validation in the later 706/736-native-assertion runs and four-backend CI
at `bc3980201`; broader failure/concurrency acceptance remains pending. Stored-hash upgrades can require web sign-in again.

`logging` provides enabled, application, smtp, pop3, imap, tcpIp, debug, keepFilesOpen and
awStats. The native bitmask is never exposed. Multiple changed flags are merged into one
checked underlying update, preserving omitted and unknown native flags; a concurrent change
to that underlying setting causes 409 rather than overwriting it. AWStats remains its separate
native setting and participates in the same checked batch. Configuration uses existing log
storage/access policy; this endpoint does not expose filesystem paths or read log contents.
Keep logs private and monitor volume before enabling detailed conversations/debug. This later
addition passed initial Compact VM validation in the 736-native-assertion run and four-backend
CI at `bc3980201`; broader operational diagnostics and failure acceptance remain pending.

Panel tests cover cancellation, stale-edit refusal, refresh/retry, omitted-field preservation
and mobile layout. Native tests cover both roles, schema/secret boundaries, malformed patches,
persistence, cleanup and actual POP3 greeting behavior. Published live browser tests exercise
a real settings change and restore it. This branch still needs complete VM/CI evidence before
acceptance; the entire Phase 1b completion ledger remains open.

`tls` adds eight fields: verifyRemoteCertificate, cipherList, tls10Enabled, tls11Enabled,
tls12Enabled, tls13Enabled, preferServerCiphers and prioritizeChaCha. The underlying version
and option masks are never exposed. Multiple flag edits merge while preserving omitted and
unknown bits; dependencies and omitted underlying settings participate in the checked batch.
At least one known TLS version must remain enabled. ChaCha prioritization requires server
cipher preference and TLS 1.2 or 1.3. Obsolete TLS 1.0/1.1 remain explicit compatibility choices
with warnings; defaults are unchanged. Retain remote certificate verification to prevent
impersonation. Its existing native behavior applies to outgoing mail connections; mail TLS
settings do not configure the separate web HTTPS binding.

The cipher expression is limited to 4096 UTF-8 bytes. CR/LF and spaces retain the legacy
normalization behavior; other controls/NUL are rejected. Before persistence, OpenSSL must
accept a nonempty expression with at least one usable legacy-suite match. OpenSSL may ignore
unknown components in an otherwise matching expression; this is not a strict token allowlist.
An empty expression selects library defaults. This controls TLS 1.2 and earlier, not TLS 1.3
cipher suites. Cipher/version/option changes require a later service restart; saving neither
replaces active contexts nor restarts listeners. Pre-save checks do not prove endpoint handshake,
certificate/hostname/key suitability or rollback acceptance. Those A06/E03 gates remain open.
Native/managed compilation and the full Compact VM run passed (781 native assertions,
including 246 settings assertions, and both browsers), followed by four-backend CI at `82a3bdaf5`.

## Server operations and remaining protocol settings (bundle 1 checkpoint)

These close the remaining fields of the legacy SMTP, IMAP, POP3, performance, Auto-ban,
logging, mirror, status, server message, sendout, diagnostics and advanced pages (A01, A02,
A03, A05, A13, A14, A16, A20). Every operation is server-administrator only in the HTTP
handler and again in the native dispatcher.

**Additional settings.** `smtp` adds `deliverySecurity` (0 none, 2 STARTTLS when offered,
3 STARTTLS required; implicit TLS is refused for direct delivery), `smartHost`, `smartHostPort`,
`smartHostSecurity` (0-3), `smartHostAuthentication`, `smartHostUsername`, `srsEnabled` and
`srsMaxAgeDays` (1-512, the SRS day-counter limit). `imap` adds `masterUser` (empty, a mailbox
address, or a local part that takes the authenticating account's domain) and
`hierarchyDelimiter` (`.`, `/` or `\`). `performance` adds `messageIndexing`. Enumerated
values carry `options` (value and label) in the schema, and a value outside them is refused.
A stored value outside the options (set through COM) stays visible as "current value" in the
panel, so saving other fields never replaces it silently.

`hierarchyDelimiter` and `messageIndexing` are marked `direct`: they go through their native
setters, which rewrite rule folder references and clear rule caches, or start and stop the
indexer. A patch that changes one must contain only that field (422 otherwise). The expected
value is still checked, but unlike the other fields this is not one guarded database
transaction. The delimiter change returns 409 while a folder or rule action already contains
the new character.

The smart-host credential is write-only: `GET /settings/smtp` reports only
`credentials.smartHostConfigured`. It is stored through the existing encrypted setting. The
built-in Administrator password lives in `hMailServer.ini`, not a mailbox; changing it requires
the current Administrator password (403 when it is wrong) and revokes Administrator sessions
through the existing security stamp. Clearing the message index removes indexed metadata; it is
rebuilt gradually while indexing is enabled.

Not exposed, with reasons: worker thread priority (`WorkerThreadPriority`) is stored but no
server code reads it, so the setting has no effect; `background_iphome_save.php` targets an
`IPHomes` COM collection that no longer exists in the server or type library. Both are recorded
as retired in the [6.0 breaking-change register](../../../docs/breaking-changes-6.0.md).

**Status and control.** `GET /status` adds `architecture`, `startTime`, `processedMessages`,
`spamMessagesDetected`, `virusesRemoved`, `sessions` (`smtp`, `pop3`, `imap`) and
`cacheHitRates` (`domain`, `account`, `alias`, `list`, percent). Counters cover the current
service process. `server.stop` and `server.start` stop or start the protocol listeners,
delivery and external fetching inside the service process (the same operation as the desktop
Start/Stop button). The management pipe, configuration and database stay available, so the
panel can start the services again. The caller must name the state it observed (`3` running to
stop, `1` stopped to start): a different current state returns 409, which keeps two
administrators from toggling the service back and forth. This is not a Windows service
restart; restart-required settings still need one.

**Server messages.** Message names are fixed identifiers the server looks texts up by; only
`text` changes (at most 8000 UTF-8 bytes; tab, CR and LF allowed, other controls refused). The
update needs the complete `expected` object as loaded and returns 409 when it differs.

**Sendout.** `recipients` is a wildcard over account addresses (`*` matches any characters; no
spaces, commas, quotes or angle brackets). Only active accounts in active domains match, as in
the desktop tool. `POST /sendout/preview` returns the count. `POST /sendout` requires exactly
`recipients`, `fromAddress` (a valid address), `fromName`, `subject`, `body` (at most
12288 bytes) and `expectedRecipients`, which must equal the current count (409 otherwise, 422
when it is zero). `fromName` refuses quotes and backslashes, and both header fields refuse
control characters, so no header can be injected. One message with all matching recipients is
queued and delivered locally; the service log records the count. The panel enables sending only
after a preview of the same pattern and confirms the exact count.

**Diagnostics.** The native diagnostics (directories, IP ranges, error logs, IPv6 and,
optionally, MX records and self-connection for a local domain and outbound port 25 to a test
domain) can take longer than a management request, so `POST /diagnostics/run` starts them as
one maintenance work-queue task (409 while one is running) and `GET /diagnostics/results`
returns `running`, `run`, `completed` and the results. Results live in memory until the next
run or service restart. `localDomain` must be an existing local domain or empty; `testDomain`
must be a domain name or empty. `POST /diagnostics/mail-servers` returns up to 50 host/address
pairs that direct delivery would use for a domain, as the desktop MX query did; routes and the
smart host can override them.

**Logs.** `GET /logs` lists at most 200 files in the configured log directory whose names
match the native allowlist (`hmailserver_<date>.log`, `ERROR_hmailserver_<date>.log`,
`hmailserver_<KIND>_<date>.log`, `hmailserver_awstats.log`, `hmailserver_backup.log`,
`hmailserver_events.log`), newest first. Directories and reparse points are skipped. Reads take
only a name from that allowlist, never a path, open the file shared with the writer and refuse
reparse points. A page holds at most 16 KiB ending at `before`, starting at the next line
boundary; invalid UTF-8 becomes U+FFFD and control characters other than tab, CR and LF become
spaces, so a page always fits the management frame. Logs mask authentication credentials
(`***`) but contain addresses, IP addresses and message metadata, so keep this to server
administrators. There is no live log stream, search or download.

Tests: `ServerOperationsTests` runs against the native dispatcher on every CI backend. It
covers role refusals for every operation; status fields; a real stop (POP3 refused while the
management channel still answers), stale-state refusal and restart; option, range, header and
host validation; credential storage reporting only `configured`; the delimiter's alone-only rule
and restoration; index toggling and clearing; server message conflict/validation/persistence
and restoration; a sendout to two of three accounts in a temporary domain, verified by POP3;
diagnostics polling; mail-server lookup validation; and log name, offset and boundary refusals.
When the test administrator is the built-in Administrator it also changes and restores that
password. `LiveRestTests` checks the HTTP role, CSRF and log-name boundaries, and
`browser.py` covers every new panel control, including inert rendering of log and diagnostic
text and the mobile layout.

## Fetch accounts, groups, public folders and maintenance (bundle 2 checkpoint)

These replace the legacy external-account, group, public-folder, folder-permission, backup and
secret-key surfaces (A07, A10, A15).

**External POP3 fetch accounts** belong to one mailbox. Server administrators manage any
mailbox's; domain administrators manage those of mailboxes in their own domain that are not
server administrators; mailbox users cannot, because a fetch account makes the server connect
to any host it names. Fields: `name`, `active`, `serverAddress` (host name or IP), `port`,
`security`, `username`, write-only `password` (`passwordConfigured` is reported), and
`minutesBetweenFetch` (1-10080). `daysToKeep` is -1 to delete after download, 0 to keep on the
server, or N to delete after N days. The remaining fields are `processMimeRecipients`,
`mimeRecipientHeaders` (comma-separated header names), `processMimeDate`, `routeRecipients`
(only with MIME recipients), `useAntiSpam` and `useAntiVirus`. `nextTry` and `running` are
read-only. New accounts start disabled, on port 995 with implicit TLS.

`security` accepts only 1 (SSL/TLS) or 3 (STARTTLS required), so the panel never stores a
new plaintext POP3 logon. An existing account set to 0 or 2 through the legacy tools keeps that
value until it is changed, and the panel shows it as legacy. "Download now" makes an enabled
account due and wakes the fetch manager. While a download runs, the account cannot be deleted
(409). The password is stored with the existing encryption and read back internally after
each save; it is never returned.

**Groups** have a unique name and members chosen by account. Deleting a group removes its
memberships and the folder permissions granted to it. **Public folders** are changed in the
live tree that every IMAP session shares, exactly as the desktop tool does. Names are Unicode
(stored as modified UTF-7), must not contain the hierarchy delimiter, cannot be `INBOX`, and
are unique among siblings. New public folders are subscribed automatically. Deleting a folder
deletes its messages, and is refused while it has subfolders, so one confirmation never removes
a whole tree. **Permissions** go to one mailbox, one group or anyone (at most one of each per
holder), with the eleven IMAP rights `lookup`, `read`, `writeSeen`, `writeOthers`, `insert`,
`post`, `create`, `deleteMailbox`, `writeDeleted`, `expunge` and `administer`. An update
changes only the rights it names. Mailbox owners share their own folders from their mail
client (IMAP ACL); that is not part of this API.

**Backup settings** are the `backup` settings section: `includeSettings`, `includeDomains`,
`includeMessages` (requires domains) and `compress`, which are bits of one native setting
merged like the logging flags, and `destination`. The destination must be an existing absolute
local folder (no UNC share, `..`, reparse point or the Windows folder), because the service
writes archives holding every mailbox there. The passphrase is write-only and needs at least
12 characters; without one, new backups are not encrypted. `POST /backup/start` uses the
configured contents; progress and errors go to `hmailserver_backup.log`, which the Logs page
reads. `GET /backup` lists up to 100 `HMBackup *.7z` archives in the destination, newest first.

**Restore** takes an archive name from that listing, never a path. `confirmName` must repeat
it, and `settings`, `domains` and `messages` choose the parts (messages require domains, and
each part must be in the archive). `passphrase` opens an encrypted archive: a wrong
passphrase returns 422 and starts nothing. Restoring replaces the chosen parts and runs in the
background like a backup. Restoring settings from another server also needs that server's
installation key, imported first.

**Installation key.** `GET /secretkey` returns the key identifier. Export wraps the key under
a passphrase of at least 12 characters and returns the export text. Import takes such a text
and its passphrase, re-protects every stored secret under the imported key (as the desktop
tool does), and returns the new identifier. A wrong passphrase or damaged export returns 422
and keeps the current key; importing the installation's own key changes nothing. Exports and
imports are logged without the key or passphrase. Treat an export like the key itself.

Not migrated here: the desktop's per-mailbox folder editor (A08) and moving data-directory
contents (`DataDirectorySynchronizer`, a desktop tool covered by E10). The database setup and
upgrade tools stay desktop tools until E10 moves them to the management API.

Tests: `FetchFoldersMaintenanceTests` runs against the native dispatcher on every CI backend.
Fetch accounts:
- domain-administrator and role scoping;
- every validation rule;
- password non-disclosure and preservation on update;
- refusing "download now" while disabled;
- deletion.

Groups and folders:
- group names, duplicates and members;
- folder tree, Unicode names, delimiter refusal and subfolder delete protection;
- each permission holder type, duplicates and partial rights updates;
- a real IMAP `LIST` by a group member, which shows the folder until the membership is removed.

Backup and key:
- destination, content and passphrase validation;
- a real encrypted backup, polled to completion and listed;
- every restore refusal (name, confirmation, wrong passphrase, parts missing from the archive);
- key export and re-import.

A restore that actually replaces data is left to the existing NUnit backup/restore tests, so
the shared CI server is not overwritten. `LiveRestTests` checks the HTTP role boundaries and
`browser.py` the new panel controls.

## Spam allowlists, antivirus providers and DKIM (bundle 3a checkpoint)

**Spam allowlist.** Entries have `lowerAddress`/`upperAddress` (one IP family, lower first),
an `address` pattern (`*` for any sender, otherwise a pattern with `@`, without spaces, commas,
quotes or angle brackets) and a `description`. Several entries may share an IP range with
different patterns. Allowlisted senders skip every spam check, so mail forged with their
address does too; the panel says so. **Greylisting exceptions** are IP patterns of hex digits,
dots, colons, `*` and `?`; they skip greylisting only. Both use the typed policy collections
(`expected`-value updates and deletes, 409 on conflict) and refresh the server's caches.

**Existing antivirus providers.** ClamAV (host, port) is configured in the `antivirus` settings
section. ClamAV is installed separately, on this server or another host, and ArzoieMail never
ships it. `GET /antivirus` reports ClamAV, the custom scanner (program and expected return value)
and ClamWin (program and database folder). The custom scanner and ClamWin make the service
run a program, so their paths are read-only here. Allowing a web session to set them would let
it run any program as the service. E10 must give them a migration route before the desktop tool
goes. `POST /antivirus/test` scans an EICAR test file and a clean file with one configured
scanner; the request names only the scanner, never a host or path. A missing configuration
returns 412.

**SpamAssassin.** ArzoieMail does not build or ship SpamAssassin (maintainer decision,
2026-10-31). Administrators install it themselves, typically `spamd` on a Linux host,
container or WSL, and point the existing connection settings at it. `spamd` must allow this
server's address (`--allowed-ips`); the spamc protocol is unencrypted, so use a private network
or a tunnel. `POST /spamassassin/test` scans the GTUBE sample and now returns `connected`,
`gtubeDetected` and the `X-Spam-*` headers, so a reachable but misconfigured engine shows as a
failure.

**DKIM.** `GET /domains/{id}/dkim` returns:
- `signEnabled`, `signAliases`, `selector`, `headerCanonicalization` and `bodyCanonicalization`
  (1 simple, 2 relaxed), and `algorithm` (2 RSA-SHA256, 1 RSA-SHA1);
- `keyConfigured` and `keyManaged`;
- every generated key with its `dnsName`, its `dnsRecord` (`v=DKIM1; k=rsa; p=...`) and whether
  it is `active`.

Keys are RSA 2048, generated by the service. Larger keys are refused for now: generating them can outlast a management request, and the management pipe serves one request at a time. A later change can add them with background generation. 2048 is the size RFC 8301 and large receivers expect, and its record fits in one DNS string. They are stored as
`DKIM\<domain>\<selector>.pem` beside the data directory (not inside it, so data-directory
tools never mistake the folder for a domain), in a folder readable only by SYSTEM, administrators
and the service. Requests name a key by selector (a DNS label), never by path, and the private
key is never returned.

Rotation:
1. Generate a new key.
2. Publish its record.
3. Activate it. The API looks the record up with the server's DNS configuration and refuses
   (409) until the published `p=` matches; `skipDnsCheck` overrides that explicitly.
4. Delete the old key once mail signed with it has been delivered. An active key cannot be
   deleted.

Signing cannot be enabled without a readable active key (409). A key file set by the legacy
tools keeps working and shows as not managed.

Tests: `SpamDkimTests` runs against the native dispatcher on every CI backend. It covers:
- **Roles:** every new operation is refused for domain administrators and mailbox users where
  required.
- **Allowlist and greylisting exceptions:** pattern and range validation, shared ranges,
  conflicts and cleanup.
- **Antivirus:** provider status and test refusals.
- **SpamAssassin:** the result shape.
- **DKIM:**
  - selector and size validation;
  - private key non-disclosure;
  - activation, signing and protection of the active key from deletion;
  - **conformance:** a message the server signs, read back over POP3 and verified by an
    independent RFC 6376 check in the test, while a tampered copy fails.

`LiveRestTests` checks roles and that activation is refused until the DNS check is skipped
explicitly; `browser.py` covers the new controls.

## Native Bayesian spam filter (bundle 3b checkpoint)

The built-in spam filtering is native (maintainer decision, 2026-10-31): a Bayesian filter in
the server, with no separate engine to install. SpamAssassin stays an optional integration.
The filter is off until an administrator enables it (maintainer decision, 2026-10-31), in new
and upgraded installations alike; the administrator also chooses its score, threshold,
minimum training and whether it learns from Junk folders.

**Settings** are five fields of the antispam section (`GET`/`PATCH /antispam`):

| Field | Default | Meaning |
|---|---|---|
| `BayesEnabled` | `false` | Run the filter on incoming mail and learn from Junk copies; nothing is scored or learned while it is off |
| `BayesScore` | 5 | Spam score added when a message reaches the threshold (0-10000) |
| `BayesThreshold` | 90 | Spam probability, in percent (50-100), at or above which the score is added |
| `BayesMinimumMessages` | 200 | The filter scores nothing until it has learned at least this many spam and this many ham messages (1-100000) |
| `BayesLearnFromJunk` | `true` | Learn from users' IMAP copies into and out of Junk folders, while the filter is enabled |

**Scoring.** The filter is a post-transmission spam test, so it runs where the IP range has
spam protection, after the cheaper tests, and adds its score to theirs. It tokenizes the subject
(as separate tokens), the plain and HTML body with markup removed (first 200 KB), link host
names and the sender's domain, and keeps at most 2000 distinct tokens. Token probabilities use
Robinson's method and are combined with Fisher's chi-square method, as SpamBayes and
SpamAssassin's Bayes plugin do. The reason header names the probability, for example
`Bayesian filter: spam probability 99%`.

**Training.**
- A user's IMAP COPY into a folder with the `\Junk` special use trains the copies as spam.
  A COPY out of a Junk folder trains them as ham, unless it goes to a `\Trash` folder. Copies
  are queued only once the whole COPY has succeeded. Public folders never train.
- `POST /bayes/train` queues every message of one mailbox folder, for example a Junk folder as
  spam and an Inbox as ham, to start a new filter. The folder is a path with the server's
  hierarchy delimiter. Unknown mailboxes and folders return 404. It works while the filter is
  off, so an administrator can train it before enabling it.
- One background task at a time trains from a queue of at most 10,000 messages; further
  messages are counted as `dropped`. `GET /bayes` reports `spamMessages`, `hamMessages`,
  `tokens`, `minimumMessages`, `ready`, `pending`, `training`, and the `trained`, `failed` and
  `dropped` counts since the service started.
- `POST /bayes/reset` with `{"confirm": true}` clears the queue and the token table.

**Storage.** `hm_bayes_tokens` (schema 5715) holds a 64-bit hash of each token with its spam and
ham counts; the row with hash 0 holds the message totals. No token text or message content is
stored. The hashes are not a secret: someone who can read the database can test whether a
given word has appeared in trained mail.

**Limits.**
- One model for the whole server, trained from every mailbox; there are no per-user models.
- No untraining: a message learned twice, or learned wrongly, stays counted until a reset.
  Moving a message into Junk and back out trains it once as spam and once as ham.
- Tokens are never expired, so the table grows with the vocabulary of trained mail.
- Only IMAP COPY trains. ArzoieMail has no IMAP MOVE yet (V6-03B); clients fall back to COPY.

**Content rules** are the existing global and mailbox rules (#42): criteria on headers and
body text with actions such as moving to a folder, setting a header or deleting. The filter
adds no separate rule engine.

**Scoring guidance.** Every test adds its score to one total, compared with the mark and delete
thresholds (defaults 5 and 20 in new installations):
- Score each signal once. A DNSBL and SURBL listing, an SPF or DMARC failure, SpamAssassin and
  the Bayesian filter can all react to the same spam run; if one score alone reaches the mark
  threshold, the others only matter for the delete threshold.
- Keep the Bayesian score at or below the mark threshold until it has been trained on mail
  representative of the server, and check its reason headers before raising it.
- When SpamAssassin runs its own Bayes, keep only one of the two Bayesian filters scoring, or
  lower both scores.
- Use the delete threshold only for combinations that are certain, such as a DNSBL listing
  together with a high Bayesian probability.

Tests: `BayesTests` runs against the native dispatcher and the real mail service on every CI
backend:
- roles and argument refusals for every operation, and the setting ranges;
- training spam by an IMAP COPY into a `\Junk` folder and ham from a folder, with a COPY to
  an ordinary folder training nothing;
- scoring over SMTP: a message like the trained spam carries the Bayesian reason header and is
  marked as spam, and one like the ham is not;
- a COPY from Junk to Trash training nothing and from Junk to another folder training ham;
- reset.

The tokenizer, hash, probability and copy rules are also checked by `BayesFilterTests` in the
portable GCC/Clang build under the sanitizers and in ClassTester. `LiveRestTests` covers the
routes and roles; `browser.py` covers the panel's training card and confirmed reset.

## Listener certificates, ACME and the installed panel (bundle 4 checkpoint)

Maintainer decisions, 2026-11-01: keys in a protected PEM folder (CNG machine keys deferred),
an ACME client of our own rather than a library, HTTP-01 answered by the management API, and
certificates and hosting in one checkpoint.

**Certificates.** The mail listeners' certificates are managed by the service:

- **Import** PEM text (`certificatePem` with its chain, `privateKeyPem`) or a PFX file (`pfx`
  base64, `password`), which the API opens with the PFX-inspection limits and passes on as PEM.
  Uploads are at most 1 MiB, PFX files 512 KiB.
- **Checks before anything is stored:** the key matches and is RSA 2048-8192 or EC P-256 or
  larger, and is not encrypted; the leaf is not a CA, is valid now (or within a day) and not
  expired; each further certificate issued the one before it. Supplied roots are kept as chain,
  never trusted. Refusals return 422 with a combined explanation; a duplicate name returns 409.
- **Storage:** PEM files under `Certificates` beside the data directory, in a folder only
  SYSTEM, administrators and the service can read, as the DKIM keys are. Requests never name a
  path and no response contains a path or key. Certificates registered with file paths by the
  legacy tools keep working and show as `managed: false`.
- **Replace** keeps the current generation for one **rollback**; an older generation is
  deleted. **Delete** is refused (409) while a listener uses the certificate.
- **Views** (`GET /certificates`) give subject, issuer, DNS names, validity and days left,
  SHA-256 fingerprint, key type, the listeners using it, whether a previous generation exists,
  and ACME state (host names, automatic renewal, last attempt and error).

**Listeners.** `POST /listeners`, `PATCH /listeners/{id}` and `DELETE /listeners/{id}` set the
protocol (`protocolCode` 1 SMTP, 3 POP3, 5 IMAP), address, port, `securityCode` (0 none,
1 implicit TLS, 2 STARTTLS optional, 3 STARTTLS required) and `certificateId`. A TLS listener
needs a certificate the listener can actually load (409 otherwise); two listeners cannot share
an address and port (409). Changes take effect when the mail services restart, which rebuilds
every listener's TLS context; responses say `restartRequired`, and the panel offers the restart.
`POST /listeners/{id}/probe` connects as a client (with STARTTLS where configured) and reports
the certificate actually served and whether it is the configured one.

**ACME (Let's Encrypt and others).** RFC 8555, with HTTP-01:

- `GET`/`PATCH /acme`: the directory URL (https; empty clears it), a contact address and
  acceptance of the certificate authority's terms. A new directory starts a new account. `GET`
  also returns `httpChallenge`: the challenge port, whether its listener is running, and why
  not. Let's Encrypt no longer sends expiry mail to the contact address.
- The **account key** (EC P-256) and every **certificate key** are generated by the mail
  service and never leave it. The API sends the service each JWS signing input to sign
  (`acme.sign`), and gets a CSR for each order (`certificates.acme.prepare`); the issued chain
  is installed only if it uses the order's key and names every host (`certificates.acme.complete`).
  The CSR's common name is the first host name of at most 64 characters, or none.
- A certificate has **one order at a time**: preparing another while one is in progress returns
  409 (an order abandoned for ten minutes no longer counts), and the API drops an order it could
  not complete (`certificates.acme.discard`, `acme.renewals.discard`).
- Host names are lower-case DNS names, up to 10, no wildcards or IP addresses (HTTP-01 cannot
  prove them).
- `POST /certificates/acme` (`name`, `hostnames`, `autoRenew`) and
  `POST /certificates/{id}/renew` start a background job (202); poll
  `GET /certificates/acme/jobs/{id}` for `running`, `succeeded` (with the certificate) or
  `failed` (with the certificate authority's reason). One job runs at a time. Both return 409
  with the reason when the challenge listener is not running.
- **HTTP-01** is answered on a separate listener, `Acme:HttpPort` (setup chooses 80 when it is
  free), which serves `/.well-known/acme-challenge/{token}` for pending challenges and nothing
  else. The host names must resolve to this server and port 80 must be reachable. When the port
  is taken the panel still starts, logs a warning, and refuses issuance and renewal until the
  service restarts with the port free.
- **Renewal** runs in the API every `Acme:RenewalCheckMinutes` (default 360) without an
  administrator. The service lists certificates set to renew that expire within 30 days; after a
  failure a certificate waits 12 hours. A renewed certificate in use restarts the mail services
  so the listeners load it. Renewal uses a narrow automation scope (below).

**Renewal automation scope.** A request with `"automation": "certificate-renewal"` and no
credentials may only list due renewals, read the account, sign, prepare and complete a renewal
of a certificate an administrator issued with automatic renewal (for the host names recorded
then), discard its own failed order, and record a failure. The pipe accepts only the configured API identity, so this does not
widen who can reach the service; it means a compromised API process could renew those
certificates and sign ACME requests with the account key, but could not import a certificate,
change a listener or read any key. Every other operation in the scope returns 403.

**The installed panel (E07).** Setup's new *Web administration panel* component (part of the
full installation):

- installs the panel into `WebAdmin`, and the ASP.NET Core Runtime 10 when it is missing
  (bundled; SHA-512 pinned in `libraries\fetch-dotnet-runtime.ps1`);
- runs `WebAdmin\Setup\Install-WebAdmin.ps1`, which creates the **ArzoieMailAdmin** service
  running as its own virtual account `NT SERVICE\ArzoieMailAdmin` (no mail-store, database or
  administrator access), writes its SID as `[Management] ApiClientSID` and restarts the mail
  service, and registers the event log source the panel writes to;
- on a first installation creates the panel's own self-signed HTTPS certificate in the machine
  store, readable by that account only, and writes `appsettings.Production.json`: HTTPS on port
  8443 for this computer's name and `localhost`, and `Acme:HttpPort` 80 when the port is free;
- keeps `appsettings.Production.json` on upgrade; uninstall removes the service, the pipe
  access and the panel's own certificate (`Uninstall-WebAdmin.ps1`).

No firewall port is opened. To reach the panel from elsewhere, open 8443 (and 80 for ACME), add
the name used to `AllowedHosts`, and replace the self-signed certificate with a trusted one in
`Kestrel:Endpoints:Https:Certificate`. The panel's certificate is separate from the mail
listeners' and the panel never reads their keys.

**Limits.**
- Keys are protected by the folder's permissions, not by Windows machine-key storage (CNG);
  that stays a possible later change.
- No DNS-01 (so no wildcard certificates) and no TLS-ALPN-01.
- The panel's own HTTPS certificate is not issued by ACME yet.
- Certificate authorities that need external account binding (ZeroSSL, Google Trust Services)
  are not supported. Renewal uses a fixed 30-day window, not ACME Renewal Information.
- Deleting a certificate does not revoke it, and the account key cannot be changed.
- Applying a certificate restarts all mail services, which disconnects connected clients.
- Backups keep the certificate registrations but not the files in the Certificates folder, so
  a restore onto another computer needs each listener's certificate imported again.

Tests:
- `CertificateTests` (live native, every backend): import refusals, a real TLS listener on a
  free port serving each generation after restarts (import, replacement, rollback), ACME orders
  completed by a test certificate authority in the test, renewal automation and its refusals,
  and the scope's 403s.
- `AcmeLiveTests` (CI, every backend): the whole flow against Pebble, Let's Encrypt's test
  server, with its DNS server resolving the test name to the runner: terms refusal, account
  registration, HTTP-01 through the API's challenge port, issuance, unattended renewal and
  *Renew now*.
- `LiveRestTests`: PEM and PFX import over real HTTP, refusals, rename, delete, probe refusal
  and role boundaries. `browser.py`: the Certificates page, import, ACME settings and issuance,
  listener changes with confirmation, the served-certificate check and the restart.
- `build\test-installed-webadmin.ps1` (CI, every backend): the installed service and its
  virtual account, its pipe identity, its own certificate and key access, a real sign-in
  through the installed panel, HTTPS-only service and a challenge-only ACME port; then removal
  and set-up again with the scripts setup uses.

## Mail transport inventory (completion branch, validation in progress)

`GET /api/v1/transport/listeners` and `GET /api/v1/transport/certificates` are server-only
in REST and native code. Native commands are `transport.listeners.list` and
`transport.certificates.list`; other transport commands are not exposed. These are read-only
configuration records, not a live socket census or certificate-health check.

Both return `items`, `total`, `offset`, `limit`, `maximumLimit:25` and `source:"configured"`.
Filters are `search` (case-insensitive substring, maximum 255 UTF-8 bytes), `offset`
(0-2147483647) and `limit` (1-25, default 25). Unknown filters and repeated REST query filters are rejected.
Listener search covers protocol, address and port; certificate search covers name and ID text.
Responses are bounded, but metadata is currently scanned in memory; DB-side paging/capacity
acceptance remains open. Configured state can differ from running contexts until restart.

Listener fields are `id`, `protocol`, `protocolCode`, `address`, `port`, `security`,
`securityCode` and `certificateId`. IDs are exact decimal strings; an unassigned certificate
is null. Known protocol codes are SMTP=1, POP3=3 and IMAP=5; security codes are plaintext=0,
implicit TLS=1, optional STARTTLS=2 and required STARTTLS=3. Unknown legacy codes remain
visible as `Unknown` with their numeric code so they are not silently reinterpreted.
Certificate fields are only `id` and `name`. Queries do not select private-key/certificate
paths and do not open certificate files. Registrations do not establish existence, expiry,
chain/hostname/key validity, access permissions or renewal readiness.

The panel renders registration names as inert text, supports search/paging and distinguishes
saved listeners from running state. Guided import/ACME, listener edits/assignment, validation,
activation/restart and rollback remain A06/E03 work. Native/managed builds and the full Compact
VM run passed (831 native,449 HTTP/security,145 real HTTP and both browsers), followed by
four-backend CI at `8cdc085b4`.

### PFX inspection (completion branch, VM validated)

`POST /api/v1/transport/certificates/inspect` accepts exactly `pfx` (base64 PFX bytes),
`password` (string, empty only for an unprotected file) and `hosts` (1-32 DNS names/IPs).
Server administration, fresh native session validation, CSRF and same-origin rules apply.
No server paths, filenames, arbitrary storage/provider settings or caller-supplied clocks
are accepted. This action inspects a browser upload; it does not install, register, assign,
renew or activate anything, and does not transfer uploaded key material to native commands.

This route alone accepts up to 1 MiB of JSON; decoded PFX is capped at 512 KiB. Passwords
are capped at 1024 UTF-8 bytes with no NUL. Other management routes keep their 16 KiB body
limit. There are at most two inspections per process and 30 route requests per minute per
remote IP; 429 supplies retry guidance. Body transfer has a trusted-clock 30-second deadline
(408). This is not a hard deadline for every OS/cryptographic operation. KDF/count limits are
separate: MAC and individual KDF iterations 300000, total KDF 1000000, one private key,
16 certificates and 128 DNS/IP alternative names. Unsafe loader limits return 422 with
re-export guidance. Malformed JSON returns 400; invalid/duplicate/extra fields return 422.

The .NET 10 loader uses ephemeral key storage and ignores storage-provider/key-name/alias
attributes. Owned UTF-8 wire, decoded PFX and password-character buffers are cleared on
completion/error/cancellation, including trailing JSON. Certificate/key handles are disposed.
This does not claim every browser/runtime/OS internal copy is erased. The panel clears its
password field and byte buffer, renders public output as inert text, and never displays PFX
or private-key bytes. The protected browser PFX fixture stays outside CI evidence and is deleted.

Results include SHA-256 identity, bounded subject/issuer display, UTC validity, key algorithm/
size/minimum-strength flag, CA/EKU/key-usage metadata, SAN names, per-host matches, separate
chain status and warnings. DNS/IDNA and canonical IP inputs are normalized; hostname matching
uses SAN with common-name fallback disabled. Chain trust uses this web host's Windows identity,
disables certificate downloads and explicitly reports revocation `NotChecked`; it does not
establish trust for remote clients or the native service. RSA/ECDSA fixtures passed; DSA/provider
and production IIS compatibility still require acceptance. There is no overall activation approval.

The bounded helper passed 60 focused VM assertions. The endpoint/form then passed the full
native/API/browser/cleanup VM run (513 HTTP/security,60 helper,831 native,145 real HTTP and
both browsers). The final rate-budget/harness run passed 546 HTTP/security,60 parser and
the same native/live HTTP/browser layers with checkpoint cleanup. Four-backend CI passed at
`ae903a182` with 1685 separate assertions and both browser suites; exact NUnit outcomes are
recorded in the CI inventory. No import/activation acceptance is implied.

## Filtering and trusted-relay collections (completion branch)

`/api/v1/policies/{resource}` supports GET/list and POST/create; `/{id}` supports GET,
PATCH and DELETE. Resources are `surbl`, `blockedattachments`, `incomingrelays`, `securityranges`, all server-only
in both API and native code. Lists return bounded `items`, total/offset/limit, explicit typed
`schema`, `defaults` and `idMaximum`. Current legacy adapters accept positive IDs through
2147483647 and encode policy IDs as decimal strings. Larger stored IDs fail visibly rather
than narrowing into another object. High-ID persistence modernization remains pending.

SURBL fields: zone, active, score and rejectMessage. New providers default disabled. Attachment
fields: pattern and description; patterns are filenames rather than paths. Relay fields: name,
lowerAddress and upperAddress; endpoints must be ordered within the same IP family. Relay trust
affects upstream sender attribution: use exact intended ranges. Text rejects control characters;
zones, lengths, types and bounds are checked before saving. Unknown fields return 422, duplicate
names 409. PATCH requires the loaded full object in `expected` alongside changed fields. DELETE
requires `{ "expected": { ...loaded object... } }`. Stale snapshots return 409. Writes read back
persisted state and refresh native caches. Cross-COM transactional concurrency/failure recovery
and complete workload acceptance remain pending. These collections passed initial Compact VM
native and browser validation; final four-backend CI remains outstanding.

IP security ranges add name, lower/upperAddress, priority, SMTP/IMAP/POP3 permissions, all four
sender/recipient delivery classes and their SMTP authentication requirements, TLS-for-auth,
spam/virus applicability, expires and expiresAt. Timestamps are exact server-local
`YYYY-MM-DD HH:MM:SS`; enabled expiry must be in the future. Native expiry cleanup is asynchronous;
existing protocol connections are not terminated by an edit. New policies deny connections and
relay until deliberately configured. Higher numeric priorities win; avoid equal-priority overlap.
POST `/policies/securityranges/match` with `{ "address": "..." }` inspects the native selected
policy without opening a network connection. Management remains independent of mail-protocol
range permissions, allowing an authenticated administrator to repair accidental mail lockout.

The transient boolean `acknowledgeUnauthenticatedRelay` must explicitly be true on any write
whose resulting range permits local-to-external or external-to-external delivery without its
corresponding authentication requirement. Use only exact trusted source ranges. The panel warns
about open-relay risk and confirms changes. The acknowledgement is never persisted and always
reads false, so a later unsafe-range edit requires a fresh decision. This preserves intentional
trusted-relay configurations while preventing an ordinary edit from silently opening relay.
The IP policy addition passed 32 targeted native assertions and the later full Compact VM run,
including real SMTP denial/recovery/authentication. Four-backend CI passed at `bc3980201`;
broader acceptance remains pending.

## Mailbox self-service (completion branch, under validation)

Authenticated mailbox users, domain administrators and mailbox-backed server administrators
can access `/api/v1/self`. The synthetic installation Administrator has no mailbox and receives404.
The response contains address, directoryBacked and passwordChangeable, never password material.
GET/PATCH `/self/profile`, `/self/signature` and `/self/behavior` use the existing bounded field
contracts for those areas. Their native parent identity comes exclusively from authentication;
browser-supplied account/domain IDs are rejected. Privileged account fields are unavailable.
Self-service persistence updates only supplied permitted columns, guarding current role, active
state, directory flag, address and password. It does not save the entire loaded account object.
Concurrent changes can return 409; reload/reconcile before retrying.

`/self/rules` offers bounded list/create and `/{id}` read/update/delete with ownership checked
in native code. New rules are disabled drafts; existing condition/action restrictions remain.
Another mailbox's rule ID does not grant access. This is not access to global rules or routing.

POST `/self/password` requires `{ "currentPassword": "...", "newPassword": "..." }`.
Both are nonempty and bounded to 1024 UTF-8 bytes natively. Directory-backed changes return 409;
change those passwords through the directory. Native code separately proves the current password,
reloads after possible legacy-hash upgrade, verifies the fresh hash, and performs a guarded update
of password/hash-format columns only. Wrong current passwords return 403; invalid fields 422.
Success removes the calling browser's vault entry/cookie and returns an anonymous CSRF token.
Other old-password sessions fail their next native reauthentication. The endpoint uses login
throttling and existing CSRF/origin/HTTPS rules. The panel requires matching confirmation and
clears input controls after success; plaintext passwords are never returned.

Native `authenticate` now includes a required 64-hex `securityStamp`. The API stores this opaque
binding in its server-side vault entry and checks it after each native reauthentication. It is
not returned in browser identity JSON or stored in the cookie, which retains only the vault ID.
The native binding uses HMAC-SHA256 over persisted credentials/identity/directory binding under
a random process-local key, including the freshly persisted hash after automatic hash upgrade.
Resetting even to the same password creates a new salted hash and invalidates old bindings.
Native or web process restart and stored-hash upgrades also require another sign-in. Native/API
builds must be upgraded together; a missing or malformed binding fails closed with 503.
AD password reuse and unobserved disable/re-enable cycles still need lifecycle acceptance;
in-flight requests are not retrospectively cancelled. The fix passed the real same-password-reset
regression and full Compact VM run (739 native, 145 real HTTP/native assertions).

Vault/cookie timing uses injected TimeProvider with System as the production default, without an
HTTP clock override. The 43 controlled-clock VM assertions cover idle/absolute vault expiry,
sliding cookie renewal/expiry, refreshed roles, opaque-only renewed cookies and binding failures.
Affected real HTTP/TLS/browser checks and four-backend CI at `bc3980201` also passed;
broader acceptance remains pending.

Self-service native, HTTP and fixture/published browser regressions are under VM validation.
There is no full Phase 1b or production security acceptance claim. Self-service reply-history clearing follows successful guarded save/readback. Broader legacy
concurrency and database failure recovery remain acceptance obligations.
