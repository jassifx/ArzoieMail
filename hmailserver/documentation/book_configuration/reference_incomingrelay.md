---
title: "Incoming relay"
slug: reference_incomingrelay
parent: book_configuration
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


## Overview

Unless the Incoming Relays section is populated with a matching address, ArzoieMail assumes that itself is the MX for your domain.

When ArzoieMail performs anti-spam tests on a message it will use the connecting IP address to determine where the message is arriving from.

When ArzoieMail receives an email from a MX backup server, ArzoieMail can't use the senders TCP/IP address since this is the IP address of the backup server. If you add the MX backup servers IP address as an incoming relay, ArzoieMail will know that messages from this TCP/IP address is being forwarded and that the backup sender is not the original sender. ArzoieMail will then try to determine the original senders IP address by parsing the *Received* headers of the email message.

When ArzoieMail is not the MX, it must be made aware of those IP addresses that belong to your MX and the MX’s internal hand-off IPs. Set the Incoming Relays section to all the IPs that can appear in a mail header that belong to your MX and its internal hand-offs.

Most anti spam methods in ArzoieMail are performed before the actual message body has been delivered to hMailServer. ArzoieMail uses the HELO, MAIL FROM and RCPT TO commands to determine whether the message is likely to be spam. However, if a message arrives from a incoming relay, ArzoieMail has to wait until the entire message has been received before it can perform anti spam. In other words, using incoming relays will make the anti spam mechanisms more expensive, in terms of bandwidth and time.

Please note that incoming relays does not affect grey listing. Grey listing always takes place before the *Received* headers have been transmitted to hMailServer. You may want to add any incoming relays IP address to the grey listing white list, to prevent incoming relays from being grey listed.
