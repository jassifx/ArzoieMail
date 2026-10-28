# Working on ArzoieMail — notes for agents and new contributors

These notes are for the AI agents and people who pick up ArzoieMail work. They record what the
repository's other documents don't: how the work is organised, the environment it runs in, the
traps found along the way, and where the last session left off.

## Read in this order

Current handoff: [2026-10-28](handoffs/2026-10-28.md). It records merged work through #41,
the authorized queue/routing/rules bundle, validation evidence, remaining delivery and the
deferred CodeQL re-audit. The previous handoff retains historical baselines and environment
lessons. The maintainer prefers grouped Phase 1b capabilities over tiny PRs.
The [CI inventory](../ci-test-inventory.md) separates automated checks from manual/planned
coverage. Verify live PR/CI state before continuing; planned features are not implemented.
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

- **Don't copy what is written elsewhere.** ROADMAP.md, AGENTS.md, BUILDING.md, commit messages
  and pull requests are the record; link to them.
- **A finished roadmap item updates ROADMAP.md**, not these notes.
- Keep the newest handoff current when the maintainer changes the plan or asks to save project
  memory. Record authorization/review gates and link the authoritative requirements; remove or
  clearly supersede stale status so the next agent cannot mistake old PHP plans for current work.
- **At the end of a session, add a handoff** to [`handoffs/`](handoffs/) named `YYYY-MM-DD.md`
  (the `handoff` skill describes what goes in one). Older handoffs stay as history; only the
  newest describes the current state.
- **Never write secrets or personal data here**: no passwords (including test VM credentials),
  tokens, keys or email addresses.
- Add a lesson to [`gotchas.md`](gotchas.md) when something costs time and could catch the next
  person too; add an engineering lesson about the code to ROADMAP.md instead.
