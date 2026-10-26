---
title: "ArzoieMail Administrator error messages"
slug: reference_error_messages_hmailadmin
parent: book_error_messages
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


## ArzoieMail Administrator errors

### The remote server machine does not exist or is unavailable

When you connect to a ArzoieMail instance using ArzoieMail Administrator, DCOM is used. ArzoieMail connects to the ArzoieMail service using the DCOM API and then manages the server via the API. ArzoieMail Administrator (and PHPWebAdmin) does not connect directly to the database server. Because of this, to be able to run ArzoieMail Administrator, the ArzoieMail service must be running.

If ArzoieMail Administrator is unable to connect to the ArzoieMail service, you may see the error above. By default, ArzoieMail Administrator tries to connect to the ArzoieMail instance running on localhost. If you get this error message, make sure that the host name you're trying to connect to in the connection dialog really is correct. If it is, you should ask a question in the [user forum](https://www.hmailserver.com/forum/)
