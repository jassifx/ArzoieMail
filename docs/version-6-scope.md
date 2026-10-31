# ArzoieMail 6.0 scope and validation contract

Approved by the maintainer **2026-10-28 (Asia/Kolkata)** after reviewing the Desktop
documentation proposals. This is the finite product-scope owner under [ROADMAP](../ROADMAP.md).
It supersedes the former flexible scheduling of later phases and the decision to retain COM
in the 6.0 product. [Release policy](release-versioning.md) owns version/build identities;
[breaking changes](breaking-changes-6.0.md) owns migration impact.

## Vision and operating principles

Windows-native, modular, and maintainable over a ten-year architectural horizon: prefer
supported Windows facilities and documented APIs, keep the C++ mail service independent of
the ASP.NET Core management process, and preserve a practical single-machine installation.
This is not a ten-year vendor-support promise, a no-update promise, or a claim that a management
outage cannot affect any mail operation. Test failure isolation and document its limits.

The standard installation must work without a mandatory external stack. External databases,
SpamAssassin, scanner providers and other integrations remain selectable. Required delivery
of an optional capability does not make enabling or installing it compulsory. Webmail will
integrate an existing maintained open-source application, optional to install; PHP is allowed
for that component. PHP is removed from administration delivery, not from unrelated applications.
Select the webmail application, license compatibility, maintenance ownership, authentication
boundary, deployment/update model and rendering/attachment protections before implementation.

## Frozen mandatory scope

Baseline: the roadmap at merged master `74eecc017f48ddd445870bfd73dd6eb00aed6cef`
([PR #42](https://github.com/jassifx/ArzoieMail/pull/42)), plus the explicit decisions recorded
here on 2026-10-28. Every row is mandatory before the **6.0 integrated validation baseline**.
Future roadmap additions do not automatically enter this checklist. Additions, removals or
deferrals require an explicit, dated maintainer decision and synchronized documentation.
No item may silently move to 6.1 to accelerate the milestone. Each row needs traceable PRs,
implemented contracts, acceptance evidence and an explicit disposition of known limitations.

| ID | Phase / required outcome | Acceptance boundary / current status |
|---|---|---|
| V6-00 | Foundation | C++20/v145, binary hardening, modern installer, .NET 10 tools/tests and NUnit infrastructure. Implemented in Phase 0; keep regression evidence current. |
| V6-01 | Security foundation | Parameterized database operations, protected secrets/key portability, passphrase backups, saved-password protection, Argon2id/logon review and UTF-8 credentials. Defined Phase 1 work is implemented; security assessment continues. |
| V6-1B | Administration and operations | Finish F01-F17 boundaries, A01-A20 migration dispositions and E01-E10 in the [Phase 1b ledger](phase-1b-status.md): full supported administration, roles/self-service, native/REST parity, spam/DNS/key workflows, guided certificates, optional Defender with durable lifecycle and measured pressure acceptance, restricted hosting/installer delivery, PHPWebAdmin retirement, and API migration of tools/tests. In progress; #42 is merged. |
| V6-COM | Complete COM API removal | Migrate Administrator, DB tools, maintenance utilities, regression/volume/VM fixtures and supported automation to secured management API contracts. Inventory interfaces, callbacks/events, utilities, activation, type libraries, interop generation, build hooks and packaging. Remove COM API implementations/registration/dependencies, prove activation unavailable on fresh and upgraded installs, and publish script migration. Existing COM consumers remain only in intermediate development revisions; no COM compatibility bridge in 6.0. Not implemented. |
| V6-02 | PowerShell scripting | Persistent out-of-process event host; inventory and migrate VBScript/JScript events/arguments and rule-script dependencies, preserve semantics where feasible, document differences and examples, bound time/concurrency, isolate failure and test cancellation/restart/permissions. Planned. |
| V6-03A | Identifier and IMAP UID integrity | Complete the [identifier plan](imap-uid-modernization.md): end-to-end checked boundaries, transactional folder allocation, UIDVALIDITY renewal, recovery, client resynchronization and capacity evidence. Planned. |
| V6-03B | Protocol/authentication/transport modernization | SMTP PIPELINING, 8BITMIME, ENHANCEDSTATUSCODES, CHUNKING, DSN, SMTPUTF8; IMAP ENABLE, MOVE, LITERAL+, CONDSTORE/QRESYNC, UTF8=ACCEPT; SCRAM-SHA-256; MTA-STS, DANE; DKIM Ed25519. Record existing versus missing behavior and test positive, negative, downgrade/failure and client interoperability paths. Planned. |
| V6-04 | SQLite built-in database | Replace Compact, with supported-source detection, backed-up automatic migration, database/mail-store integrity checks, interrupted migration/retry/recovery, backup/restore and installer integration. Normal regression lanes become SQLite/MySQL/PostgreSQL/Express; retain separate Compact migration/recovery fixtures. Planned. |
| V6-05 | Finite code-quality baseline | Define the owned modules/refactor checklist before implementation; close silent catch-all handling, ownership/smart-pointer work and Common/Util splits in that inventory; /W4 on maintained native targets with explicit reviewed exceptions; portable MIME/SMTP/IMAP/DKIM parsing coverage alongside existing SPF fuzzers; analyzers, bounded CI fuzz smoke and recorded longer VM/CI campaigns. Fix or explicitly disposition findings; re-audit CodeQL #1-#3 at modernization end. Continuous work, not complete; module inventory, warning baseline and fuzz budgets still require specification and maintainer acceptance before closure. No claim that all technical debt disappears. |
| V6-06 | Additional features | Sieve filtering, DMARC aggregate reports, ARC and an optional existing open-source webmail integration. Specify contracts, privacy/trust boundaries, resource limits, failure modes, lifecycle and interoperability acceptance before each implementation. Planned; no webmail application has been selected. |
| V6-REL | Operational and release acceptance | Supported OS/database matrix, upgrade/migration and recovery evidence, continuous and final security/load testing, documentation/manual/publication review, signed official binaries/setup, checksums/provenance and explicit production-release approval. Pending. |

### Delivery decisions (2026-10-30)

On 2026-10-30 (Asia/Kolkata) the maintainer asked to finish the remaining Phase 1b work and
decided how the open V6-1B items are delivered. These choose implementation routes; they do
not add, remove or defer scope.

- **Review gate per bundle.** Each remaining bundle is one checkpoint PR. After it is green, the
  agent stops for review; it does not merge or stack bundles. Order: 2 fetch/folders/maintenance
  (A07, A10, A15) → 3 spam, DNS/DKIM and SpamAssassin (A11, E01, E02) → 4 certificates and hosting
  (E03, E07) → 5 Defender (E04-E06) → 6 cutover: PHP retirement, COM migration and removal,
  acceptance and documentation (E08-E10). A08, A12, A18 and A19 fold into the nearest bundle.
- **Spam filtering (E02), revised 2026-10-31:** the earlier decision to package SpamAssassin
  is withdrawn. ArzoieMail never builds or ships SpamAssassin or ClamAV; both stay optional
  integrations that administrators install themselves, on Windows or another server such as
  Linux, and connecting them must be easy (connection, health and detection tests, guidance).
  The default becomes native filtering: a Bayesian filter trained from IMAP Junk moves, plus
  content rules, in the server (bundle 3b). Microsoft Defender (bundle 5) provides built-in
  antivirus. Bundle 3 is delivered as two checkpoints: 3a (A11, A12, E01, E02 integration) and
  3b (native Bayesian filter and rules).
- **Native Bayesian filter control (2026-10-31):** the administrator controls enabling and
  configuring the filter. It ships disabled, in new and upgraded installations alike.
- **ACME (E03) is built into the .NET management API**, with HTTP-01 challenges, renewal, and
  keys held in native protected storage (see the
  [certificate storage proposal](certificate-storage-design.md)).
- **Certificates and hosting (E03, E07), decided 2026-11-01:** keys are managed PEM files in a
  protected folder (Windows machine keys deferred); the ACME client is our own small RFC 8555
  client instead of a library (the .NET libraries are lightly maintained); the management API
  answers HTTP-01 on a challenge-only port; certificates and hosting are one checkpoint PR.
- **Microsoft Defender scanning (E04-E06) stays in Phase 1b, in roadmap order** (bundle 5, before
  the cutover bundle), including its 10,000-message pressure acceptance.

The checklist freezes capabilities, not unapproved implementation details. Outstanding bounded
design work (especially Phase 5 module ownership, webmail selection and measured performance
targets) must be specified and accepted before its row can close; an agent cannot invent a pass.
CalDAV/CardDAV are outside 6.0 and only potential, uncommitted 6.1 extensions.

## Dependencies and compatibility

Retain phase ordering and the per-PR review gate; scope approval does not authorize starting
every phase. Phase 1b owns tool/test management parity and migration planning; Phase 2 owns
event-script conversion. The final COM deletion gate spans both: do not create a dependency
cycle by requiring all Phase 2 work before Phase 1b can be accepted. Every supported consumer
must have an accepted replacement before removal, and complete removal is mandatory before
the integrated baseline. Do not add new COM features or silently remove current test coverage.

Retain `hMailServer.exe`, `hMailAdmin.exe`, service/INI names, installer AppId, upgrade data
paths, schema history and other non-COM compatibility identifiers. The COM ProgID, type library,
activation/registration and interop are explicitly retired by V6-COM. Historical descriptions
remain truthful; do not erase provenance, licensing or old schema mappings. Mail preservation
and recoverable migration take priority over obsolete implementation details. No universal
zero-breakage or in-place schema-downgrade guarantee is made.

Minimum technical compatibility and production support are separate. The present technical
floor is Windows 10 / Server 2016 x64; it does not certify every such system for production.
Production support requires applicable vendor security updates for the OS and required stack,
and project validation of the exact edition/version/support arrangement. Publish the actual
matrix at release; do not infer entitlement to extended support. Review LTS/dependency and OS
transitions before support expires. The ten-year objective requires continuing maintenance.

## Continuous validation and milestone gates

1. **During every relevant phase:** run regression, migration, recovery, security and load
   checks as their implementations land. Runtime execution stays in disposable VM/CI. Retain
   failures and exact tested revisions; establish quantitative workload, latency, backlog,
   fairness and recovery targets before accepting performance claims.
2. **Development artifacts:** CI may continue producing downloadable installers solely for
   development testing, clearly identified as incomplete/non-production. No development
   previews are released before feature completion. Artifact upload is not preview or production
   publication. A public preview or production release always needs explicit authorization.
3. **6.0 integrated validation baseline:** all mandatory scope meets accepted implementation
   criteria; reconcile capability inventories, migrations and known issues. Freeze an immutable
   source SHA and build/dependency identity with exact passing CI evidence. Then conduct
   comprehensive operational, interoperability, migration, recovery, security and load validation.
   This milestone is not production readiness. A fix creates a new identified candidate and
   requires rerunning affected tests; a frozen artifact is never silently replaced.
4. **Security/release candidate:** independent review and independent test design, defined by
   responsibility/evidence rather than a fixed number of AI agents. Specify critical native,
   authentication, privilege and data-integrity review areas and owners; seek specialist human
   review when available, and record availability, gaps and their maintainer disposition.
   AI review is not equivalent to independent human assurance. Use controlled test domains and
   synthetic mail for internet validation; use an isolated malware-test environment for scanner
   testing. No host runtime tests or automatic cloud deployment are authorized by this policy.
5. **Official production release:** actual install/upgrade/uninstall, restricted identities,
   certificates/scanners, supported platform matrix, migration/recovery instructions and
   documentation must pass. Require Authenticode signing of shipped executables and setup,
   checksums and build-provenance verification, plus explicit maintainer authorization. Signing
   certificate/service acquisition and secret handling are separate setup work, not authorized
   purchases. Traceable source, pinned dependencies, recorded toolchains and verifiable artifacts
   are mandatory; bit-for-bit reproducibility is a separate future goal.

After release, prioritize compatible security, defect, reliability and tested LTS/dependency
updates. Any unavoidable future contract/data-format break requires explicit migration guidance.
