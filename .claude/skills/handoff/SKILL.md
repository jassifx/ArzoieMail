---
name: handoff
description: Compact the current conversation into a handoff document for another agent to pick up.
argument-hint: "What will the next session be used for?"
disable-model-invocation: true
---

Condense this conversation into a handoff note that lets a fresh agent resume the work without re-reading the transcript.

Write the file to `docs/agents/handoffs/YYYY-MM-DD.md` in the repository (today's date; add `-2`
for a second one on the same day), so the next agent finds it from `docs/agents/README.md`. It is
committed like any other change, on its own branch and pull request. Keep lasting guidance out of
it: a working rule belongs in `docs/agents/working-agreement.md`, an environment fact in
`environment.md`, a trap in `gotchas.md`, and an engineering lesson in `ROADMAP.md`.

What to write:

- The task, why it's being done, and where it currently stands.
- Decisions already made and options already ruled out, so the next agent doesn't reopen them.
- What remains: the next concrete step, plus anything blocked or waiting on the user.
- Gotchas discovered along the way — failing commands, environment quirks, dead ends.
- A "Suggested skills" section listing the skills the next agent should invoke via the Skill tool.

What to leave out:

- Anything already written down elsewhere: specs, plans, ADRs, issues, commit messages, diffs. Link or cite the path instead of copying the content.
- Secrets and personal data. Strip API keys, passwords (test VM credentials included), tokens,
  and PII — the file is published with the repository.

If arguments were passed, they describe the next session's focus — weight the document toward that and trim detail that isn't relevant to it.
