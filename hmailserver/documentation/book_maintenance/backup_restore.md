---
title: "Backup & Restore"
slug: backup_restore
parent: book_maintenance
index: 10
is_book: false
---

## What must be preserved

Back up the database, mail data, configuration, certificates/keys and event scripts consistently.
The database stores configuration and message references; copying mail files alone is not a
complete server backup. Coordinate snapshots or service downtime so database and files describe
the same state. Verify restoration in a separate VM before relying on a backup procedure.

## Built-in protected backups

ArzoieMail supports passphrase-protected built-in backups. With a passphrase, the archive and
its filenames are encrypted, messages are included in the archive and the protected settings can
be restored on a different machine. Use the current Administrator's backup controls and the
corresponding encrypted-backup APIs. Keep the passphrase separately and test correct/wrong-password
and cross-machine recovery procedures.

Without a backup passphrase, an archive is not a portable encrypted export of the installation's
secrets. Database/route/fetcher credentials protected by the installation key require the matching
key for recovery; do not assume the old manual's plaintext-secret XML behavior still applies.
The compatibility filename inside a backup remains `hMailServerBackup.xml`.

## Installation secret key

Current secret values use AES-256-GCM under a random per-installation key protected by
machine-scope DPAPI. Export/import that key with its own passphrase when moving installations or
using an external database/file backup. An INI copied to another machine is not by itself a
portable secret-key backup. Legacy Blowfish values are read and migrated; old decryption recipes
are not the current credential-management procedure.

## Operations and acceptance

Choose a backup schedule and retention/capacity plan for the actual deployment and backend.
Use SQL Server/MySQL/PostgreSQL backup tools under appropriate privileges when taking external
backups; preserve the corresponding mail files and key material. Do not treat old numerical
account/message thresholds in the inherited manual as a validated ArzoieMail capacity limit.

Development backup/restore tests run only in the disposable VM or CI and can replace settings,
accounts and messages. See [security](?page=security), [upgrade guidance](?page=Upgrade_ToLatest),
[ROADMAP](https://github.com/jassifx/ArzoieMail/blob/master/ROADMAP.md) and the CI inventory for
current implementation and verified coverage.
