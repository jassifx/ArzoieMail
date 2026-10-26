---
title: "GroupMember object"
slug: com_object_groupmember
parent: com_objects
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


### Description

The GroupMember object represents a single member in a ArzoieMail group.

### Methods

<div class="api_method_name">Delete()</div>

<div class="api_description">Deletes the object from the database.</div>

<div class="api_method_name">Save()</div>

<div class="api_description">Saves changes of the object in the database.</div>

### Properties

<div class="api_method_name"><a href="?page=com_object_account">Account</a> Account</div>

<div class="api_description">Returns the account referenced by this member.<br> <i>(read-only)</i></div>

<div class="api_method_name">long AccountID</div>

<div class="api_description">The database identifier of the member account.<br></div>

<div class="api_method_name">long GroupID</div>

<div class="api_description">The database identifier of the group this member belongs to.<br></div>

<div class="api_method_name">long ID</div>

<div class="api_description">The unique database identifier for the object.<br> <i>(read-only)</i></div>
