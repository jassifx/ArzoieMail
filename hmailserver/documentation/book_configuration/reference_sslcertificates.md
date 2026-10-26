---
title: "TLS certificates and planned guided setup"
slug: reference_sslcertificates
parent: book_configuration
index: 0
is_book: false
---

## Current certificate configuration

ArzoieMail's native mail service reads a PEM certificate chain and private-key file. The desktop
Administrator lets an administrator add a named certificate under advanced settings and assign
it to the appropriate TCP/IP listeners with their SSL/TLS or STARTTLS connection mode. Protect
private-key files so only the required service and administrators can access them.

Use a certificate covering the hostname clients actually connect to, with the required intermediate
chain. One certificate can serve SMTP, IMAP and POP3 when it covers their configured hostnames.
An IIS HTTPS binding does not configure these native mail listeners. Reconfigure/restart affected
listeners as required and verify the certificate actually presented to clients.

## Guided lifecycle - planned Phase 1b work

The replacement panel must provide guided certificate import, validation and assignment, plus
automatic issuance/renewal, expiry/status feedback, safe activation and rollback. Certificate/key
matching, hostname/chain validation, service permissions and failure recovery must be verified.
PFX import and automated certificate management are requirements, not features delivered by the
current API foundation. The present native file configuration is not a completed wizard.

See [certificate requirements](https://github.com/jassifx/ArzoieMail/blob/master/docs/webadmin-requirements.md#certificate-setup-and-lifecycle)
and [connection security](?page=reference_ssltls). Treat mail TLS, web HTTPS and SQL Server TLS
as separate bindings and trust configurations, even when a deliberately provisioned certificate
can be reused for multiple covered names. Never put private keys or issuance secrets in logs or
source control.
