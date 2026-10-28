# ArzoieMail documentation authority and reading index

Repository documentation is the shared project record for maintainers and AI agents. A new
agent must be able to establish requirements, delivered behavior, validation, unresolved work
and authorization without recovering a chat transcript. This index defines which document
owns each subject. README summaries and handoffs link to those owners instead of creating
competing plans.

## Authoritative records

| Subject | Owner | Required detail |
|---|---|---|
| Product overview and entry points | [Root README](../README.md) | Accurate public summary, current phase, review state and links to the detailed records |
| Engineering decisions, phase sequence and historical delivery | [ROADMAP](../ROADMAP.md) | Settled decisions, merged PRs, active proposals, dependencies, future phases and open follow-ups |
| Detailed Phase 1b capability and migration status | [Phase 1b ledger](phase-1b-status.md) | Delivered versus missing controls, legacy surface inventory, restrictions, acceptance and PHP cutover gates |
| Agreed web administration requirements | [Requirements](webadmin-requirements.md) | Product and security obligations, certificate and Defender requirements; requirements are not completion evidence |
| Management boundary and security design | [Architecture](management-api-architecture.md) | Native/REST/.NET separation, authentication, roles, trust boundaries and deployment design |
| Implemented REST contract | [Management README](../hmailserver/source/Management/README.md) | Actual routes, fields, limits, permissions, defaults, errors, recovery and preview restrictions |
| Builds, tests, releases and prerequisites | [BUILDING](../BUILDING.md) | Reproducible commands and runtime prerequisites; runtime execution stays in VM/CI |
| Test coverage and evidence | [CI inventory](ci-test-inventory.md) | Exact tested revision, backend counts, checks, retries, fixture boundaries and manual/planned coverage |
| SQL Server and Express | [SQL Server guide](sql-server.md) | OLE DB 19, TLS policy, supported/tested matrix, diagnostics and migration limits |
| Defender design and capacity acceptance | [Defender assessment](defender-antivirus-feasibility.md) | Feasibility limits, safe integration, lifecycle/pressure acceptance; not an implementation claim |
| Database IDs and IMAP UID modernization | [Identifier plan](imap-uid-modernization.md) | Boundaries, checked allocation, generation renewal, migration and client-resynchronization acceptance |
| End-user manual and publication gaps | [Manual index](../hmailserver/documentation/README.md), [documentation status](documentation-status.md) | Current guides, inherited/historical content, technical review, assets and publication still required |
| Codebase rules and compatibility identifiers | [AGENTS](../AGENTS.md) | Layout, naming, persistence/schema, regression and build rules |
| Maintainer instructions | [Working agreement](agents/working-agreement.md) | Scope authorization, VM-only runtime testing, PR/CI/review/merge gates |
| Current task and resumption | [Agent index](agents/README.md), [latest handoff](agents/handoffs/2026-10-28.md) | Branch/base, PR, verified head, work done, open issues, next permitted action and stop conditions |
| Local setup and lessons | [Environment](agents/environment.md), [gotchas](agents/gotchas.md) | Local prerequisites, VM procedures and known traps; never credentials |

## Status and evidence rules

- **Merged** means the PR is merged into the development branch. Include its PR/merge reference.
- **Implemented, verified, awaiting review** means code exists on an unmerged branch and named
  checks passed on a specific commit. It does not mean released or installed for users.
- **Partial** identifies delivered subfeatures and names the missing parts. A legacy desktop
  or PHP capability does not establish that the replacement native/REST/panel exposes it.
- **Planned / not started** is a requirement or design without accepted implementation.
- **Unverified / decision required** is an unresolved question, not an assumed capability.
- **Historical** records an earlier state. Older handoffs and test counts cannot override the
  latest explicit record, or prove that a later revision passed.

Source code establishes implemented behavior; tests and CI establish only the coverage they
actually execute. If either disagrees with a document, record the discrepancy and correct the
document or implementation. Do not use documentation to override an observed defect. GitHub
is the live record for PR state and check execution; repository snapshots must give the exact
revision/date and direct evidence links. A documentation commit cannot truthfully claim its
own future CI result: record the last verified head, then update the PR's validation record
after the new head finishes. Handoffs must tell the next agent to verify that final head.

The inherited manual has not had a complete setting-by-setting operational review. Its
remaining uncertainty is explicitly tracked. Calling this documentation the project record
does not turn unreviewed inherited recipes into validated instructions.

## Required update procedure

1. Read the latest handoff, working agreement, roadmap and relevant requirement/contract.
2. Inventory every affected native operation, REST route, panel workflow, legacy consumer,
   role boundary, migration requirement and test. Record unsupported or intentionally restricted
   behavior; never hide it under a broad "done" label.
3. Update the owning document and all affected summaries in the same PR. Feature changes must
   keep the Phase 1b ledger, roadmap, README, REST contract, CI inventory, documentation status
   and handoff aligned. New/removed legacy files must update the parity inventories.
4. Record exact verification: revision, VM/CI environment, counts, failures, fixes/retries and
   limitations. Keep local artifact paths optional; checked-in procedures and CI links must
   let another agent work without ignored `out/` files.
5. At submission, record the PR, branch and review gate. After CI, put final-head results in
   the PR validation record. At merge/next authorized change, reconcile repository snapshots,
   mark the merged item and clean only the authorized branches/artifacts.
6. Preserve historical evidence with explicit labels. Keep secrets out of every document,
   log excerpt, commit and PR. Run link/inventory/stale-status checks before committing.

Do not infer approval from a recommendation, green CI or elapsed time. Suggested next bundles
remain proposals until the maintainer authorizes them. Do not merge or begin the next phase
automatically.
