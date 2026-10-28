# Working on ArzoieMail — notes for agents and new contributors

These notes are for the AI agents and people who pick up ArzoieMail work. They record what the
repository's other documents don't: how the work is organised, the environment it runs in, the
traps found along the way, and where the last session left off.

## Read in this order

Current handoff: [2026-10-28](handoffs/2026-10-28.md). PR #42 is merged at `74eecc017`;
its final head `28dd3df` passed all four backend jobs, 1317 NUnit cases, 897 management
assertions and both browser suites. The current authorized task is a documentation PR for the
maintainer's approved [frozen 6.0 scope](../version-6-scope.md) and
[breaking changes](../breaking-changes-6.0.md), including complete COM removal and migration
of every supported tool/test/automation consumer. Implementation remains pending. All current
phases are mandatory; later scope changes require approval. Read the [release policy](../release-versioning.md)
for identities and release gates. No next implementation bundle is authorized.
The [authority index](../README.md) defines document ownership, status meanings and evidence
updates. The [Phase 1b ledger](../phase-1b-status.md) tracks granular gaps and all legacy
PHP/desktop main-pane migration owners. Read them before calling a feature or phase complete.
The [CI inventory](../ci-test-inventory.md) separates automated checks from manual/planned
coverage. Verify live PR/CI state before continuing; planned features are not implemented.
The [CI acceptance matrix](../ci-acceptance-matrix.md) records the newly requested per-backend
.NET 10/runtime/publish/real REST/HTTPS/native/browser checks and remaining acceptance gaps.
The [documentation status](../documentation-status.md) records the manual rebrand, refreshed
current guides and remaining inherited-content, screenshot and website-publication work.

| # | Document | What it gives you |
|---|---|---|
| 1 | [`handoffs/`](handoffs/) — the newest file | Where the work stands, what is waiting on the maintainer, what to do next |
| 2 | [`../../ROADMAP.md`](../../ROADMAP.md) | The source of truth for the project: decisions, every change with its PR, lessons, plans |
| 3 | [`../../AGENTS.md`](../../AGENTS.md) | Code layout, the schema checklist, test rules, the names that stay hMailServer |
| 4 | [`working-agreement.md`](working-agreement.md) | How the maintainer wants the work done: PRs, merges, phases, reporting |
| 5 | [`environment.md`](environment.md) | Building, CI, driving GitHub without the `gh` CLI, the test VM |
| 6 | [`gotchas.md`](gotchas.md) | Problems that cost real time, and how to avoid them |
| 7 | [`../../BUILDING.md`](../../BUILDING.md) | Building, testing and releasing in full |

Tools: [`tools/gh.py`](tools/gh.py) drives GitHub (checks, logs, pull requests, merges) through
its REST API.

## Keeping these notes useful

- **Use the owning document.** Follow the authority index; keep summaries and handoffs aligned
  through links and concise status/evidence, without creating a competing plan.
- **A finished roadmap item updates ROADMAP.md**, not these notes.
- Keep the newest handoff current when the maintainer changes the plan or asks to save project
  memory. Record authorization/review gates and link the authoritative requirements; remove or
  clearly supersede stale status so the next agent cannot mistake old PHP plans for current work.
- **At the end of a session, add a handoff** to [`handoffs/`](handoffs/) named `YYYY-MM-DD.md`
  using the required fields in the authority index and working agreement. Older handoffs stay as history; only the
  newest describes the current state.
- **Never write secrets or personal data here**: no passwords (including test VM credentials),
  tokens, keys or email addresses.
- Add a lesson to [`gotchas.md`](gotchas.md) when something costs time and could catch the next
  person too; add an engineering lesson about the code to ROADMAP.md instead.
