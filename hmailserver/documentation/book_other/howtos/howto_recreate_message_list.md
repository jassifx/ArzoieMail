---
title: "Recreating messages lists in the database"
slug: howto_recreate_message_list
parent: howtos
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


## Background

ArzoieMail e-mail messages are stored in the ArzoieMail data directory (normally *C:\Program Files\hMailServer\Data*). The database contains lists of these messages. For example, the path to the message file, the size of the message, message flags and recipients of the message is stored in the database. <br>

  

When you download messages from ArzoieMail using POP3 or IMAP, ArzoieMail loads a list of messages from the database to determine whether email exists for your account. So if the email file exists on the disk in the data directory but not in the database, you will not be able to download the message. <br>

  

Normally, you should **not** have to recreate message lists. Do not try to recreate message lists unless you are certain that you need to do so. If you are a unsure, feel free to ask in the user forum.

## Recreating message lists in database

Using the tool DataDirectory Synchronizer, you can recreate the message list in the database.  

  

Follow these steps to recreate message lists in the database:

1. Backup your ArzoieMail system (database and data directory). Since DataDirectorySynchronizer makes large changes to your ArzoieMail database, this is very important.
2. Start *DataDirectorySynchronizer.exe* found in the Addons folder
3. Click *Next* to start synchronization

The speed of the synchronization varies depending on computer hardware. On a Intel P4 2.4Ghz, you can estimate about 50 messages per second will be imported.

## Important notes

- You don't have to stop ArzoieMail before running DDS.
- After all messages have been imported, DDS will automatically re-start the ArzoieMail server to refresh the cache.
- Since IMAP message data, such as folder information, message flags etc are stored in the database and not in the message files, that information will not be recreated when recreate the message list. Hence, when messages are inserted into the database, all messages will end up in the inbox folder and marked as unread.
- Files located in the root data folder will be delivered to local recipients in the To and CC headers. The original recipient information, from the SMTP envelope, is not available in the .EML files. Hence, the To/CC headers are used. E-mail are only delivered to local recipients to prevent mail from being re-sent to external addresses.
- Files located in data folder sub directories, such as domain\username will be connected to that user in the database.
- Only files with the extension .eml or .hma are imported into the database
- Files imported directly to user accounts won't be treated by filters, rules, virus scanning features in ArzoieMail
