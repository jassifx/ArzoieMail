---
title: "ArzoieMail fails to deliver messages to certain servers"
slug: ts_send_messages_to_specific_server
parent: troubleshooting_tips
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


## Overview

This document covers the problem when ArzoieMail fails to deliver email to a specific server, for example Yahoo, Google or Hotmail. If you have tried to send messages to several different servers, but none works, you should [read this instead](?page=ts_send_messages_outside).

### Missing MX-records

If your domain does not have any MX records set up, other email servers might reject email from your server. If you are unsure of whether your domain has any MX records, either go to [intoDNS.com](http://www.intodns.com/), enter your domain and click *DNS Report* or contact the company that registered the domain for you.

### Incorrect records in DNS

Incorrect DNS entries might have the effect that other email servers don't accept email from your domain. To check whether your DNS records are correct, go to [intoDNS.com](http://www.intodns.com/), enter your domain and click *DNS Report*.

### Your IP isn't matching your SPF record

If SPF records are set up in the DNS for your domain, you need to ensure that you deliver email from the IP address specified in your SPF records. If you are sending from other IP addresses, many servers will reject email from you.

### Incorrect host name in HELO

In the ArzoieMail SMTP settings, you need to specify the host name of the computer where you run hMailServer. This should be a host name which has an A-record in the DNS which points at the IP address of your computer. If you enter some other host name, such as *localhost*, *mycomputername*, many servers will reject email from your server.

### Not allowing empty sender address

In the ArzoieMail SMTP settings, you can choose not to allow email with empty sender addresses. By default, this is allowed, and many email servers will reject email from your server if you disallow this.

### Your  IP address is blacklisted.

If spam has been sent from your IP address, your IP address may be in a black list. If the recipients server checks this black list, they may reject your email. On [http://blacklistalert.org](http://blacklistalert.org/)/ you can check whether your IP address is in a common black list.

### Custom filter

The recipients server may use a custom filter which does some type of spam check listed above. The only way to be sure about this is to contact the administrators of the remote server and ask them why your messages are not being accepted. If you can send to other servers but not this specific one, and the above tips doesn't solve the problem, it's recommended that you contact the administrators of the remote server.
