<h1 align="center">ArzoieMail</h1>

<p align="center">
  <strong>Email for your own domains, on your own Windows server.</strong><br>
  A free, open source mail server for Windows: SMTP, IMAP and POP3, spam and virus protection,
  encryption and automation, in one installer.
</p>

<p align="center">
  <a href="https://github.com/jassifx/arzoiemail/actions/workflows/build.yml"><img alt="Build and regression tests" src="https://github.com/jassifx/arzoiemail/actions/workflows/build.yml/badge.svg?branch=master"></a>
  <a href="LICENSE"><img alt="License: AGPL v3" src="https://img.shields.io/badge/license-AGPL--3.0-blue"></a>
  <img alt="Windows 10 / Server 2016 and later, 64-bit" src="https://img.shields.io/badge/Windows-10%20%7C%2011%20%7C%20Server%202016%E2%80%932025-0078D4">
</p>

<p align="center">
  <a href="https://www.arzoie.in">Website</a> ·
  <a href="https://www.arzoie.in/download">Download</a> ·
  <a href="https://www.arzoie.in/docs/">Manual</a> ·
  <a href="https://www.arzoie.in/faq">FAQ</a> ·
  <a href="https://www.arzoie.in/community">Community</a> ·
  <a href="BUILDING.md">Build from source</a>
</p>

---

ArzoieMail runs your organisation's email on hardware you control: as many domains as you need,
each with its own accounts, aliases, distribution lists, limits and DKIM key, filtered against
spam (SPF, DKIM, DMARC, DNS blacklists, SURBL, greylisting) and viruses (ClamAV), with TLS
throughout. It stores its data in a built-in database, or in MySQL, MariaDB, SQL Server or
PostgreSQL, and everything it does can be scripted through its COM API.

It is free for everyone under the [GNU AGPLv3](LICENSE): no paid editions, no locked features,
and **no telemetry, ever**.

## Where ArzoieMail comes from

ArzoieMail carries on [hMailServer](https://github.com/hmailserver/hmailserver), the Windows mail
server Martin Knafve started in 2002 and that many organisations still run. It continues from
hMailServer's code after the 5.7.2 release, as of October 2026.

hMailServer earned that trust over two decades, but the platform under it aged: a compiler and
libraries from years ago, a database engine Microsoft stopped supporting in 2021, a scripting
language Microsoft is phasing out of Windows, and secrets protected by a key printed in the source
code.
ArzoieMail exists to modernise it end to end - toolchain, libraries, security, protocols and
tooling - while every existing installation keeps working.

It is a project of its own, with its own roadmap, tests and releases, maintained by
Jaspreet Singh in Delhi, India. The name honours Arzoie, the maintainer's daughter.

**The next release target is ArzoieMail 6.0.0.** ArzoieMail follows its own release line rather
than a hMailServer 5.8 target. [VERSION](VERSION) owns the product target; the
[release/version policy](docs/release-versioning.md) explains build metadata, compatibility,
schema independence and release gates. Version 6 remains in development; this is not a release
announcement or a claim that unfinished roadmap work has shipped.

## What is different

**Tested on every change, on every database.** Each push builds the server, installs it on a
clean runner and runs the full regression suite - more than 1,200 end-to-end tests over SMTP,
IMAP, POP3 and the COM API - on four backends: SQL Server Compact, PostgreSQL, MySQL and SQL
Server 2025 Express. Each database job also runs management API, security, DNS and browser checks.
The [CI test inventory](docs/ci-test-inventory.md) lists the checks, fixture boundaries, verified
test totals and coverage that still requires manual testing.
Installers are also tested on clean Windows Server virtual machines, which is how a missing runtime DLL that only
broke installations on machines without Visual C++ installed was found and fixed.

**Secure by construction, not by convention.**

- SQL values are bound as parameters instead of being pasted into the SQL, on the built-in
  database, SQL Server, PostgreSQL and MySQL alike. Along the way, an SQL injection inherited from hMailServer
  (a crafted address could break out of its quotes) was found and fixed in public.
- The server is compiled with the Windows hardening options: `/sdl`, Control Flow Guard,
  Spectre mitigations and CET shadow-stack compatibility, and tests check that the shipped
  binaries carry them.
- Passwords are hashed with Argon2id, and older hashes are upgraded when users log on. A logon
  to an account that does not exist takes as long as one to an account that does, so the time it
  takes does not tell which accounts exist.
- Stored secrets - route and external account passwords, the relay and database passwords - are
  encrypted with AES-256-GCM under a key of the installation's own, protected by Windows DPAPI.
  The key can be exported under a passphrase to move to a new server.
- Backups can be encrypted under a passphrase, file names included; a backup without one keeps
  its secrets encrypted as in the database. The Administrator protects the passwords it saves
  with DPAPI.
- Third-party downloads in the build are pinned by version and checked against their SHA-256 or
  SHA-512 hashes.

**Current, supported foundations.** C++20 on Visual Studio 2026, OpenSSL 3.5 LTS, Boost 1.92,
PostgreSQL 18's libpq, MariaDB Connector/C 3.4, and administration tools on .NET 10 LTS.

**A drop-in upgrade.** Run the installer over hMailServer 5.x and it upgrades the installation and
its database in place. The names existing setups depend on are kept on purpose: the
`hMailServer` Windows service, the `hMailServer.Application` COM API, `hMailServer.exe` and
`hMailServer.ini`. Existing scripts, legacy PHPWebAdmin and other tools retain compatibility during migration.
The replacement web admin uses .NET 10; PHPWebAdmin will be retired after migration validation. (Back up first:
the database upgrade cannot be undone.)
Full SQL Server upgrades require the OLE DB 19, engine-version and trusted-TLS preparation in
the [SQL Server guide](docs/sql-server.md); the installer blocks missing-driver or legacy-provider
configurations before interrupting the existing service. Compact is a separate backend.

## Why things were replaced

Modernising means retiring things that still work today but will not tomorrow. Each replacement
below has a reason, and each keeps existing installations working.

| Was | Is (or will be) | Why |
|---|---|---|
| Visual Studio 2019, C++14 | Visual Studio 2026, C++20, conformance mode | Supported compiler, modern language, and the hardening and analysis the new toolset brings |
| .NET Framework 4.5 tools | .NET 10 LTS, runtime bundled in the installer | Long-term supported runtime; bundling keeps offline installations possible |
| Oracle `libmysql.dll` | MariaDB Connector/C, built with OpenSSL | Maintained, open source, talks to both MySQL 8 and MariaDB, with authentication plugins built in |
| SQL text assembled from values | Bound parameters | Values can never become SQL, whatever they contain |
| Windows Vista / Server 2008 support | Windows 10 / Server 2016 and later | Required by PostgreSQL's current client library, and lets the server use what Windows 10 provides instead of workarounds for systems long out of support |
| Blowfish with a fixed key | AES-256-GCM with a per-installation key protected by Windows DPAPI, exportable with a passphrase | The old key is published in the source code, so anyone with an `hMailServer.ini` or a backup could decrypt the passwords in it |
| A hard-coded key for the Administrator's saved passwords | Windows DPAPI for the current user | A key in the source code protects nothing |
| Secrets written into backups as they were stored | An encrypted archive under the backup's own passphrase, or secrets protected under the installation's key | A backup is a copy of every secret on the server, and is often kept somewhere less protected |
| VBScript / JScript event scripts | A PowerShell event host, out of process *(planned)* | Microsoft has deprecated VBScript and is phasing it out of Windows, and a crashing script should never take the mail server down with it |
| SQL Server Compact 4.0 | SQLite, with automatic migration *(planned)* | SQL Server Compact has been out of support since 2021 |

## Roadmap

ArzoieMail is modernised phase by phase, each change as its own reviewed and tested pull request.
[ROADMAP.md](ROADMAP.md) has the details: the decisions and why they were made, what each change did,
and the plan for the phases ahead.

The [documentation authority index](docs/README.md) defines the shared project record and update
rules. The [detailed Phase 1b ledger](docs/phase-1b-status.md) lists implemented capabilities,
remaining work, acceptance gates and every legacy PHP/desktop main-pane migration owner.

| Phase | Scope | Status |
|---|---|---|
| **0 · Foundation** | Visual Studio 2026 and C++20, hardening flags, Windows 10 minimum, Inno Setup 6.7, .NET 10 tools and tests, NUnit 5 | ✅ Done |
| **1 · Security** | Bound SQL parameters on every database, retiring the `libmysql.dll` fallback, AES-GCM and DPAPI instead of Blowfish with an exportable key, passphrase-protected backups, DPAPI for the Administrator's saved passwords, an Argon2id review (logon timing), UTF-8 client credentials | ✅ Done |
| **1b · Web admin** | Modern Bootstrap panel with advanced administration and REST API from the first release; SpamAssassin management, configurable DNSBLs, SPF/DKIM/DMARC guidance and validation including shared-provider SPF; .NET 10 backend with native management access and no COM dependency; guided TLS certificate setup/renewal, optional Microsoft Defender Antivirus scanning, full PHP retirement after migration and SQL Server Express modernization | In progress |
| **2 · Scripting** | PowerShell event host replacing VBScript and JScript | Planned |
| **3 · Protocols and data integrity** | Checked identifiers and transactional IMAP UID allocation, safe generation renewal and capacity controls; SMTP/IMAP extensions, SCRAM-SHA-256, MTA-STS, DANE and DKIM with Ed25519 | Planned |
| **4 · Built-in database** | SQLite replacing SQL Server Compact, migrated automatically | Planned |
| **5 · Code quality** | Modern ownership (smart pointers), compiling at warning level 4, smaller modules, more of the server in the portable core with its tests and fuzzers - continuously, alongside the other phases | Ongoing |
| **6 · Features** | Sieve filtering, DMARC aggregate reports, ARC, webmail | Planned |

The phases run in this order, each starting once the one before it is done; code quality (5) runs
alongside all of them.

Phase 1b delivered domain and mailbox CRUD, storage limits, password resets, roles and paginated
search in merged PR #35. PR #36 adds aliases, distribution lists, posting settings and member
management to the native API and responsive panel. See the
[REST contract](hmailserver/source/Management/README.md#rest-v1-contract) for the supported
fields and permissions. Mailbox forwarding and automatic replies merged in #39 after VM and
four-backend CI acceptance. Mailbox signatures merged in #40; profiles, advanced domain policies,
object limits, domain signatures and alternate names merged in #41. [PR #42](https://github.com/jassifx/ArzoieMail/pull/42)
adds queue inspection/retry/removal, routes/allowed recipients and global/mailbox rule builders.
Its application/test head passed VM and all four CI backends: 1,316 total NUnit cases and 734 separate
management assertions per backend, plus browser checks. It remains unmerged, awaiting review;
later documentation revisions require their own CI. Full advanced administration and production
cutover remain unfinished. Implementation still requires
the maintainer's confirmation after each PR review. Related Phase 1b capabilities are now grouped
into larger coherent PRs; the VM/CI and review gates remain in force.

The [identifier and IMAP UID lifecycle plan](docs/imap-uid-modernization.md) tracks boundary
fixes, checked allocation, controlled generation renewal and panel capacity monitoring. It is
planned work, independent of PHP removal. Standard IMAP's finite UID range remains; the goal is
safe operation and recovery, with explicit client resynchronization when generations change.

**Microsoft Defender Antivirus scanning is planned within Phase 1b; implementation has not
started.** Its [delivery checklist](ROADMAP.md#microsoft-defender-antivirus-delivery-checklist)
tracks the native adapter, receive/send/draft coverage, durable queue, mitigation, panel/API
controls and VM capacity acceptance with at least 10,000 queued/in-flight messages. Requirements
and feasibility were merged in PR #32; production throughput remains unverified.

The [manual source](hmailserver/documentation/README.md) now uses ArzoieMail product naming
while preserving compatibility identifiers and historical upstream references. The
[documentation status](docs/documentation-status.md) tracks the remaining technical review,
new screenshots, website rendering/publication and guides for unfinished Phase 1b features.

## Getting started

1. Check the [system requirements](https://www.arzoie.in/docs/system_requirements): 64-bit
   Windows 10 or 11, or Windows Server 2016 to 2025.
2. [Download](https://www.arzoie.in/download) the installer, or take it from
   [GitHub Releases](https://github.com/jassifx/arzoiemail/releases). Builds are not code-signed
   yet, so Windows SmartScreen may warn: download only from these places and compare the SHA-256
   checksum.
3. Follow the [quick-start guide](https://www.arzoie.in/docs/book_quickstartguide) and publish the
   [DNS records](https://www.arzoie.in/get-started#dns) (MX, SPF, DKIM, DMARC and reverse DNS) your
   domains need.

Upgrading from hMailServer? Read the [upgrade guide](https://www.arzoie.in/upgrade) first.

## Help, security and contributing

- **Questions and problems:** search the [manual](https://www.arzoie.in/docs/), then open a
  [GitHub issue](https://github.com/jassifx/arzoiemail/issues) with your ArzoieMail version,
  Windows version, database and a log (passwords removed). Most answers on the
  [hMailServer forum](https://www.hmailserver.com/forum/) apply too.
- **Security problems:** report them privately through
  [GitHub's security advisory form](https://github.com/jassifx/arzoiemail/security/advisories/new),
  not in a public issue.
- **Contributing:** contributions from anyone are welcome. [BUILDING.md](BUILDING.md) explains how
  to build, run and test ArzoieMail. Work happens on `feature/`, `bugfix/` or `techdebt/` branches,
  which GitHub Actions builds and tests, and reaches `master` through a pull request once the build
  and the regression tests pass. Every bug fix and every new feature comes with a regression test.

## Privacy

ArzoieMail sends no information to the ArzoieMail or hMailServer projects, or to anyone else. It
only connects to other systems as part of what the administrator configures it to do, such as
delivering email, looking up DNS records, checking DNS blacklists and fetching email from external
accounts.

## License and credits

ArzoieMail is free software under the [GNU Affero General Public License version 3](LICENSE): you
may use, change and redistribute it, including in your business, under its terms.

ArzoieMail builds on hMailServer by Martin Knafve and its contributors, and on the third-party
components whose licenses are in [`hmailserver/licenses`](hmailserver/licenses), among them
OpenSSL, Boost, PostgreSQL's libpq, MariaDB Connector/C, 7-Zip and the Fat Cow icons.
