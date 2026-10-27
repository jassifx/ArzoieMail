---
title: "Group object"
slug: com_object_group
parent: com_objects
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


### Description

The Group object represents a single group set up in hMailServer.

### Methods

<div class="api_method_name">Delete()</div>

<div class="api_description">Deletes the object from the database.</div>

<div class="api_method_name">Save()</div>

<div class="api_description">Saves changes of the object in the database.</div>

### Properties

<div class="api_method_name">long ID</div>

<div class="api_description">The unique database identifier for the object.<br> <i>(read-only)</i></div>

<div class="api_method_name"><a href="?page=com_object_groupmembers">GroupMembers</a> Members</div>

<div class="api_description">Collection of group members<br> <i>(read-only)</i></div>

<div class="api_method_name">string Name</div>

<div class="api_description">The name of the group.<br></div>
