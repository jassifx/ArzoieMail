---
title: "Security"
slug: reference_security
parent: book_configuration
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


This document describes the settings listed under Advanced -> Security.

### SSL/TLS ciphers

Use this setting to override the ciphers which ArzoieMail will allow for encrypting SSL/TLS connections. This should be entered in OpenSSL cipher list format.

### Verify remote server SSL/TLS certificates

During testing, you may want to disable certificate verification completely. To do this, de-select this option.

Keeping certificate verification enabled in production is strongly recommended.
