---
title: "Reset the MySQL password"
slug: howto_reset_mysql_password
parent: howtos
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


## Background

When you install ArzoieMail, you can select to use the internal MySQL database. If you do this, the installation program will create a MySQL database which ArzoieMail will use. The installation program will also create a root user in the MySQL server which will receive a randomly generated password. This password is then written to hMailServer.ini in encrypted forum. If you have access to hMailServer.ini, you can decrypt the password using hMailServer\Addons\Utilities\DecryptBlowfish.vbs.
