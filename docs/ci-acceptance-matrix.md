# CI acceptance matrix for the modernized stack

Requested by the maintainer on **2026-10-28**. Every installed-server backend job must exercise
the current C++ service, .NET 10 tools, native management boundary, REST application and both
browser test layers. The four jobs use Compact, MySQL, PostgreSQL and SQL Server 2025 Express.
Linux portable-core and static-analysis jobs retain their separate purpose.

[CI inventory](ci-test-inventory.md) owns exact verified revisions/counts; this matrix owns
coverage obligations and gaps. [Capability status](phase-1b-status.md) distinguishes delivered
features from requirements. A planned feature must not receive a fabricated passing CI test.

## Required checks for the current implementation

"New" below means added in the current #42 CI expansion; final-head acceptance must be verified
in that PR. Tests execute only in disposable VM/CI. Host compilation/static inspection is allowed.
The exact tracked driver passed all expanded layers in the VM (897 assertions plus live browser
and selected product-identity regression). Earlier four-backend CI at the version-6 baseline
passed 1317 NUnit cases and 734 management assertions. The new final head needs its own CI.

| ID | Technology / boundary | Coverage and evidence | Execution |
|---|---|---|---|
| C01 | .NET 10 LTS toolchain | Root global.json selects stable SDK 10 with latest compatible feature band; check actual SDK, runtime major, x64 Windows, API target, ASP.NET Core 10 loaded and version-6 metadata | New runtime mode and SDK check on every backend |
| C02 | Replacement API architecture | Direct assembly-reference guard rejects legacy COM interop and database-driver references; source/CodeQL review and native permission tests remain necessary for indirect/dynamic behavior | New runtime mode plus existing analysis/native suites |
| C03 | Publishable web application | dotnet publish, runtime/dependency manifests, required JS/CSS assets and actual published application startup; browser tests run that output | New publish and live browser stages on every backend |
| C04 | HTTP/session security contracts | Real HTTP host with controlled channel: cookies, session refresh/revocation, CSRF, origins, allowed fields, pagination, bounds, rate limits, headers, CRUD route/parent mapping and error handling | Existing deterministic suite on every backend |
| C05 | Actual HTTPS and secure cookies | In-memory private test CA, Windows-backed fixture server key, validated hostname/chain, HSTS, Secure/HttpOnly/SameSite Strict cookies and authenticated HTTPS request; reject untrusted chain and wrong hostname | New production TLS suite on every backend |
| C06 | Production hosting policy | Refuse missing/wildcard AllowedHosts, reject foreign HTTP Host on valid HTTPS and reject plaintext even with forged X-Forwarded-Proto | New TLS checks and expanded existing plaintext boundary checks |
| C07 | Pipe trust boundary | SCM/server-PID verification, impostor rejection with zero credential bytes; invalid frame/malformed JSON/authorization checks | Existing two-assertion squatting mode newly automated before pipe opt-in; native framing suite on every backend |
| C08 | Real REST/native/database integration | Real Kestrel, cookies, authentication, native channel and selected backend; domain/mailbox/profile/signature/alias/list/member/rule/route/recipient persistence and validation, stored-mail queue exclusion and request-size limits | New live HTTP mode on every backend, without a fake management channel |
| C09 | Real identity and session lifecycle | Domain/admin/user restrictions, cross-domain denial, protected-account denial, native account disable revokes HTTP session, old cookies cannot resume on a fresh host/vault | New live HTTP mode; controlled-clock idle/absolute expiry checks remain a gap below |
| C10 | HTTPS through the native service | Repeat production TLS/cookie/host/chain checks with actual native authentication and version-6 service status | New live TLS/native mode inside the opted-in fixture on every backend |
| C11 | Browser interaction edge cases | Fixture-backed workflows, confirmations/cancellation, inert HTML/source, password omission, queue precision/busy controls, rule order/activation, role restrictions, session recovery and mobile layout | Existing browser suite retained on every backend |
| C12 | Published browser end to end | No API interception: real login, service version, domain/mailbox/profile/rule writes, persistence after reload/readback, native role restrictions, logout, cascading cleanup and mobile layout | New browser_live.py against the published API on every backend |
| C13 | Native management behavior | Parent/role/revocation/parser/persistence boundaries; domain/account/alias/list/forward/reply/signature/policy/name/queue/route/rule workflows; local SMTP/POP3 effects and controlled busy queue | Existing 402-assertion live-native suite on every backend |
| C14 | Mail protocols and data/security | SMTP/IMAP/POP3, SQL binding, TLS, password hashing/UTF-8, crypto/key/backup, permissions and product/install identity; expected explicit/inconclusive outcomes reported separately | Installed-server NUnit suite on every backend; currently 1317 cases at the verified version-6 baseline |
| C15 | SQL Server Express | OLE DB 19, native SQL-session encryption, certificate rejection/trust behavior and DB migration fixture | Express-specific setup/tests; equivalent shared behavior on all backends |
| C16 | Product 6 and compatibility | Service/PE version agreement, product name, retained service name, new/legacy paths and installer/native-tool compilation; schema 5714 stays independent | Version-6 product regression and existing installation/identity checks |
| C17 | Documentation as project memory | Local links, maintained PHP/desktop inventories and capability ownership; SDK/runtime evidence and results distinguish snapshots from unverified work | Static documentation checker newly run in every backend job |
| C18 | Native portability and static security | GCC/Clang sanitizer/CTest, CodeQL for enabled languages, workflow actionlint/zizmor when workflow files change; NuGet restore auditing remains enabled | Existing separate CI jobs, with the expanded test code included |
| C19 | Test evidence and diagnosis | SDK/runtime details, publish manifests, live native/REST/TLS logs, fixture/live browser screenshots and host logs; unique backend artifact names, always uploaded when available | New management-test-evidence artifacts; no service INI, credentials, private keys, HAR or authentication traces |

## Fixture boundaries and secret handling

- Native and REST integration use temporary test identities and owned objects; COM is used only
  by setup/cleanup. New-panel requests never use COM or direct SQL. Production SQL credentials
  and mail data are not inputs to these suites.
- Live HTTP integration runs on loopback Development hosting; production HTTPS behavior is
  checked separately and also repeated with the real native channel. Published browser smoke
  runs on loopback Development hosting. Neither establishes IIS provisioning or certificate
  issuance/renewal. Those are separate acceptance gates.
- HTTPS tests use a short-lived generated CA trusted only by their client chain policy. The
  server test key is imported into a Windows user key context for Schannel and disposed; no
  CA is added to a machine/user trust store. The normal client must reject the untrusted root,
  and a trusted-root client must still reject a wrong hostname. Never replace this with an
  always-true certificate callback.
- Session restart coverage creates a fresh host/vault/key provider; it is not an IIS recycle or
  machine-reboot deployment test. Fixture browsers cover more UI edge cases than the live
  browser smoke; both layers must run and remain distinguished.
- Generated fixture passwords are GitHub-masked before use; authentication uses environment
  variables and JSON bodies rather than process arguments. Failure screenshots clear password
  inputs. Do not capture HAR/traces, request bodies, service configuration or test private keys
  in artifacts. Logs must retain useful stage/status failures without credentials.
- Tests clean their own data, restore service configuration/environment, stop child hosts and
  propagate failures. VM checkpoint restoration adds isolation; CI runner disposal is the final
  safeguard, not a substitute for cleanup.

## Required later coverage and acceptance gaps

These are explicit obligations, not currently passing automated coverage. Add implementation,
fixtures and failure-path tests together when the owning feature is delivered.

| Owner / area | New tests required before acceptance | Appropriate execution |
|---|---|---|
| Session timing | Deterministic inactivity/absolute expiry and boundary tests using an injectable clock; preserve reauthentication/restart/revocation checks | Per-backend management CI after adding a safe test-time clock seam |
| E07 restricted hosting | Dedicated non-admin service/IIS identity, no DB/mail-store/DCOM grant, allowed/denied SID access, service-PID replacement, IIS recycle/reboot, HTTPS/AllowedHosts/runtime provisioning and permission repair | Disposable Windows install/upgrade/security matrix; not proven by administrator-run fixtures |
| E07 installer delivery | Package/deploy the actual API, offline ASP.NET runtime, install/update/uninstall, service/pool identity cleanup, old installation migration and rollback | Installer VM tests on supported Windows/DB combinations |
| E03 certificate wizard | PFX/PEM/chain/password import, wrong key/name/chain/expired rejection, private-key ACLs, real listener certificates, ACME challenge/issuance, renewal failure/recovery/rollback, separate IIS/mail bindings | Deterministic VM fixtures plus controlled external-CA acceptance; never public-CA requests in every PR |
| E01 full DNS/key setup | DKIM generation/selection/rotation/key protection, multi-provider SPF changes and lookup limits, actual signing/verification/alignment, malformed DNS/timeouts and cautious DMARC policy transitions | Per-backend deterministic integration tests; controlled live DNS supplements |
| E02 SpamAssassin operations | Engine package/version/licensing/update, offline install, service start/stop/recovery, protocol timeouts/score handling, local fixture messages and double-scoring guidance | Functional CI after engine delivery; measured load in VM |
| E04-E06 Defender | Clean/EICAR/decoded MIME/archive/encrypted/unsupported content, permissions/signatures/health, timeout/engine outage, safe staging/cleanup, each receive/send/fetch/APPEND/draft/import path, changed-content re-scan and mitigation | Disposable malware-test VM; deterministic CI subset only after a safe adapter exists |
| E05-E06 durable pressure | At least 10000 queued/in-flight mixed messages, fairness/backpressure/verdict gates, crash/restart/outage/soak, retry exhaustion/quarantine consistency, no duplicate release, measured throughput/p95/p99/resource headroom | Dedicated opt-in VM capacity lane with defined hardware/message mix and numeric targets |
| A01-A20 parity | Every newly exposed field/action needs role/parent/validation/persistence/cache/failure/concurrency tests and browser acceptance; legacy restrictions need approved migration decisions | Per-backend CI as capabilities land; destructive maintenance/restore in isolated VM |
| E08 PHP retirement | Upgrade disables old URL, source/assets/packaging/runtime removal, supported user-role migration, no active fallback and unaffected independent COM consumers/unrelated PHP apps | New-install/upgrade VM lane before cutover |
| E09 publication/docs | Actual screenshots, inherited recipe review, front matter/HTML rendering, navigation/routes/images/redirects and deployed-site checks | Static/rendering CI once website build/hosting is established; current checker covers local engineering links/inventories |
| Queue/database capacity | Indexed paging behavior, representative large queues/collections, concurrency/reservation races, restart and measured resource/latency envelope | Deterministic regression plus dedicated capacity VM; current paging bounds responses only |
| Signing/release path | SignPath/manual release credentials and policy, binary/installer signature verification, exact version/artifact/tag/title and draft review | Explicitly authorized master-only manual release lane; not executed merely to test release wording |
| Later phases | PowerShell process isolation/events, UID allocation/generation/crash/client interoperability, SQLite migration, protocol extensions and future features | Separate roadmap authorization and implementation; no fabricated Phase 1b completion |

The dedicated capacity/security/release lanes must record exact build, OS, engine, hardware,
message mix, limits and outcomes. Ordinary green PR CI must not be presented as proof of those
unimplemented or manual scenarios. Deferred CodeQL observations remain governed by the
maintainer's modernization-end audit decision.
