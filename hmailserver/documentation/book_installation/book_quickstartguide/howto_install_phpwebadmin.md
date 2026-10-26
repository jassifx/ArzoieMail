---
title: "Legacy PHP administration and migration"
slug: howto_install_phpwebadmin
parent: book_quickstartguide
index: 20
is_book: false
---

## Legacy PHP panel during migration

PHPWebAdmin is the inherited PHP/COM administration application. It remains in the current
installation while its replacement is completed. This page does not prescribe an old PHP 7.x
runtime or claim that the legacy panel has passed current PHP-version compatibility/security tests.
Do not treat historical PHP recipes as the new deployment architecture.

## Replacement direction

The ArzoieMail replacement is ASP.NET Core/.NET 10, with a modern panel, versioned REST API and
restricted native management channel. It has no PHP or COM backend dependency. IIS or standalone
hosting can be used, but final hosting, HTTPS and installer provisioning remain delivery work.
The current installer has not switched existing installations to the replacement.

Complete administration parity, upgrade/security/permission acceptance and an administrator
migration guide must precede removal of legacy PHP source, packaging and endpoints. Preserve
mail-server settings and disable the old endpoint at cutover; do not uninstall unrelated PHP
applications or a machine-wide PHP runtime.

See [management architecture](https://github.com/jassifx/ArzoieMail/blob/master/docs/management-api-architecture.md)
and [requirements](https://github.com/jassifx/ArzoieMail/blob/master/docs/webadmin-requirements.md)
for current evaluation boundaries and remaining work. The original page slug is retained so old
links still resolve during migration.
