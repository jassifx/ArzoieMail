# ArzoieMail management API and panel (Phase 1b, in progress)

The replacement panel is an ASP.NET Core / .NET 10 application. It communicates with the native
server over a restricted local Windows named pipe, never through COM or direct SQL. The old
PHP application and COM API remain available as compatibility surfaces during migration.

Read [the requirements](../../../docs/webadmin-requirements.md) and
[architecture/security boundaries](../../../docs/management-api-architecture.md).
The foundation was merged in PR #31; SQL Express connectivity in PR #33 is a separate native
database improvement. See the [Phase 1b tracker](../../../ROADMAP.md#phase-1b-delivery-tracker)
and [documentation status](../../../docs/documentation-status.md) for remaining administration,
certificate, Defender, hosting and PHP-retirement work. Do not present the preview as full parity.

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
