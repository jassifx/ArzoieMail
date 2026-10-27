---
title: "Change the data directory"
slug: howto_change_data_directory
parent: howtos
index: 20
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


## Background

By default, all emails are stored in the ArzoieMail data directory, under C:\Program Files\hMailServer\Data. In some cases, you might want to change this to a different path. For example, if you have more disk space on another drive. 

## Steps

1. Stop the ArzoieMail service
2. Open up hMailServer.ini
3. Locate *DataFolder* under the *Directories*-section
4. Specify the new path
5. Move all the message files from the old to the new path

 

NOTE:- This will only work if the diagnostic tests show that 'Relative message paths are stored in the database for all messages' - (second last test). If this is not the case then head to the forum and ask how this can achieved.
