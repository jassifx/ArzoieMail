# ArzoieMail engineering roadmap

This is the working roadmap behind the short table in [README.md](README.md#roadmap). It is
written for the people and AI agents who work on ArzoieMail. It records what has been done and
why, the decisions that are settled, the details of each change, the lessons learned on the way,
and the plan for what comes next.

The [documentation authority index](docs/README.md) defines the shared project record.
The [detailed Phase 1b ledger](docs/phase-1b-status.md) owns granular capability status, legacy
PHP/desktop migration inventories and completion gates.

Read [AGENTS.md](AGENTS.md) for the layout of the code and [BUILDING.md](BUILDING.md) for how
to build, run and test it. **Keep this file current:** a pull request that finishes or changes a
roadmap item updates its entry here.

Status: ✅ done · 🔄 in progress · 🔜 next · 📋 planned · ⏳ open follow-up

---

## 1. How we work

- **One change, one pull request** to `master`. `master` is protected: it takes a pull request
  with passing CI checks. Merged branches are deleted.
- **Larger coherent Phase 1b bundles**, requested on 2026-10-27: combine related settings and
  workflows across native/REST/panel, regressions and documentation. The earlier 18–22 tiny-slice
  estimate is superseded by this grouping preference; it is not a fixed remaining PR count.
  Submit one bundle, monitor/fix its CI and wait for maintainer review before the next bundle.
- **Every bug fix and every feature has a regression test** (see AGENTS.md). For a bug fix the
  test is written first and seen to fail.
- **Branch names decide whether CI runs.** The workflows run for `master`, `claude/**`,
  `feature/**`, `techdebt/**`, `bugfix/**` and the version branches. A branch named otherwise,
  for example `security/...`, gets no CI.
- **CI runs the regression suite on four backends**: the built-in database (SQL Server Compact),
  PostgreSQL, MySQL and SQL Server 2025 Express. A change is ready when all four are green.
- **Phases are confirmed before they start.** The order below is agreed; the start of each phase
  is a separate decision.

### Test baseline

Merged [PR #42](https://github.com/jassifx/ArzoieMail/pull/42), 2026-10-28 (Asia/Kolkata):
merge `74eecc017f48ddd445870bfd73dd6eb00aed6cef`, tested head
`28dd3df912fac9109260efe9f5571dc05b402d12`. All four backend jobs, Release, GCC/Clang,
workflow lint, CodeQL and CI installer publication passed. Each backend recorded 1317 NUnit
cases with zero failures/warnings, **897 management/runtime/security assertions** and both
fixture and published-live browser suites. NUnit inconclusive/skipped outcomes and prior
failure/retry history remain in the [CI inventory](docs/ci-test-inventory.md).
PR #43's scope/documentation head `7a143ca` also passed CI and merged at `92ac28d`. The current
completion branch needs its own final-head checks; local VM evidence is recorded separately.

Tests known to depend on the live internet and that can fail by themselves on CI, in which case
the failed job is re-run:

- `AntiSpam.AuthenticationResults.TestHeaderIsAdded` (live outlook.com SPF lookups).
- `Infrastructure.DnsResolution.CNameRecordsShouldBeFollowed` (live hmailserver.com DNS).

---

## ArzoieMail 6.0 scope and release acceptance

Maintainer decisions, **2026-10-28**: all current phases are mandatory for the **6.0 integrated
validation baseline**, as frozen in the [scope checklist](docs/version-6-scope.md). Later
additions/deferrals require explicit approval. That document owns architecture/maintenance,
finite acceptance criteria, development artifacts, independent review, supported-platform
policy and signed production release gates; the [breaking-change register](docs/breaking-changes-6.0.md)
owns migration impact. No development previews before feature completion; CI test artifacts
remain allowed. Continuous migration/recovery/security/load tests precede comprehensive
integrated validation. No preview, production release or next implementation phase is
implicitly authorized by these scope decisions.

COM is completely removed in 6.0 after supported tools/tests/automation migrate to the secured
management API; the older retention decision below is superseded. Retain non-COM service,
executable, INI and installer identities. Webmail integrates an existing maintained open-source
application, optional to install, with PHP permitted. CalDAV/CardDAV remain uncommitted 6.1 ideas.


## 2. Settled decisions

These were decided deliberately; reopen them only with a new reason.

| Decision | Why |
|---|---|
| **The next product release is ArzoieMail 6.0.0, on its own release line.** | Maintainer decision 2026-10-28: divergence in architecture and scope makes the former 5.8.0 target inappropriate. VERSION owns the release target; product, schema, build and compatibility identities remain distinct. See [release policy](docs/release-versioning.md). This does not publish a release or complete future phases. |
| **Active Scripting (VBScript/JScript) is replaced by a PowerShell event host**, persistent and out of process. Chosen over embedded JavaScript and over webhooks. | Windows disables VBScript by default from about 2027 and will remove it. PowerShell is what Windows administrators already script in, and out of process a script can no longer crash or stall the server. |
| **Admin tools and tests run on .NET 10 LTS** (supported until 2028-11-14), not .NET Framework 4.8.1. | A current runtime, SDK-style projects and current libraries. |
| **The installer bundles the .NET 10 Desktop Runtime** and installs it silently when missing, rather than downloading it on demand. | Installs work offline; setup grows by about 60 MB. |
| **SQLite replaces SQL Server Compact** as the built-in database, with automatic migration. | SQL Server Compact is out of support. |
| **The SQL injection in string-built SQL was fixed publicly**, with no private report upstream first. | The fix is ArzoieMail's own, made in the open like the rest of the project. |
| **Secrets use AES-256-GCM under a random per-installation key protected by machine-scope DPAPI**, with passphrase export and import of the key. | The old Blowfish key was the same in every installation and is in the public source. DPAPI ties the key to the machine; export moves it to a new one. |
| **Backups get their own passphrase**, independent of the installation's key. | A backup must restore on a new computer, which does not have the old key. |
| **Windows 10 / Server 2016 is the technical minimum**, x64 only. | Production support separately requires applicable vendor security updates and project validation of the exact OS/stack; minimum compatibility is not a support guarantee. |
| **The replacement web panel uses ASP.NET Core / .NET 10 with a native management channel, without COM.** | Revised by the maintainer on 2026-10-26. 2026-10-28 revision: remove COM completely for 6.0 after all supported consumers migrate; remove PHP from administration delivery after tested migration. PHP remains allowed for optional webmail (see Phase 1b and docs/management-api-architecture.md). |

---

## 3. Phase overview

| Phase | Scope | Status | Pull requests |
|---|---|---|---|
| Base | hMailServer's code after 5.7.2, up to upstream `master` of 2026-10-11 | ✅ | |
| Pre-roadmap | libpq 18.6 + MariaDB Connector/C 3.4.11, Windows 10 minimum, ArzoieMail rebrand, README | ✅ | #1, #2, #3 |
| **0 · Foundation** | Toolset, C++20, hardening, installer, .NET 10, NUnit 5 | ✅ | #4–#8, #10 |
| **1 · Security** | Bound SQL parameters, AES-GCM + DPAPI secrets, key export, encrypted backups, Administrator DPAPI, Argon2id review, UTF-8 client credentials | ✅ | #9, #11–#13, #17, #19–#26 (CI and VM fixes: #14, #15; README: #18) |
| **1b · Web admin** | Modern panel and REST/native API, advanced administration, spam/DNS guidance, certificate lifecycle, optional Defender scanning and PHP retirement | 🔄 | #30–#42 merged; detailed parity, tool/test API migration, COM retirement and expanded requirements remain |
| **2 · Scripting** | PowerShell event host replacing VBScript/JScript | 📋 | |
| **3 · Protocols and data integrity** | Safe UID allocation/generation renewal, identifier boundaries, SMTP/IMAP/authentication extensions, MTA-STS, DANE, DKIM Ed25519 | 📋 | UID plan added; implementation not started |
| **4 · Built-in database** | SQLite replacing SQL Server Compact | 📋 | |
| **5 · Code quality** | Ownership, `/W4`, smaller modules, more of the portable core, more fuzzing | 🔄 ongoing | |
| **6 · Features** | Sieve, DMARC aggregate reports, ARC, webmail | 📋 | |

---

## 4. Phase 0 · Foundation ✅

| Item | PR | What changed |
|---|---|---|
| 0a Installer | #4 | Inno Setup 6.7.3, downloaded from its GitHub release and hash-pinned. No pre-Windows 10 runtimes or UCRT redistributables. `x64compatible` architecture. |
| 0b Toolset | #5 | Visual Studio 2026 toolset **v145** and **C++20** with `/permissive-` (`ConformanceMode`). `_WIN32_WINNT 0x0A00`. The `BOOST_USE_WINAPI_VERSION 0x0600` override in `stdafx.h` is gone: Boost 1.92 Atomic needs `WaitOnAddress`. Boost is built as `msvc-14.5` / `vc145`. `IntDir` is pinned to `$(Platform)\$(Configuration)\` so `hMailServer.tlb` stays beside the exe. `SQLCEConnection.cpp` needs `/Zc:strictStrings-`. |
| 0b2 Hardening | #6 | `/sdl`, `/guard:cf`, `/Qspectre` and `/CETCOMPAT` on both C++ projects. Fixed an `@@IDENTITY` BSTR leak in the ADO and SQL CE connections. `BinaryHardening` tests check the PE flags. `/W4` is left for Phase 5. |
| 0c1 Tools on .NET 10 | #7 | SDK-style `net10.0-windows` projects with explicit item lists, output to `bin\x64\<Config>`. Setup bundles the Windows Desktop Runtime installer (`libraries\fetch-dotnet-runtime.ps1`, SHA-512 pinned). Detection looks for `Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\10.*` or the 32-bit registry view, because the .NET installers register x64 runtimes there. `Rijndael` became `Aes`, compatible with existing saved passwords. |
| 0c2 Tests on .NET 10 | #8 | Test projects are SDK-style `net10.0-windows`; versions pinned in `hmailserver/test/Directory.Packages.props` (NUnit 5, NUnit console runner 3.22 with the net10.0 agent, NLog 6, Microsoft.PowerShell.SDK 7.6 for VMTestRunner). All classic asserts were rewritten to `Assert.That`. The COM interop is generated by `Interop.hMailServer.targets` from the registered type library, not a `COMReference`. |
| Debug build fix | #10 | `PersistenceMode.h` defined `new` as `DEBUG_NEW` in a header, which broke Boost 1.92's placement new. `build.ps1` builds Debug by default; CI builds Release. |

**Lesson from 0c2:** on .NET, an exception on a thread-pool thread kills the process (the .NET
Framework NUnit agent used to swallow it). `BeginAcceptSocket` + `EndAcceptTcpClient` threw this
way in `TcpServer.cs` and took the test agent down.

---

## 5. Phase 1 · Security ✅

### 1.1 SQL injection: bound parameters on every database ✅

**The problem.** MySQL and PostgreSQL reported `GetSupportsCommandParameters() == false`, so
`SQLStatement::GenerateFromCommand` built the SQL text by replacing each `@param` in turn and
escaping the value naively. A value that contained the *name of a later parameter* was replaced
again and broke out of its quotes. Example: greylisting with the sender
`x@RECIPIENTADDRESS` put the recipient outside the string literal. The same bug is in upstream
hMailServer.

| Step | PR | What changed |
|---|---|---|
| 1.1a | #9 | `GenerateFromCommand` substitutes in a single pass, whole tokens only. `SQLStatementTester` (7 cases) runs through `Utilities.RunTestSuite`. |
| CI on PostgreSQL | #11 | `tests.yml` takes a `database` input. Job *Tests (PostgreSQL)* starts the runner's `postgresql-x64-*` service and creates the database through `CreateExternalDatabase(3, ...)`. |
| 1.1b PostgreSQL | #12 | `PQexecParams` through `SQLStatement::ToPositionalParameters`, which refuses a parameter inside a literal. The fetch-account `INTERVAL '@MINUTES minutes'` became `faminutes * INTERVAL '1 minute'`. `PQconnectdbParams` in UTF-8. Backslashes are no longer doubled (`standard_conforming_strings=on`). Also fixed `Unicode::WideToMultiByte`/`MultiByteToWide` on empty strings. |
| CI on MySQL | #13 | MySQL is initialised in `RUNNER_TEMP` (port 3307, root without password) and **run as a Windows service** (`mysqld --install ArzoieTestMySQL --defaults-file=...`) with `--skip-log-bin --innodb-flush-log-at-trx-commit=2`. |
| 1.1c MySQL | #17 | Prepared statements through `mysql_stmt_*`. `MySQLStatement.cpp` is the only file that includes MariaDB's `mysql.h` (per-file include directories in the vcxproj). Every column is fetched as a string. `ToPositionalParameters` has a `?` placeholder mode. Tests in `API/SqlValues.cs`. |
| 1.4 libmysql fallback removed | #19 | Only `libmariadb.dll` is loaded, and every export it needs must be present. `GenerateFromCommand`, `Escape` and `EscapeString` are removed: the data access layer refuses a command whose parameters it cannot bind. |
| Last `Escape` call sites | #20 | Security ranges, IMAP folders and rule actions use `EscapeLike` with `ESCAPE '/'` (MySQL treated backslash as an escape). `Property::WriteStringSetting_` binds its name. Tests in `HierarchyDelimiter.cs` and `AutoBan.cs`. |

**Lessons:**
- **The runner kills processes a step started when the step ends.** A `Start-Process mysqld`
  was gone by the next step ("Can't connect ... (10061)"), and every test failed in setup after a
  76-second timeout. Run long-lived helpers as services.
- **`Unicode::WideToMultiByte` leaves the terminating NUL in the result's size.** MySQL rejected
  the value ("Data too long"). Strip it; `Unicode::ToUTF8` does.
- **The CI cache pruning removed the MariaDB headers.** The cache keeps the include folders, and
  its key has a `headers` marker.

### 1.2 Secrets: AES-256-GCM under a DPAPI-protected key ✅

**The problem.** Database password (in the ini), route and external account passwords,
encrypted settings, the backup XML and the COM `BlowfishEncrypt` all used Blowfish with the
fixed key `THIS_KEY_IS_NOT_SECRET`, the same in every installation and public in the source.

| Step | PR | What changed |
|---|---|---|
| 1.2a | #21 | `SecretProtector`: AES-256-GCM. A random 256-bit key per installation in `hMailServer.ini` `[Security] SecretKey`, protected by **machine-scope DPAPI**. `Crypt::ETSecret = 5`. `SecretMigration` re-protects Blowfish values at startup, after the database connects (errors 5739/5740). Without a key it can keep, it falls back to Blowfish. Fixed the ini reader, whose 255-character buffer truncated the DPAPI blob. |
| 1.2b | #22 | `Utilities.ExportSecretKey` / `ImportSecretKey` / `SecretKeyId` (administrators only). PBKDF2-SHA256 with 600,000 iterations + AES-256-GCM. Previous keys are kept for the run so secrets can be re-protected. Administrator page *Advanced › Secret key*. Upgrade notes. |
| 1.2c | #26 | Passphrase-protected backups (below). |

**Formats:**

| Value | Format |
|---|---|
| Protected secret | `aes1:<key id, 8 hex>:<base64(nonce 12 ‖ ciphertext ‖ tag 16)>` |
| Exported key | `arzoiemail-key1:` … (PBKDF2-SHA256 600k + AES-256-GCM) |
| Legacy secret | Blowfish, hex digits only; still read, re-protected at startup |

Minimum passphrase length for key export and backups: `SecretProtector::MinimumPassphraseLength`
(12).

#### 1.2c Passphrase-protected backups ✅ (#26)

- **With a passphrase**, 7-Zip encrypts the whole archive with AES-256, file names included
  (`-p`, `-mhe=on`). Messages go into the archive rather than beside it (compression is forced
  on). The secrets inside are plain, so the backup restores on any computer that has the
  passphrase.
- **Without one**, secrets in the backup XML are protected under the installation's key, as in
  the database, and restore on the same installation, or on another after importing the key.
- `BackupInformation` records which: attribute `Secrets="Archive"` or `"InstallationKey"`. A
  backup from an earlier version has neither and restores in `Legacy` mode: account and route
  passwords Blowfish (or under the key), settings plain. See
  `Server/Common/Application/BackupSecrets.h`.
- The passphrase is quoted for the C runtime's command-line rules and masked as `-p***` in every
  logged 7za command line (`ProcessLauncher::SetLoggedCommandLine`). Reading an archive without a
  passphrase still passes one, so 7za fails instead of waiting for input.
- COM: `BackupSettings.Passphrase` (write-only, empty removes it), `BackupSettings.PassphraseIsSet`,
  `BackupManager.LoadEncryptedBackup(file, passphrase)`.
- Administrator: *Encrypt with a passphrase* with a confirmation field on the Backup tab; a
  passphrase field and *Open* on the Restore tab.
- Schema **5714**: the `backuppassphrase` setting, stored as a secret.
- Tests: `Infrastructure/BackupPassphrase.cs`.

**Lessons:**
- **The scripts that create the database are split into statements at blank lines**, not at
  semicolons. A new `insert` needs a blank line before it, or it runs together with the previous
  statement: MySQL rejects the script and the built-in database is not created at all.
- **A COM class must implement `ISupportErrorInfo`** for the text of the errors it returns to
  reach callers. Without it they see only the HRESULT (`0x800403E9`).

### 1.3 Administrator saved passwords → DPAPI ✅ (#23)

`Tools/Administrator/Utilities/Encryption.cs` had a hard-coded AES key. Saved server passwords now
use DPAPI in the current user's scope (`CryptProtectData` through P/Invoke), format `dpapi1:`.
`UserSettings.Load` re-protects values saved the old way.

### Argon2id review ✅ (#24)

Argon2id is already the default (`PasswordHasher`, the OpenSSL KDF, with an automatic upgrade at
logon). The review found one issue: **logon timing revealed which accounts exist**, because an
unknown or inactive account skipped the hash. `PasswordHasher::SpendVerificationTime` now spends
the same time. Test: `Security/LogonTiming.cs`.

### Client credentials as UTF-8 ✅ (#25)

The SMTP client (route `AUTH LOGIN`) and the external-account POP3 client (`USER`/`PASS`) sent
credentials in the system code page: `ä` became one byte servers did not read as `ä`, and a
character outside the code page was dropped. They now go as UTF-8 (`Unicode::ToUTF8`). Test:
`Security/ClientCredentialsEncoding.cs`.

**Lesson:** a test that fetches or receives a message must wait for local delivery before it
ends (`Pop3ClientSimulator.AssertMessageCount`). Otherwise the next test's setup deletes the
account mid-delivery, the server logs HM5165, and the *next* test fails in teardown.

---

## 6. Test environments

- **GitHub Actions**: on `windows-2025` with VS 2026, the release build, the installer and the
  regression suite on the supported database jobs; on Linux, the portable core with gcc and clang
  (`portable-core.yml`). The runner has the full VC++ redistributable,
  which hides missing-DLL problems in the installer.
- **Hyper-V virtual machines** through `test/VMTestRunner.Console` (`TestEnvironments.json`),
  which install a real installer on a clean Windows and run the suite there. The current VM is
  Windows Server 2025 (Generation 2, 4 vCPU, 4 GB) on an internal switch with NAT, restored from
  a checkpoint named *Ready* before each run. Notes:
  - It found a real installer bug at once: `hMailServer.exe` imports
    `msvcp140_atomic_wait.dll`, which setup did not ship (#14, which also checks `/Register`'s
    exit code and adds a `dumpbin` import check to CI).
  - Spamhaus and SURBL refuse queries that come through public resolvers, which failed 17
    AntiSpam tests. The VM runs its own Windows DNS Server (root hints, no forwarders).
  - The test output's `nunit.engine*.dll` must not overwrite the console runner's: the console is
    copied last (#15). `gflags` is looked up only when page heap is used, and setup runs with
    `/SUPPRESSMSGBOXES`.
  - MySQL in a VM needs the VC++ redistributable DLLs copied into its `bin` folder.
  - Credentials for test VMs never go into the repository.

---

## 7. Phase 1b · Web admin and REST API 🔄

`hmailserver/source/WebAdmin`: 89 PHP files with dtree.js, tabber and a custom `style.css`,
talking to the server through `new COM("hMailServer.Application")`.

The maintainer expanded this phase on **2026-10-26**: deliver a powerful, modern panel with
advanced fine-tuning, a REST API from the first release, integrated SpamAssassin administration,
multiple administrator-configurable DNSBL providers, and SPF/DKIM/DMARC DNS recommendations
and validation, including complex SPF shared with other providers.

The detailed requirements and open architecture decisions are in
[WebAdmin requirements](docs/webadmin-requirements.md). REST moves here from Phase 6. Preserve
temporary COM compatibility in current code; all supported tools/tests/automation migrate to the secured management API and COM is removed before the 6.0 baseline.
The maintainer has authorized implementation and revised the backend: ASP.NET Core / .NET 10,
with a restricted local native management channel and no COM dependency in the replacement
panel/API. COM remains only in intermediate development code pending complete 6.0 retirement. See
[management architecture](docs/management-api-architecture.md). Implementation is in progress;
PR #31 merged the native/API/panel foundation with passing CI. Engine bundling, remaining
administration capabilities, certificate lifecycle and installer provisioning are not complete.

The maintainer confirmed full PHP retirement on **2026-10-26**. Complete the replacement and
migration acceptance tests, then remove legacy WebAdmin source, packaging, PHP runtime checks
and deployment requirements. Existing PHP administration remains temporary until that cutover;
unrelated PHP installations are outside scope. The earlier PHP-version modernization goals
are superseded. The 2026-10-28 decision now also requires migrating desktop tools/scripts and removing COM entirely for 6.0; see the frozen scope and breaking-change register.

Certificate installation must be user-friendly from the replacement's first production release:
a guided import or automatic issuance workflow, hostname/chain/key validation, SMTP/IMAP/POP3
listener assignment, expiry and renewal status, automatic renewal, safe activation and rollback.
IIS HTTPS bindings and mail TLS bindings are managed separately. Certificate automation is a
remaining delivery item, not a capability delivered by #31. Detailed acceptance requirements
are in [WebAdmin requirements](docs/webadmin-requirements.md#certificate-setup-and-lifecycle).

The maintainer also requested optional **Microsoft Defender Antivirus** email scanning. Repository
and Microsoft-documentation review supports feasibility through a dedicated native adapter using
existing message/decoded-attachment scan hooks. Add guided setup, health/test results, explicit
error/unscannable handling and VM acceptance; integration is not implemented. The maintainer
requires high-pressure coverage across receive/send queues, server-saved drafts and intermediate
states: durable scheduling, bounded workers, backpressure, verdict gates and measured capacity
with at least 10,000 messages in flight/queued in acceptance testing. Do not claim Defender
throughput before lifecycle, burst, outage/recovery and soak benchmarks pass. See
[Defender assessment](docs/defender-antivirus-feasibility.md).

### Current completion authorization

After #43 merged at `92ac28d`, the maintainer requested all remaining Phase 1b. On 2026-10-30 the
maintainer chose one checkpoint PR per coherent bundle instead of a single completion PR; the first
checkpoint, `feature/phase1b-completion`, merged as #45 (`5cce5a906`). The second, bundle 1 (server and
protocol operations: status/control, remaining SMTP/IMAP/performance settings, server messages,
sendout, diagnostics, logs), merged as #46 (`2a2b23b6f`). The third, bundle 2 (external POP3 fetch
accounts, groups and public folders with permissions, backup/restore and the installation key), is
`feature/phase1b-fetch-folders-maintenance`, merged as #47 (`a72e32cbc`). Bundle 3a (spam allowlists,
antivirus tests, DKIM keys) merged as #48 (`79df40565`). Bundle 3b, the native Bayesian spam filter,
is `feature/phase1b-bayes`. The maintainer's delivery decisions (review
gate per bundle, native spam filtering with optional user-installed SpamAssassin/ClamAV, ACME in the
API, Defender in roadmap order) are in the
[6.0 scope](docs/version-6-scope.md#delivery-decisions-2026-10-30). No checkpoint claims Phase 1b completion. Typed server settings,
checked persistence and three filtering/relay collections passed initial Compact VM validation
(630 native assertions plus HTTP/TLS/browser coverage; see the CI inventory). Mailbox self-service
and restricted account-field writes passed the subsequent 656-assertion VM/browser run.
Password-policy/IP security-range and logging additions passed subsequent VM/browser checks
(latest 736 native assertions). A same-password-reset session regression was reproduced and
its native/vault credential-binding fix passed the full 739-native/145-live-HTTP VM run.
An additional 43 controlled-clock session checks and affected integration tests passed;
database corrections at `bc3980201` passed all four backend jobs, Release, GCC/Clang and CI
installer generation. A subsequent eight-field mail TLS section adds certificate verification,
version/cipher policy and checked dependency validation; the full VM run (781 native) and
four-backend CI passed at `82a3bdaf5`. The detailed ledger
continues to own every remaining A/E gate; nothing is complete merely because scope was approved.
Subsequent listener/certificate persistence regressions reproduced two false-success saves on
real Compact write rejection. Error propagation is corrected and both focused VM cases passed,
including collection non-publication and successful retry. A disconnected-manager regression
also reproduced unsafe upgrade-prerequisite success; both upgrade/script entry points now
refuse missing connections and TestInternals passed in the VM. All four backend jobs, Release,
GCC/Clang and CI installer generation passed these corrections at `170442658`;
see the handoff and CI inventory for exact revision attribution.
The next transport-inventory addition exposes bounded server-only configured listener and
certificate metadata, with exact string IDs and no private paths/file reads. Native/managed
compilation and the full VM/native/REST/browser run passed (831 native,449 HTTP/security).
All four backend jobs, Release, GCC/Clang and CI installer generation passed at `8cdc085b4`. This supports future
guided setup; it does not deliver certificate provisioning, assignment or activation.
The subsequent bounded PFX inspection workflow adds a protected upload form, public metadata/
host/time/usage/chain warnings, strict secret-owning JSON input and resource guards. Its final
VM run passed 546 HTTP/security,60 parser,831 native and145 real HTTP assertions, both browsers
and selected TestInternals, with private fixtures and checkpoint cleaned up. Four-backend CI at
`ae903a182` passed 1685 separate assertions, both browsers and the 1319-case NUnit suite
(skips/inconclusive separately recorded in the CI inventory). Protected native installation/
assignment/renewal/rollback and IIS acceptance remain open.
The next native prerequisite fixes 32-bit narrowing in certificate/listener IDs, certificate
references, returned insert identities and backup certificate-name lookup. A real-provider
Compact VM TestInternals regression failed before the fix and passed afterward; original
service files and the temporary checkpoint were restored. This is not a schema change or
COM interface expansion. Its own four-backend CI is still pending; other high-ID persistence
and legacy COM long properties remain separate migration work.

### Phase 1b delivery tracker

Use the [detailed capability ledger](docs/phase-1b-status.md) for F01-F17 delivered boundaries,
A01-A20 remaining administration work, E01-E10 expanded requirements, the complete 89-PHP-file
and 54-desktop-pane checklist, suggested grouping and completion gates. This table is its summary.
The six broad workstreams are not six fixed PRs. The current authorization covers all remaining
Phase 1b, following merged #43; it does not authorize the next implementation phase.

Requirements documented in an open PR do not mean the functionality is implemented. Merged PR #32
records certificate, PHP-retirement and Defender requirements; it does not implement them.
The maintainer authorized continued Phase 1b work with SQL Server Express modernization on
2026-10-27. The database slice was merged as #33 with all four database jobs green. The
maintainer then requested the CI inventory/documentation refresh before domain/account code;
that work merged as #34. The maintainer subsequently authorized beginning the domain/mailbox
slice with “begin phase 1b”. Deliver this slice in one PR, monitor/fix CI and wait for explicit
confirmation before the next task. A green PR does not authorize merging. PR #35 was merged as
`7d00d2d8b3794aaf2ae8bad3c5e35b5625bd68f0` after all required checks passed. The maintainer's
subsequent “continue” authorized the alias/distribution-list/member slice, merged as #36
(`17a9b6f63cf514c644b7cfd1e3276fa7f4dc55f0`) after all four database jobs passed. Dependabot
CI dependency update #37 also merged after compatibility review, workflow lint and full CI;
actual SignPath certificate signing remains a separate master-only manual validation.

| Item | Status | Evidence or remaining acceptance |
|---|---|---|
| Native management boundary, REST/session security and modern panel foundation | ✅ Done | #31 merged; VM and CI checks passed. This is a foundation, not full administration parity. |
| SQL Server Express modernization | ✅ Done | #33 merged; OLE DB 19, required TLS/certificate validation, migration diagnostics and SQL Server 2025 Express VM/CI coverage passed. Other engine versions and workload capacity still need separate validation; see [SQL Server guide](docs/sql-server.md). |
| Basic domain/mailbox administration | ✅ Done | [PR #35](https://github.com/jassifx/ArzoieMail/pull/35) merged; native CRUD, password resets, roles/quotas, bounded responses and responsive panel passed VM/CI acceptance. |
| Aliases, distribution lists and member administration | ✅ Done | [PR #36](https://github.com/jassifx/ArzoieMail/pull/36) merged; scoped native/REST CRUD, posting controls, members, search and panel confirmations passed VM/four-backend CI. New aliases/lists default disabled; lists default members-only with SMTP authentication required. |
| Mailbox forwarding and automatic replies | ✅ Done | #39 merged; native/REST/panel controls, domain/target-role checks, validation and local SMTP/POP3 coverage passed VM and all four CI backends. 358 management assertions per backend, browser checks and separate VM pipe-identity checks passed. Mailbox-user self-service remains planned. |
| Mailbox signatures | ✅ Done | #40 merged; scoped native/REST controls, inert source editor, SMTP/MIME and browser safety/newline tests passed VM and all four CI backends (404 management assertions per backend). |
| Advanced domain/mailbox administration bundle | ✅ Done | #41 merged as `5294d67a09823a286dd3f1f6b477772bb4b5388f`; profiles, catch-all/plus/greylisting, object limits, domain signatures and alternate names passed VM and all four CI backends (528 management assertions per backend). |
| Mail operations bundle | ✅ Merged #42 | Queue metadata/recipients, guarded retry/removal, routes/recipients, global/scoped rule drafts and activation; native/REST/panel regressions. Final tested head `28dd3df912fac9109260efe9f5571dc05b402d12` passed all four backends (1317 NUnit cases, zero failures/warnings, 897 management assertions and both browser suites), Release, portable builds, workflow lint, CodeQL and CI installer publication. Merge `74eecc017`. Queue metadata scans and workload acceptance remain follow-ups. |
| Checkpoint 1: typed settings, self-service, mail TLS, transport inventory, PFX inspection | ✅ Merged #45 | 81 typed settings, SURBL/attachment/relay collections, IP security ranges, mailbox self-service, global mail TLS, read-only transport inventory and PFX inspection; head `3c07946f3` passed four-backend CI and CodeQL. Merge `5cce5a906`. |
| Checkpoint 2: server status/control, protocol settings and server operations (A01, A02/A03, A05, A13, A14, A16, A20) | ✅ Merged [#46](https://github.com/jassifx/ArzoieMail/pull/46) (`2a2b23b6f`) | Status counters and mail-service stop/start, 11 more settings (92), write-only smart-host credential, Administrator password, server messages, sendout, background diagnostics, mail-server lookup, bounded log reading; worker thread priority and the IP-home handler retired (BC-009). Head `b65cc8555` passed four-backend CI (963 native, 182 live HTTP assertions per backend, both browser suites); PR head passed CodeQL. Remaining per row is in the ledger. |
| Checkpoint 3: fetch accounts, groups, public folders and maintenance (A07, A10, A15) | ✅ Merged [#47](https://github.com/jassifx/ArzoieMail/pull/47) (`a72e32cbc`) | Head `aa126ef45` passed four-backend CI (1039 native, 204 live HTTP assertions per backend, both browser suites). Per-mailbox POP3 fetch accounts (write-only password, encrypted logon for new choices, download now), groups/members, public folders and their IMAP permissions, backup settings/start/restore with typed confirmation, installation-key export/import; real IMAP LIST, encrypted backup and key round trip in CI. Remaining per row is in the ledger. |
| Checkpoint 4: spam allowlists, antivirus tests and DKIM (A11, A12, E01, E02 integration) | ✅ Merged [#48](https://github.com/jassifx/ArzoieMail/pull/48) (`79df40565`) | Final head `ce39cddde` passed four-backend CI (1086 native, 224 live HTTP assertions per backend, both browser suites) and CodeQL. DKIM keys are RSA 2048 only. Spam allowlist and greylisting exceptions, antivirus provider tests, DKIM key generation with DNS-gated activation and rotation, GTUBE-aware SpamAssassin testing; a server-signed message verified by an independent RFC 6376 check in CI. |
| Checkpoint 5: native Bayesian spam filter (E02 default, A11 guidance) | 🔄 Bundle 3b, `feature/phase1b-bayes` | Schema 5715 token table on all four databases; a post-transmission spam test; training from IMAP copies into and out of Junk folders and from a mailbox folder on one background worker; statistics, reset and settings in native, REST and panel; scoring guidance. A live SMTP/IMAP test trains, scores and resets on every backend. |
| Remaining advanced administration | 📋 Planned | A08, A12, A18 and A19 decisions fold into the nearest bundle. No production PHP cutover yet. |
| Complete SPF/DKIM/DMARC setup workflows | 📋 Planned | Initial guidance exists; finish key lifecycle, provider coexistence and protocol-conformance acceptance. |
| Spam engine | 🔄 In progress (bundle 3b) | Decided 2026-10-31: SpamAssassin and ClamAV are not shipped; they stay optional, administrator-installed integrations (3a, merged, improved their tests and guidance). 3b makes the native Bayesian filter the default; content rules are the existing global and mailbox rules. Limits: one global model, no untraining, no token expiry. |
| Guided TLS certificate installation and renewal | 🔄 In progress | Configured inventory/PFX inspection passed checkpoint CI; [storage proposal](docs/certificate-storage-design.md) has a bounded CNG VM probe. Native import/storage adapter, issuance, assignment, renewal, safe activation/rollback and hosting acceptance remain unfinished. |
| **Microsoft Defender Antivirus email scanning** | **📋 Planned — implementation not started** | Dedicated native adapter, lifecycle coverage, durable scheduling, mitigation and measured high-pressure acceptance; requirements/assessment in #32. |
| Production hosting, installer and PHP retirement | 📋 Planned | Restricted identity, HTTPS/runtime provisioning, full parity, upgrade acceptance and removal of the legacy PHP endpoint/source/packaging. |

### Microsoft Defender Antivirus delivery checklist

| Deliverable | Status | Completion gate |
|---|---|---|
| Feasibility and requirements | ✅ Documented | #32 merged; service permissions, result classification and engine behavior still require VM implementation validation. |
| Native scanner adapter and result handling | 📋 Planned | Distinct clean/infected/error/unscannable results; bounded execution and no destructive scans of live mail storage. |
| Message lifecycle coverage | 📋 Planned | SMTP receipt/submission, queued sending, external fetching, server-saved drafts/IMAP APPEND, imports and changed content; valid verdict before release. |
| High-pressure queue and workers | 📋 Planned | Durable state, bounded resources, fair scheduling, backpressure, restart recovery and no scheduler-induced duplicate release. |
| Mitigation and recovery | 📋 Planned | Separate detection/error policies, bounded hold/retry, consistent quarantine/attachment handling, retention and audit controls. |
| Modern panel and REST controls | 📋 Planned | Server-admin-only setup, enable/disable, test scan, engine/signature health, results and queue/latency/overload diagnostics. |
| VM regression and capacity acceptance | 📋 Planned | At least 10,000 queued/in-flight messages, mixed receive/send/draft activity, bursts, outage/recovery and soak; publish hardware, message mix, throughput, p95/p99 latency and resource use. |

Defender stays optional and is not claimed to meet a production load until the measured
acceptance envelope passes. All executable validation remains VM/CI-only. Other scanner choices
remain available if Defender cannot meet the agreed workload.

---

## 8. Later phases 📋

### Phase 2 · PowerShell scripting
A persistent, out-of-process PowerShell event host replaces VBScript/JScript. Events keep their
names and arguments where possible (`OnAcceptMessage`, `OnDeliverMessage`, `OnBackupFailed`, ...),
with a migration guide for existing scripts. A script that hangs or crashes must not affect the
server.

### Phase 3 · Protocols
- Message identifier and IMAP UID lifecycle modernization: [delivery plan](docs/imap-uid-modernization.md).
  Audit 64-bit IDs end to end; fix signed/narrowing defects and UIDNEXT boundaries; add checked
  transactional per-folder allocation and controlled UIDVALIDITY generation renewal, recovery,
  monitoring and client interoperability. Boundary fixes may be brought forward through Phase 5
  after authorization. No wrap, silent UID reuse or manual SQL resets; do not claim unlimited
  per-folder capacity or invisible client resynchronization. Full acceptance gates high-volume
  production-readiness claims, independently of PHP retirement and the SQLite migration.
- SMTP: PIPELINING, 8BITMIME, ENHANCEDSTATUSCODES, CHUNKING, DSN, SMTPUTF8.
- IMAP: ENABLE, MOVE, LITERAL+, CONDSTORE/QRESYNC, UTF8=ACCEPT.
- Authentication: SCRAM-SHA-256.
- Transport security: MTA-STS, DANE.
- DKIM with Ed25519.

### Phase 4 · SQLite
SQLite replaces SQL Server Compact as the built-in database with tested automatic migration,
backup/integrity checks and failure recovery. Normal regression lanes become SQLite, MySQL,
PostgreSQL and Express; retain separate Compact migration/recovery coverage.

### Phase 5 · Code quality (continuous) 🔄
Finite mandatory 6.0 closure is V6-05 in the [scope checklist](docs/version-6-scope.md);
module ownership, warning exceptions and fuzz budgets must be specified and accepted before closure.
Replace `catch (...)` that hides errors, move to `std::unique_ptr` / clear ownership, compile at
`/W4`, and split `Common/Util`. So far it has come along with other work: the `@@IDENTITY` BSTR leak
(#6) and the Debug build (#10).

There is a base to build on, from upstream: a **portable core** (`Server/Portable`) that builds with
gcc and clang on Linux and runs `PortableCoreTests` - the *gcc* and *clang* CI checks - and
**libFuzzer fuzzers** for the SPF record parser and macro expander (`Server/Portable/Fuzz`). The plan
is to move more of the protocol parsing into the portable core (MIME, the SMTP and IMAP command
parsers, DKIM) and fuzz each part that reads bytes from the network.

### Phase 6 · Features
Sieve filtering, DMARC aggregate reports, ARC and optional integration of an existing maintained
open-source webmail application (PHP permitted; application not yet selected). REST API delivery and domain DNS
setup/validation have moved to Phase 1b; DMARC aggregate reporting remains here.

---

## 9. Open follow-ups ⏳

- Make the PostgreSQL, MySQL and SQL Server Express test jobs required checks in the `master`
  protection rule; workflow publication already waits for all four database jobs.
- Commit the Hyper-V VM setup scripts (switch with NAT, VM creation) to the repository.
- Documentation product prose is rebranded in PR #34, with current installation/security/database
  guidance and explicit historical/reference boundaries. Complete the remaining technical review,
  screenshots, route/rendering checks and website publication in
  [documentation status](docs/documentation-status.md). Application/UI/logo renames remain a
  separate gradual task; compatibility identifiers stay unchanged.
- Upgrading from old MySQL server versions with the MariaDB client is untested in a VM.
- Re-audit CodeQL observations #1–#3 at the end of modernization, as requested by the maintainer.
  Confirm which source/packaging was removed and which native paths remain; do not auto-dismiss
  or assume PHP retirement removes shared C++ issues. Findings are reviewed, not all confirmed
  exploitable vulnerabilities; native fixes still need regression evidence when scheduled.
- Correct and publish `maintenance_database` after the identifier/UID review. Its global-ID
  explanation and `MAX(messageid)` advice are not a current IMAP UID-capacity assessment.

---

## 10. Working notes for contributors and agents

- **Schema changes** follow the checklist in AGENTS.md: upgrade scripts for all four database
  types, the three `CreateTables*.sql`, `REQUIRED_DB_VERSION`, and DBUpdater. Remember the blank
  line between statements. The current schema version is **5715** (the native Bayesian filter's
  `hm_bayes_tokens` table and five `bayes*` settings).
- **Naming**: the product is ArzoieMail, but some identifiers keep the hMailServer name on purpose
  (`hMailServer.exe`, service/INI names and installer identity). AGENTS.md lists them;
  COM is explicitly excluded and retires under V6-COM. Do not rename retained identifiers.
- **Do not add COM features.** Migrate supported tools/tests/automation through secured
  management contracts and preserve regression coverage; E10/V6-COM own complete retirement.

- **`Formatter::Format`** takes at most five arguments.
- **Secrets** are never logged and never written plain: use `Crypt::ETSecret`
  (`SecretProtector`), and route anything that goes into a backup through `BackupSecrets`.
- **Tests that need the internet** should be avoided; when unavoidable, list them in the test
  baseline above.
- Line endings follow `.gitattributes`; source files are CRLF in the working copy.
