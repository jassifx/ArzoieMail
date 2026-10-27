---
title: "Connect to MySQL"
slug: howto_connect_to_mysql
parent: howtos
index: 0
is_book: false
---

## Current MySQL / MariaDB connection

ArzoieMail connects to an external MySQL/MariaDB server through bundled MariaDB Connector/C.
Use Database Setup and an administrator-provisioned database identity with appropriate permissions.
Use a separately authorized database client for diagnostics; the web panel must not access SQL directly.

The current installer does not bundle an old MySQL server or require MySQLFront. Historical
Windows-directory INI paths and DecryptBlowfish scripts are not the current credential workflow.
Configured database passwords are protected through the installation's secret-key mechanism.
Do not paste plaintext credentials or a decryption result into logs, documentation or source control.

See [database choices](?page=choosing_database_engine), [security](?page=security),
[backup/restore](?page=backup_restore) and the current project CI inventory. An older MySQL-server
upgrade or different MariaDB version needs its own compatibility/recovery test; current MySQL CI
coverage does not validate every version combination.
