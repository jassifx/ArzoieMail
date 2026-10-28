# ArzoieMail documentation source

This is the ArzoieMail manual, inherited from hMailServer and maintained with this repository.
Product prose uses ArzoieMail. Historical releases, original authorship, source filenames,
URL slugs, COM identifiers, service/executable names and examples needed for compatibility
retain their original names. See [AGENTS.md](../../AGENTS.md) for the compatibility list.

The former description of an upstream hmailserver.com daily publishing job does not describe
an ArzoieMail deployment. This repository contains the manual source; an ArzoieMail rendering,
link/image check and publication pipeline still need to be finalized. The main README links
readers to the ArzoieMail documentation site; that is not evidence these edits are deployed.

## 6.0 migration notice

The [approved 6.0 scope](../../docs/version-6-scope.md) requires all roadmap phases and complete
COM API removal after supported tools/tests/automation migrate to secured management APIs.
Current manual COM, VBScript/JScript and PHPWebAdmin examples describe intermediate or legacy
behavior; they are not supported 6.0 end-state recipes. The [migration register](../../docs/breaking-changes-6.0.md)
tracks conversion, recovery and release-note requirements. Optional existing open-source webmail
may use PHP; that does not retain PHP administration. Existing executable/service/INI/installer
identities remain. Production support differs from minimum technical compatibility and requires
applicable vendor security updates plus project validation. No 6.0 public release is announced.

## Start here

- [ArzoieMail 6 release/version policy](../../docs/release-versioning.md): the independent next
  release target is 6.0.0; historical hMailServer version references below retain their provenance.

- [Documentation status and remaining work](../../docs/documentation-status.md)
- [Project roadmap](../../ROADMAP.md) and [CI coverage](../../docs/ci-test-inventory.md)
- [System requirements](book_installation/system_requirements.md)
- [Install ArzoieMail](book_installation/book_quickstartguide/howto_install.md)
- [Database choices](book_installation/choosing_database_engine.md)
- [Security and secret storage](book_other/security.md)
- [Backup and restore](book_maintenance/backup_restore.md)
- [SQL Server / Express deployment](../../docs/sql-server.md)
- [Management architecture](../../docs/management-api-architecture.md)

## Structure and compatibility

One Markdown file represents each page. A section's body is `_index.md`; child pages are
sibling files or subdirectories. YAML front matter supplies `title`, `slug`, `parent`, `index`
and `is_book`. Keep `slug`, `parent`, existing filenames, `?page=` targets and image paths stable
until the website migration has an explicit redirect/asset plan. Change the visible title and
prose without silently breaking those routes.

The inherited manual contains 258 Markdown files including this index. Naming updates do not
establish that every inherited instruction is current. Historical upgrade/integration pages are
labelled separately; current installation/security/database guides take precedence over old
platform, package-version or credential-decryption instructions. The remaining operational and
rendering review is tracked in the status document.

Fenced/inline code and compatibility identifiers are preserved during the prose rename. Reviewed
current guides replace obsolete operational examples explicitly. Old
upstream screenshots and version-specific examples may still show hMailServer; new screenshots
must be captured in the disposable VM as those workflows are verified. No third-party attribution
or upstream history is being reassigned to ArzoieMail.
