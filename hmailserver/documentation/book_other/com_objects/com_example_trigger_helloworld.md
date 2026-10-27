---
title: "Trigger example: Hello World"
slug: com_example_trigger_helloworld
parent: com_objects
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


## Overview

It's possible to implement custom scripts in ArzoieMail which are executed when certain actions occrur. This example shows you how to write the text Hello World to the Event log whenever a message is accepted for delivery by hMailServer.

### Script

``

<p><span class="Code">Sub OnAcceptMessage(oClient, oMessage)<br>
   EventLog.Write("Hello World")<br>
End Sub</span></p>
