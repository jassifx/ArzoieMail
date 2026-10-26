---
title: "Open relay tests"
slug: open_relay_tests
parent: book_other
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


As soon as you finish installing and configuring ArzoieMail, you should run some open relay tests. It ensures that your server will not be used for spamming.[](http://members.iinet.net.au/~remmie/relay/)

- [http://www.aupads.org/test-relay.html](http://www.aupads.org/test-relay.html)
- [http://www.dnsgoodies.com/](http://www.dnsgoodies.com/)

## False positives

In some cases, open relay tests may return false positives. This means that the open relay test claims that your server may be an open relay even though it's not. In other words, the server may give you a warning even though you have nothing to worry about.

### Default domain and catch-all address

If you specify a default domain in ArzoieMail, this domain name will be appended to all email addresses which are missing a domain name (the setting is located in ArzoieMail Administrator under Settings->Advanced). If you specify the default domain example.com, and someone tries to send an email to webmaster, ArzoieMail will assume the message should be sent to webmaster@example.com. <br>

  

If you specify a catch-all address for a domain, ArzoieMail will accept all email sent to this domain. If the recipient address does not match an account, the message will be forwarded to the catch-all address.

Combining the default domain setting with the catch-all address setting may lead to false positives. If you have enabled both settings, and someone connects to your server and wants to deliver an email to <any-random-string>, ArzoieMail will accept the delivery. ArzoieMail will first append the default domain to the address - <any-random-string>@example.com. After that, it will try to find the account <any-random-string>@example.com. If this fails, the message will still be delivered to the catch-all account.
