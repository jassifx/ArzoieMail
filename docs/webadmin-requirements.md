# Phase 1b: Web administration and REST API requirements

Requirements agreed with the maintainer on 2026-10-26 and subsequently revised to remove
COM from the replacement panel/backend. Phase 1b implementation is now authorized and in progress;
requirements below are not claims of completed features. See
[management architecture](management-api-architecture.md) for the design and delivery status.

The API/panel foundation is merged in PR #31 and SQL Express modernization in PR #33. Full
administration parity, certificate lifecycle, Defender scanning and PHP retirement remain pending.
Merged PR #35 implements basic domain/mailbox CRUD, quota controls, password resets, roles
and paginated search with native permissions. The next authorized slice adds alias/list/member
administration and posting-policy controls. Their precise fields and remaining limits are
documented in the [REST contract](../hmailserver/source/Management/README.md#rest-v1-contract);
advanced mailbox settings and self-service are still pending acceptance/delivery.
Keep end-user guidance aligned with [documentation status](documentation-status.md); inherited
manual naming updates do not imply that these requirements have been implemented.

## Modern administration

- Deliver a modern, responsive Bootstrap interface from the first release, replacing dtree,
  tabber and legacy styling. Provide searchable navigation, accessible controls, consistent
  forms and clear feedback for validation, failed operations and long-running work.
- Provide comprehensive administration and fine-tuning, including advanced settings. Preserve
  existing administration functions and server, domain and account permission boundaries.
  Explain consequential settings and confirm destructive operations.
- Use ArzoieMail in user-facing text while preserving compatibility identifiers in AGENTS.md.

## REST API from the first release

- The panel uses a documented, versioned REST API for management operations from its first
  release. REST is brought forward from Phase 6 into Phase 1b.
- Enforce authentication, authorization and input validation in the API, not just the browser.
- The replacement panel and API must not use COM. Host them on ASP.NET Core / .NET 10 and
  reach the native business layer through a restricted local management channel. Preserve COM
  separately for existing desktop tools and scripts during migration.
- Enforce authorization in the native management dispatcher as well as at the HTTP boundary.
  Use the architecture and security requirements in management-api-architecture.md.

## SpamAssassin and DNSBL control

- Integrate SpamAssassin administration: connection configuration, availability checks, scoring,
  thresholds and test results. Whether to bundle and operate the SpamAssassin engine itself,
  and which engine settings can be exposed, remains an explicit design decision.
- Support multiple administrator-chosen DNSBL providers, with add, configure, enable, disable
  and delete operations. Do not restrict administrators to Spamhaus.
- Expose lookup zones, expected response codes, scores and rejection messages where supported.
  Identify server/API additions needed to deliver the agreed controls.
- Present SpamAssassin and server DNSBL checks separately and explain how their scores interact
  so administrators can avoid unintended double scoring.

## Microsoft Defender Antivirus option

- Offer Microsoft Defender Antivirus as an optional email malware scanner alongside the existing
  antivirus integrations. Keep it separate from SpamAssassin and DNSBL spam scoring.
- Provide guided enable/disable, availability and signature-health checks, a test-scan action,
  results and actionable errors through the modern panel and authorized native REST operations.
  Scan messages and decoded attachments in the mail service, never in the IIS/web process.
- Use a dedicated adapter with clean, infected, error and unscannable outcomes. Configure actions
  for confirmed detections and unavailable/failed/skipped scans separately; default scan failures
  to a bounded hold/retry policy rather than silently treating mail as clean. Show limits,
  retry exhaustion and encrypted/unsupported content clearly to the administrator.
- Avoid destructive scanning of live mail-store files. Use protected staging, detection-only
  invocation, bounded execution/concurrency and safe cleanup, with the mail server controlling
  the resulting message action. Test interference from independent real-time antivirus activity.
- Do not disable Defender, tamper protection or organizational policy, add broad exclusions, or
  grant the web process elevated scan access. Respect administrator-managed cloud/sample-submission
  policy and explain the privacy implications before enabling integration.
- Support high-pressure activity across SMTP receipt/submission, queued outbound delivery,
  external fetching, server-saved IMAP drafts/APPEND and imports. Client-local drafts become
  scannable only when uploaded. Re-scan changed content and enforce a valid verdict before
  delivery or normal user retrieval; specify safe staging for each protocol entry point.
- Use a durable work queue and bounded workers with fair scheduling, backpressure, crash recovery
  and idempotent mitigation/delivery transitions. Expose queue age/depth, throughput, latency,
  resource limits and overload alerts. A simple increase in concurrent scanner processes is
  insufficient. Validate at least 10,000 in-flight/queued messages, mixed lifecycle activity,
  bursts, outages, recovery and soak load in the VM before claiming high-volume support.
- Publish measured capacity for specified hardware and message mix, with numeric throughput
  and latency acceptance targets and headroom. Thousands concurrently queued is not a defined
  per-second rate. Defender remains conditional on these results; retain an alternative scanner
  path if it cannot sustain the required pressure without releasing unscanned mail.
- This is feasible in principle, not implemented or VM-validated. Require VM-only clean/EICAR,
  MIME attachment, archive, error, timeout, permission and throughput acceptance before delivery.
  See [Defender feasibility and acceptance](defender-antivirus-feasibility.md).

## SPF, DKIM and DMARC guidance

- Include per-domain recommendations and validators from the first release. Generate copyable
  DNS record names and values for administrators to publish at their DNS provider. Automatic
  DNS-provider writes are not required.
- Read published DNS on demand and explain missing records, invalid configuration and recommended
  corrections. Distinguish lookup failures and inconclusive results from invalid records.
- Handle complex SPF policies shared with other sending providers. Inspect existing policies
  and propose a merged record preserving legitimate senders; do not blindly overwrite provider
  settings or recommend a second SPF record. Account for nested includes, redirects, DNS lookup
  limits, syntax and conflicting policies. Show the proposed change for administrator review.
- Provide DKIM selector and public-key publication guidance, and DMARC policy/alignment guidance.
  Account for all legitimate sending providers before recommending stronger DMARC enforcement.
- Distinguish DNS publication validation from actual message authentication: a published DKIM
  key alone does not prove outgoing messages are signed correctly or aligned for DMARC.
- DMARC aggregate reporting remains in Phase 6; DNS setup and validation are in Phase 1b.

## Certificate setup and lifecycle

The maintainer requires user-friendly TLS certificate installation and configuration for
SMTP, IMAP, POP3 and web administration. These are delivery requirements, not implemented
features of the foundation merged in PR #31.

- Provide a guided wizard with plain-language explanations and sensible defaults. Let the
  administrator choose automatic certificate issuance/renewal or import an existing certificate
  and private key (including chain and password-protected PFX import). Avoid requiring manual
  file editing, certificate conversion or repeated configuration for each protocol.
- Show the hostnames mail clients use and validate certificate coverage, key matching, chain,
  validity and service access before applying a change. Explain that one certificate can serve
  SMTP, IMAP and POP3 when it covers their configured hostnames; mail domains do not each need
  a certificate when clients connect through a shared covered mail hostname.
- Offer explicit assignment to mail listeners and explain implicit TLS versus STARTTLS.
  Preview affected services, ports and any interruption; confirm changes and provide a safe
  rollback if installation or activation fails. Never silently weaken TLS after a failure.
- Support automatic issuance and renewal through ACME, with guided domain-control validation
  and actionable DNS/network prerequisite messages. Provider/client, challenge support and
  scheduling implementation must be selected and documented before delivery. Do not claim
  automation is available until issuance and renewal have been verified end to end.
- Show certificate issuer, covered names, expiry, listener assignments, renewal status and last
  error in one place. Warn about approaching expiry and failed renewals; validate replacements
  before activation and retain the working certificate on failure. Activate renewed certificates
  with minimal disruption and verify that the affected listeners present the new certificate.
- Treat web HTTPS and mail TLS as separate bindings, whether the panel runs under IIS or as a
  standalone service. Reuse a certificate only when it covers the required names and access is
  explicitly provisioned; an IIS HTTPS binding does not configure SMTP, IMAP or POP3.
- Restrict certificate changes to server administrators. Keep private keys and issuance secrets
  out of browser responses, logs and source control. The web process requests narrowly scoped
  operations through the native API; a restricted privileged component performs installation,
  key protection and activation. Do not grant the web process unrestricted filesystem or key access.

## PHP retirement and runtime security

- Remove PHP completely from the delivered administration application. The replacement is
  ASP.NET Core / .NET 10 with REST and native management access; it has no PHP runtime, PHP
  extension, COM bridge or PHP fallback dependency.
- Retire `hmailserver/source/WebAdmin` and remove its packaging entries, deployment instructions,
  PHP runtime checks and obsolete assets when replacement parity and migration acceptance pass.
  Do not delete unrelated administrator-managed PHP applications or a machine-wide PHP installation.
- Provide an upgrade guide for existing WebAdmin users, preserving mail-server settings and
  supported permission boundaries. Explain the new URL, hosting identity, HTTPS setup and how to
  disable the old endpoint. Verify that upgrades leave no active legacy PHP administration route.
- Legacy PHP remains a temporary migration component until that cutover. The former PHP 8.5/8.6
  modernization goals are superseded; do not develop a second PHP replacement or claim the
  legacy application has already been removed. Keep deployment guidance accurate until retirement.
- Retain antiforgery protection, consistent output escaping, secure session cookies, session
  regeneration at login, a Content Security Policy and login rate limiting.
- Include diagnostics for the .NET runtime, HTTPS, native management connectivity, certificate
  lifecycle and deployment configuration.

## Validation and delivery

- Run all runtime tests and browser previews only inside the disposable VM or CI. The maintainer's
  development computer is for editing and compilation, not application or test execution.

- Establish repeatable API and browser tests against an installed ArzoieMail in the disposable
  Hyper-V VM, plus CI coverage. Check authentication, permissions, sessions, CSRF, escaping,
  administration operations and failure handling.
- Verify certificate import, wrong-key/hostname/chain rejection, role restrictions, initial
  issuance, successful and failed renewal, expiry warnings, rollback and the certificate
  presented by SMTP/IMAP/POP3 and HTTPS. Exercise upgrade removal of the old PHP endpoint.
- Use deterministic DNS fixtures for SPF/DKIM/DMARC edge cases and provider coexistence.
  Live DNS checks supplement rather than replace reproducible tests.
- Follow AGENTS.md regression-test requirements for server features and bug fixes.
- Agree architecture, acceptance criteria and PR boundaries before implementation. Reassess
  effort for this expanded scope; the previous UI/security-refresh estimate does not cover it.
