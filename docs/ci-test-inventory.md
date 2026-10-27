# CI test inventory

Mailbox signatures are the current authorized slice after merged PR #39. `SignatureTests.cs`
adds native scope/type/size/persistence checks and local SMTP/POP3 plain, HTML, multipart and
disabled-signature probes. HTTP and browser coverage includes scoped routes, CSRF/origin,
confirmation, literal HTML source, omission/newline preservation, UTF-8/request limits and
mobile layout. VM validation passed **26 DNS + 152 HTTP + 2 HTTPS + 224 native = 404 assertions**,
plus browser workflows and 2 separate pipe-identity checks. The CRLF omission regression failed
before the panel fix and passed afterward. Verify exact-head four-backend CI in the feature PR;
the merged baseline below remains historical evidence.

Mailbox forwarding/automatic replies merged in PR #39.
`MailboxBehaviorTests.cs` adds native permission/input/persistence coverage and local-only
SMTP/POP3 delivery probes; HTTP route/CSRF and browser confirmation/mobile tests are expanded.
Disposable-VM acceptance passed **26 DNS + 142 HTTP + 2 HTTPS + 188 native = 358 assertions**,
plus browser workflows and 2 separate pipe-identity checks. The added live probes send local
SMTP DATA and verify POP3 delivery, forward-only/retained copies, deactivation, reply suppression,
expiry and re-enabling; none attempts external delivery. Native delivery waits account for the
existing one-minute queue poll. All four backend jobs passed these 358 assertions on tested
head `99e0948261efad10279fc34f257d9b6efeca3374`, merged as `c9bcd51861f22cf83a7e4be6fd0531c0e410fb28`.
Verify the new signature feature head independently; earlier green CI is historical.

This describes the workflows and test entry points delivered through merged PR #39, including
forwarding/reply coverage. CI dependency updates were separately validated in #37. Update
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

### Verified baseline after PR #39

Verified on **2026-10-27 (Asia/Colombo)** from the successful logs for tested head
`99e0948261efad10279fc34f257d9b6efeca3374`, merged by
[PR #39](https://github.com/jassifx/ArzoieMail/pull/39) as
`c9bcd51861f22cf83a7e4be6fd0531c0e410fb28`. NUnit totals match #33/#35/#36:

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

Each database job runs the `Test the native management API` step after NUnit:

| Entry point | Verified coverage | Boundary |
|---|---|---|
| [test-management.ps1](../build/test-management.ps1) | 26 deterministic DNS assertions covering shared-provider SPF, syntax, includes/redirects, cycles/lookup limits, DKIM and DMARC publication advice. | Uses DNS fixtures; does not prove live DNS-provider configuration or message-level authentication. |
| Same executable, default mode | 142 HTTP/session/security assertions, plus 2 production HTTPS-boundary assertions at #39. | Uses a controlled management-channel fixture to exercise the HTTP host, validation, sessions, CRUD route/parent-ID mapping, pagination and security behavior. |
| [test-management-browser.ps1](../build/test-management-browser.ps1) and [browser.py](../hmailserver/test/RegressionTests/Management/browser.py) | Domain/mailbox/alias/list/member CRUD, posting-policy and deletion confirmation/cancellation, password controls, session recovery, responsive layout, DNSBL mutations, SpamAssassin feedback and DNS advice. | Playwright uses explicit API response fixtures. It verifies UI workflows, not an end-to-end browser-to-database mutation. |
| [test-management-live.ps1](../build/test-management-live.ps1) | 188 live native transport/parser/authorization/persistence assertions at #39, including CRUD, quotas, address validation, parent/role boundaries, credential revocation and forwarding/replies. [AddressingTests.cs](../hmailserver/test/RegressionTests/Management/AddressingTests.cs) adds SMTP RCPT routing/policy probes; [MailboxBehaviorTests.cs](../hmailserver/test/RegressionTests/Management/MailboxBehaviorTests.cs) adds local SMTP DATA and POP3 delivery checks. | Uses the installed service/database. Alias/list probes issue no DATA; forwarding/reply probes deliver only to disposable local mailboxes. No external delivery is attempted. COM is used only to prepare/clean fixtures; management commands are native. |

The deterministic/HTTP/HTTPS/live groups total **358 assertions per database job at merged #39**, independently
of NUnit totals; browser checks are separate. Do not report the manually exercised pipe-squatting
checks as part of that automated total. Merged #33 passed 100 (26/54/2/18) and #35 passed
180 (26/83/2/69). The expanded 26/132/2/152 counts passed VM and all four CI backend jobs
in #36; #39 passed 26/142/2/188 on all four backends. These are verified historical results,
not proof that a later signature-feature head has passed.

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
| Pipe impersonation (`Management.Tests --squatting`) | Two assertions exercised in the disposable VM during the foundation work. The mode exists but is not invoked by the current CI scripts. Use an isolated VM with no competing management pipe. |
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
`out/management-browser-results` during execution; they are not currently uploaded by the workflow.

Use exact commit SHAs when checking results. Historical green checks do not validate a newer
head. Keep downloaded logs, VM credentials, certificates and test artifacts out of commits.
All runtime tests and previews execute only in the disposable VM or CI. The development host
is restricted to editing, static inspection and compilation.
