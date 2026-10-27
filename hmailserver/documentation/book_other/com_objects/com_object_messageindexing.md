---
title: "MessageIndexing object"
slug: com_object_messageindexing
parent: com_objects
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


### Description

IInterfaceMessageIndexing Interface

### Methods

<div class="api_method_name">Clear(void)</div>

<div class="api_description">Clear the message indexing cache.</div>

<div class="api_method_name">Index()</div>

<div class="api_description">Trigger an index start if it is not already running.</div>

### Properties

<div class="api_method_name">bool Enabled</div>

<div class="api_description">Gets or sets whether message indexing is enabled.<br></div>

<div class="api_method_name">long TotalIndexedCount</div>

<div class="api_description">Gets the total number of indexed messages in the ArzoieMail installation.<br> <i>(read-only)</i></div>

<div class="api_method_name">long TotalMessageCount</div>

<div class="api_description">Gets the total number of messages in the ArzoieMail installation.<br> <i>(read-only)</i></div>
