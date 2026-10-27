---
title: "Alias object"
slug: com_object_alias
parent: com_objects
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


### Description

The alias object represents one ArzoieMail alias.

### Methods

<div class="api_method_name">Delete()</div>

<div class="api_description">Deletes the alias from the database.</div>

<div class="api_method_name">Save()</div>

<div class="api_description">Saves the alias to the database.</div>

### Properties

<div class="api_method_name">bool Active</div>

<div class="api_description">The alias is active.<br></div>

<div class="api_method_name">long DomainID</div>

<div class="api_description">The identifier of the domain in which the alias belong.<br></div>

<div class="api_method_name">long ID</div>

<div class="api_description">The unique database identifier for the alias.<br> <i>(read-only)</i></div>

<div class="api_method_name">string Name</div>

<div class="api_description">The name of the alias.<br></div>

<div class="api_method_name">string Value</div>

<div class="api_description">The value of the alias.<br></div>
