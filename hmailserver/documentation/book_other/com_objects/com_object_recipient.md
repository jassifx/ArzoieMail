---
title: "Recipient object"
slug: com_object_recipient
parent: com_objects
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


### Description

The Recipient object represents a single email recipient (found in the SMTP envelope).

### Properties

<div class="api_method_name">string Address</div>

<div class="api_description">The e-mail address of this recipient<br> <i>(read-only)</i></div>

<div class="api_method_name">bool IsLocalUser</div>

<div class="api_description">Is this user a local user?<br> <i>(read-only)</i></div>

<div class="api_method_name">string OriginalAddress</div>

<div class="api_description">The original e-mail address<br> <i>(read-only)</i></div>
