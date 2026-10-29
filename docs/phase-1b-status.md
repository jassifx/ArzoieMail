# Phase 1b capability ledger and completion gates

Status snapshot: **2026-10-29, Asia/Colombo**. [PR #42](https://github.com/jassifx/ArzoieMail/pull/42)
is merged at `74eecc017f48ddd445870bfd73dd6eb00aed6cef`; tested head
`28dd3df912fac9109260efe9f5571dc05b402d12` passed all four database jobs, Release,
GCC/Clang, CodeQL, workflow lint and CI installer publication. See [CI evidence](ci-test-inventory.md)
and the [current handoff](agents/handoffs/2026-10-29.md). PR #43 is merged at `92ac28d`;
the present completion branch implements remaining work against the approved
[6.0 scope](version-6-scope.md).

This is the detailed status owner under [ROADMAP](../ROADMAP.md). The [requirements](webadmin-requirements.md)
own product obligations; the [REST contract](../hmailserver/source/Management/README.md#rest-v1-contract)
owns exact fields, types, limits and errors. "Merged" below describes the replacement
native/REST/panel, unless a row explicitly describes native database or legacy capabilities.
No row claims production cutover, full parity or workload capacity merely because tests passed.

The release target is now **ArzoieMail 6.0.0**, superseding 5.8.0; see [version policy](release-versioning.md).
The merged follow-up updates product/build metadata, the schema-5714 display label, API product
metadata and a version-identity regression. Final #42 CI passed **1317** NUnit cases per backend,
**897** separate management/runtime/security assertions and both browser suites. The new
scope decision requires all current phases and complete COM removal before the integrated
baseline; these are obligations, not delivered features.

## Current full-phase completion effort

After merging #43 (`92ac28d`), the maintainer authorized completing all remaining Phase 1b
in the current work effort. Branch `feature/phase1b-completion`; no PR submitted yet. Initial
server-settings implementation adds 60 typed native/REST/panel fields and checked database
patches. SURBL, blocked attachments and trusted incoming relays now have typed CRUD controls.
The Compact VM passed 630 native assertions, real HTTP/TLS and both browser suites; see the
CI inventory for exact attribution and failures fixed. Mailbox self-service (own profile,
signature, forwarding/replies, rules and password) uses restricted field writes and passed the
subsequent 656-native-assertion VM/browser run. Four additional password-policy settings bring
the allowlist to 64 fields; IP security-range management adds IPv4/IPv6 permission/priority/
expiry controls, effective-policy inspection and explicit transient relay-risk acknowledgement.
Those additions passed a full Compact VM run (706 native assertions). This covers portions of
A02/A03/A04/A05/A09/A11/A12/A14/A17/A20. A subsequent logging section adds nine native flags
(73 settings total), including atomic multi-flag patches that preserve omitted bits, and passed
the following full 736-native-assertion run. Both runs passed browser/REST/TLS checks and restored
their checkpoints. A same-password-reset session revocation regression was then reproduced;
the native/vault credential-binding fix passed the full 739-native/145-live-HTTP VM run.
Another 43 controlled-clock lifecycle checks and affected integration tests passed. Four-backend
CI remains pending. A13 log access/diagnostics remains incomplete. These are implementation
progress, not full A/E acceptance. All open obligations below remain visible;
no Defender/certificate/hosting/consumer-migration/PHP-retirement completion is implied.

## Implemented capabilities and their boundaries

| ID | Capability | State / evidence | Exact boundary or remaining work |
|---|---|---|---|
| F01 | Modern panel and REST foundation | Merged #31 | ASP.NET Core/.NET 10, local opt-in native pipe, explicit commands, no COM or direct SQL in replacement; restricted production hosting/provisioning is unfinished |
| F02 | Authentication and roles | Merged #31, expanded #35–#42; completion branch extends it | Native reauthentication, bounded ephemeral sessions, CSRF/origin/CSP/rate limits, server/domain/protected-account checks; own-mailbox native/REST/panel workflows passed initial VM checks but await full acceptance/CI |
| F03 | SQL Server Express modernization | Merged #33 | OLE DB 19, encrypted/validated SQL connections, migration diagnostics, SQL Server 2025 Express CI; not every engine version or production workload validated |
| F04 | Domain and mailbox CRUD | Merged #35 | Roles, storage quotas, password reset, search and bounded responses; names/addresses immutable through these endpoints; directory-backed password reset rejected |
| F05 | Aliases, lists and recipients | Merged #36 | Parent-scoped CRUD, posting modes/authentication, member management; defaults disabled/members-only; envelope sender is not bound to SMTP authenticated identity |
| F06 | Mailbox forwarding and replies | Merged #39 | Forward/copy policy, spam-abort flags, reply text/expiry/suppression, real local delivery; indirect forwarding cycles not detected and reply history not durable across restart |
| F07 | Mailbox signatures | Merged #40 | Plain/HTML source, macros, omission/newline/size handling; HTML stays inert in panel, mail-engine/domain policy remains authoritative |
| F08 | Advanced domain policies | Merged #41 | Catch-all mailbox validation, plus addressing/separator, domain greylisting flag, object limits; global greylisting and other server-wide policy remain separate |
| F09 | Profiles and domain signatures | Merged #41 | First/last names, read-only directory/last-logon state, domain signature formats/replacement/append/local/reply policy; no directory provisioning |
| F10 | Alternate domain names | Merged #41 | Scoped read/list, server-only create/delete, canonical names/collisions/cache invalidation; no rename, DNS ownership proof or automatic DNS/certificate/DKIM setup |
| F11 | Queue controls | Merged #42 | Metadata/search, first 100 recipients, exact decimal-string IDs, due-mail wake, guarded idle retry/remove; stored/busy items protected; no body/path/bulk-clear endpoint; listing still scans metadata |
| F12 | Routes and allowed recipients | Merged #42 | Server-only CRUD, immutable route domain, host/port/retry/classification/TLS/auth controls, write-only secrets, allowed-recipient list/add/read/delete; immediate effects, no address PATCH |
| F13 | Global and mailbox rules | Merged #42 | Scoped disabled drafts, typed conditions, ordered actions, separate activation; max 20 conditions/actions; regex/script/local-binding excluded; checked writes can leave a disabled partial draft on DB failure |
| F14 | Multiple DNSBL providers | Merged #31 | Provider add/configure/enable/disable/delete, zone/results/score/rejection text; not restricted to Spamhaus; provider availability and scoring interactions still need operational guidance |
| F15 | Spam settings and SpamAssassin connection | Merged #31, partial overall | Allowlisted SPF/MX/HELO/PTR/DKIM/DMARC flags/scores, mark/delete/header/size policy, SpamAssassin host/port/merge-score and saved-connection test; engine delivery/lifecycle and remaining spam controls unfinished |
| F16 | DNS guidance | Merged #31, partial overall | On-demand SPF/DKIM/DMARC publication inspection, copyable advice, shared-provider SPF proposals, deterministic include/redirect/lookup fixtures; key generation/rotation and complete setup/conformance acceptance unfinished |
| F17 | Status and diagnostics | Merged #31, partial overall | Basic version/state/domain count and management-boundary checks; not full server start/stop, live sessions, log viewing or complete operational diagnostics |

## Remaining administration parity

These rows make the broad "advanced administration" item concrete. They are **planned** for
replacement coverage or need an explicit maintainer-approved migration disposition. Existing
native/desktop support does not make these new-panel features implemented. Before marking a
row complete, enumerate and compare fields/actions in its legacy PHP and desktop sources.

| ID | Work area | Missing work / acceptance |
|---|---|---|
| A01 | Server status and control | Start/stop/restart policy, protocol state/counters, live sessions and operational status; least-privilege native operations and interruption confirmation |
| A02 | SMTP settings | Delivery/retry/relay defaults, authentication, size/recipient/session limits, local hostname/banner, advanced protocol and outgoing policies; retain UTF-8 credential handling and transport security |
| A03 | IMAP and POP3 settings | Protocol switches, limits, IMAP extensions/delimiter/public-folder policy and POP3 behavior; actual protocol regression checks after changes |
| A04 | IP/security ranges | Implemented on completion branch: IPv4/IPv6 ranges, priority, protocol/relay/authentication/TLS/spam/virus/expiry fields, native effective match and independent management recovery; 32 targeted Compact VM assertions include real SMTP denial/recovery/authentication. Trusted unauthenticated relay requires transient explicit acknowledgement. Four-backend/matrix/failure/field-parity acceptance remains pending |
| A05 | Incoming relays and Auto-ban | CRUD/policy, trusted-hop interpretation, ban timing/thresholds, effective-state inspection and recovery without silently bypassing security |
| A06 | TCP/IP listeners and global TLS | Bind addresses/ports/protocols, implicit TLS/STARTTLS, protocol/cipher/certificate-validation policies, collision/lockout detection, safe apply/rollback |
| A07 | External POP3 fetch accounts | Scoped configuration, encrypted/write-only credentials, scheduling, SSL/TLS, delivery/rule/delete policy, manual fetch/status/error behavior and lifecycle tests |
| A08 | Remaining domain/mailbox controls | Field-level comparison with legacy forms, directory bindings/provisioning policy and user-level permissions; document controls intentionally retained only in desktop tools |
| A09 | Mailbox self-service | Own profile/signature/forwarding/replies/rules/password implemented on completion branch; 26 native assertions and real own-profile browser checks passed. Restricted-column guarded account saves, current-password proof, sign-out, ownership and AD-password refusal implemented. Complete AD/revocation/concurrency/failure/browser-password/legacy workflow and four-backend acceptance remain pending |
| A10 | Groups, public/shared folders and ACLs | Desktop group/member and IMAP folder/permission surfaces; secure role matrix, hierarchy and client interoperability before any replacement claim |
| A11 | Remaining spam controls | SURBL lists, allowlists, global greylisting timing/exceptions, other advanced filtering options and effective scoring guidance; existing DNSBL controls do not cover these |
| A12 | Existing antivirus and blocked attachments | Current custom/ClamAV configuration, test actions, timeouts/results, attachment extension/policy controls and interaction with optional Defender; no silent loss of scanner choices |
| A13 | Logging and diagnostics | Nine typed logging flags implemented on completion branch with merged checked bit updates; initial Compact VM/native/browser checks passed. Bounded/redacted log access, operational diagnostics and DNS/MX tools remain; no unrestricted file reader or shell execution |
| A14 | Performance settings | Connection/worker/cache/database knobs, units/limits/effective status, guarded changes and measured workload guidance; collection/queue DB-side paging remains a capacity follow-up |
| A15 | Administrative maintenance | Backup/restore, archive passphrases, installation secret-key export/import, upgrade/database/data-directory tools and recovery; choose safe narrowly scoped web support or an explicitly documented desktop-only procedure |
| A16 | Mirror, server messages and sendout | Mirror destination/policy, SMTP response templates and administrator sendout, with role, privacy, validation and confirmation checks |
| A17 | Password hashing policy | Four typed policy/upgrade settings implemented on completion branch with unchanged defaults, zero/default semantics, algorithm-dependent cross-field bounds and guarded dependencies. Initial Compact settings checks passed; full four-backend, concurrency/failure and operational acceptance remain pending. No hashes/secrets are returned |
| A18 | Scripts and utility migration decisions | Legacy script configuration/execution is not exposed by #42 rules. PowerShell event-host implementation belongs to Phase 2. Resolve how affected PHP workflows migrate before cutover; do not add unrestricted web script/shell execution as a shortcut |
| A19 | Existing route/rule/queue restrictions | Explicit disposition for legacy regex, script/local-binding rule actions, immutable names, bulk queue operations and other omitted controls; supported migration or approved restriction required, not silent parity claims |
| A20 | General server settings | Default domain, IPv6 preference and legacy IP-home/multihoming settings, plus remaining general options; review even orphaned handlers and preserve or explicitly retire their supported behavior |

## Expanded Phase 1b requirements

| ID | Workstream | Remaining delivery and completion gate |
|---|---|---|
| E01 | SPF/DKIM/DMARC setup | Finish key creation/protection/selection/rotation, server signing configuration, DNS revalidation, provider coexistence and actual message authentication/alignment conformance; publication inspection alone is insufficient |
| E02 | SpamAssassin engine operations | Decide packaged engine/version/licensing/update/support model, install/configure/service lifecycle, health/test/error behavior, offline provisioning and interaction with native DNSBL scores; connection settings are already delivered |
| E03 | Guided certificates | Import including protected PFX/chain, ACME provider/client/challenge selection, hostname/key/chain/access checks, SMTP/IMAP/POP3 listener assignment, independent web HTTPS binding, renewal/expiry, safe activation and rollback |
| E04 | Optional Defender adapter | Native protected staging and bounded execution, clean/infected/error/unscannable outcomes, clean/EICAR/MIME/archive/encrypted-content/permission/error tests; implementation has not started |
| E05 | Defender lifecycle, scheduling and mitigation | SMTP receive/submission, queued send, external fetch, uploaded drafts/IMAP APPEND/import and changed-content coverage; durable fair bounded workers, verdict gates, hold/retry/quarantine, retention/audit, restart/outage recovery and observability |
| E06 | Defender capacity acceptance | Define numerical throughput/latency goals and hardware/message mix, test at least 10,000 queued/in-flight messages, concurrent lifecycle activity, bursts/outages/recovery/soak, p95/p99/resource use/headroom; conditional on results, keep alternative scanner path |
| E07 | Production hosting and installer | Restricted IIS pool or service identity, ASP.NET runtime/offline delivery, SID/service-status/pipe permissions, HTTPS/hostname configuration, identity/secret/key protection, diagnostics, install/uninstall/upgrade and rollback tests |
| E08 | PHP retirement | Complete parity/dispositions, migration guide and role mapping, disable old URLs on upgrade, remove PHP source/assets/packaging/runtime/deployment dependencies and prove old administration endpoint inaccessible; retain unrelated PHP apps; migrate supported management consumers and retire COM under E10/V6-COM |
| E09 | Production acceptance and user documentation | Role matrix on every operation, malicious/invalid/oversized inputs, revocation/concurrency/persistence failures, browser-to-service acceptance, four DB jobs and supported Windows matrix, migration/rollback, actual screenshots/manual review/rendering/navigation/assets/publication |
| E10 | Tools/tests/automation API migration and COM retirement | Inventory all desktop/DB/maintenance consumers, regression/volume/VM fixtures, scripts, interfaces/events/utility calls and build/packaging registration. Deliver secured API parity and bootstrap/recovery workflows, migrate supported consumers without coverage loss; complete COM removal before the 6.0 integrated baseline, coordinated with Phase 2. Not implemented; BC-007/V6-COM own final acceptance. |

Details for E03–E06 remain in [requirements](webadmin-requirements.md) and the
[Defender assessment](defender-antivirus-feasibility.md). No scanner throughput, certificate
automation, PHP removal or production provisioning has been implemented by PR #42.

## Suggested grouping and next authorization

The maintainer prefers larger coherent bundles. Suggested sequence after merged #42 and a new explicit implementation authorization:
server/protocol/security/listener operations; external fetch/mailbox/shared-folder and maintenance
coverage; spam/DNS/key workflows; guided certificates/production hosting; Defender adapter and
durable lifecycle/capacity; final migration/installer/PHP retirement. Grouping and dependencies
may require splitting or combining these. This is **not a fixed PR count or authorization**.
Certificates depend on safe listener/privilege handling; PHP cutover depends on accepted parity
and deployment. Phase 2 starts only after Phase 1b acceptance and maintainer authorization.

## Phase 1b completion checklist

- Every capability and legacy inventory entry has evidence of replacement or an explicit,
  maintainer-approved restriction/migration decision. File presence is not field-level parity.
- Native/REST/panel contracts, parent/role protections, defaults, secrets, persistence failure
  recovery and in-flight behavior are documented and tested.
- E01–E09 requirements, E10 supported tool/test API migration, and relevant A01–A20 dispositions
  are accepted; E10 final COM deletion is coordinated with Phase 2 and gates the 6.0 baseline.
  Defender capacity and certificate lifecycle are demonstrated; no blanket performance claims from functional CI.
- New install and upgrade use the replacement with its restricted identity/runtime/HTTPS.
  Old PHP administration URLs/source/packaging are retired and tested; unrelated applications
  continue to work. Supported tools/tests must have secured API migration
  coverage; event-script conversion belongs to Phase 2. Complete COM deletion is a cross-phase
  6.0 baseline gate, not a claim that this phase already removed it.
- Required four-backend CI, VM security/protocol/upgrade/rollback and workload acceptance pass
  on identified revisions. Record manual, skipped and unverified scenarios separately.
- README, roadmap, ledger, contract, requirements, CI inventory, manual/documentation status
  and AI handoff agree. Remaining website publication and inherited-recipe uncertainty have
  explicit ownership/disposition. Maintainer signs off before the next phase.

## Legacy surface inventories

The following maintained-source inventories are a **coverage checklist**, not a claim that every inherited
field or procedure has been reviewed. They include every current PHP source file and every
non-designer Administrator main-pane C# file. The shared area IDs above identify the capability
owner; a "partial" inventory entry still needs field/action/role comparison. Supporting includes,
authentication handlers and shells also require retirement/security acceptance. New/deleted files
must be reconciled here before PHP removal. Static inventory coverage must be checked against
the source tree, excluding ignored generated output such as `include_versioncheck.php`;
it does not execute PHP or the desktop tools.

### PHP source checklist (89 files)

Paths are relative to `hmailserver/source/WebAdmin/`. Area IDs refer to the tables above.

| PHP source | Capability / migration owner |
|---|---|
| `background_account_externalaccount_save.php` | A07 |
| `background_account_save.php` | F04/A08/A09 |
| `background_ajax_spamassassintest.php` | F15/E02 |
| `background_ajax_virustest.php` | A12/E04 |
| `background_alias_save.php` | F05 |
| `background_blocked_attachment_save.php` | A12/E04 |
| `background_distributionlist_recipient_save.php` | F05 |
| `background_distributionlist_save.php` | F05 |
| `background_dnsblacklist_save.php` | F14 |
| `background_domain_name_save.php` | F10 |
| `background_domain_save.php` | F04/F08/F09/A08/E01 |
| `background_incomingrelay_save.php` | A05 |
| `background_iphome_save.php` | A20 |
| `background_login.php` | F01/F02/E07/E08 |
| `background_route_address_save.php` | F12/A19 |
| `background_route_save.php` | F12/A19 |
| `background_rule_save.php` | F13/A18/A19 |
| `background_securityrange_save.php` | A04 |
| `background_servermessage_save.php` | A16 |
| `background_sslcertificate_save.php` | A06/E03 |
| `background_surblserver_save.php` | A11 |
| `background_tcpipport_save.php` | A06/E03 |
| `background_whitelistaddress_save.php` | A11 |
| `config-dist.php` | F01/F02/E07/E08 |
| `error.php` | F01/F02/E07/E08 |
| `hm_account.php` | F04/A08/A09 |
| `hm_account_externalaccount.php` | A07 |
| `hm_account_externalaccounts.php` | A07 |
| `hm_accounts.php` | F04/A08/A09 |
| `hm_alias.php` | F05 |
| `hm_aliases.php` | F05 |
| `hm_autoban.php` | A05 |
| `hm_backup.php` | A15 |
| `hm_blocked_attachment.php` | A12/E04 |
| `hm_diagnostics.php` | F16/F17/A13 |
| `hm_distributionlist.php` | F05 |
| `hm_distributionlist_recipient.php` | F05 |
| `hm_distributionlist_recipients.php` | F05 |
| `hm_distributionlists.php` | F05 |
| `hm_dnsblacklist.php` | F14 |
| `hm_dnsblacklists.php` | F14 |
| `hm_domain.php` | F04/F08/F09/A08/E01 |
| `hm_domain_aliasname.php` | F10 |
| `hm_domains.php` | F04/F08/F09/A08/E01 |
| `hm_frontpage.php` | F01/F02/E07/E08 |
| `hm_greylisting.php` | A11 |
| `hm_imap.php` | A03 |
| `hm_incomingrelay.php` | A05 |
| `hm_incomingrelays.php` | A05 |
| `hm_logging.php` | A13 |
| `hm_login.php` | F01/F02/E07/E08 |
| `hm_mirror.php` | A16 |
| `hm_passwordhashing.php` | A17 |
| `hm_performance.php` | A14 |
| `hm_permission_denied.php` | F01/F02/E07/E08 |
| `hm_pop3.php` | A03 |
| `hm_route.php` | F12/A19 |
| `hm_route_address.php` | F12/A19 |
| `hm_route_addresses.php` | F12/A19 |
| `hm_routes.php` | F12/A19 |
| `hm_rule.php` | F13/A18/A19 |
| `hm_rule_action.php` | F13/A18/A19 |
| `hm_rule_criteria.php` | F13/A18/A19 |
| `hm_rules.php` | F13/A18/A19 |
| `hm_scripts.php` | A18 |
| `hm_securityrange.php` | A04 |
| `hm_securityranges.php` | A04 |
| `hm_servermessage.php` | A16 |
| `hm_servermessages.php` | A16 |
| `hm_smtp.php` | A02 |
| `hm_smtp_antispam.php` | F15/A11 |
| `hm_smtp_antivirus.php` | A12/E04 |
| `hm_sslcertificate.php` | A06/E03 |
| `hm_sslcertificates.php` | A06/E03 |
| `hm_ssltls.php` | A06/E03 |
| `hm_status.php` | F17/A01/F11 |
| `hm_surblserver.php` | A11 |
| `hm_surblservers.php` | A11 |
| `hm_tcpipport.php` | A06/E03 |
| `hm_tcpipports.php` | A06/E03 |
| `hm_whitelistaddress.php` | A11 |
| `hm_whitelistaddresses.php` | A11 |
| `include/functions.php` | F01/F02/E07/E08 |
| `include/initialization_test.php` | F01/F02/E07/E08 |
| `include/rule_strings.php` | F13/A18/A19 |
| `include_treemenu.php` | F01/F02/E07/E08 |
| `index.php` | F01/F02/E07/E08 |
| `initialize.php` | F01/F02/E07/E08 |
| `logout.php` | F01/F02/E07/E08 |

### Desktop main-pane checklist (54 files)

Paths are relative to `hmailserver/source/Tools/Administrator/Main panes/`. Designer/resource
files and nested dialogs are not separate capability claims; inspect them with their owning pane
when comparing fields/actions. DBSetup, DBUpdater, DataDirectorySynchronizer and migration tools
are covered by A15/E10 and currently use COM; their secured API migration and complete COM
retirement are mandatory for the 6.0 baseline, not an optional later compatibility decision.

| Desktop source | Capability / migration owner |
|---|---|
| `ucAccount.cs` | F04/A08/A09 |
| `ucAccounts.cs` | F04/A08/A09 |
| `ucAdvanced.cs` | A20 |
| `ucAlias.cs` | F05 |
| `ucAliases.cs` | F05 |
| `ucAntiSpam.cs` | F15/A11/E02 |
| `ucAntiVirus.cs` | A12/E04 |
| `ucAutoBan.cs` | A05 |
| `ucBackup.cs` | A15 |
| `ucDNSBlackList.cs` | F14 |
| `ucDNSBlackLists.cs` | F14 |
| `ucDiagnostics.cs` | F16/F17/A13 |
| `ucDistributionList.cs` | F05 |
| `ucDistributionLists.cs` | F05 |
| `ucDomain.cs` | F04/F08/F09/A08/E01 |
| `ucDomains.cs` | F04/F08/F09/A08/E01 |
| `ucGlobalRules.cs` | F13/A18/A19 |
| `ucGreyListing.cs` | A11 |
| `ucGroup.cs` | A10 |
| `ucGroups.cs` | A10 |
| `ucIMAPFolders.cs` | A10 |
| `ucIPRange.cs` | A04 |
| `ucIPRanges.cs` | A04 |
| `ucIncomingRelay.cs` | A05 |
| `ucIncomingRelays.cs` | A05 |
| `ucLogging.cs` | A13 |
| `ucMXQuery.cs` | F16/F17/A13 |
| `ucMirror.cs` | A16 |
| `ucPasswordHashing.cs` | A17 |
| `ucPerformance.cs` | A14 |
| `ucProtocolIMAP.cs` | A03 |
| `ucProtocolPOP3.cs` | A03 |
| `ucProtocolSMTP.cs` | A02 |
| `ucProtocols.cs` | A03 |
| `ucRoute.cs` | F12/A19 |
| `ucRoutes.cs` | F12/A19 |
| `ucRules.cs` | F13/A18/A19 |
| `ucSSLCertificate.cs` | A06/E03 |
| `ucSSLCertificates.cs` | A06/E03 |
| `ucSSLTLS.cs` | A06/E03 |
| `ucSURBLServer.cs` | A11 |
| `ucSURBLServers.cs` | A11 |
| `ucScripts.cs` | A18 |
| `ucSecretKey.cs` | A15 |
| `ucServerMessage.cs` | A16 |
| `ucServerMessages.cs` | A16 |
| `ucServerSendout.cs` | A16 |
| `ucSettings.cs` | F01/E07/E08 |
| `ucStatus.cs` | F17/A01/F11 |
| `ucTCPIPPort.cs` | A06/E03 |
| `ucTCPIPPorts.cs` | A06/E03 |
| `ucUtilities.cs` | F01/E07/E08 |
| `ucWelcome.cs` | F01/E07/E08 |
| `ucWhiteListing.cs` | A11 |
