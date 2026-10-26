# ArzoieMail engineering roadmap

This is the working roadmap behind the short table in [README.md](README.md#roadmap). It is
written for the people and AI agents who work on ArzoieMail. It records what has been done and
why, the decisions that are settled, the details of each change, the lessons learned on the way,
and the plan for what comes next.

Read [AGENTS.md](AGENTS.md) for the layout of the code and [BUILDING.md](BUILDING.md) for how
to build, run and test it. **Keep this file current:** a pull request that finishes or changes a
roadmap item updates its entry here.

Status: ✅ done · 🔄 in progress · 🔜 next · 📋 planned · ⏳ open follow-up

---

## 1. How we work

- **One change, one pull request** to `master`. `master` is protected: it takes a pull request
  with passing CI checks. Merged branches are deleted.
- **Every bug fix and every feature has a regression test** (see AGENTS.md). For a bug fix the
  test is written first and seen to fail.
- **Branch names decide whether CI runs.** The workflows run for `master`, `claude/**`,
  `feature/**`, `techdebt/**`, `bugfix/**` and the version branches. A branch named otherwise,
  for example `security/...`, gets no CI.
- **CI runs the regression suite three times**: on the built-in database (SQL Server Compact),
  on PostgreSQL and on MySQL. A change is ready when all three are green.
- **Phases are confirmed before they start.** The order below is agreed; the start of each phase
  is a separate decision.

### Test baseline

The regression suite on `master`, after Phase 1, on each of the three databases:

| Total | Passed | Failed | Inconclusive | Skipped |
|---:|---:|---:|---:|---:|
| ≈1311 | ≈1200 | 0 | 34 | 75 |

The 34 inconclusive and 75 skipped tests are expected: they are explicit detail tests and
environment-dependent tests. A pull request should keep *Failed* at 0 and move *Passed* only up.

Tests known to depend on the live internet and that can fail by themselves on CI, in which case
the failed job is re-run:

- `AntiSpam.AuthenticationResults.TestHeaderIsAdded` (live outlook.com SPF lookups).
- `Infrastructure.DnsResolution.CNameRecordsShouldBeFollowed` (live hmailserver.com DNS).

---

## 2. Settled decisions

These were decided deliberately; reopen them only with a new reason.

| Decision | Why |
|---|---|
| **Active Scripting (VBScript/JScript) is replaced by a PowerShell event host**, persistent and out of process. Chosen over embedded JavaScript and over webhooks. | Windows disables VBScript by default from about 2027 and will remove it. PowerShell is what Windows administrators already script in, and out of process a script can no longer crash or stall the server. |
| **Admin tools and tests run on .NET 10 LTS** (supported until 2028-11-14), not .NET Framework 4.8.1. | A current runtime, SDK-style projects and current libraries. |
| **The installer bundles the .NET 10 Desktop Runtime** and installs it silently when missing, rather than downloading it on demand. | Installs work offline; setup grows by about 60 MB. |
| **SQLite replaces SQL Server Compact** as the built-in database, with automatic migration. | SQL Server Compact is out of support. |
| **The SQL injection in string-built SQL was fixed publicly**, with no private report upstream first. | It is fixed in ArzoieMail's code; upstream hMailServer is no longer maintained. |
| **Secrets use AES-256-GCM under a random per-installation key protected by machine-scope DPAPI**, with passphrase export and import of the key. | The old Blowfish key was the same in every installation and is in the public source. DPAPI ties the key to the machine; export moves it to a new one. |
| **Backups get their own passphrase**, independent of the installation's key. | A backup must restore on a new computer, which does not have the old key. |
| **Windows 10 / Server 2016 is the minimum**, x64 only. | Everything older is out of support. |
| **The web admin requires PHP 8.5.12 or later.** | The 2026-10-21 PHP security release fixed two CVSS 9.2 remote code execution bugs (see Phase 1b). |

---

## 3. Phase overview

| Phase | Scope | Status | Pull requests |
|---|---|---|---|
| Pre-roadmap | libpq 18.6 + MariaDB Connector/C 3.4.11, Windows 10 minimum, ArzoieMail rebrand, README | ✅ | #1, #2, #3 |
| **0 · Foundation** | Toolset, C++20, hardening, installer, .NET 10, NUnit 5 | ✅ | #4–#8, #10 |
| **1 · Security** | Bound SQL parameters, AES-GCM + DPAPI secrets, key export, encrypted backups, Administrator DPAPI, Argon2id review, UTF-8 client credentials | ✅ | #9, #11–#15, #17, #19–#26 |
| **1b · Web admin** | Bootstrap UI, PHP 8.5.12+, CSRF, escaping, CSP, rate limiting | 🔜 | |
| **2 · Scripting** | PowerShell event host replacing VBScript/JScript | 📋 | |
| **3 · Protocols** | SMTP, IMAP and authentication extensions; MTA-STS, DANE, DKIM Ed25519 | 📋 | |
| **4 · Built-in database** | SQLite replacing SQL Server Compact | 📋 | |
| **5 · Code quality** | Ownership, `/W4`, smaller modules, portable tests, fuzzing | 📋 ongoing | |
| **6 · Features** | Sieve, DMARC reports, ARC, REST API, new web admin, webmail | 📋 | |

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

- **GitHub Actions** (`windows-2025` with VS 2026): the release build, gcc and clang checks, and
  the regression suite on the three databases. The runner has the full VC++ redistributable,
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

## 7. Next: Phase 1b · Web admin 🔜

`hmailserver/source/WebAdmin`: 89 PHP files with dtree.js, tabber and a custom `style.css`,
talking to the server through `new COM("hMailServer.Application")`.

Goals:
- Current **Bootstrap** and a modern, responsive interface, replacing dtree and tabber.
- **PHP 8.5.12 or later**: the web admin refuses to run when `PHP_VERSION_ID < 80512`. That
  release (2026-10-21) fixed CVE-2026-109618 (out-of-memory corruption RCE) and CVE-2026-109619
  (stale global state RCE), both CVSS 9.2, in FPM, mod_php and php-cgi (also fixed in 8.4.27,
  8.3.36 and 8.2.35). CVE-2026-115560 (session upload progress, 2026-10-23) is mitigated by
  configuration.
- Code that runs on both 8.5 and 8.6 (8.6 is expected in November 2026).
- The install guide sets, at server or pool level: `session.upload_progress.enabled=0`,
  `fatal_error_backtraces=0` and a small `post_max_size`. `memory_limit=-1` is deliberately not
  recommended.
- A diagnostics page that warns about unsafe ini values.
- CSRF tokens on every form, consistent output escaping, session hardening (cookie flags,
  regeneration at logon), a Content Security Policy and login rate limiting.

---

## 8. Later phases 📋

### Phase 2 · PowerShell scripting
A persistent, out-of-process PowerShell event host replaces VBScript/JScript. Events keep their
names and arguments where possible (`OnAcceptMessage`, `OnDeliverMessage`, `OnBackupFailed`, ...),
with a migration guide for existing scripts. A script that hangs or crashes must not affect the
server.

### Phase 3 · Protocols
- SMTP: PIPELINING, 8BITMIME, ENHANCEDSTATUSCODES, CHUNKING, DSN, SMTPUTF8.
- IMAP: ENABLE, MOVE, LITERAL+, CONDSTORE/QRESYNC, UTF8=ACCEPT.
- Authentication: SCRAM-SHA-256.
- Transport security: MTA-STS, DANE.
- DKIM with Ed25519.

### Phase 4 · SQLite
SQLite replaces SQL Server Compact as the built-in database. Existing installations migrate
automatically. SQL Server Compact keeps its upgrade scripts until then.

### Phase 5 · Code quality (continuous)
Replace `catch (...)` that hides errors, move to `std::unique_ptr` / clear ownership, compile at
`/W4`, split `Common/Util`, make the tests portable, and add fuzzing of the protocol parsers.

### Phase 6 · Features
Sieve filtering, DMARC aggregate reports, ARC, a REST API with a new web admin, webmail.

---

## 9. Open follow-ups ⏳

- Make the PostgreSQL and MySQL test jobs required checks in the `master` protection rule.
- Commit the Hyper-V VM setup scripts (switch with NAT, VM creation) to the repository.
- The documentation, the web admin and the logos still say hMailServer.
- Upgrading from old MySQL server versions with the MariaDB client is untested in a VM.

---

## 10. Working notes for contributors and agents

- **Schema changes** follow the checklist in AGENTS.md: upgrade scripts for all four database
  types, the three `CreateTables*.sql`, `REQUIRED_DB_VERSION`, and DBUpdater. Remember the blank
  line between statements. The current schema version is **5714**.
- **New COM API**: add it to `hMailServer.idl` with a new `id`, implement it in `Server/COM`, and
  make sure the class implements `ISupportErrorInfo` if it returns error messages.
- **`Formatter::Format`** takes at most five arguments.
- **Secrets** are never logged and never written plain: use `Crypt::ETSecret`
  (`SecretProtector`), and route anything that goes into a backup through `BackupSecrets`.
- **Tests that need the internet** should be avoided; when unavoidable, list them in the test
  baseline above.
- Line endings follow `.gitattributes`; source files are CRLF in the working copy.
