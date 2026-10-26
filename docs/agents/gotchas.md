# Gotchas

Problems that cost real time in earlier sessions, and how to avoid them. Lessons about the code
itself — formats, protocols, database behaviour — are in ROADMAP.md (§4, §5 and §10).

## Editing files

- **Shell quoting mangles Windows paths and C++.** Bash heredocs and `-c` strings ate
  backslashes and `$(...)`; JSON with Windows paths failed to parse. Write edit scripts, commit
  messages and PR bodies to files and run or pass the file. For replacements, a Python file of
  raw strings works.
- **Source files are CRLF, some with a UTF-8 BOM.** An edit script should read bytes, normalise to
  `\n`, apply exact replacements that must each match exactly once, then write back with the
  original line endings and BOM. Line-ending warnings from Git ("LF will be replaced by CRLF")
  come from `.gitattributes` and are harmless.
- Some editing tools strip trailing whitespace, and some original lines have it (for example
  `STDMETHODIMP ` before a line break). Match patterns that don't depend on it.
- `sed` patterns with backslashes are fragile on these files; prefer Python.

## Building

- **`Formatter::Format` takes at most five arguments**; split longer messages.
- `AnsiString::operator[]` is ambiguous; index through a `const std::string&`.
- C# rejects narrowing constant casts to `byte` that the old compiler accepted.
- A Debug build broke once because a header defined `new` as `DEBUG_NEW` (#10). CI only builds
  Release, so build Debug locally when you touch headers that the debug allocator involves.

## Database scripts

- **`CreateTables*.sql` are split into statements at blank lines, not at semicolons.** A new
  statement without a blank line before it is sent together with the previous one: MySQL rejects
  the script, the built-in database is not created, and *every* test fails. PostgreSQL happens to
  accept it, so one green database proves nothing.
- A schema change follows the checklist in AGENTS.md (four upgrade scripts, three create scripts,
  `REQUIRED_DB_VERSION`, DBUpdater).

## COM

- **A COM class must implement `ISupportErrorInfo`** for its error messages to reach callers.
  Without it, .NET sees only `0x800403E9`, and a test that checks the message fails.
- New methods get new `id`s in `hMailServer.idl`; the tools and tests must be rebuilt against the
  new type library.

## Tests

- **Wait for delivery before a test ends** (`Pop3ClientSimulator.AssertMessageCount`). Otherwise
  the next test's setup deletes the account mid-delivery, the server logs HM5165, and the *next*
  test fails in teardown — which points at the wrong test.
- The SMTP and POP3 simulators read plain connections as ASCII; non-ASCII bytes arrive as `?`.
- **Known flaky tests** (live internet): `AntiSpam.AuthenticationResults.TestHeaderIsAdded` and
  `Infrastructure.DnsResolution.CNameRecordsShouldBeFollowed`. When one fails alone, re-run the
  job; when it fails twice, look closer.
- On .NET an exception on a thread-pool thread kills the test agent; it doesn't just fail a test.

## CI

- **The runner kills processes a step started when that step ends.** A helper that must outlive
  its step — such as the MySQL server — runs as a Windows service.
- The CI cache prunes library folders; keep what the build needs (the MariaDB headers) and change
  the cache key when its contents change.
- A branch whose prefix the workflows don't list gets no CI. The PR looks fine and simply never
  gets checks.

## Git and the working copy

- **An editor can lock files.** A `git pull` once stopped halfway with "unable to unlink
  RegressionTests.csproj", leaving `master` at the old commit with the new files written. Check
  that the working tree matches `origin/master` (`git diff origin/master --stat`), then
  `git reset origin/master` (mixed, no file changes) and `git checkout -- <the locked file>`.
- Use a separate worktree (`git worktree add`) to fix another branch while uncommitted work sits
  in the main clone, and remove it afterwards.

## Agent sessions

- Background commands started with a trailing `&` are not tracked; use the tool's own background
  mode so the session is told when they finish.
- The session's scratch folder can be cleared between sessions. Keep anything that must last —
  tools, notes — in the repository (like `tools/gh.py`) or in a handoff.
