# ArzoieMail — pre-detachment engineering archive

> Original repository: `jassifx/ArzoieMail`; common ancestor: `hmailserver/hmailserver`.
>
> Snapshot master: `92ac28dea6fe2b136b7e57fc3cc69ced6fd80b34` (merged PR #43).
> Export prepared 28 October 2026, *before* GitHub fork-network detachment.

## Why this archive exists

GitHub warns that leaving a fork network does not retain original pull requests, issues, comments, review discussions, stars, watchers, wikis or other GitHub-specific metadata. Git commit metadata is preserved. Original PR URLs may cease to work. This independent archive makes the PR-era engineering record available from normal repository Git history.

## Contents and verification

- [Full PR index](PULL_REQUEST_INDEX.md): original numbers, titles and outcomes.
- [Raw original PR snapshots](pull-requests/): `PR-001.json` through `PR-043.json`. Each snapshot includes the PR REST response, available conversation/inline/review records, PR commits, changed-file entries including GitHub's available patches, issue timeline and check-run metadata for its head SHA.
- [Machine-verifiable manifest](MANIFEST.json): each archived JSON Git blob SHA and byte size, for audit of source snapshots.
- Git history: inherited and ArzoieMail commits are kept in the repository; the snapshot does **not** reapply already-merged code.
- Closed, unmerged original PR #16: head commit `4b4f2b9e563265edc5849c2165d2724cadd43437` preserved on branch `archive/closed-pr-016-unmerged-head`. This is historical reference, **not approved code**.

**Snapshot counts:** 43 original PRs; 42 merged; one closed without merging (#16); zero open at the time of export. Exported PR files were independently counted by enumerating the archive Git tree (43 of 43 present). API exports yielded no reported fetch or pagination errors in the per-PR export steps. Original GitHub PR search/URLs may not survive detachment.

## Limitations and what must be saved separately

This is a snapshot of **GitHub API records available at export**, not a byte-for-byte copy of the GitHub service's internal database. It does not embed expiring Actions job logs or downloadable build artifacts, expired third-party image attachments, repository secrets, unpublished workflow-run logs, branch protection settings, or all GitHub-hosted metadata. Check runs are indexed by their API metadata and URLs; those URLs may become unavailable after detachment. Original PRs have no open work awaiting merge.

**Do not detach until an independent offline backup has been verified.** The repository archive alone is not a backup against repository deletion, corruption or unexpected service behavior. Before clicking *Leave fork network*, do from a trusted workstation:

```bash
git clone --mirror https://github.com/jassifx/ArzoieMail.git ArzoieMail-pre-detach.git
cd ArzoieMail-pre-detach.git
git show-ref > ../ArzoieMail-pre-detach-refs.txt
git fsck --full
```

Store the resulting mirror, refs listing and exported CI artifacts outside GitHub, and preserve an immutable copy. If necessary use `gh run download` on the important still-available CI runs before detachment. Check critical settings, workflow permissions, credentials, release configuration and repository links afterward.

## Provenance and licensing

The code remains historically derived from hMailServer even after its independent GitHub identity is established. Retain the applicable licence texts, copyright notices and original authorship. No PR snapshot should be described as a clean-room rewrite or a new code merge. This archive is for audit, migration and engineering continuity.
