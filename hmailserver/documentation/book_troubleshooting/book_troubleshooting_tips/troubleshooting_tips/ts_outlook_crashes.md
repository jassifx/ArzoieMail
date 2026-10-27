---
title: "Outlook crashes"
slug: ts_outlook_crashes
parent: troubleshooting_tips
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


The Outlook 2003 IMAP implementation behaves a bit differently from the IMAP implementation in other email clients. There are problems encountered with Outlook, that are not specific to hMailServer. They will occur regardless of which IMAP server you are using. 

Outlook 2003 sometimes crashes if it can't connect to the IMAP server. This is an Outlook bug and has nothing to do with hMailServer. 

Outlook 2003 may sometimes show an error message that says it can't open a folder named Null. For some reason, international versions of Outlook use international names for the INBOX folder, even though the IMAP specification states that the name should always be INBOX. To override Outlook's behaviour, go to Tools->E-mail accounts->View, or change the account. In the properties for the IMAP account, click More Settings, select Advanced and then enter INBOX as Root folder path. This text box is not available in the English version of Outlook.
