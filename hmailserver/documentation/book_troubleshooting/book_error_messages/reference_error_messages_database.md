---
title: "Database error messages"
slug: reference_error_messages_database
parent: book_error_messages
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


### MySQL server has gone away

If ArzoieMail looses the connection to the database server during a query, ArzoieMail tries to re-open the connection and re-run the query. If this fails, the error MySQL server has gone away is logged. This can happen if the MySQL server is run on another computer, and the network connection is dropped. It can also happen when the MySQL server is about to be stopped, for example just before a computer reboot. In most cases, this failure will not have any side effects.
