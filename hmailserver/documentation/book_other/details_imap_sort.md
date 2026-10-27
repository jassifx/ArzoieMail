---
title: "IMAP Sort extension"
slug: details_imap_sort
parent: book_other
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


## Overview

hMailServer 4.2 and later supports the IMAP sort extension. This extension can improve performance when loading mailboxes that contains a large number of emails. If the IMAP client uses the IMAP sort extension, the sorting of messages are done on the server, instead of in the client. For example, if the mailbox contains 1000 emails and the client only wants to load the 10 newest, it can ask the server for the 10 newest emails. If the client does not use the IMAP sort extension, it has to download all 1000 emails and do the sorting itself.
