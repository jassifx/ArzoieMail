---
title: "OnDeliverMessage"
slug: scripting_ondelivermessage
parent: reference_scripts
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


## Signature

OnDeliverMessage(oMessage as hMailServer.Message)

## Result values

- 0 - Deliver the message
- 1 - Do not deliver the message

## Examples

VB-script:

```
If oMessage.Size > 1000000 Then
      ' This message is to big. We should not      
      ' deliver it.      
      Result.Value = 1                 
Else      
      ' Accept the message      
      Result.Value = 0    
End If
```
