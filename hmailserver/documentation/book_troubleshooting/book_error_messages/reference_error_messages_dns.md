---
title: "DNS error messages"
slug: reference_error_messages_dns
parent: book_error_messages
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


## DNS errors

### General information

ArzoieMail uses the Windows API to query the DNS server. ArzoieMail itself does not not ask Windows to use a specific DNS server. The DNS query is made using the Windows API function DnsQuery. Depending on what DNS error that has occurred, DnsQuery return different values. If an error occurs, this error is listed in the ArzoieMail log.

### 1460, ERROR_TIMEOUT

DNS error 1460 means that there was a timeout when the DnsQuery was made. This can occur if the DNS server is rebooting or isn't available due to network problems.

### 9002, DNS_ERROR_RCODE_SERVER_FAILURE

Error number 9002 means that there was a DNS server failure.
