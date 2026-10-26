---
title: "WhiteListAddress object"
slug: com_object_whitelistaddress
parent: com_objects
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


### Description

The WhiteListAddress object represents a single white listed IP address or email address.

### Methods

<div class="api_method_name">Delete()</div>

<div class="api_description">Deletes the object from the database.</div>

<div class="api_method_name">Save()</div>

<div class="api_description">Saves changes of the object in the database.</div>

### Properties

<div class="api_method_name">string Description</div>

<div class="api_description">Description of the record.<br></div>

<div class="api_method_name">string EmailAddress</div>

<div class="api_description">E-mail address to whitelist<br></div>

<div class="api_method_name">long ID</div>

<div class="api_description">The unique database identifier for the object.<br> <i>(read-only)</i></div>

<div class="api_method_name">string LowerIPAddress</div>

<div class="api_description">Lower IP address.<br></div>

<div class="api_method_name">string UpperIPAddress</div>

<div class="api_description">Upper IP address.<br></div>
