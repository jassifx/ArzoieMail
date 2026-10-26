---
title: "System requirements"
slug: system_requirements
parent: book_installation
index: 10
is_book: false
---

## Platform and runtime

ArzoieMail targets **64-bit Windows 10 / Windows Server 2016 or newer**. The current installer
and imported native libraries require that baseline; historical hMailServer support for XP,
Windows 7 or older Server releases does not apply. See BUILDING.md for the exact toolchain.

Desktop administration tools use .NET 10; the installer includes the Desktop Runtime for offline
installation when needed. The replacement web panel uses ASP.NET Core/.NET 10 and requires its
own hosting/runtime provisioning. IIS and standalone hosting are deployment options under design;
the panel is not yet provisioned as the production replacement by the installer.

## Database

| Backend | Current project status |
|---|---|
| SQL Server Compact 4.0 | Existing built-in backend; CI-covered. Replacement by SQLite is planned. |
| SQL Server 2019 or newer, including Express | OLE DB 19, TCP and required TLS/certificate validation. SQL Server 2025 Express is the verified VM/CI target. |
| MySQL / MariaDB | External backend through bundled MariaDB Connector/C; MySQL has an installed-server CI job. Validate the chosen server version. |
| PostgreSQL | External backend through bundled libpq; installed-server CI coverage. Validate the chosen server version. |

SQL Server's optional OLE DB 19 driver is not bundled: install its x64 driver and both x86/x64
Visual C++ prerequisites, and configure trusted SQL TLS before upgrading an MSSQL installation.
See [SQL Server deployment](https://github.com/jassifx/ArzoieMail/blob/master/docs/sql-server.md)
and [database choices](?page=choosing_database_engine). Do not follow old SQL Server 2000 or
bundled-MySQL instructions for a new ArzoieMail deployment.

## Administration and security

The desktop Administrator remains available. The new web panel/API does not use COM or direct SQL;
the legacy COM API remains for desktop tools, setup and existing scripts. PHP is required only
by the legacy PHP panel during migration, not by the replacement.

Provide appropriate DNS, firewall/router configuration and certificates for the hostnames clients
use. The mail-service TLS bindings and IIS web HTTPS binding are separate. Optional antivirus
integrations require their own validated engine configuration. Defender integration is planned,
not delivered; do not infer email scanning from the presence of Windows Defender alone.

## Capacity and verification

Choose CPU, RAM and storage from measured accounts, message sizes, protocol concurrency, database
work and scanning load. This project does not publish a validated universal message-rate limit.
The planned Defender 10,000-message pressure test is an acceptance requirement, not a benchmark
already passed. Review [CI coverage](https://github.com/jassifx/ArzoieMail/blob/master/docs/ci-test-inventory.md)
and [security](?page=security) before deployment.
