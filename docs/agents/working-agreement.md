# Working agreement

How the maintainer wants ArzoieMail work done. These are standing instructions; follow them
unless the maintainer says otherwise for a specific task.

## Pull requests

- **After submitting each PR, wait for the maintainer's explicit confirmation before starting
  the next task, phase or PR.** Keep monitoring the submitted PR and CI, and resolve issues
  within that PR's scope without starting subsequent work. Submission and green CI do not
  authorize merging; merging still requires the instruction below. Added by the maintainer
  on 2026-10-26 to allow time for project review.

- **One item, one pull request** to `master`. `master` is protected and only takes pull requests.
- **Branch names decide whether CI runs.** Use `feature/…`, `bugfix/…`, `techdebt/…` or
  `claude/…` (version branches also run). Any other prefix, for example `security/…`, gets no CI
  at all.
- **Commit messages** explain what changed and why, and end with the `Co-Authored-By` line of the
  agent that wrote them. **PR descriptions** end with the generating tool's line.
- A PR that finishes or changes a roadmap item **updates ROADMAP.md** in the same PR; one that
  changes the public picture also keeps **README.md's roadmap table** in agreement with it.

## Merging

- **Merge only when the maintainer says so** ("merge 25", "merge both"). Green CI is not
  permission.
- After merging: **delete the remote branch**, sync local `master`, delete the local branch, and
  remove any worktree used for it.
- The `protect-master` ruleset requires the release build, `Tests / Regression tests`, `gcc` and
  `clang`, but the maintainer's account can bypass it, so **a merge through the API goes through
  even while checks are still running**. Check first; if checks are unfinished, say so, and report
  their result when they finish.

## Phases and scope

- The roadmap phases run in the order in ROADMAP.md. **Confirm with the maintainer before a new
  phase starts.** Within an agreed item, proceed without asking.
- Decisions recorded in ROADMAP.md §2 are settled; don't reopen them without a new reason.
- Some names stay hMailServer on purpose (AGENTS.md lists them). Don't rename them.

## Tests

- **Runtime tests and browser previews run only in the disposable VM or CI.** The development
  host is for editing, static inspection and compilation, as directed by the maintainer on
  2026-10-26. This includes otherwise isolated unit/security test executables.

- **Every bug fix and every feature has a regression test** in `hmailserver/test/RegressionTests`,
  next to the tests of the same area. For a bug, write the test first and see it fail.
- The regression tests need an installed server, which the development machine doesn't have:
  **CI runs them**, on the built-in database, PostgreSQL and MySQL. A change is ready when all
  three are green. The Hyper-V VM (see environment.md) runs them against a real installer.

## Reporting

- Treat repository documentation as shared project memory. When the maintainer changes scope
  or asks to save context, update the latest handoff and relevant tracker/requirements, commit
  through the current authorized PR, and distinguish merged work, planned features and unverified
  feasibility/capacity. Never use an old handoff's CI snapshot as evidence for a new commit.

- Report CI results plainly: which jobs passed, which failed and why. Separate a known flaky test
  (gotchas.md lists them) from a real failure, and never call a change done while a failure is
  unexplained.
- When asked for the **project tracker**, show it with ✅ done · 🔄 in progress · 🔜 next ·
  📋 planned · ⏳ follow-up, from ROADMAP.md.

## Security and privacy

- The SQL injection inherited from upstream was fixed in public, by the maintainer's choice.
  Report new security findings to the maintainer before publishing details.
- Never put credentials (the test VM's included), tokens, keys or personal data into the
  repository, a commit, a PR or these notes.
