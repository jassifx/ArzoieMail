# Message identifiers and IMAP UID lifecycle plan

Status: planned, added at the maintainer's request on 2026-10-27 (Asia/Kolkata).
This document authorizes no implementation or phase change. Deliver each implementation slice
through a separate reviewed PR, with all runtime validation confined to the disposable VM or CI.

## Goal and protocol boundary

Eliminate identifier truncation, signed formatting errors, counter wraparound, accidental UID
reuse and unsafe manual database resets. Keep database message identities independent of IMAP
folder membership and preserve message content and client-visible identity correctly.

Standard IMAP UIDs and UIDVALIDITY values are nonzero unsigned 32-bit numbers. A folder has its
own UID namespace; expunging mail does not permit reuse within the same UIDVALIDITY generation.
A new generation requires clients to detect changed UIDVALIDITY and resynchronize. See
[RFC 3501 section 2.3.1.1](https://www.rfc-editor.org/rfc/rfc3501.html#section-2.3.1.1),
[RFC 9051 section 2.3.1.1](https://www.rfc-editor.org/rfc/rfc9051.html#section-2.3.1.1) and
[RFC 2683 section 3.4.3](https://www.rfc-editor.org/rfc/rfc2683.html#section-3.4.3).

Sending arbitrary 64-bit UIDs to existing IMAP clients is not a compatible solution. The goal
is safe capacity management, not unlimited messages in one folder or invisible client resync.
If live membership cannot fit in a fresh UID namespace, or no fresh valid UIDVALIDITY can be
allocated, stop that operation safely and expose an administrator-controlled recovery/archival
path. Never wrap either counter or silently discard messages.

## Current evidence

- The create-table scripts use 64-bit database message IDs, separate `messageuid` values and
  folder UID counters. `Message` has an unsigned IMAP UID and an inherited 64-bit database ID.
- `PersistentIMAPFolder::GetUniqueMessageID` increments the database counter and narrows it to
  an unsigned integer. It needs checked bounds and explicit allocation-failure handling.
- SELECT/EXAMINE still format UIDNEXT using signed `%d`; other read/format/cast paths need a
  complete audit. A 64-bit database column alone does not establish end-to-end capacity.
- The inherited [database maintenance page](https://www.arzoie.in/docs/maintenance_database)
  conflates global database message IDs with per-folder IMAP UIDs. `MAX(messageid)` is not the
  IMAP UID-headroom metric. Its source and published website both need reviewed replacement
  guidance; no billions-of-messages capacity claim has been validated.

## Delivery slices

| Slice | Work | Acceptance |
|---|---|---|
| 1. Identifier and boundary safety | Introduce explicit identifier types; audit database adapters, native objects, caches, IMAP parsing/formatting, administration APIs, backups and compatibility interfaces. Fix narrowing and signed-format defects; check UIDNEXT before serialization. | Values around 2^31 and 2^32 serialize/read correctly or fail safely. Internal IDs remain intact beyond 32 bits. Existing clients and migrations retain their documented contracts. |
| 2. Transactional allocation | Allocate a UID and persist folder membership under a database transaction and per-folder serialization. Commit before publishing cache/client notifications. Check affected rows, failures and counter bounds; do not rely solely on a process-local mutex. | Concurrent delivery/APPEND/COPY/MOVE cannot publish duplicate, zero, decreasing or wrapped UIDs. Rollback/retry/restart preserve mail and ordering. Other folders continue operating. |
| 3. Controlled generation renewal | Schedule renewal before exhaustion; quiesce the affected folder, close selected sessions, stage a new UID map and fresh UIDVALIDITY, atomically switch generations and invalidate caches. Resume queued work with durable, idempotent recovery. | Every old-generation session is rejected/closed before reuse. A crash exposes either the old complete map or the new complete map. Message files, flags, dates, stable IDs and POP3 UIDLs remain correct. |
| 4. Operations and documentation | Add native/REST/panel counters, headroom, allocation rate, exhaustion estimates, progress/failure state and maintenance scheduling. Provide explicit archival/retention recovery policy and rewrite/publish database-maintenance guidance. | Administrators can identify and manage a constrained folder without SQL resets. Normal operation reports the active generation; estimates state their measurement window. Publication is verified separately from Markdown edits. |

Boundary fixes can be brought forward through the continuous code-quality track after explicit
authorization. Allocation and generation renewal belong to Phase 3 protocol/data-integrity work.
Complete UID-lifecycle acceptance before claiming high-volume production readiness; do not tie
this native work to PHP deletion or mark it delivered by the SQLite migration alone.

## Design requirements

- Use explicit types for database IDs, IMAP UIDs and UIDVALIDITY. Keep required database IDs
  64-bit throughout every adapter and serialization boundary. Browser APIs must preserve exact
  values beyond JavaScript's safe-integer range, for example with decimal strings in a versioned
  contract. Keep existing COM/native/REST consumers compatible through deliberate migration;
  never silently narrow an out-of-range identifier.
- Preserve standards-valid UIDNEXT headroom. Use checked wider arithmetic internally and
  validate the wire value before conversion. Choose the renewal threshold and reserved headroom
  explicitly, including behavior at the final usable UID; never emit zero or an oversized UIDNEXT.
- UID reservations may have gaps, but must not be reused within an exposed generation.
  Reservation, insertion, visibility and notification ordering must remain correct under
  concurrency. Failures must be observable; an unsuccessful increment must not return stale data.
- Add uniqueness and generation constraints appropriate to the chosen schema. Any schema change
  follows AGENTS.md for every supported backend, creation/upgrade scripts and DBUpdater. Include
  the planned SQLite backend in the design and validate it when that backend is implemented.
- Keep renewal off the SMTP hot path. Scope maintenance to the affected folder; hold its new
  delivery work durably, with bounded resources and backpressure, while other folders proceed.
  Include SMTP delivery, IMAP APPEND/drafts, COPY/MOVE, external fetching, imports and rules.
- Quiescence must cover selected read-only/shared-folder sessions as well as writers. Fence
  commands and background work by generation so a stale operation cannot run against a new map.
  Rebuild caches, indexes and protocol sequence mappings after the atomic activation.
- Allocate a nonzero UIDVALIDITY greater than the previous value for that mailbox name. Preserve
  the necessary history when folders are deleted/recreated or backups are restored; a random
  replacement or a restored older counter must not cause reuse of a previously exposed identity.
- A restart must resume or roll back a staged renewal safely. Temporary mappings must not
  become visible prematurely. Backup/restore must preserve or safely invalidate generations;
  restoring an old counter must not rebind a client's cached UID to a different message.
- Retain data while a folder is unavailable. Retry must not duplicate delivery or falsely mark
  a recipient delivered. Provide progress, audit records, scheduling, cancellation boundaries
  and a recovery procedure. Client resynchronization and its network/storage cost are explicit.
- Preserve stable content identity and any per-content-version antivirus verdict references.
  Changing an IMAP UID mapping alone must not be treated as changing message content or
  disconnect future scan/delivery state from the message it protects.
- A nearly full live UID namespace needs archival or another explicit capacity decision;
  renumbering alone cannot create sufficient room. Do not silently split folders or auto-delete
  mail as a fallback. UIDVALIDITY exhaustion also requires an explicit safe-failure policy.

## Validation and completion gate

Seed disposable fixtures near 2,147,483,647 and 4,294,967,295; do not create billions of real
messages just to test arithmetic. Include internal database IDs beyond 32 bits and API exactness
around 2^53. Exercise SELECT, EXAMINE, STATUS, UID FETCH/SEARCH/STORE, IDLE, APPEND, COPY/MOVE,
EXPUNGE, reconnects and client-cache invalidation, adding coverage for extensions as they land.

Run concurrent writers and readers, queued deliveries, shared folders and server-saved drafts.
Inject allocation failures, transaction rollbacks, disconnects and crashes before/during/after
generation activation. Verify backup/restore, no duplicate/lost delivery, stable POP3 UIDLs and
unchanged message content/flags/dates. Test folders with low churn and high churn, plus a live
membership count too large for the configured renewal headroom.

Execute regression and recovery tests on Compact, PostgreSQL, MySQL and SQL Server Express;
include SQLite once available. Test representative supported IMAP clients and publish their
observed resynchronization behavior. Measure staging time, storage overhead, p95/p99 latency,
queue growth and recovery under load. Boundary simulation does not establish sustained throughput.

Completion requires passing protocol/data-integrity tests, a reviewed migration and recovery
procedure, panel/API evidence and corrected published documentation. Each PR is followed by the
maintainer's review gate; this roadmap request starts no native implementation.
