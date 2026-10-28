# ArzoieMail documentation status and remaining work

The documentation refresh in [PR #34](https://github.com/jassifx/ArzoieMail/pull/34) covers the
project Markdown and the 258-file inherited manual. It updates product prose to **ArzoieMail**
and separates current guidance, historical upstream material and functionality still planned.
This is a documentation change; it does not rename executables, migrate a running installation,
implement planned features or publish the website.

The [authority index](README.md) assigns document ownership and evidence/update rules. The
[Phase 1b ledger](phase-1b-status.md) adds granular delivered/remaining/restricted capabilities,
completion gates and exhaustive file-level checklists for 89 PHP sources and 54 desktop main
panes. Field-level legacy parity and inherited operational review remain unfinished; this
inventory makes those obligations visible rather than declaring them complete.

## Current documentation

| Area | Current state |
|---|---|
| Product overview and engineering roadmap | README/ROADMAP track merged work through #41, the current queue/routing/rules bundle, remaining Phase 1b delivery and planned UID lifecycle work. |
| Release identity | Independent ArzoieMail 6.0.0 target supersedes 5.8.0. VERSION/release policy own metadata, compatibility and schema separation. New product-identity regression expects a 1317-case baseline after fresh acceptance; no production release is claimed. |
| CI test inventory | Verified #42 application/test head: 1,316 NUnit cases per backend, zero failures/warnings, 734 separate management assertions and browser checks on all four DBs; exact outcomes/retry history, historical 1,315-case baselines, fixture boundaries and manual/planned checks are explicit. Later documentation heads require their own CI. |
| Manual entry and installation | ArzoieMail source/provenance, current platform baseline, installation paths, database prerequisites and supported upgrade procedure replace old generic hMailServer instructions. |
| Database guidance | Full SQL Server/Express is separated from Compact; OLE DB 19/TLS prerequisites and validated SQL Server 2025 Express coverage are explicit. Old bundled-MySQL/Blowfish recipes are not current connection guidance. |
| Security and backup | Current service identity, protected secrets, key export and passphrase-protected backups replace the obsolete universal-obfuscation description. |
| Web administration | Merged through #41; #42 queue/routing/rules passed VM/four-backend CI on the recorded application/test head and remains unmerged awaiting review. Detailed parity/acceptance is in the ledger, actual endpoint semantics in the contract. Advanced parity and production provisioning remain unfinished; PHP/COM consumers remain during migration. |
| Certificates and antivirus | Present manual file/listener configuration and existing scanners are separated from the planned certificate wizard/renewal and optional Defender lifecycle/pressure acceptance. |
| Inherited references | Product prose is rebranded; code/compatibility examples are preserved. Pages needing technical review carry a reference-status note. Historical upgrade/integration recipes retain upstream names and explicit historical notices. |

## Names deliberately preserved

Human-facing product prose uses ArzoieMail. Retain actual identifiers, paths and historical
attribution, including `hMailServer.exe`, `hMailServer.ini`, `hMailAdmin.exe`, service name
`hMailServer`, `NT SERVICE\hMailServer`, `hMailServer.Application`, type-library/.NET namespace,
`hm_` tables, `hMailServerBackup.xml`, source/project filenames and the compatibility installer
AppId/headers/registry keys. AGENTS.md holds the full policy.

Existing manual filenames, YAML `slug`/`parent` values, `?page=` targets and image paths remain
stable. Historical hMailServer releases and original authorship are not renamed into ArzoieMail
history. Third-party license text is not changed by this refresh.

## Remaining documentation work

| Work | Completion requirement |
|---|---|
| Technical review of inherited reference pages | Walk each setting, protocol, error, script and administration recipe against the current source and a supported VM. Naming cleanup alone does not validate inherited behavior. |
| Remaining credential/database recovery recipes | Replace version-specific old MySQL/Compact repair, password-recovery and migration instructions with reviewed procedures for current protected secrets and supported tools. |
| Message-ID/IMAP UID maintenance guidance | Replace `maintenance_database`'s global message-count explanation with reviewed database-ID versus per-folder-UID guidance and publish it. Add tested monitoring, generation renewal, client-resync and recovery instructions as the [UID plan](imap-uid-modernization.md) is implemented. |
| New screenshots and UI walkthroughs | Capture the actual Administrator, installer and replacement panel in the disposable VM; replace legacy screenshots without inventing UI that has not shipped. |
| Website rendering/publication | Confirm the ArzoieMail hosting/build pipeline, render front matter/raw HTML, validate internal navigation and image assets, and publish the reviewed manual. This repository's Markdown changes do not prove deployment. |
| Route and external-link migration | Validate legacy slugs and `?page=` routing, add redirects before changing routes, and review upstream/external links for current purpose and availability. |
| Remaining Phase 1b features | Add user instructions and acceptance evidence as domain/account parity, advanced administration, DNS/DKIM lifecycle, SpamAssassin packaging and hosting/installer provisioning land. |
| Certificate lifecycle | Publish tested import/issuance/renewal, listener assignment, failure/rollback and IIS-versus-mail-binding workflows after implementation. |
| Defender scanning and capacity | Document the implemented adapter, effective limits, mitigation, queue/latency observability and measured hardware/workload envelope after VM acceptance. The 10,000-message scenario is still a requirement. |
| PHP retirement | Publish the final upgrade/cutover guide, verify old endpoints are disabled, then remove legacy PHP deployment material with the source/packaging migration. |
| Release and database matrices | Expand tested Windows/engine versions and capacity evidence as runs occur; do not generalize Express 2025 functional success into a universal throughput guarantee. |

Basic domain/account, alias/list management, forwarding/replies, mailbox signatures and advanced
domain/mailbox settings are merged through #41. The current larger bundle adds queue operations,
routes/allowed recipients and global/mailbox rules, with VM/CI acceptance on its application/test
head; review/merge and verification of later documentation heads remain. Phase 2
remains the separate PowerShell event-host migration after Phase 1b acceptance; UID lifecycle
implementation is planned in Phase 3, with earlier boundary fixes possible after authorization.
Keep this list, ROADMAP, README, requirements and the current agent handoff aligned at each PR.
All application previews, screenshots, runtime checks and tests execute only in the disposable
VM or CI; the development host is for editing, static inspection and compilation.

The source-tree link check found three pre-existing image references whose targets are absent
locally: `howto_route_through_isp.gif`, `howto_setup_ipranges_for_home_network.gif` and
`scr_installation_small.jpg`. Confirm whether the website supplies those legacy assets before
changing their paths; they remain part of the rendering/asset follow-up, not newly verified images.
