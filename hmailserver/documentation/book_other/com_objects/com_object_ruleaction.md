---
title: "RuleAction object"
slug: com_object_ruleaction
parent: com_objects
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


### Description

The RuleAction object represents a single action in a specific rule.

### Methods

<div class="api_method_name">Delete()</div>

<div class="api_description">Deletes the object from the database.</div>

<div class="api_method_name">MoveDown()</div>

<div class="api_description">Moves the action up in the order.</div>

<div class="api_method_name">MoveUp()</div>

<div class="api_description">Moves the action up in the order.</div>

<div class="api_method_name">Save()</div>

<div class="api_description">Saves changes of the object in the database.</div>

### Properties

<div class="api_method_name">bool AbortSpamFlagged</div>

<div class="api_description">Abort on messages marked as spam.<br></div>

<div class="api_method_name">string Body</div>

<div class="api_description">Body<br></div>

<div class="api_method_name">string Filename</div>

<div class="api_description">Filename<br></div>

<div class="api_method_name">string FromAddress</div>

<div class="api_description">From (address)<br></div>

<div class="api_method_name">string FromName</div>

<div class="api_description">From (name)<br></div>

<div class="api_method_name">string HeaderName</div>

<div class="api_description">Header name<br></div>

<div class="api_method_name">long ID</div>

<div class="api_description">ID<br> <i>(read-only)</i></div>

<div class="api_method_name">string IMAPFolder</div>

<div class="api_description">IMAP folder<br></div>

<div class="api_method_name">long RouteID</div>

<div class="api_description">Route ID to deliver via<br></div>

<div class="api_method_name">long RuleID</div>

<div class="api_description">RuleID<br></div>

<div class="api_method_name">string ScriptFunction</div>

<div class="api_description">Script function to run<br></div>

<div class="api_method_name">string Subject</div>

<div class="api_description">Subject<br></div>

<div class="api_method_name">string To</div>

<div class="api_description">To<br></div>

<div class="api_method_name">eRuleActionType Type</div>

<div class="api_description">Type<br></div>

<div class="api_method_name">string Value</div>

<div class="api_description">Value<br></div>
