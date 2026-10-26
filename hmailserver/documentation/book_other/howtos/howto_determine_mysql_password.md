---
title: "HOWTO: Determine ArzoieMail database password"
slug: howto_determine_mysql_password
parent: howtos
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


If you are using the built-in version that comes with ArzoieMail, you might not know your password. To determine the password, follow these steps:

1. Run the script hMailServer/Addons/Utilities/DecryptBlowfish.vbs.
2. Enter your main ArzoieMail Administrator password. This is the password you specified during installation. Click OK.
3. Open up hMailserver.ini, located in your Windows directory or ArzoieMail bin directory.
4. Copy the database password from hMailServer.ini to the "Enter encrypted password" dialog.
5. Click OK in DecryptBlowfish to view your password
