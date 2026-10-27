---
title: "DeliveryQueue object"
slug: com_object_deliveryqueue
parent: com_objects
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


### Description

The DeliveryQueue object contains method to manipulate the ArzoieMail queue.

### Methods

<div class="api_method_name">Clear()</div>

<div class="api_description">Clears the delivery queue.</div>

<div class="api_method_name">Remove(long iMessageID)</div>

<div class="api_description">Removes the message from the delivery queue.</div>

<div class="api_method_name">ResetDeliveryTime(long iMessageID)</div>

<div class="api_description">Reset next delivery time</div>

<div class="api_method_name">StartDelivery()</div>

<div class="api_description">Forces the SMTP deliverer to look for messages to deliver.</div>
