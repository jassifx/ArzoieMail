# ArzoieMail — Codebase Guide for Agents

ArzoieMail is a free, open-source email server for Microsoft Windows, licensed under AGPLv3. It carries on hMailServer's code after the 5.7.2 release, up to upstream `master` of 2026-10-11, as a project of its own. It implements SMTP, IMAP, and POP3, and runs as a Windows service. The codebase is written primarily in C++ (server core) and C# (admin tools), targeting C++20 with the Visual Studio 2026 v145 toolset, with 64-bit Windows builds.

**Release target: ArzoieMail 6.0.0**, independently versioned from hMailServer; the former
5.8.0 target is superseded by the maintainer's 2026-10-28 instruction. Read [VERSION](VERSION)
and [release versioning](docs/release-versioning.md). Product major, build number, schema 5715,
assembly identities and historical upgrade mappings are separate. Do not globally rename old
versions or compatibility identifiers, or publish a release without explicit authorization.

**Names that stay hMailServer.** The product is ArzoieMail in everything users read. Some identifiers keep the
hMailServer name on purpose, because existing installations, scripts and the web admin depend on them, or so that
the installer upgrades an hMailServer installation in place. Don't rename them without a decision to do so:

- the `hmailserver/` source folder and the project files (`hMailServer.sln`, `hMailServer.vcxproj`, `hMailServer.idl`,
  `hMailServer Tools.sln`, `hMailServer Tests.sln`, `hMailServer64.iss`), and the `%hMailServerLibs%` variable;
- `hMailServer.exe`, `hMailServer.ini`, `hMailAdmin.exe`, the Windows service name `hMailServer` (its display name is ArzoieMail) and
  `NT SERVICE\hMailServer`;
- non-COM registry identities needed for install/upgrade discovery; the COM API, type library
  and activation/registration are explicitly scheduled for removal in 6.0 after consumer migration;
- the installer's AppId, the `X-hMailServer-*` message headers, the `hm_` database tables, and `hMailServerBackup.xml`
  inside backups.

Fresh installations go to `Program Files\ArzoieMail` and `ProgramData\ArzoieMail`; the server and the installer still
find `ProgramData\hMailServer` in an upgraded installation. The source code and its comments still say hMailServer in
many places; that is renamed gradually. Retained identifiers are not renamed incidentally;
retiring COM may remove COM-only assets such as its IDL rather than preserving them indefinitely.

## Mandatory 6.0 scope and COM retirement

The [dated scope checklist](docs/version-6-scope.md) and [breaking-change register](docs/breaking-changes-6.0.md)
own the 2026-10-28 decisions: all current phases are mandatory, COM is completely removed
after supported desktop/DB/maintenance tools, test fixtures and automation migrate to secured
management APIs. Current COM code/tests still exist; do not remove consumers or coverage early
or add new COM features. Retain non-COM executable/service/INI/installer identities. PHP may
be used for optional existing open-source webmail, not the replacement administration panel.
CI test artifacts remain allowed; no development previews before feature completion. Runtime
execution remains VM/CI-only and every new implementation bundle needs authorization.

## Repository Layout

The repository root contains source, dependencies, build scripts, documentation and config files:

```
hmailserver/          # All source code, tools, tests, installation scripts
libraries/            # Vendored third-party libraries checked into the repo
build/                # Scripts for building and running tests.
docs/agents/          # Notes for agents: working agreement, environment, gotchas, session handoffs
.editorconfig
.gitattributes
.gitignore
BUILDING.md           # How to build, run, test and release
LICENSE               # GNU AGPLv3
README.md             # What the project is: roadmap, design decisions, getting started
ROADMAP.md            # Engineering roadmap: decisions, what each phase changed, lessons, what is next
```

Product source lives under `hmailserver/`; root documents and `docs/` hold the shared engineering
record, `build/` the build/test procedures and `libraries/` the dependency sources/build scripts.

Agents picking up work: start with `docs/agents/README.md`, which gives the reading order and links the
newest session handoff. Before starting roadmap work, read `ROADMAP.md`: it records the settled decisions, the state of each
phase, and lessons learned. A pull request that finishes or changes a roadmap item updates it.

The [documentation authority index](docs/README.md) defines ownership and evidence rules for the
shared project record. Keep the [detailed Phase 1b ledger](docs/phase-1b-status.md), its legacy
surface inventories, requirements, REST contract, CI inventory and latest handoff aligned with
the roadmap and README. Record missing/partial/unverified work explicitly. Check documentation
links and inventory coverage with `python docs/agents/tools/check-docs.py` (static inspection).
Root `global.json` keeps the SDK on stable .NET 10. The [CI acceptance matrix](docs/ci-acceptance-matrix.md)
defines current runtime, real REST/native/HTTPS, published-browser and per-backend gates plus
unimplemented acceptance work. Keep every newly exposed control covered at native, REST and
browser boundaries. Do not count mocks, manual checks or future scanner capacity as live CI proof.

Current plan/status handoff: [2026-11-01](docs/agents/handoffs/2026-11-01.md). Phase 1b's
native/.NET API foundation is merged in #31; complete PHP retirement, guided certificate
management and optional high-pressure Defender scanning remain delivery requirements, not
completed functionality. Read the [Phase 1b tracker](ROADMAP.md#phase-1b-delivery-tracker)
and [Defender assessment](docs/defender-antivirus-feasibility.md) before planning those changes.
SQL Server Express modernization is merged in #33; follow its OLE DB 19/TLS migration and VM/CI
requirements in [the SQL guide](docs/sql-server.md). The [CI inventory](docs/ci-test-inventory.md)
lists four database jobs, their verified baseline and automated versus manual coverage.
The [manual source](hmailserver/documentation/README.md) uses ArzoieMail product prose while
retaining compatibility identifiers, original authorship and historical routes. Follow
[documentation status](docs/documentation-status.md) for remaining technical/website work;
a naming refresh does not establish that every inherited recipe has been validated.
Honor the maintainer's PR review gate and VM/CI-only runtime-testing rule; check live PR/CI state.
PR #38's identifier/UID roadmap plan, #39's forwarding/replies, #40's mailbox signatures and #41's
advanced domain/mailbox bundle are merged. The merged #42 bundle combines
queue operations, routes/allowed recipients and global/mailbox rules. Its final
head `28dd3df` passed 1317 NUnit cases, 897 management assertions and both browser suites per
backend; merge `74eecc017`. Consult the CI inventory for inconclusive/skipped outcomes.
PR #43 merged the approved 6.0 scope at `92ac28d`. The current authorization is to complete
all remaining Phase 1b, submitted as one checkpoint PR per coherent bundle (maintainer decision,
2026-10-30; see the working agreement); follow the latest handoff for implementation/evidence status. No Phase 2 start or automatic merge is authorized.
Checkpoints #45-#49 are merged (#48, bundle 3a, as `79df40565`; #49, bundle 3b, the native
Bayesian spam filter with schema 5715, as `df10bc451`; #50, bundle 4, listener certificates,
built-in ACME and the installed ArzoieMailAdmin panel, as `ec2b42315`). A follow-up for gaps
found in review is `feature/phase1b-certificates-followup`.

---

## `hmailserver/` — Top-Level Structure

```
hmailserver/
  installation/       # InnoSetup scripts that produce the installer .exe
  source/             # All source code
```

### `installation/`

Contains InnoSetup 6 scripts (`.iss`) and third-party components bundled with the installer. The installer is built as the final step of the release pipeline. Do not add business logic here; this directory is purely packaging.

---

## `hmailserver/source/`

```
source/
  Addons/             # Various scripts, for example, default event scripts
  DBScripts/          # SQL scripts: create DB schema, upgrade old DBs
  Migration/          # Unpublished tools for migrating from other mail servers
  Management/         # .NET 10 REST API and replacement web panel (Phase 1b, in progress)
  Server/             # The ArzoieMail server (Windows service) — main C++ codebase
  Tools/              # C# .NET 10 Windows Forms tools (Administrator, DBSetup, etc.)
```

---

## `source/Server/` — The Core Mail Server

This is where almost all feature work happens. The entry point is the Visual Studio solution `source/Server/hMailServer/hMailServer.sln`.

```
Server/
  COM/                # COM/API layer (the public API, hMailServer.Application)
  common/             # Shared C++ infrastructure used by all server components
  ExternalFetcher/    # POP3 download from remote servers (fetch-from-external)
  hMailServer/        # Service entry point (WinMain, Windows service wiring)
  IMAP/               # IMAP protocol implementation
  POP3/               # POP3 protocol implementation
  SMTP/               # SMTP protocol implementation
```

### `Server/COM/`

Defines the legacy compatibility API, `hMailServer.Application` (the `.idl` file lives here).
The existing Administrator GUI, DBSetup and legacy scripts use it. New replacement-panel
capabilities belong in the native management dispatcher and versioned REST API, with permission
checks at both boundaries. Do not extend COM; migrate supported consumers to secured management APIs;
do not route the replacement panel through this layer.

### `Server/Common/`

Shared infrastructure used by SMTP, IMAP, POP3, and the COM layer. Broken into focused sub-folders:

| Sub-folder | Purpose |
|---|---|
| `AntiSpam/` | Anti-spam algorithms: SPF validation, SURBL lookups, DNS blacklists, greylisting, and score-based filtering |
| `AntiVirus/` | Anti-virus software integrations (Custom scanner, ClamAV scanner)|
| `Application/` | Application startup, configuration loading, scheduling, and logging |
| `BO/` | Business objects — domain-model classes for domains, accounts, aliases, distribution lists, rules, etc. This is the core domain layer |
| `Cache/` | In-memory caches that wrap the BO layer to reduce database round-trips |
| `Diagnostics/` | Diagnostic checks exposed through the Administrator GUI ("Run diagnostics") |
| `Mime/` | MIME encoding and decoding for message parsing and construction |
| `Management/` | Shared management authentication and the restricted native named-pipe dispatcher for the REST API |
| `Persistence/` | ORM-like persistence layer — one class per business object, maps to DB columns |
| `Scripting/` | Server-side event hooks that fire VBScript/JScript event handlers |
| `SQL/` | Low-level database abstraction: connection management, parameterised queries; supports MySQL, MS SQL Server, PostgreSQL and SQL Server Compact |
| `TCPIP/` | Async networking using Boost.Asio: DNS resolution, connection management, TLS |
| `Threading/` | Thread pool, task queuing, and thread lifecycle management |
| `Tracking/` | Publish/subscribe notification bus between server components |
| `Util/` | Miscellaneous utilities; a mixed bag of helpers that don't fit elsewhere |

When adding a feature that persists new data, touch `BO/` for the domain object, `Persistence/` for database mapping, and `Cache/` if hot-path reads are expected.

### `Server/SMTP/`

Full SMTP server implementation: incoming message reception, relay decisions, delivery queue, bounce handling, DKIM signing and verification, and outbound delivery. This is the most complex protocol module. Key concepts: the delivery queue is disk-backed; outbound delivery is asynchronous via Boost.Asio.

### `Server/IMAP/`

IMAP4rev1 server implementation including IDLE, SORT, Quota, and ACL extensions. Each IMAP command has a corresponding command-handler class. Folder hierarchy and message flags are stored in the database via the Persistence layer.

### `Server/POP3/`

POP3 server implementation. Simpler than IMAP; messages are read from the same storage backend.

### `Server/ExternalFetcher/`

Implements the "fetch mail from external POP3 accounts" feature, where ArzoieMail connects as a POP3 *client* to external servers and downloads messages on behalf of local accounts.

### `Server/hMailServer/`

The Windows service shell: `WinMain`, service control handler, and the code that registers and starts the service. Does not contain protocol or business logic.

---

## `source/Tools/` — C# Administration Tools

Built by the separate solution `source/Tools/hMailServer Tools.sln`.

```
Tools/
  Administrator/                # The Administrator desktop GUI (C#/WinForms on .NET 10)
                                # See hmailserver\source\Tools\Administrator\README.md for more information about Administrator.
  DataDirectorySynchronizer/    # Tool for synchronizing data directory with database.
  DBSetup/                      # Interactive DB creation tool (uses the COM API)
  DBSetupQuick/                 # Headless/scripted DB setup variant
  DBUpdater/                    # Schema migration tool for upgrading existing databases
  Shared/                       # Logic shared between different tools
```

`Administrator/` talks exclusively to the server through the COM API (`Server/COM/`). `DBSetup` and `DBUpdater` also go through the COM API. No tool accesses the database directly.

---

## `source/Addons/`

Various scripts, for example, default event scripts

---

## `source/DBScripts/`

SQL scripts that create the database schema from scratch (`CreateTables*.sql`), and upgrade scripts that move a database from one version to the next (`Upgrade<from>to<to><type>.sql`). They cover MySQL, MS SQL Server, PostgreSQL and SQL Server Compact. SQL Server Compact is created with `CreateTablesMSSQL.sql` but has its own upgrade scripts (`*MSSQLCE.sql`).

A schema change needs:
- upgrade scripts for all four database types, ending with `update hm_dbversion set value = <new version>`;
- the same change in the three `CreateTables*.sql` scripts, including the version they insert into `hm_dbversion`;
- the new version in `REQUIRED_DB_VERSION` in `Server/Common/Application/Constants.h`;
- the new script and version name in `Tools/DBUpdater/formMain.cs`.

---

## `test/`

The automated test suite.

```
test/
  RegressionTests/    # Integration test suite (C#, talks to a live running server via COM)
  VolumeTests/        # Long-running volume tests (200 000 messages, 100 MB messages, ...)
  VMTestRunner.Console/ # Runs the regression tests in Hyper-V virtual machines
```

`RegressionTests/` is the primary test suite — it executes against a real ArzoieMail instance and exercises it end-to-end over SMTP, IMAP, and POP3. The full suite covers 500+ scenarios.

`RegressionTests/Stress/` holds stress tests: concurrency and malformed input. They are part of
`RegressionTests.dll` and run when the test runner is asked to include stress tests.

`RegressionTests/Details/` holds detail tests: edge cases and RFC checks that go beyond what the
regression suite needs. They are `[Explicit]` and in the `Details` category, so no automated run
includes them. Run them by hand with `--where "cat == Details"`. A bug fix still needs its
regression test in the normal suite.

`VolumeTests/` is a separate assembly, built by `hMailServer Tests.sln`. Its tests push large
volumes through the server and take a long time to run, so they are never run automatically. Run
them by hand when needed:

```
hmailserver\test\packages\nunit.consolerunner\<version>\tools\nunit3-console.exe hmailserver\test\VolumeTests\bin\x64\Debug\VolumeTests.dll
```

`VMTestRunner.Console/` defines its runs in `TestEnvironments.json`. A run picks its suite with
`testSuite` (`RegressionTests` or `VolumeTests`), and `enablePageHeap` turns page heap on for
hMailServer.exe before the tests start, which needs the Debugging Tools for Windows feature of
the Windows SDK on the host. Runs with `"enabled": false` are left out of a normal run and only
execute when named:

```
VMTestRunner.Console.exe -i <installer> --test "Windows 10 - Page heap: Regression tests"
```

**Every bug fix and every new feature needs a regression test.** Add it to `RegressionTests/`,
next to the existing tests for the same protocol or area. For a bug fix, write the test first and
confirm it fails before the fix, so it is known to actually reproduce the issue.

---

## `libraries/`

Vendored third-party C++ libraries checked directly into the repository. Large external dependencies (OpenSSL, Boost, libpq, MariaDB Connector/C) are **not** included here and must be built separately using the `hMailServerLibs` environment variable; this folder holds the `build-*.ps1` scripts that build them.

---

## Key Architectural Patterns

**Management boundaries.** Existing desktop tools, regression tests and scripts use the COM API
in `Server/COM/`, which remains only in intermediate development code pending mandatory 6.0 removal. The replacement panel uses the .NET 10
REST API in `source/Management/Api`, communicating directly with the native business layer through
`Server/Common/Management`; it must not use COM or direct SQL. Enforce permissions in the native
dispatcher as well as HTTP handlers. See `docs/management-api-architecture.md` for the revised
Phase 1b design and current delivery status.

**Layered persistence.** Data flows: `BO/` (domain object) → `Persistence/` (SQL mapping) → `SQL/` (connection/query execution). The `Cache/` layer sits in front of `Persistence/` for frequently-read objects.

**Boost.Asio for all networking.** The `TCPIP/` module wraps Boost.Asio. Protocol implementations (SMTP, IMAP, POP3) use async I/O through this layer.

**Event scripting.** `EventHandlers/` allows administrators to run VBScript/JScript at server-defined hook points (e.g., on message receive). This is exposed as an optional, sandboxed scripting layer.

**Database portability.** The SQL layer abstracts over MySQL, MS SQL Server, PostgreSQL and SQL Server Compact (the built-in database). Use parameterised queries exclusively; never construct SQL strings manually.

---

## Build Overview

| Artifact | Solution file |
|---|---|
| `hMailServer.exe` (the service) | `source/Server/hMailServer/hMailServer.sln` |
| Admin tools | `source/Tools/hMailServer Tools.sln` |
| Replacement API/panel (in progress) | `source/Management/Api/ArzoieMail.Api.csproj` |
| Installer | `installation/hMailServer64.iss` (InnoSetup) |

**External dependencies** (not in repo): OpenSSL, Boost, PostgreSQL (libpq), MariaDB Connector/C (libmariadb). Set `%hMailServerLibs%` to the parent directory where these are built. See `BUILDING.md` for step-by-step build instructions.

**Branches:** `master` is the development branch for new features. Version branches (e.g., `5.6.2`) are used for bug fixes only.


# Running builds and tests

On the maintainer's development host, edit, inspect and compile only. Run all test executables,
application previews and integration checks inside the disposable VM or CI, as required by the
maintainer. Do not register a COM server or install/run the product on this host. The new management
tests live under `hmailserver/test/RegressionTests/Management` and are built as a separate executable
by `source/Management/Tests`; the scripts in `build/test-management*.ps1` enforce VM-only execution.

The below scripts will automatically locate prerequisites. They must be run using `powershell.exe` (not bash/sh). Use the `powershell.exe -NoProfile -ExecutionPolicy Bypass -File <script>` invocation from bash.

* Use `build/build.ps1` to build the ArzoieMail server.
* Use `build/post-build.ps1` to copy DLLs and register the COM server after a successful build. It stops the
  service, copies the dependencies, registers the COM server if that is needed, and starts the service again.
  Only the registration needs elevation, and it is skipped when the build output is already registered and the
  type library is unchanged, so an ordinary rebuild does not prompt. Pass `-ForceRegister` to register anyway.
* Run `build/grant-service-control.ps1` once, elevated, to let your account start and stop the ArzoieMail
  service (service name `hMailServer`) without a UAC prompt. Re-run it if the service is ever deleted and recreated.
* Use `build/build-tests.ps1` to build the regression test solution.
* Run the regression tests with the NUnit console runner from the restored packages, for example `hmailserver\test\packages\nunit.consolerunner\<version>\tools\nunit3-console.exe hmailserver\test\RegressionTests\bin\x64\Debug\RegressionTests.dll`. They need an installed and running hMailServer, since they drive it through its COM API. The test projects target .NET 10 and NUnit 5; the package versions are pinned in `hmailserver\test\Directory.Packages.props`.
