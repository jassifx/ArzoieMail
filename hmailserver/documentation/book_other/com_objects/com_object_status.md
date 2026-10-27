---
title: "Status object"
slug: com_object_status
parent: com_objects
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


### Description

The Status object contains information on your system, such as uptime and basic statistics.

### Properties

<div class="api_method_name">long ProcessedMessages</div>

<div class="api_description">Gets the number of delivered messages<br> <i>(read-only)</i></div>

<div class="api_method_name">long RemovedSpamMessages</div>

<div class="api_description">Gets the number of detected spam messages<br> <i>(read-only)</i></div>

<div class="api_method_name">long RemovedViruses</div>

<div class="api_description">Gets the number of removed virues<br> <i>(read-only)</i></div>

<div class="api_method_name">long SessionCount(eSessionType iType)</div>

<div class="api_description">Gets the current number of sessions<br> <i>(read-only)</i></div>

<div class="api_method_name">string StartTime</div>

<div class="api_description">Gets the server start time<br> <i>(read-only)</i></div>

<div class="api_method_name">long ThreadID</div>

<div class="api_description">Gets the current thread number<br> <i>(read-only)</i></div>

<div class="api_method_name">string UndeliveredMessages</div>

<div class="api_description">Gets a list of undelivered messages.<br> <i>(read-only)</i></div>
