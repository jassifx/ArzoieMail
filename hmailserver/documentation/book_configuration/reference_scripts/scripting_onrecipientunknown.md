---
title: "OnRecipientUnknown"
slug: scripting_onrecipientunknown
parent: reference_scripts
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


## Overview

This event is fired when an SMTP client specifies a recipient in a local domain which does not exist. ArzoieMail rejects the recipient regardless of what the script does, so the event is used for logging or notification purposes.

## Signature

OnRecipientUnknown(oClient as hMailServer.Client, oMessage as hMailServer.Message)

## Result values

- None

## Examples

VB-script:

```
Sub OnRecipientUnknown(oClient, oMessage)

   EventLog.Write("Unknown recipient from " & oClient.IPAddress)

End Sub
```
