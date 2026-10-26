# Phase 1b: Web administration and REST API requirements

Requirements agreed with the maintainer on 2026-10-26 and subsequently revised to remove
COM from the replacement panel/backend. Phase 1b implementation is now authorized and in progress;
requirements below are not claims of completed features. See
[management architecture](management-api-architecture.md) for the design and delivery status.

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

## Existing runtime and security requirements

- The replacement panel uses ASP.NET Core / .NET 10, not PHP. The previous PHP 8.5.12/8.6
  requirement is superseded for the replacement; legacy PHP deployment guidance applies only
  while that application remains in use.
- Retain CSRF tokens on every form, consistent output escaping, secure session cookies,
  session regeneration at login, a Content Security Policy and login rate limiting.
- Include diagnostics for the new runtime, HTTPS, native management connectivity and deployment
  configuration. Keep legacy PHP security guidance while documenting the migration.

## Validation and delivery

- Run all runtime tests and browser previews only inside the disposable VM or CI. The maintainer's
  development computer is for editing and compilation, not application or test execution.

- Establish repeatable API and browser tests against an installed ArzoieMail in the disposable
  Hyper-V VM, plus CI coverage. Check authentication, permissions, sessions, CSRF, escaping,
  administration operations and failure handling.
- Use deterministic DNS fixtures for SPF/DKIM/DMARC edge cases and provider coexistence.
  Live DNS checks supplement rather than replace reproducible tests.
- Follow AGENTS.md regression-test requirements for server features and bug fixes.
- Agree architecture, acceptance criteria and PR boundaries before implementation. Reassess
  effort for this expanded scope; the previous UI/security-refresh estimate does not cover it.
