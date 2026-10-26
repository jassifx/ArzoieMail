---
title: "OnDeliveryFailed"
slug: scripting_ondeliveryfailed
parent: reference_scripts
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


## Overview

This event is executed if delivery to a recipient fails. If a message to multiple recipients cannot be sent, the event is executed once for every recipient.

Notice that the event is only executed if ArzoieMail has accepted a message for delivery. If ArzoieMail rejects a message, for instance during anti spam check, OnDeliveryFailed is not executed.

## Signature

OnDeliveryFailed(oMessage, sRecipient, sErrorMessage)

## Parameters

- oMessage - The message which has not been delivered.
- sRecipient - The recipient which the message has not been delivered to
- sErrorMessage - The error which caused the delivery failure

## Result values

- None
