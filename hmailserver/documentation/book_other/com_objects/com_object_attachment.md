---
title: "Attachment object"
slug: com_object_attachment
parent: com_objects
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


### Description

The attachment object represents a single email attachment.

### Methods

<div class="api_method_name">Delete()</div>

<div class="api_description">Removes this attachment from the message.</div>

<div class="api_method_name">SaveAs(string sName)</div>

<div class="api_description">Saves the attachment to a file with the given name.</div>

### Properties

<div class="api_method_name">string Filename</div>

<div class="api_description">The filename of the attachment.<br> <i>(read-only)</i></div>

<div class="api_method_name">long Size</div>

<div class="api_description">The size of the attachment.<br> <i>(read-only)</i></div>
