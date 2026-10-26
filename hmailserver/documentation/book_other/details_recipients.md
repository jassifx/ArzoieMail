---
title: "Resolving recipients"
slug: details_recipients
parent: book_other
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


## Overview

This document describes how recipients are resolved in hMailServer. When a SMTP client delivers a message to ArzoieMail, it specifies a sender and a recipient. The recipient address specified by the client may not be the final recipient address, if aliases, domain aliases and routing is used. If these features are used, ArzoieMail may modify the recipient address. This document describes the process of this.

### Recipient rewriting

When a client specifies the recipient address (using the SMTP RCPT TO command), the following happens:

1. ArzoieMail checks if the recipient domain name matches a local domain name alias. If this is the case, the recipient address is rewritten to use the primary domain name. For instance, if the domain example.com has the alias sub.example.com, and a message is sent to someone@sub.example.com, the recipient address is rewritten to someone@example.com. <br>

  
2. If a domain exists matching the recipient domain:  

   

   1. If the domain is inactive, ArzoieMail returns an error to the sender.
  2. ArzoieMail attempts to locate an account matching the recipient address. <br>

     - If an account is found, ArzoieMail will deliver the message to the account.
    - If the account is inactive, ArzoieMail returns an error tot he sender.
  3. ArzoieMail attempts to locate an alias matching the recipient address. <br>

     - If an alias is found, ArzoieMail restarts the entire rewriting process, but this time with the address specified in the alias value.
    - If an alias if found, but it's inacitve, an error message is given to the sender.
  4. ArzoieMail attempts to locate a distribution list matching the recipient address
     - If a distribution list is found, ArzoieMail will add the recipients of the distribution list to the message, using the process described in this document. <br>

  
3. If no domain exists matching the recipient domain:  

   

   1. ArzoieMail tries to locate a route matching the recipient address. Note that when this is done, the original address is used. For instance, if the domain example.com has the alias sub.example.com, and a message is sent to someone@sub.example.com, ArzoieMail will try to locate a route for the domain someone@sub.example.com.

### Examples

**Simple delivery**

The domain example.com and account user@example.com. A client sends a message to user@example.com.

1. ArzoieMail will locate the domain example.com and confirm it's enabled.
2. ArzoieMail will locate the account user@example.com and confirm it's enabled.
3. ArzoieMail will deliver the message to user@example.com.

**Delivery using aliases**

The domain example.com and account user@example.com. An alias is set up pointing from someone@example.com at user@example.com. A client sends a message to someone@example.com.

1. ArzoieMail will locate the domain example.com and confirm it's enabled.
2. ArzoieMail will attempt to locate the account someone@example.com but will fail.
3. ArzoieMail will locate the alias someone@example.com and see that it's pointing at user@example.com.
4. ArzoieMail will locate the domain example.com and confirm it's enabled.
5. ArzoieMail will locate the account user@example.com and confirm it's enabled.
6. ArzoieMail will deliver the message to user@example.com.

**Delivery using routes**

No domain is set up, but a route with the name example.com is set up. A message is sent to the domain someone@example.com.

1. ArzoieMail will attempt to locate the domain example.com but fail.
2. ArzoieMail will locate the route example.com and confirm its enabled.
3. ArzoieMail will deliver to the address in the route.
