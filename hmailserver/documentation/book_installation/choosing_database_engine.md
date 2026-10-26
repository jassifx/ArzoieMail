---
title: "Choosing database engine"
slug: choosing_database_engine
parent: book_installation
index: 15
is_book: false
---

## Supported database paths

ArzoieMail has four database backends: SQL Server Compact, full SQL Server (including Express),
MySQL/MariaDB and PostgreSQL. These store server configuration and message metadata; message
bodies remain in the mail data directory. Back up both components consistently.

- **SQL Server Compact 4.0** is the current built-in database. It is distinct from Express and
  remains covered by CI. SQLite replacement and automatic migration are planned, not implemented.
- **SQL Server** uses Microsoft OLE DB 19 over TCP with required TLS and certificate validation.
  The engine baseline is SQL Server 2019+. SQL Server 2025 Express has passed the current full CI
  suite; that does not validate every other SQL version or high-load deployment.
- **MySQL/MariaDB** use the bundled MariaDB Connector/C (`libmariadb.dll`). MySQL CI covers the
  native driver's persistence path. Version-specific upgrades still need their own validation.
- **PostgreSQL** uses the bundled libpq driver and has its own installed-server CI job.

The external SQL Server driver and its prerequisites must be installed separately. See
[SQL Server / Express deployment](https://github.com/jassifx/ArzoieMail/blob/master/docs/sql-server.md)
for endpoint/authentication settings, trusted certificates, driver migration and offline preparation.
The legacy description that every database option works without any extra prerequisites is obsolete.

## Selection and operations

Choose a backend based on the required capacity, administration skills, backup/recovery plan and
measured workload. Express has edition/version limits; a successful functional suite is not a
capacity guarantee. Check the relevant vendor's current limits and terms for the exact version.
The original manual's blanket commercial-use claims about Compact are not a substitute for those terms.

Use Database Setup to create/select the database. The service must have access under its actual
Windows or SQL identity; the web panel must not receive database credentials. Preserve protected
credentials, the installation's exportable secret key and a tested restore path when moving hosts.
See [backup and restore](?page=backup_restore) and the current CI inventory.
