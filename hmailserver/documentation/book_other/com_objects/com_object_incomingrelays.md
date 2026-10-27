---
title: "IncomingRelays object"
slug: com_object_incomingrelays
parent: com_objects
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


### Description

A collection of incoming relays set up on the server.

### Methods

<div class="api_method_name">Add()</div>

<div class="api_description">Adds a new incoming relay to the collection.</div>

<div class="api_method_name">Delete(long Index)</div>

<div class="api_description">Deletes the incoming relay with the specified index.</div>

<div class="api_method_name">DeleteByDBID(long DBID)</div>

<div class="api_description">Deletes the incoming relay with the specified database identifier.</div>

<div class="api_method_name">Refresh()</div>

<div class="api_description">Refreshes the collection.</div>

### Properties

<div class="api_method_name">long Count</div>

<div class="api_description">The number of objects in the collection.<br> <i>(read-only)</i></div>

<div class="api_method_name"><a href="?page=com_object_incomingrelay">IncomingRelay</a> Item(long Index)</div>

<div class="api_description">Returns an incoming relay by a given index.<br> <i>(read-only)</i></div>

<div class="api_method_name"><a href="?page=com_object_incomingrelay">IncomingRelay</a> ItemByDBID(long DBID)</div>

<div class="api_description">Returns a incoming relay by a given database identifier.<br> <i>(read-only)</i></div>

<div class="api_method_name"><a href="?page=com_object_incomingrelay">IncomingRelay</a> ItemByName(string sName)</div>

<div class="api_description">Returns a incoming relay by a given name.<br> <i>(read-only)</i></div>
