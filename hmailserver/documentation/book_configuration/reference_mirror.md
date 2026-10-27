---
title: "Mirror"
slug: reference_mirror
parent: book_configuration
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


# Overview

## Settings

### Mirror e-mail address

The email address all email should be forwarded to. 

- Messages with the mirror address in the recipient list are not mirrored to prevent looping.
- If a local address has been specified, but the domain or account is inactive, or the account does not exist, an error is logged.
