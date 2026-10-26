---
title: "Installation tutorial"
slug: howto_install
parent: book_quickstartguide
index: 0
is_book: false
---

## Before installation

Read the [current system requirements](?page=system_requirements), choose the database backend
and prepare backups if upgrading. Download ArzoieMail from its [GitHub releases](https://github.com/jassifx/ArzoieMail/releases)
or the download link in the project README. The release build produces an ArzoieMail-named setup
artifact; do not select an old upstream hMailServer installer as the current project release.

## Installation

1. Run the installer with administrator privileges, review the license and choose a local
   installation directory and components.
2. New installations use `Program Files\ArzoieMail` and `ProgramData\ArzoieMail`. Upgrades
   preserve existing layouts, including `ProgramData\hMailServer` where applicable.
3. Choose the built-in database or configure an external backend with Database Setup. Full
   SQL Server requires the driver, engine and trusted TLS prerequisites in the SQL guide.
4. Configure the server administrator credential and open ArzoieMail Administrator to connect
   to the installed service. For unattended installations, use the documented setup parameters
   and protect the supplied credential; do not copy secret values into scripts or logs.
5. Configure domains/accounts, DNS, necessary mail listener ports, firewall routes and TLS
   certificates, then verify delivery and client access before accepting production mail.

The executable remains `hMailServer.exe` and the service name remains `hMailServer` for
compatibility; the displayed product/service name is ArzoieMail. SQL prerequisite rejection
occurs before stopping the existing service or replacing its files.

## Web administration

The modern panel/API foundation is available for controlled evaluation, but complete administration
parity and production installer/HTTPS provisioning remain unfinished. Do not assume installing
this build automatically deploys it under IIS. Legacy PHP administration remains temporary until
the documented migration removes it; the replacement itself requires no PHP or COM backend.

## Upgrades and validation

Follow [upgrade guidance](?page=Upgrade_ToLatest), preserve database/mail/configuration/secret-key
backups and test restoration. Database upgrades may prevent an in-place binary downgrade.
Development tests and browser previews run only in the disposable VM or CI; the development host
is for source editing and compilation. See BUILDING.md and the CI inventory for validation scope.

Inherited setup screenshots were removed from this current walkthrough because they depict an
older upstream installer. New screenshots and website rendering remain documentation follow-ups.
