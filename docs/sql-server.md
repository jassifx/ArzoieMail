# SQL Server and Express deployment

## Scope and compatibility

The SQL Server modernization work uses **Microsoft OLE DB Driver 19 (x64)** and sets a
**SQL Server 2019 or newer** engine baseline, including Express. This is separate from the
built-in SQL Server Compact database. Compact remains unchanged until the planned SQLite migration.

The driver name is `MSOLEDBSQL19`. The service no longer silently selects SQLOLEDB, SQLNCLI
or OLE DB 18. An old `Provider` override produces a migration error instead of a downgrade.
Windows ADO remains the native data-access wrapper; it is supported with OLE DB 19 and is not
the web administration COM API. The replacement panel still uses REST/native management only.

Current implementation/validation status belongs in ROADMAP.md and the latest agent handoff.
SQL Server 2025 Express is the new VM/CI target. Other qualifying engine versions require
version-specific validation before claiming a tested deployment combination.

## Install prerequisites and configure TLS

1. Back up the mail database, mail files, configuration and exportable secret key before an
   upgrade. Test restoring those backups. Upgrade older SQL engines before the mail service.
2. Install both x86 and x64 Visual C++ redistributable prerequisites and the x64
   [Microsoft OLE DB Driver 19](https://learn.microsoft.com/en-us/sql/connect/oledb/download-oledb-driver-for-sql-server).
   Download signed installers in advance for offline deployment; the ArzoieMail installer does
   not download or bundle this optional external-database driver. Existing MSSQL upgrades are
   blocked before file replacement when its driver or Provider migration is missing.
3. Configure SQL Server's own TLS certificate and full chain, covering the DNS name used by
   ArzoieMail. Trust its issuing CA on the mail-service machine. This is independent of IIS HTTPS
   and SMTP/IMAP/POP3 certificates. Do not use a trust bypass as the production default.
4. Configure the SQL endpoint, enable TCP where required and open only the necessary firewall
   route. For Express named instances use `HOST\SQLEXPRESS` with port 0, or prefer an explicit
   fixed TCP endpoint. Named-instance discovery can require SQL Browser; a fixed port avoids that
   dependency. Do not open SQL Server to the public internet for web-panel access.
5. Provision a dedicated database identity. Windows authentication uses the actual mail-service
   identity, not the person running DBSetup. A local virtual service account must have a SQL login
   on a local SQL instance; remote Windows authentication needs an appropriate domain identity.
   Give provisioning privileges only while creating the database, then restrict normal operation
   to its own database. Never run the web application as SQL sysadmin or give it database credentials.
6. Run DBSetup and test initialization, message delivery, restart and backup/restore in the VM
   before deploying the same configuration to production.

## Connection settings

These `[Database]` keys apply to the full SQL Server backend:

```ini
[Database]
Type=MSSQL
Provider=MSOLEDBSQL19
Server=sql.example.test
Port=1433
Database=ArzoieMail
Username=
MSSQLEncryption=Mandatory
MSSQLTrustServerCertificate=0
MSSQLHostNameInCertificate=
```

- `Provider` may be blank (the same OLE DB 19 default) or `MSOLEDBSQL19`.
- All SQL Server connections use TCP with TLS, including local connections; shared-memory,
  named-pipe and LocalDB deployments are outside this supported configuration.
- `Port=0` preserves the server/instance endpoint; a positive port sets its TCP port. A port may instead
  be included in `Server`, such as `tcp:sql.example.test,1433`; do not specify it in both places.
  DBSetup exposes the SQL port field, including the instance/default value 0.
- Blank `Username` selects Windows integrated authentication. For SQL authentication, use DBSetup
  so the existing protected database-password storage is used. Do not add plain passwords to docs.
- `MSSQLEncryption=Mandatory` is the default; unencrypted/Optional connections are rejected.
  `Strict` is supported for SQL Server 2022 and later with compatible server TLS configuration.
- `MSSQLTrustServerCertificate=0` is the default and validates the certificate. Value `1` is an
  explicit exception for isolated testing only: it skips server identity validation while keeping
  encryption. It is rejected with Strict mode. Invalid values fail rather than weaken security.
- `MSSQLHostNameInCertificate` optionally supplies an expected certificate name when connecting
  through a differently named endpoint; use a deliberately provisioned name, not a trust bypass.
- Change protected INI settings as an administrator and restart/reinitialize the mail service.
  The same options apply to DBSetup's create/select operations and all service connections.

Connection values are quoted, preserving delimiters in SQL credentials without allowing those
values to change encryption properties. The adapter explicitly enables ADO data-type compatibility
and identifies its SQL sessions as `ArzoieMail`. It does not log its connection string.

## Express capacity

Express has edition/version resource limits. Passing functional tests does not establish that it
can sustain the high-pressure mail or scanning workload. Size and measure database CPU, memory,
connection/lock contention, metadata growth, backups and queue operations alongside the scan engine.
Message bodies are stored in the mail data directory; that does not remove database limits.
Use the [Microsoft edition matrix](https://learn.microsoft.com/en-us/sql/sql-server/editions-and-components-of-sql-server-2025)
for the selected release; do not assume limits from an older Express version.

## Repeatable tests

`build/provision-mssql-test.ps1 -DisposableEnvironment` installs SQL Server 2025 Express and
OLE DB 19 only inside an explicitly selected disposable VM/CI environment. It configures a
fixed test endpoint and a VM-only certificate. It is not a production installer. Microsoft-signed
payloads are verified, and the selected OLE DB MSI is additionally SHA-256 pinned.

`build/test-mssql-connection.ps1` exercises rejection of an untrusted certificate, wrong hostname,
legacy provider and Optional encryption; SQL-authenticated schema creation with delimiter-bearing
credentials; Windows authentication, Strict and Mandatory TLS; and encrypted native sessions.
Provisioning dbcreator rights are removed after setup. The full regression and management suites
then run on the installed SQL Server-backed service in CI, in addition to the other database jobs.
Installer publishing waits for all four database jobs. Runtime tests never run on the development host.

VM certificates, SQL fixture users, logs and installer media remain outside source control. Restore
the VM checkpoint after testing; CI runners are discarded. A negative-policy probe can be run against
the old build before deployment to demonstrate the previous behavior.

## Microsoft references

- [Current provider and encryption changes](https://learn.microsoft.com/en-us/sql/connect/oledb/major-version-differences)
- [ADO compatibility requirements](https://learn.microsoft.com/en-us/sql/connect/oledb/applications/using-ado-with-oledb-driver-for-sql-server)
- [Driver installation prerequisites](https://learn.microsoft.com/en-us/sql/connect/oledb/applications/installing-oledb-driver-for-sql-server)
