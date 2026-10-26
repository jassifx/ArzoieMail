---
title: "SQL Server and Express connections"
slug: howto_connect_to_mssql
parent: howtos
index: 0
is_book: false
---

## Full SQL Server / Express

ArzoieMail's full SQL Server backend uses x64 Microsoft OLE DB 19, TCP and required TLS with
certificate validation. SQL Server 2019+ is the engine baseline; SQL Server 2025 Express is the
verified VM/CI target. The driver and its x86/x64 VC prerequisites are external prerequisites.

Use Database Setup for the mail-service connection and a separately authorized database tool
for administrator diagnostics. Windows authentication uses the mail service's actual identity;
SQL authentication uses its protected configured credential. Do not give a web panel direct SQL
access or assume the interactive administrator's rights apply to the service.

The [SQL Server deployment guide](https://github.com/jassifx/ArzoieMail/blob/master/docs/sql-server.md)
contains endpoint/port settings, trusted certificate setup, Mandatory/Strict options and upgrade
requirements. Test the selected version and workload before production use.

## SQL Server Compact is different

The built-in Compact database is a separate backend/file format; it is not SQL Server Express.
The old instructions on this page used a Windows-directory INI and a Blowfish decryption script.
They do not describe the current AES-GCM/DPAPI credential storage or modern Express connections.
Use the configured database path and authorized tools appropriate to that backend. Keep backups
and secret-key exports, and do not edit database rows or copy plaintext credentials into logs.
