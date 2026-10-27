---
title: "Upgrading to ArzoieMail"
slug: Upgrade_ToLatest
parent: book_upgrading
index: 0
is_book: false
---

## Upgrade to ArzoieMail

ArzoieMail preserves compatibility identifiers so an existing hMailServer installation can be
upgraded in place. This does not guarantee that an arbitrary historical release can skip every
intermediate migration. Use the supported source/target schema path and test the upgrade in an
isolated VM before changing production.

1. Back up the database, mail files, configuration, certificates and scripts; export the
   installation secret key with a passphrase. Verify restoration on a separate machine/VM.
2. Review the current system requirements and release notes. Close administration tools.
3. For full SQL Server, prepare SQL Server 2019+, x64 OLE DB 19, its x86/x64 VC prerequisites,
   TCP and trusted certificate configuration before installing the new mail-service binary.
   Remove legacy provider overrides as described in the SQL Server guide. Compact is separate.
4. Run the ArzoieMail installer and complete database setup/updating. Existing paths and database
   settings are retained where supported. If a required SQL prerequisite is missing, setup must
   refuse before stopping the running service or replacing files.
5. Verify startup, schema version, credentials, queued delivery, client protocols, TLS and
   backups. Preserve the pre-upgrade backup until the migration has been accepted.

The executable, service identifier, COM ProgID, database table prefix and backup XML filename
retain their compatibility names. Do not rename these files or keys by hand during upgrade.
A schema upgrade is not undone by replacing only the executable; restore the complete compatible
backup for a rollback. Older version-specific pages in this section are historical references.

See [SQL Server deployment](https://github.com/jassifx/ArzoieMail/blob/master/docs/sql-server.md),
[security](?page=security), [backup/restore](?page=backup_restore) and ROADMAP.md for current status.
