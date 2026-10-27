---
title: "Security"
slug: security
parent: book_other
index: 0
is_book: false
---

## Service and compatibility identities

ArzoieMail's Windows service identifier remains `hMailServer`; its display name is ArzoieMail.
New installations use the isolated `NT SERVICE\hMailServer` identity where Windows can apply it;
legacy/fallback configurations must be reviewed independently. The native executable remains
`hMailServer.exe`. Do not rename compatibility files, COM ProgIDs or service registry keys by hand.

## Data, permissions and secrets

New layouts use `Program Files\ArzoieMail` for the application and `ProgramData\ArzoieMail`
for writable state. Upgrades may retain `ProgramData\hMailServer` or an administrator-configured
location. Check the installed `hMailServer.ini` rather than assuming every deployment uses a default.
Review ACLs on configuration, mail, logs, temporary files, scripts and certificate/private-key files;
restrict them to the required service and administrator identities for the chosen deployment.

Database and stored route/fetcher secrets now use AES-256-GCM with a random installation key
protected by machine-scope DPAPI. Administrator authentication is separate. The former statement
that database passwords are merely obfuscated with a universal Blowfish key is obsolete; old
values can be read and migrated. File access remains sensitive: encryption does not make mail
or configuration safe to expose. Export the installation key with a passphrase for portable recovery.

Built-in backups support an independent passphrase-protected archive. Without that passphrase,
do not assume a backup contains portable plaintext credentials. Keep recovery passphrases and
keys outside the repository and test restoration on a separate machine/VM.

## Management and transport boundaries

The modern panel uses ASP.NET Core/.NET 10, versioned REST and a restricted local native pipe.
It has no COM backend or direct SQL credentials. Its restricted hosting identity must not be an
administrator or LocalSystem. The existing desktop/setup/script COM API remains independently
available for compatibility; review its activation permissions separately.

Mail TLS, web HTTPS and database TLS have separate configuration. Full SQL Server requires
OLE DB 19, TCP and required encryption with certificate validation by default. Legacy PHP remains
a temporary migration surface and must be retired after replacement acceptance. Guided certificate
lifecycle and Defender email scanning are still planned; their requirements are not current capability.

See the current [management architecture](https://github.com/jassifx/ArzoieMail/blob/master/docs/management-api-architecture.md),
[SQL Server guide](https://github.com/jassifx/ArzoieMail/blob/master/docs/sql-server.md),
[backup guidance](?page=backup_restore) and [CI inventory](https://github.com/jassifx/ArzoieMail/blob/master/docs/ci-test-inventory.md).
