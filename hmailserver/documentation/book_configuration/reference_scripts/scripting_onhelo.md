---
title: "OnHELO"
slug: scripting_onhelo
parent: reference_scripts
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


## Overview

This event is fired when an SMTP client sends the HELO or EHLO command. At this point the client has not been authenticated, so oClient.Username returns an empty string. The host name given by the client is available in oClient.HELO.

## Signature

OnHELO(oClient as hMailServer.Client)

## Result values

- 0 - ArzoieMail accepts the HELO/EHLO command
- 1 - ArzoieMail rejects the command with the error *554 Rejected*
- 2 - ArzoieMail rejects the command with *554* and a script-defined message
- 3 - ArzoieMail rejects the command with *453* and a script-defined message. This is a temporary error, so the client is expected to try again later.

## Examples

VB-script:

```
Sub OnHELO(oClient)

   If oClient.HELO = "" Then
      Result.Message = "Invalid HELO host name."
      Result.Value = 2
   End If

End Sub
```
