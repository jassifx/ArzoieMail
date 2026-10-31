# CI test inventory

The [documentation index](README.md) defines evidence ownership. Capability/coverage gaps
are tracked in the [Phase 1b ledger](phase-1b-status.md). Test counts do not imply full parity,
production deployment, capacity, or that skipped/manual scenarios executed.

## Bundle 3b checkpoint: native Bayesian spam filter

Branch `feature/phase1b-bayes` (E02 default, A11 guidance; see the
[ledger](phase-1b-status.md#bundle-3b-checkpoint-native-bayesian-spam-filter-2026-10-31)).
Head `d823a33b9817dd7f827d07a461a3d070b98c7cae` passed all four database jobs, Release x64,
GCC/Clang (with `BayesFilterTests` under the sanitizers) and CI installer publication. Per backend:

| Check | Compact, MySQL, PostgreSQL | SQL Server Express |
|---|---|---|
| NUnit regression | 1319 cases: 1209 passed, 0 failed, 34 inconclusive, 76 skipped | 1319 cases: 1210 passed, 0 failed, 34 inconclusive, 75 skipped |
| Live native management | 1114 assertions (1086 before; +28 from `BayesTests`) | same |
| Live HTTP/native integration | 238 assertions (224 before; +14 REST checks) | same |
| Other management checks | unchanged from bundle 3a | same |
| Browser | fixture suite (new controls included) and published live suite | same |

That is 2059 numbered assertions per backend plus the unnumbered plaintext-boundary line.
The NUnit outcome is unchanged, so the filter (off by default) does not affect the existing suite.

Failure fixed on the way: the first run (`cad407087`) failed on every backend after training had
passed, because the test's temporary copy of the 127.0.0.1 IP range allowed unauthenticated
local-to-external relay without acknowledging it, which the native validation refuses (422).
A test fix (`d823a33b9`), not a product bug.

New coverage, per backend:

- `BayesTests` (live native): roles and argument refusals; training spam through a real IMAP
  COPY into a `\Junk` folder and ham from a folder; SMTP scoring of a spam-like and a ham-like
  message with the reason header checked; COPY from Junk to Trash and to another folder; reset.
  It enables spam checks for 127.0.0.1 with a temporary higher-priority copy of the matching IP
  range and restores every changed setting.
- `LiveRestTests`: the three routes and their server-administrator boundary.
- `browser.py`: the training card, an unhosted mailbox refused before any request, and a reset
  that is submitted only after confirmation.
- `BayesFilterTests` (portable GCC/Clang under the sanitizers, and ClassTester): tokenizer,
  hash, probability and copy classification.

Schema 5715 is created on every backend by the CI database setup; the upgrade scripts
5714-to-5715 are not run by CI (as for earlier schema steps).

## Bundle 3a checkpoint: spam allowlists, antivirus tests and DKIM

Merged as #48 (`79df405656d75aa74be5e0b5ccd40a6dd0b9d894`) on 2026-10-31.
Branch `feature/phase1b-spam-dkim` (A11/A12/E01/E02 integration; see the
[ledger](phase-1b-status.md#bundle-3a-checkpoint-spam-allowlists-antivirus-tests-and-dkim-2026-10-31)).
Head `d2b051f67d4d665f84597aa20a9e91b3e0f778ba` passed all four database jobs, Release x64,
GCC/Clang and CI installer publication. Per backend:

| Check | Compact, MySQL, PostgreSQL | SQL Server Express |
|---|---|---|
| NUnit regression | 1319 cases: 1209 passed, 0 failed, 34 inconclusive, 76 skipped | 1319 cases: 1210 passed, 0 failed, 34 inconclusive, 75 skipped |
| Live native management | 1084 assertions (1039 before; +45 from `SpamDkimTests`); 1086 at the final head | same |
| Live HTTP/native integration | 224 assertions (204 before; +20 REST checks) | same |
| Other management checks | unchanged from bundle 2 | same |
| Browser | fixture suite (new controls included) and published live suite | same |

That is 2015 numbered assertions per backend plus the unnumbered plaintext-boundary line.

After review, DKIM keys were limited to RSA 2048: 3072-bit generation outlasted the 10-second
management request on PostgreSQL and Express. The refusal of 1024, 3072 and 4096 adds two native
assertions. The final head `ce39cddded0c542e6b922dcb698e06eb48935665` passed all four database
jobs, Release x64, GCC/Clang, CI installer publication and CodeQL, with the NUnit outcome above,
1086 native and 224 live HTTP assertions and both browser suites per backend (2017 numbered).

Exercised for real: a message from a DKIM-signing domain, signed by the service, read back over
POP3 and verified by an independent RFC 6376 implementation in the test (relaxed/relaxed,
rsa-sha256), with a tampered copy failing; DNS-gated activation refused until the check was
skipped explicitly. Not exercised: a live SpamAssassin or ClamAV engine (CI has neither, so only
result shapes and refusals are checked) and DKIM verification by an external receiver.

Failures fixed on the way, all in the browser fixture: the SpamAssassin step still expected the
old connection-only message (`c20368672`), and the DKIM step checked the reload that follows a
save instead of the save request (`ccccd87c2`).

## Bundle 2 checkpoint: fetch accounts, groups, folders and maintenance

Branch `feature/phase1b-fetch-folders-maintenance` (A07/A10/A15; see the
[ledger](phase-1b-status.md#bundle-2-checkpoint-fetch-accounts-groups-folders-and-maintenance-2026-10-30)).
Head `aa126ef4556a8061ea74bdf85b041e03f927ec8f` passed all four database jobs, Release x64,
GCC/Clang and CI installer publication. Per backend:

| Check | Compact, MySQL, PostgreSQL | SQL Server Express |
|---|---|---|
| NUnit regression | 1319 cases: 1209 passed, 0 failed, 34 inconclusive, 76 skipped | 1319 cases: 1210 passed, 0 failed, 34 inconclusive, 75 skipped |
| Live native management | 1039 assertions (963 before; +76 from `FetchFoldersMaintenanceTests`) | same |
| Live HTTP/native integration | 204 assertions (182 before; +22 REST boundary checks) | same |
| Other management checks | unchanged from bundle 1 (7 runtime, 11 + 12 production TLS, 2 pipe-squatting, 26 DNS, 546 security, 43 lifecycle, 60 PFX) | same |
| Browser | fixture suite (new controls included) and published live suite | same |

That is 1950 numbered assertions per backend plus the unnumbered plaintext-boundary line.

Exercised for real: an IMAP `LIST` by a group member showing a public folder through a group
permission and hiding it after the membership is removed; an encrypted backup written by the
service, polled to completion and listed; installation-key export and re-import. Not exercised:
a real POP3 download (the CI POP3 listener has no TLS certificate, and new fetch accounts must
use TLS); a restore that replaces data (left to the NUnit restore tests so the shared CI server
is not overwritten).

Failures fixed on the way, none of them flaky:
- `9cc721037`: the fetch-form browser steps matched the hidden sign-in form's Password field.
- `472123db6`: a real panel bug. The desktop sidebar could not scroll, so the eighteenth link
  was unreachable at a 1000-pixel window height.
- `833567bde`: the folder test assumed `/` as the hierarchy delimiter; the default is `.`.
- `b35a0abb9`: the backup test used a folder the restricted `NT SERVICE\hMailServer` account
  could not see. The service checks the destination under its own identity, as it should.

## Bundle 1 checkpoint: server and protocol operations

Branch `feature/phase1b-server-operations` (A01/A02/A03/A05/A13/A14/A16/A20; see the
[ledger](phase-1b-status.md#bundle-1-checkpoint-server-and-protocol-operations-2026-10-30)).
Head `b65cc8555927b86d91c55c868dbf400904cd83b9` passed all four database jobs, Release x64,
GCC/Clang and CI installer publication. Per backend:

| Check | Compact, MySQL, PostgreSQL | SQL Server Express |
|---|---|---|
| NUnit regression | 1319 cases: 1209 passed, 0 failed, 34 inconclusive, 76 skipped | 1319 cases: 1210 passed, 0 failed, 34 inconclusive, 75 skipped |
| Live native management | 963 assertions (831 before; +132 from `ServerOperationsTests`) | same |
| Live HTTP/native integration | 182 assertions (145 before; +37 REST boundary checks) | same |
| Other management checks | 7 runtime, 11 + 12 production TLS, 2 pipe-squatting, 26 DNS, 546 security, 43 lifecycle, 60 PFX | same |
| Browser | fixture suite (new controls included) and published live suite | same |

That is 1852 numbered assertions per backend plus the unnumbered plaintext-boundary line
(1854 on the 1685 basis used for `ae903a182`). The NUnit outcome is unchanged from #45.

Not exercised in CI: the built-in Administrator password change (the CI test administrator
is not the built-in Administrator, so the test logs that it skipped it); smart-host delivery
through a real relay; IMAP client resynchronization after a delimiter change; outbound
port-25 diagnostics (no test domain is given). The test clears a smart-host credential left
by earlier NUnit fixtures because a write-only credential cannot be restored.

Failures fixed on the way, each a real defect or test fault rather than flakiness:
`3351cc87e` and `e6e6cad86` exposed a panel helper named like the rule editor's
`choiceField`, which replaced it (the SMTP section failed with "name.replace is not a
function"); `e6e6cad86` also failed the documentation inventory check; `f55e5e930` failed the
browser page-error assertion on console messages from intentional 409/401 responses;
`e98e34ecd` failed restoring SMTP values that NUnit fixtures had left outside the API's
accepted range; `a5641e838` exposed native sendout requiring seven fields instead of six.

## Current completion-branch validation (not CI acceptance)

Branch `feature/phase1b-completion`, based on merged #43, adds typed server settings,
checked transactional patches/cache synchronization and SURBL/blocked-attachment/incoming-relay
collections. On 2026-10-29 the disposable Windows Server 2025/Compact VM passed **630 native
assertions** (402 prior + 156 settings + 72 policy collections), **131 real HTTP/native**,
**12 production TLS/native**, fixture and published-live browser suites, and the selected
product-version NUnit case (1 passed). The temporary checkpoint and original service/database/
INI/executable were restored. Evidence: local ignored `out/phase1b-settings-policies-vm.log`;
repeatable test sources and driver are checked in. Settings tests include a real POP3 greeting;
browser tests include persisted settings changes and restoration.

Earlier settings validation reproduced a Compact parameter-binding failure: ADO binds by
ordinal, so guarded UPDATE parameters must follow SQL order (SET, then WHERE). Corrected code
passed the run above. Earlier diagnostic and browser-navigation failures are historical failures,
not additional successful tests. Database failure injection and concurrent legacy-tool acceptance
remain open. Subsequent mailbox self-service changes are **not covered by that first VM pass**.
The subsequent self-service revision passed a full Compact VM run: **656 native assertions**
(630 above +26 self-service), **131 live HTTP/native**, **12 native TLS**, both browser suites
(including ordinary-user/domain-admin own-profile persistence), and the selected NUnit case
(1 passed). Original files/service and the checkpoint were restored. Local evidence:
`out/phase1b-self-service-vm-v2.log`. A prior fixture identity-switch failure was fixed by
explicitly reloading the mocked session after hash navigation. The later deterministic HTTP
run caught a new test DELETE request without the API-required JSON body (415); the test was
corrected to use the same request contract as the panel. Its checkpoint was restored.

Later password-policy and IP security-range code is **not covered by those passes**. New tests
cover algorithm-dependent cost validation/transaction restoration; IPv4/IPv6 permissions and
expiry; real SMTP denial/recovery/authentication gates; transient trusted-relay acknowledgement;
and management access during mail-protocol lockout. Those additions passed a full run with
**706 native assertions**, 26 deterministic DNS, 413 HTTP/security, 2 plaintext HTTPS-boundary,
7 runtime, 11 TLS, 131 real HTTP/native and 12 native TLS checks, both browser suites and
the selected NUnit case (1 passed). The checkpoint was restored. Evidence:
`out/phase1b-security-policy-vm.log`.

The following logging revision passed **736 native assertions** (402 previous +204 settings
+72 filtering/relays +26 self-service +32 IP policies), the same other assertion layers,
both browser suites and selected NUnit (1 passed), with original service state and checkpoint
restored. Evidence: `out/phase1b-logging-policy-vm.log`. Logging tests exercise merged bit flags,
omitted flags, stale/mixed-invalid patches and restoration. These are Compact VM runs, not
four-backend CI or full security/capacity acceptance.

A new real REST regression then **reproduced** session reuse after resetting an account to the
same password: old session GET /status returned 200 instead of 401. Evidence:
`out/phase1b-session-repro-vm.log`, LiveRestTests.cs; original state/checkpoint restored.
The fix then passed **739 native assertions**, **415 HTTP/security**, **145 real HTTP/native**,
26 DNS, 2 plaintext-boundary, 7 runtime, 11 TLS and 12 native TLS assertions, both browser suites
and selected NUnit (1 passed). Original files/service and checkpoint were restored. Evidence:
`out/phase1b-session-binding-vm.log`. The new same-password-reset regression passed; native
metadata checks verify binding shape, stability across preferences and rotation on password change.

Following that pass, an injected TimeProvider was added to vault/cookie timing with System as
the production default. **43 controlled-clock assertions** have passed in the follow-up VM run:
30-minute idle and 8-hour absolute vault expiry, activity below the absolute bound, cookie
sliding renewal/idle expiry, fresh roles, only an opaque ID in renewed cookies, changed-binding
revocation with a still-valid password, and missing-binding login refusal. The follow-up affected
HTTP/TLS/browser run passed in `out/phase1b-session-lifetime-vm.log`: 415 HTTP/security, 43 lifecycle,
26 DNS, 2 plaintext-boundary, 7 runtime, 11 TLS, 145 live HTTP/native and 12 native TLS assertions,
both browser suites and selected NUnit (1 passed). Original files/service and checkpoint restored.
Native code is
unchanged from the 739-assertion pass; its full direct suite is not redundantly rerun there.
Checkpoint head `92c03fc154b40c6d12ccdd6b7267371b3f68aefa` was pushed without a PR.
Release, GCC/Clang and Compact/MySQL/PostgreSQL regression jobs passed. SQL Server Express
failed in live mailbox self-service after its 1317-case NUnit run returned exit code 0;
CI installer publication was skipped. Run `40870226699` exposed ADO replacing `@SELF_AD`
inside `@SELF_ADDRESS`, leaving an invalid SQL parameter. This is a real product failure.

The correction uses the existing whole-token positional binder and tests prefix collisions,
repeated parameters, SQL occurrence order, Unicode and 64-bit values in ADOConnectionTester.
Transaction-pool regressions also reproduced failed-BEGIN lease leaks and unsafe recycling
after failed COMMIT/ROLLBACK (11 failed assertions), then unsupported/no-op transactions
(2 failed assertions), before fixes passed with zero failures. These fault-injection tests
run in both the portable core and native TestInternals; they do not simulate real provider
outages or lost commit acknowledgements. Evidence: ignored `out/transaction-lease-red-vm-v2.log`,
`out/transaction-capability-red-vm.log`, `out/transaction-lease-green-vm.log`.

The corrected fresh native/managed build passed the disposable Compact VM run recorded in
`out/phase1b-database-fixes-vm.log`: 739 native, 415 HTTP/security, 43 controlled-clock,
26 DNS, 2 plaintext-boundary, 7 runtime, 11 TLS, 145 live HTTP/native and 12 native TLS
assertions, both browser suites and selected NUnit **TestInternals** (1 passed, including
the new ADO and transaction tests). Original executable/database/INI/service state was restored.
Corrected head `bc3980201` then passed all four backend jobs, Release, GCC/Clang and CI installer
generation. Per backend: **1402 management/runtime/security assertions** = 26 DNS +415 HTTP
+2 plaintext-boundary +43 lifecycle +739 native +7 runtime +2 pipe-squatting +145 real HTTP
+11 TLS +12 native TLS, plus both browser suites. NUnit on Compact/MySQL/PostgreSQL recorded
1317 total, 1207 passed, 0 failed/warnings, 34 inconclusive, 76 skipped; Express recorded
1208 passed, 34 inconclusive and 75 skipped. These are not 1317 passing cases. Ignored logs:
`out/database-green-{compact,mysql,postgresql,express}-ci.log`. CodeQL/workflow lint were not
checks in this branch-push run. Subsequent TLS changes were validated separately below; no PR exists.

The next TLS revision adds eight controls (81 settings total), tests for invalid/all-disabled
version policies, cipher validation, combined option masks, stale edits, role denials and
configuration restoration, plus a real published browser validation/save/reload workflow.
Native and managed builds passed with host service hooks disabled. The full Compact VM run
passed in `out/phase1b-tls-controls-vm.log`: **781 native** (246 settings +72 collections
+29 self +32 IP ranges +402 prior),415 HTTP/security,43 lifecycle,26 DNS,2 plaintext-boundary,
7 runtime,11 TLS,145 live HTTP/native,12 native TLS, both browsers and selected TestInternals
(1 passed). Original executable/database/INI/service state and checkpoint were restored.
TLS head `82a3bdaf5f15755c25730cbf03d40f80b7c3766b` then passed all four database jobs,
Release, GCC/Clang and CI installer generation. Per backend: **1444** separate assertions
(the prior 1402 with native 739 increased to 781), both browser suites, and the same NUnit
1317-case outcomes as `bc3980201`. Logs: `out/tls-green-{compact,mysql,postgresql,express}-ci.log`.
CodeQL/workflow lint were not checks in this branch-push run. Do not claim listener handshake/
certificate/renewal acceptance from configuration tests.

The subsequent listener/certificate persistence review added two NUnit regressions to
Infrastructure/Persistence/TCPIPPortTests.cs. Both reproduced real Compact provider rejection
while the caller still reported success: invalid protocol-column integer and a certificate
name exceeding the 255-character schema limit. Both failed at the expected missing-exception
assertion in `out/phase1b-persistence-red-vm.log`; original VM state/checkpoint restored.
Native bool/error propagation and the existing compatibility caller were then corrected.
Native and test compilation passed (inherited analyzer warnings remain). A focused VM rerun
passed **2/2 NUnit cases** in `out/phase1b-persistence-green-vm.log`, with added no-publication
and corrected-retry checks; original VM state/checkpoint restored. The subsequent CI result
is recorded below. The certificate failure fixture
requires provider length enforcement; permissive truncation is not proven covered.

The next disconnected-manager regression in native ClassTester failed the selected TestInternals
case in the VM (1/1 failed) with the expected default-log marker confirming that upgrade
prerequisites incorrectly succeeded without a connection. Evidence:
`out/phase1b-disconnected-red-vm.log`, `out/phase1b-disconnected-red-results.xml`.
The assertion runs before the old SQL-script null dereference, so no intentional service crash
is needed. Both manager entry points then gained early null guards; native compilation and
selected TestInternals passed (1/1, zero failures/warnings/skips) in
`out/phase1b-disconnected-green-vm.log`; original VM state/checkpoint restored. These changes
then passed CI at `170442658`. This tests an unavailable connection manager, not a live provider
outage or lost commit acknowledgement.

Recovery head `170442658fc3603bface0613dafd6b271dbb3306` passed all four database jobs,
Release, GCC/Clang and CI installer generation. Every backend retained **1444** separate
management/runtime/security assertions and both browser suites. NUnit increased to **1319**:
Compact/MySQL/PostgreSQL 1209 passed,0 failed/warnings,34 inconclusive,76 skipped; Express
1210 passed,0 failed/warnings,34 inconclusive,75 skipped. This includes both new rejected-write/
corrected-retry cases and the disconnected-manager TestInternals check. Logs:
`out/recovery-green-{compact,mysql,postgresql,express}-ci.log`. CodeQL/workflow lint were not
checks in this branch-push run. No public release or full-phase acceptance is implied.

The next transport-inventory revision adds native role/filter/schema/paging/string-ID tests,
deterministic REST route/bounds checks and a published browser workflow for configured listeners,
certificate search, inert markup and mobile layout. The tracked live driver seeds one temporary
certificate registration with deliberately nonexistent file paths and a markup-bearing name,
then removes it in cleanup. No certificate files are installed/read or assigned to a listener.
Native and managed builds passed. The full Compact VM run passed in
`out/phase1b-transport-inventory-vm.log`: **831 native** (402 prior +246 settings +72 collections
+29 self +32 ranges +50 inventory on the four-listener fixture),**449 HTTP/security**,
43 lifecycle,26 DNS,2 plaintext-boundary,7 runtime,11 TLS,145 real HTTP/native and12 native TLS
assertions, both browsers and selected TestInternals (1 passed). Original executable/database/
INI/service state was restored. Inventory assertion counts depend on fixture listener count;
these are functional checks, not load evidence. Transport head
`8cdc085b4ebc9042b4ecbc4f31e8467b0a335079` then passed all four backend jobs, Release,
GCC/Clang and CI installer generation: **1528** separate assertions per backend, both browsers,
and the same 1319-case NUnit outcomes as `170442658`. Logs:
`out/transport-green-{compact,mysql,postgresql,express}-ci.log`. CodeQL/workflow lint were not
checks in this branch-push run. The subsequent certificate inspector requires its own evidence.

The next .NET 10 bounded PFX helper passed **60** focused VM assertions in
`out/phase1b-certificate-inspection-vm.log` (exit0, checkpoint restored/removed). These cover
format/password/size/key/certificate/KDF limits, SAN matching/IDNA/IP/CN behavior, validity,
CA/EKU/key-strength metadata, public-only replies and no personal-store installation. Its
newer POST endpoint/owned JSON reader/form and rate/concurrency/body/deadline controls compiled.
Their first full VM run `out/phase1b-certificate-preflight-vm.log` passed
513 management HTTP/security,43 lifecycle,60 parser,831 native,145 real HTTP/native,26 DNS,
2 plaintext-boundary,7 runtime,11 TLS,12 native TLS, both browsers and selected TestInternals
(1 passed); original state/checkpoint restored. A further independent-host inspection-rate-budget
test and the latest fixture cleanup/desktop screenshot harness were validated in
`out/phase1b-certificate-preflight-final-vm.log`; the final run passed **546 management HTTP/
security**,60 parser,43 lifecycle,831 native,145 real HTTP/native,26 DNS,2 plaintext-boundary,
7 runtime,11 TLS and12 native TLS assertions, both browsers and selected TestInternals (1 passed).
Private PFX files and the original VM state/checkpoint were cleaned up/restored. Public desktop/
mobile captures were retained and visually reviewed for layout, inert metadata and the cleared
password; these do not establish complete manual/IIS/platform acceptance. Its checkpoint CI
at `ae903a182811922e1764526936550680a84978d4` passed all four database jobs, Release and GCC/Clang.
Per backend: **1685** separate assertions = 26 DNS +546 HTTP/security +2 plaintext-boundary
+43 lifecycle +60 PFX +831 native +7 runtime +2 pipe-squatting +145 real HTTP/native +11 TLS
+12 native TLS, plus both browser suites. NUnit remains **1319** cases: Compact/MySQL/PostgreSQL
1209 passed,0 failed/warnings,34 inconclusive,76 skipped; Express 1210 passed,0 failed/warnings,
34 inconclusive,75 skipped. Logs: `out/certificate-green-{compact,mysql,postgresql,express}-ci.log`.
CI installer artifact publication also passed; CodeQL/workflow lint were not checks in this
branch-push run. The following native correction's CI result is recorded below.
The earlier focused 60-case pass predates the endpoint changes.
Current handoff:
[2026-10-30](agents/handoffs/2026-10-30.md). Native installation/activation is not implemented.

The next native transport identity prerequisite adds a regression to existing TestInternals,
without adding a separate NUnit case or management assertion count. On the Compact VM, the
fully qualified test failed before the fix and passed afterward (1 selected case). Its
application log records certificate ID, listener ID, certificate-reference and setter narrowing.
Synthetic BIGINT projections use the actual provider without changing identity seeds or
certificate/listener rows. Boundaries include signed 32-bit overflow, a colliding low 32-bit
value and signed 64-bit maximum. Sources also widen returned insert IDs and backup certificate-
name lookup; high-ID persisted insert/backup and COM compatibility are not separately proven
by this read/setter regression. Logs: `out/phase1b-identity-red-focused-vm.log`,
`out/phase1b-identity-green-vm.log`; XML: `out/phase1b-identity-{red,green}-results.xml`.
Both selected runs restored the original service files and temporary checkpoint; no VM job remains active.
An earlier incomplete filter selected zero tests and was rejected, not accepted as evidence.
The build ran with service/COM hooks disabled. This correction needs its own four-backend CI.

First pushed identity head `95c91ce55` passed Compact/PostgreSQL, Release and GCC/Clang but failed MySQL:
the new test used CAST(... AS BIGINT), which MySQL rejects. Four NUnit cases calling the
internal suite failed (1205 passed,4 failed,34 inconclusive,76 skipped,0 warnings out of 1319);
these are not production ID-fix failures or a waived flaky test. Log:
`out/identity-failed-mysql-ci.log`. The fixture now selects MySQL's CAST(... AS SIGNED),
which produces a signed BIGINT per the [MySQL contract](https://dev.mysql.com/doc/refman/8.4/en/cast-functions.html),
while preserving BIGINT on the other providers. Express and installer publication were cancelled
when the correction was pushed; they are not accepted results for that head. Native compilation
passed with hooks disabled in `out/phase1b-identity-mysql-build.log`. Corrected head
`7258068dff8062c2072eebd6d0603722362eff41` then passed all four database jobs, Release,
GCC/Clang and CI installer publication: NUnit 1319 cases (Compact/MySQL/PostgreSQL 1209 passed,
34 inconclusive, 76 skipped; Express 1210 passed, 75 skipped; 0 failed/warnings each) and the same
1685 separate assertions and both browser suites per backend as `ae903a182`. Logs:
`out/identity-green-*.log`.

Separate storage research: the [Windows machine-key probe](agents/tools/certificate-key-store-probe/README.md)
passed 50 assertions on the disposable Windows Server 2025 VM, including protected DACL readback,
reopen/export identity and unprivileged-user denial for RSA-2048/ECDSA-P256. Final tracked-run
log: `out/phase1b-cng-storage-final-vm.log`, exit0; generated keys/user cleaned up, checkpoint
restored/removed. This standalone manual probe is not invoked by application CI and adds nothing
to its 1685 count. Native OpenSSL, service/IIS identity, backup, activation, other Windows versions
and algorithms remain unproved. Earlier rejected runner/fixture attempts are recorded in the
[storage proposal](certificate-storage-design.md), not counted as successful application tests.

## Modern-stack CI expansion merged and verified

The [acceptance matrix](ci-acceptance-matrix.md) defines current checks and future coverage.
Final #42 head `28dd3df912fac9109260efe9f5571dc05b402d12` passed **897 assertions per backend**:
26 deterministic DNS + 304 HTTP/session + 2 plaintext/forwarded-proto rejection + 402 native
+ 7 runtime/product + 2 pipe-identity + 131 real REST/native + 11 production TLS + 12 native
HTTPS. Both fixture and published-live browser suites passed independently on every backend.
Each backend uploaded redacted SDK/publish manifests, logs and screenshots plus NUnit results.

The exact tracked driver also passed in the disposable VM with the content-root variable unset,
published live browser and the selected product-version NUnit case; the original checkpoint
was restored. Stable SDK 10 is selected in global.json. This proves functional coverage only:
restricted production hosting, controlled-clock expiry, wizard/scanner lifecycle, capacity,
installer/API provisioning, consumer migration and PHP/COM retirement are still pending.
The [6.0 scope](version-6-scope.md) adds mandatory acceptance obligations, not executed tests.

## Latest merged application/test baseline: PR #42

Verified **2026-10-28 (Asia/Kolkata)** on
[`28dd3df912fac9109260efe9f5571dc05b402d12`](https://github.com/jassifx/ArzoieMail/commit/28dd3df912fac9109260efe9f5571dc05b402d12/checks),
[PR #42](https://github.com/jassifx/ArzoieMail/pull/42), merged as
`74eecc017f48ddd445870bfd73dd6eb00aed6cef`. All four backend jobs,
Release x64, GCC/Clang, all CodeQL analyses and installer publication passed. Subsequent
documentation commits require their own checks; consult the PR's final-head validation record.

| Backend | NUnit total | Passed | Failed | Warnings | Inconclusive | Skipped |
|---|---:|---:|---:|---:|---:|---:|
| SQL Server Compact | 1317 | 1207 | 0 | 0 | 34 | 76 |
| MySQL | 1317 | 1207 | 0 | 0 | 34 | 76 |
| PostgreSQL | 1317 | 1207 | 0 | 0 | 34 | 76 |
| SQL Server 2025 Express | 1317 | 1208 | 0 | 0 | 34 | 75 |

Every backend also passed **897 management/runtime/security assertions**, fixture browser and
published-live browser checks, as broken down above. The 1317-case NUnit baseline includes
stored-mail queue and product-6 regressions. Inconclusive/explicit skips remain visible in the
table; this is not 1317 passing tests. Functional success is not throughput or scanner acceptance.
The earlier #42 head `766e3d9bc02ef9211803a9a87f2d2deeeb219fd6` passed 734 management
assertions and fixture browser; two pipe assertions were then separate manual VM coverage.
Preserve the distinction rather than retroactively attributing the new tests to that head.

Earlier #42 CI, before this verified version-6 baseline, exposed a browser request-history/saved-notice timing race. The fix waits for the
exact routing PATCH response. PostgreSQL's existing `Infrastructure.IPRanges.TestConnections`
then failed once when SMTP was not reachable after re-enabling a range; a full job retry passed
without a code change. Preserve that observation; do not silently classify every later failure
as the same cause or suppress the test. See the PR validation record for final results.

### Mail-operations coverage

Current mail-operations bundle: `MailOperationsTests.cs` adds server/domain/user boundaries,
stored-mail exclusion, missing/busy queue handling, retry/removal/snapshot invalidation,
routing TLS/authentication and write-only secrets, allowed-recipient parent checks, disabled
rule drafts, safe criteria/actions, activation/cache changes and actual local SMTP/POP3 effects.
A controlled loopback SMTP backend holds an in-flight delivery to test 409 mutation rejection;
no external mail is sent. Fixture setup seeds one stored mailbox message through COM solely
to test exclusion. HTTP coverage adds queue ID precision and explicit command/parent routing,
CSRF/origin and bounds checks. Browser fixtures add busy-state/confirmation/64-bit URL tests,
routing password omission/recipient CRUD, draft ordering/activation/inert source and mobile UI.

`API.UtilitiesTests.DeliveryQueueMustNotDeleteOrRetryStoredMailboxMessages` adds one ordinary
NUnit regression (1316 at the earlier #42 head, 1317 after the version-6 regression). It failed on the original VM binary and passed on the guarded build (1 passed, zero failures/warnings).
The earlier mail-operations VM pass cleared 734 assertions plus two separate pipe checks;
the subsequent exact tracked CI driver passed all 897 and published-live browser checks.
Four-backend CI also passed the counts above; final-head evidence is recorded in #42;
prior merged results below are historical and do not establish later-revision acceptance.

Advanced domain/mailbox administration merged in #41. `AdvancedAdministrationTests.cs` covers
profiles, domain policies/limits/signatures, alternate names, boundaries and local SMTP/POP3.
All four backend CI jobs passed **26 DNS + 188 HTTP + 2 HTTPS + 312 native = 528 assertions**,
browser workflows and the unchanged 1315-test NUnit baseline on tested head
`34cfdfd1476cb64f1b55108b37a186f1b6087820`, merged as
`5294d67a09823a286dd3f1f6b477772bb4b5388f`. Two separate pipe checks also passed in the VM.

Mailbox signatures merged in #40. `SignatureTests.cs`
adds native scope/type/size/persistence checks and local SMTP/POP3 plain, HTML, multipart and
disabled-signature probes. HTTP and browser coverage includes scoped routes, CSRF/origin,
confirmation, literal HTML source, omission/newline preservation, UTF-8/request limits and
mobile layout. VM validation passed **26 DNS + 152 HTTP + 2 HTTPS + 224 native = 404 assertions**,
plus browser workflows and 2 separate pipe-identity checks. The CRLF omission regression failed
before the panel fix and passed afterward. All four backend CI jobs passed these counts on
`f9a94491d2f122fdf981aeb0be612136d2267dcd`, merged as `589001f49f89dec62a390d78dbf4eecccaeeaf1d`.

Mailbox forwarding/automatic replies merged in PR #39.
`MailboxBehaviorTests.cs` adds native permission/input/persistence coverage and local-only
SMTP/POP3 delivery probes; HTTP route/CSRF and browser confirmation/mobile tests are expanded.
Disposable-VM acceptance passed **26 DNS + 142 HTTP + 2 HTTPS + 188 native = 358 assertions**,
plus browser workflows and 2 separate pipe-identity checks. The added live probes send local
SMTP DATA and verify POP3 delivery, forward-only/retained copies, deactivation, reply suppression,
expiry and re-enabling; none attempts external delivery. Native delivery waits account for the
existing one-minute queue poll. All four backend jobs passed these 358 assertions on tested
head `99e0948261efad10279fc34f257d9b6efeca3374`, merged as `c9bcd51861f22cf83a7e4be6fd0531c0e410fb28`.
Verify the new mail-operations head independently; earlier green CI is historical.

This describes the workflows and test entry points delivered through merged PR #41, plus the current mail-operations bundle. CI dependency updates were separately validated in #37. Update
this inventory and ROADMAP's baseline when coverage or test counts change. Counts below are
a verified snapshot, not a promise that a future commit has passed.

## Automated checks

| Check | Coverage | Workflow |
|---|---|---|
| `Release x64 (windows-2025-vs2026)` | Builds the native server, tools, tests and installer; checks runtime DLL packaging. Compilation is separate from installed-server test execution. | [build.yml](../.github/workflows/build.yml) |
| `Tests / Regression tests` | Installs the built artifact on a clean Windows runner; runs NUnit and management checks using SQL Server Compact. | [tests.yml](../.github/workflows/tests.yml) |
| `Tests (PostgreSQL) / Regression tests` | The same installed-server suites using the runner's PostgreSQL service. | [tests.yml](../.github/workflows/tests.yml) |
| `Tests (MySQL) / Regression tests` | The same suites using an isolated MySQL service/data directory. | [tests.yml](../.github/workflows/tests.yml) |
| `Tests (SQL Server Express) / Regression tests` | SQL Server 2025 Express with OLE DB 19 and verified TLS; SQL-specific checks followed by NUnit and management checks. | [tests.yml](../.github/workflows/tests.yml) |
| `gcc`, `clang` | Build the portable core on Linux with address/undefined-behaviour sanitizers and run its CTest suite. They do not build or exercise the Windows service. | [portable-core.yml](../.github/workflows/portable-core.yml) |
| `actionlint and zizmor` | Workflow syntax/context validation and static workflow security checks. Runs when workflow files change, or by manual dispatch. | [lint-workflows.yml](../.github/workflows/lint-workflows.yml) |

Push-triggered workflows cover `master`, `feature/**`, `bugfix/**`, `techdebt/**`, `claude/**`
and matching version branches. Build and portable-core workflows also support manual dispatch.
Fork PR creation alone does not trigger the build; the build workflow deliberately runs on
repository pushes. Documentation-only changes on an eligible branch still trigger normal CI.

`Publish the installer` waits for the release build and **all four database jobs**, then attests
and uploads installer/symbol artifacts. It is an artifact-publication gate, not another test suite
or a public GitHub release. The manually dispatched [release workflow](../.github/workflows/release.yml)
creates a draft release from an already built/tested artifact for maintainer review.

Publication dependencies and branch-protection required checks are different settings. The
workflow enforces the former; confirm the live repository rules before describing the latter.
Verified 2026-10-28: [protect-master](https://github.com/jassifx/ArzoieMail/rules/25743689)
requires `Release x64 (windows-2025-vs2026)`, `Tests / Regression tests`, `gcc` and `clang`,
with strict status checking and an administrator bypass. Adding the other backend contexts
remains an open repository-policy follow-up. The project still requires all four backends,
CodeQL and installer publication for acceptance; a bypass or weaker configured rule does not
authorize skipping them or merging without the maintainer's instruction.
Green CI never authorizes an agent to merge or start the next task without the maintainer's gate.

## Installed-server regression suite

The four database jobs run `RegressionTests.dll` through the restored NUnit console runner
against the installed mail service. Coverage includes SMTP, IMAP, POP3, legacy management API,
business/persistence behavior, bound SQL values, authentication, TLS, spam/antivirus integrations,
secret storage, backup/restore and binary hardening. Some tests depend on external services or
explicit opt-in; a passing job does not mean every optional test ran.

The native class suite is invoked from NUnit, including SQL connection-policy validation.
`API/SqlServer.cs` additionally verifies encryption of native mail-service SQL sessions in the
Express fixture. That database-specific test is explicitly skipped on the other three backends.

### Historical verified baseline after PR #40

Verified on **2026-10-27 (Asia/Kolkata)** from the successful logs for tested head
`f9a94491d2f122fdf981aeb0be612136d2267dcd`, merged by
[PR #40](https://github.com/jassifx/ArzoieMail/pull/40) as
`589001f49f89dec62a390d78dbf4eecccaeeaf1d`. NUnit totals match #33/#35/#36/#39:

| Backend | Total | Passed | Failed | Warnings | Inconclusive | Skipped |
|---|---:|---:|---:|---:|---:|---:|
| SQL Server Compact | 1,315 | 1,205 | 0 | 0 | 34 | 76 |
| PostgreSQL | 1,315 | 1,205 | 0 | 0 | 34 | 76 |
| MySQL | 1,315 | 1,205 | 0 | 0 | 34 | 76 |
| SQL Server 2025 Express | 1,315 | 1,206 | 0 | 0 | 34 | 75 |

The additional skip on non-MSSQL backends is the Express-specific session-encryption test.
The baseline includes expected explicit/environment-dependent outcomes. NUnit's summary says
`Overall result: Warning` because of inconclusive cases, despite zero warnings and failures;
the jobs completed successfully. Investigate any new unexplained failure or baseline difference.
These totals exclude the separate management assertion counts and browser checks below.

## Management API, security, DNS and browser checks

Expanded layers below passed VM tracked-driver validation and final-head #42 CI on every
backend. They supplement the historical 734-assertion groups described afterward.

| Entry point / mode | Assertions | Boundary |
|---|---:|---|
| `test-management.ps1 -Runtime` / `--runtime` | 7 | Actual .NET/ASP.NET Core 10, Windows/x64, target/product metadata and direct COM/SQL reference guard |
| `test-management.ps1 -Tls` / `--tls` | 11 | Real production TLS/cookies/HSTS/Host policy and trust/name rejection; controlled authentication channel |
| `test-management.ps1 -PipeIdentity` / `--squatting` | 2 | Impostor pipe/SCM identity and no credential transmission; run before actual endpoint opt-in |
| `test-management-live.ps1` / `--live-http` | 131 | Actual HTTP/native/backend persistence, roles, revocation, new-host cookie rejection and validation |
| Same driver / `--tls-native` | 12 | Production HTTPS/cookies/trust/Host policy with real native authentication and service status |
| `test-management-browser.ps1 -PublishedApi <directory>` | Separate browser suite | Actual published API, real roles and persistence, no API mocks; artifact must include ASP.NET runtime dependencies/assets |

Verified final-head automated total: **734 + 7 + 11 + 2 + 131 + 12 = 897** per backend, with
fixture and published-live browser suites separate. Production deployment/identity and
scanner/certificate lifecycle/capacity gaps remain in the acceptance matrix.

Each database job now runs documentation/SDK validation, API build/publish, runtime/security/
fixture-browser checks, then the live native/REST/HTTPS/published-browser driver after NUnit.
The [matrix](ci-acceptance-matrix.md) details coverage and boundaries. Earlier verified heads
used the combined `Test the native management API` step:

| Entry point | Verified coverage | Boundary |
|---|---|---|
| [test-management.ps1](../build/test-management.ps1) | 26 deterministic DNS assertions covering shared-provider SPF, syntax, includes/redirects, cycles/lookup limits, DKIM and DMARC publication advice. | Uses DNS fixtures; does not prove live DNS-provider configuration or message-level authentication. |
| Same executable, default mode | 304 HTTP/session/security assertions, plus 2 production HTTPS-boundary assertions at the verified #42 application/test head. | Uses a controlled management-channel fixture to exercise the HTTP host, validation, sessions, CRUD route/parent-ID mapping, pagination and security behavior. |
| [test-management-browser.ps1](../build/test-management-browser.ps1) and [browser.py](../hmailserver/test/RegressionTests/Management/browser.py) | Queue ID precision, busy guards and confirmations; routing secrets and recipient CRUD; global/mailbox rule drafts, order, activation and source safety; domain/mailbox/alias/list/member CRUD, role controls, responsive layout, DNSBL, SpamAssassin and DNS advice. | Playwright uses explicit API response fixtures. It verifies UI workflows, not an end-to-end browser-to-database mutation. |
| [test-management-live.ps1](../build/test-management-live.ps1) | 402 live native transport/parser/authorization/persistence assertions at the verified #42 application/test head, including CRUD, quotas, parent/role boundaries, forwarding/replies, signatures, advanced policies and mail operations. [AddressingTests.cs](../hmailserver/test/RegressionTests/Management/AddressingTests.cs) adds SMTP RCPT routing/policy probes; [MailboxBehaviorTests.cs](../hmailserver/test/RegressionTests/Management/MailboxBehaviorTests.cs) and [SignatureTests.cs](../hmailserver/test/RegressionTests/Management/SignatureTests.cs) add local SMTP DATA/POP3 delivery checks. [MailOperationsTests.cs](../hmailserver/test/RegressionTests/Management/MailOperationsTests.cs) adds controlled loopback queue contention, routing and rules. | Uses the installed service/database. Alias/list probes issue no DATA; delivery probes use only disposable local mailboxes. No external delivery is attempted. COM is used only to prepare/clean fixtures; management commands are native. |

The deterministic/HTTP/HTTPS/live groups total **734 assertions per backend at the earlier #42 head `766e3d9`**, independently
of NUnit totals; browser checks are separate. The same counts passed the VM. Verify subsequent heads independently. At that historical head, pipe-squatting checks were manual; final #42 CI automates them
as part of the 897 total above. Merged #33 passed 100 (26/54/2/18) and #35 passed
180 (26/83/2/69). The expanded 26/132/2/152 counts passed VM and all four CI backend jobs
in #36; #39 passed 26/142/2/188 and #40 passed 26/152/2/224 on all four backends. These are
verified historical results, not proof that a later mail-operations head has passed.

The [UID lifecycle plan](imap-uid-modernization.md#validation-and-completion-gate) adds future
boundary, concurrency, generation-renewal, crash/restore and client-interoperability acceptance.
Those tests are not implemented or included in the totals above. Boundary fixtures will simulate
large counters; they will not by themselves establish billions-of-messages workload capacity.

## SQL Express-specific checks

Before NUnit, the Express job calls [provision-mssql-test.ps1](../build/provision-mssql-test.ps1)
and [test-mssql-connection.ps1](../build/test-mssql-connection.ps1). The disposable fixture installs
the engine/driver and provisions its certificate and service identities. Its eight verified checks are:

1. Reject an untrusted SQL certificate.
2. Reject a certificate hostname mismatch.
3. Reject a legacy SQL provider override.
4. Reject Optional/unencrypted SQL connection policy.
5. Create the schema using SQL authentication with delimiter-bearing credentials.
6. Create/select the schema using Windows authentication and Strict TLS.
7. Confirm all native SQL sessions are encrypted after removing provisioning dbcreator rights.
8. Confirm an installer rejected for a legacy provider leaves the executable and service PID unchanged.

See [SQL Server deployment](sql-server.md) for production prerequisites and migration. The
disposable fixture is not a production installer. Passing these checks does not establish the
capacity of Express for a particular message rate or validate every SQL Server engine release.

## Manual and planned coverage

| Coverage | Current status |
|---|---|
| Pipe impersonation (`Management.Tests --squatting`) | Previously VM-only; now automated on every backend before fixture opt-in. Use an isolated VM with no competing management pipe. Final-head CI is tracked in #42. |
| Missing-driver installer rejection without service interruption | Reproduced before the ordering fix and verified after it in the VM. CI currently exercises the related legacy-provider rejection. |
| Explicit `Stress` tests | Present in `RegressionTests.dll`, opt-in; not included by the normal unfiltered NUnit run. |
| Explicit `Details` tests | Opt-in edge/RFC checks; not included by normal CI. |
| `VolumeTests.dll` and page-heap environments | Separate/manual VM runs; building the test solution does not execute them. See AGENTS.md and VMTestRunner configuration. |
| Defender scanning/mitigation and 10,000-message pressure acceptance | Planned, not implemented or benchmarked. Existing antivirus integration tests do not establish Defender support. |
| Guided certificate issuance/renewal, remaining admin parity and PHP retirement | Planned Phase 1b work. Current foundation tests do not establish those future capabilities. |

## Results, diagnostics and execution boundary

NUnit XML is uploaded as `regression-test-result` for Compact and
`regression-test-result-postgresql`, `regression-test-result-mysql`, `regression-test-result-mssql`
for the external backends. The workflow adds a per-backend summary; inspect XML and the job log
for inconclusive/skip reasons. Mail-service logs are uploaded on failure with the corresponding
`regression-test-logs` suffix. Browser screenshots/logs are produced under
`out/management-browser-results` (fixtures) and `out/management-browser-live-results`
(published real API). The expanded workflow always uploads available artifacts as
`management-test-evidence`, with `-postgresql`, `-mysql` or `-mssql` for external backends.
These include SDK details, publish manifests and native/HTTP/TLS logs under
`out/management-evidence`. They exclude service INI, credentials, private keys and HAR/traces.

Use exact commit SHAs when checking results. Historical green checks do not validate a newer
head. Keep downloaded logs, VM credentials, certificates and test artifacts out of commits.
All runtime tests and previews execute only in the disposable VM or CI. The development host
is restricted to editing, static inspection and compilation.
