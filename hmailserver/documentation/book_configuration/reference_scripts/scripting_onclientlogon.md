---
title: "OnClientLogon"
slug: scripting_onclientlogon
parent: reference_scripts
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


## Overview

This event is fired when a client has logged on to ArzoieMail using SMTP, POP3 or IMAP. The event is fired after the user name and password have been verified, so it cannot be used to reject the logon. Use it for logging or notification purposes.

## Signature

OnClientLogon(oClient as hMailServer.Client)

## Result values

- None

## Examples

VB-script:

```
Sub OnClientLogon(oClient)

   EventLog.Write("User " & oClient.Username & " logged on from " & oClient.IPAddress)

End Sub
```
