---
title: "Maintenance scripts"
slug: maintenance_scripts
parent: book_maintenance
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


## Maintenance scripts

Before running maintenance scripts, it is very important that you back up the database. The scripts may make large moditfications to the database.

<p>To run a script, copy the content, place it in a file with the extension <span class="Code">.vbs</span>. After this, double click on the file in the Windows Explorer to run the script.</p>

The scripts will automatically stop ArzoieMail before the maintenance operation and start it again afterwards.

### Reset folder UIDs

This script goes through all IMAP folders and updates the current UID's assigned with them. Only run this script if you have problems with new messages not showing up until a service restart.

<p>Before you can use this script, you need to replace <span class="Code">&lt;password&gt; </span>with your Administrator password. After the script has completed, a message box with the text Done will be shown.</p>

<p><span class="Code">   Dim obApp<br>
   Set obApp = CreateObject("hMailServer.Application")<br>
    <br>
   Call obApp.Authenticate("Administrator", "&lt;password&gt;")<br>
   Call obApp.Stop()<br>
   Call obApp.Utilities.PerformMaintenance(1)<br>
   Call obApp.Start()<br>
<br>
   MsgBox "Done"</span><br>
<br>
 </p>
