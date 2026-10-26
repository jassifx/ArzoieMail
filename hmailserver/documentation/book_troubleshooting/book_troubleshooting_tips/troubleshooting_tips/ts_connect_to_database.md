---
title: "Connect to the database"
slug: ts_connect_to_database
parent: troubleshooting_tips
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


Open the ArzoieMail Log directory in Windows Explorer. Open the file named ERROR_*.log in a text-editor
and look at the last lines in it.

- Error: Cannot connect to xx on host xx   

 Reason: Cannot connect to xx on host xx. ArzoieMail cannot connect to the database.
- Error: Cannot open requested database   

 Reason: Make sure that the database specified in hMailServer.ini actually exists and that the connecting user has
 the appropriate rights to use the database.
