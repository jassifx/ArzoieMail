// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

using System;
using System.IO;
using hMailServer;
using NUnit.Framework;
using RegressionTests.Infrastructure;
using RegressionTests.Shared;

namespace RegressionTests.API
{
   [TestFixture]
   public class UtilitiesTests : TestFixtureBase
   {
      [Test]
      [Description("Import the same message twice.")]
      public void TestImportDuplicateMessage()
      {
         var messageText =
            "From: test@example.test\r\n" +
            "\r\n" +
            "Test\r\n";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         var domainPath = Path.Combine(_application.Settings.Directories.DataDirectory, "example.test");
         var accountPath = Path.Combine(domainPath, "test");

         Directory.CreateDirectory(accountPath);

         var fileName = Path.Combine(accountPath, "something.eml");

         File.WriteAllText(fileName, messageText);

         Assert.That(_application.Utilities.ImportMessageFromFile(fileName, account.ID), Is.True);
         Assert.That(_application.Utilities.ImportMessageFromFile(fileName, account.ID), Is.False);

         Pop3ClientSimulator.AssertMessageCount("test@example.test", "test", 1);
      }

      [Test]
      [Description("Import a mail located properly in a sub directory.")]
      public void TestImportOfMessageInInvalidSubName()
      {
         var messageText =
            "From: test@example.test\r\n" +
            "\r\n" +
            "Test\r\n";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         var domainPath = Path.Combine(_application.Settings.Directories.DataDirectory, "example.test");
         var accountPath = Path.Combine(domainPath, "test");
         Directory.CreateDirectory(accountPath);

         var guid = Guid.NewGuid().ToString();
         var guidPath = Path.Combine(accountPath, guid.Substring(1, 2));
         Directory.CreateDirectory(guidPath);

         var fileName = Path.Combine(guidPath, "§§§§.eml");

         File.WriteAllText(fileName, messageText);

         Assert.That(_application.Utilities.ImportMessageFromFile(fileName, account.ID), Is.True);

         var message = _domain.Accounts[0].IMAPFolders.get_ItemByName("Inbox").Messages[0];
         Assert.That(fileName.Contains("$$$$.eml"), Is.False);
      }

      [Test]
      [Description(
         "Import all messages in public folders. This must fail, since we don't know what public folder to put it into."
      )]
      public void TestImportOfMessageInPublicFolder()
      {
         var messageText =
            "From: test@example.test\r\n" +
            "\r\n" +
            "Test\r\n";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");
         var publicFolder = Path.Combine(_application.Settings.Directories.DataDirectory, "#Public");

         if (!Directory.Exists(publicFolder))
            Directory.CreateDirectory(publicFolder);

         var fileName = Path.Combine(publicFolder, "§§§§.eml");

         File.WriteAllText(fileName, messageText);

         Assert.That(_application.Utilities.ImportMessageFromFile(fileName, account.ID), Is.False);
      }

      [Test]
      [Description(
         "Import a message stored in a sub directory of the public folder. This must fail as well, since " +
         "the public IMAP folder isn't part of the path on disk."
      )]
      public void TestImportOfMessageInPublicFolderSubdirectory()
      {
         var messageText =
            "From: test@example.test\r\n" +
            "\r\n" +
            "Test\r\n";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         var guid = Guid.NewGuid().ToString();
         var publicFolder = Path.Combine(_application.Settings.Directories.DataDirectory,
            _application.Settings.PublicFolderDiskName);
         var guidPath = Path.Combine(publicFolder, guid.Substring(1, 2));

         Directory.CreateDirectory(guidPath);

         var fileName = Path.Combine(guidPath, guid + ".eml");

         File.WriteAllText(fileName, messageText);

         Assert.That(_application.Utilities.ImportMessageFromFile(fileName, account.ID), Is.False);
         Assert.That(_application.Utilities.ImportMessageFromFile(fileName, 0), Is.False);

         // The file should have been left alone.
         Assert.That(File.Exists(fileName), Is.True);

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 0);

         Directory.Delete(guidPath, true);
      }

      [Test]
      [Description(
         "Import a message stored in the public folder into a public IMAP folder given by the caller."
      )]
      public void TestImportOfMessageInPublicFolderIntoGivenFolder()
      {
         var messageText =
            "From: test@example.test\r\n" +
            "Subject: Public folder message\r\n" +
            "\r\n" +
            "Test\r\n";

         var publicFolder = Path.Combine(_application.Settings.Directories.DataDirectory,
            _application.Settings.PublicFolderDiskName);

         Directory.CreateDirectory(publicFolder);

         var fileName = Path.Combine(publicFolder, "orphan.eml");

         File.WriteAllText(fileName, messageText);

         Assert.That(_application.Utilities.ImportMessageFromFileToPublicIMAPFolder(fileName, "Share1"), Is.True);

         _application.Reinitialize();

         var folder = _settings.PublicFolders.get_ItemByName("Share1");
         Assert.That(folder.Messages.Count, Is.EqualTo(1));

         // The file wasn't stored in a folder following the hMailServer naming standard, so it
         // should have been moved into one below the public folder.
         var importedFileName = folder.Messages[0].Filename;
         Assert.That(importedFileName, Is.Not.EqualTo(fileName));
         Assert.That(File.Exists(fileName), Is.False);
         Assert.That(importedFileName.StartsWith(publicFolder), Is.True);
         Assert.That(File.ReadAllText(importedFileName).Contains(messageText), Is.True);
      }

      [Test]
      [Description(
         "Import a message stored in a sub directory of the public folder into a public IMAP folder " +
         "given by the caller. The message should be left where it is, since it's properly stored."
      )]
      public void TestImportOfMessageInPublicFolderSubdirectoryIntoGivenFolder()
      {
         var messageText =
            "From: test@example.test\r\n" +
            "Subject: Public folder message\r\n" +
            "\r\n" +
            "Test\r\n";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "reader@example.test", "test");

         var guid = Guid.NewGuid().ToString();
         var publicFolder = Path.Combine(_application.Settings.Directories.DataDirectory,
            _application.Settings.PublicFolderDiskName);
         var guidPath = Path.Combine(publicFolder, guid.Substring(1, 2));

         Directory.CreateDirectory(guidPath);

         var fileName = Path.Combine(guidPath, guid + ".eml");

         File.WriteAllText(fileName, messageText);

         Assert.That(_application.Utilities.ImportMessageFromFileToPublicIMAPFolder(fileName, "Share1"), Is.True);

         var folder = _settings.PublicFolders.get_ItemByName("Share1");
         Assert.That(folder.Messages.Count, Is.EqualTo(1));
         Assert.That(folder.Messages[0].Filename, Is.EqualTo(fileName));
         Assert.That(File.Exists(fileName), Is.True);

         // Make sure the message is readable by an IMAP client.
         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.set_Permission(eACLPermission.ePermissionRead, true);
         permission.Save();

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.ConnectAndLogon(account.Address, "test");
         Assert.That(imapClientSimulator.GetMessageCount("#Public.Share1"), Is.EqualTo(1));
         imapClientSimulator.Disconnect();
      }

      [Test]
      [Description("Import a message into a public IMAP folder which doesn't exist yet.")]
      public void TestImportOfMessageIntoPublicFolderHierarchy()
      {
         var fileName = CreateMessageInPublicFolder();

         Assert.That(_application.Utilities.ImportMessageFromFileToPublicIMAPFolder(fileName, "Share1.Sub1"), Is.True);

         _application.Reinitialize();

         var folder = _settings.PublicFolders.get_ItemByName("Share1");
         Assert.That(folder.Messages.Count, Is.EqualTo(0));

         var subFolder = folder.SubFolders.get_ItemByName("Sub1");
         Assert.That(subFolder.Messages.Count, Is.EqualTo(1));
      }

      [Test]
      [Description(
         "Import a message into a public IMAP folder given with the name of the public folder included."
      )]
      public void TestImportOfMessageIntoFullyQualifiedPublicFolder()
      {
         var fileName = CreateMessageInPublicFolder();

         Assert.That(_application.Utilities.ImportMessageFromFileToPublicIMAPFolder(fileName, "#Public.Share1"), Is.True);

         _application.Reinitialize();

         Assert.That(_settings.PublicFolders.Count, Is.EqualTo(1));
         Assert.That(_settings.PublicFolders.get_ItemByName("Share1").Messages.Count, Is.EqualTo(1));
      }

      [Test]
      [Description(
         "A message in the public folder can only be imported if the caller specifies which public " +
         "IMAP folder it should be placed in."
      )]
      public void TestImportOfMessageIntoPublicFolderWithoutFolderName()
      {
         var fileName = CreateMessageInPublicFolder();

         Assert.That(_application.Utilities.ImportMessageFromFileToPublicIMAPFolder(fileName, ""), Is.False);

         // The file should have been left alone.
         Assert.That(File.Exists(fileName), Is.True);
         Assert.That(_settings.PublicFolders.Count, Is.EqualTo(0));

         File.Delete(fileName);
      }

      [Test]
      [Description("A public IMAP folder name must not contain empty levels.")]
      public void TestImportOfMessageIntoPublicFolderWithEmptyFolderLevel()
      {
         var fileName = CreateMessageInPublicFolder();

         Assert.That(_application.Utilities.ImportMessageFromFileToPublicIMAPFolder(fileName, "Share1..Sub1"), Is.False);

         // The file should have been left where it is, and no folder should have been created.
         Assert.That(File.Exists(fileName), Is.True);
         Assert.That(_settings.PublicFolders.Count, Is.EqualTo(0));

         File.Delete(fileName);
      }

      [Test]
      [Description(
         "Only messages stored in the public folder on disk can be imported into a public IMAP folder."
      )]
      public void TestImportOfMessageOutsidePublicFolderIntoPublicFolder()
      {
         var messageText =
            "From: test@example.test\r\n" +
            "\r\n" +
            "Test\r\n";

         SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         var domainPath = Path.Combine(_application.Settings.Directories.DataDirectory, "example.test");
         var accountPath = Path.Combine(domainPath, "test");

         Directory.CreateDirectory(accountPath);

         var accountFileName = Path.Combine(accountPath, "something.eml");
         File.WriteAllText(accountFileName, messageText);

         var queueFileName = Path.Combine(_application.Settings.Directories.DataDirectory, "something.eml");
         File.WriteAllText(queueFileName, messageText);

         Assert.That(_application.Utilities.ImportMessageFromFileToPublicIMAPFolder(accountFileName, "Share1"), Is.False);
         Assert.That(_application.Utilities.ImportMessageFromFileToPublicIMAPFolder(queueFileName, "Share1"), Is.False);

         Assert.That(File.Exists(accountFileName), Is.True);
         Assert.That(File.Exists(queueFileName), Is.True);

         Pop3ClientSimulator.AssertMessageCount("test@example.test", "test", 0);

         File.Delete(queueFileName);
      }

      [Test]
      [Description(
         "Importing a message which is already stored in a public folder should be a no-op, even if " +
         "another public IMAP folder is given."
      )]
      public void TestImportOfExistingMessageIntoOtherPublicFolder()
      {
         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var message = folder.Messages.Add();
         message.Subject = "Test";
         message.Save();

         var fileName = message.Filename;

         Assert.That(_application.Utilities.ImportMessageFromFileToPublicIMAPFolder(fileName, "Share2"), Is.True);

         _application.Reinitialize();

         Assert.That(_settings.PublicFolders.Count, Is.EqualTo(1));
         Assert.That(_settings.PublicFolders.get_ItemByName("Share1").Messages.Count, Is.EqualTo(1));
         Assert.That(_settings.PublicFolders.get_ItemByName("Share1").Messages[0].Filename, Is.EqualTo(fileName));
         Assert.That(File.Exists(fileName), Is.True);
      }

      private string CreateMessageInPublicFolder()
      {
         var messageText =
            "From: test@example.test\r\n" +
            "Subject: Public folder message\r\n" +
            "\r\n" +
            "Test\r\n";

         var publicFolder = Path.Combine(_application.Settings.Directories.DataDirectory,
            _application.Settings.PublicFolderDiskName);

         Directory.CreateDirectory(publicFolder);

         var fileName = Path.Combine(publicFolder, Guid.NewGuid() + ".eml");

         File.WriteAllText(fileName, messageText);

         return fileName;
      }

      [Test]
      [Description("Import a mail located properly in a sub directory.")]
      public void TestImportOfMessageInSubdirectory()
      {
         var messageText =
            "From: test@example.test\r\n" +
            "\r\n" +
            "Test\r\n";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         var domainPath = Path.Combine(_application.Settings.Directories.DataDirectory, "example.test");
         var accountPath = Path.Combine(domainPath, "test");
         Directory.CreateDirectory(accountPath);

         var guid = Guid.NewGuid().ToString();
         var guidPath = Path.Combine(accountPath, guid.Substring(1, 2));
         Directory.CreateDirectory(guidPath);

         var fileName = Path.Combine(guidPath, guid + ".eml");

         File.WriteAllText(fileName, messageText);

         Assert.That(_application.Utilities.ImportMessageFromFile(fileName, account.ID), Is.True);

         var message = _domain.Accounts[0].IMAPFolders.get_ItemByName("Inbox").Messages[0];
         Assert.That(message.Filename, Is.EqualTo(fileName));
      }

      [Test]
      [Description("Import a message using the mail importer")]
      public void TestImportOfMessageIntoInbox()
      {
         var messageText =
            "From: test@example.test\r\n" +
            "\r\n" +
            "Test\r\n";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         var domainPath = Path.Combine(_application.Settings.Directories.DataDirectory, "example.test");
         var accountPath = Path.Combine(domainPath, "test");

         Directory.CreateDirectory(accountPath);

         var fileName = Path.Combine(accountPath, "something.eml");

         File.WriteAllText(fileName, messageText);

         Assert.That(_application.Utilities.ImportMessageFromFile(fileName, account.ID), Is.True);

         var text = Pop3ClientSimulator.AssertGetFirstMessageText("test@example.test", "test");
         Assert.That(text.Contains(messageText), Is.True);
      }

      [Test]
      [Description("Import a message using the mail importer")]
      public void TestImportOfMessageIntoInbox2()
      {
         var messageText =
            "From: test@example.test\r\n" +
            "\r\n" +
            "Test\r\n";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         var domainPath = Path.Combine(_application.Settings.Directories.DataDirectory, "example.test");
         var accountPath = Path.Combine(domainPath, "test");

         Directory.CreateDirectory(accountPath);

         var fileName = Path.Combine(accountPath, "something.eml");

         File.WriteAllText(fileName, messageText);

         Assert.That(_application.Utilities.ImportMessageFromFileToIMAPFolder(fileName, account.ID, "Inbox"), Is.True);

         var text = Pop3ClientSimulator.AssertGetFirstMessageText("test@example.test", "test");
         Assert.That(text.Contains(messageText), Is.True);
      }

      [Test]
      [Description("Import a message using the mail importer")]
      public void TestImportOfMessageIntoOtherFolder()
      {
         var messageText =
            "From: test@example.test\r\n" +
            "\r\n" +
            "Test\r\n";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         account.IMAPFolders.Add("Woho");

         var domainPath = Path.Combine(_application.Settings.Directories.DataDirectory, "example.test");
         var accountPath = Path.Combine(domainPath, "test");

         Directory.CreateDirectory(accountPath);
         var fileName = Path.Combine(accountPath, "something.eml");

         File.WriteAllText(fileName, messageText);

         Assert.That(_application.Utilities.ImportMessageFromFileToIMAPFolder(fileName, account.ID, "Woho"), Is.True);

         Pop3ClientSimulator.AssertMessageCount("test@example.test", "test", 0);
         var sim = new ImapClientSimulator();
         sim.ConnectAndLogon("test@example.test", "test");
         Assert.That(sim.GetMessageCount("Woho"), Is.EqualTo(1));
         sim.Disconnect();
      }

      [Test]
      [Description(
         "A message stored in a public folder must be found by its file name, so that the data directory " +
         "synchronizer doesn't consider it an orphan and deletes it."
      )]
      public void TestRetrieveMessageIdForMessageInPublicFolder()
      {
         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var message = folder.Messages.Add();
         message.Subject = "Test";
         message.Save();

         var fileName = message.Filename;

         Assert.That(fileName.Contains(_settings.PublicFolderDiskName), Is.True);
         Assert.That(_application.Utilities.RetrieveMessageID(fileName), Is.EqualTo(message.ID));
      }

      [Test]
      [Description(
         "Importing a message which is already stored in a public folder should be a no-op. It should " +
         "neither be duplicated nor moved to another file."
      )]
      public void TestImportOfExistingMessageInPublicFolder()
      {
         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var message = folder.Messages.Add();
         message.Subject = "Test";
         message.Save();

         var fileName = message.Filename;

         Assert.That(_application.Utilities.ImportMessageFromFile(fileName, 0), Is.True);

         _application.Reinitialize();

         Assert.That(_settings.PublicFolders[0].Messages.Count, Is.EqualTo(1));
         Assert.That(_settings.PublicFolders[0].Messages[0].Filename, Is.EqualTo(fileName));
         Assert.That(File.Exists(fileName), Is.True);
      }

      [Test]
      [Description("Let the importer replace the full path in the database with a partial path")]
      public void TestReplaceFullPathInPublicFolderWithPartialPath()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();
         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account8@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var message = folder.Messages.Add();
         message.Subject = "Test";
         message.Save();

         // Move the message file to another folder.
         var publicFolderPath = Path.Combine(_application.Settings.Directories.DataDirectory, "#Public");
         var fileName = Path.Combine(publicFolderPath, "randomMail.eml");
         File.Move(message.Filename, fileName);

         // Update the database with the 'invalid' path.
         var sql = string.Format("update hm_messages set messagefilename = '{0}' where messageid = {1}",
            TestSetup.Escape(fileName), message.ID);
         SingletonProvider<TestSetup>.Instance.GetApp().Database.ExecuteSQL(sql);

         // Now try to insert the message.
         Assert.That(_application.Utilities.ImportMessageFromFile(fileName, 0), Is.True);

         _application.Reinitialize();

         var newMessgaeFilename = _settings.PublicFolders[0].Messages[0].Filename;
         Assert.That(newMessgaeFilename, Is.Not.EqualTo(fileName));
         Assert.That(File.Exists(newMessgaeFilename), Is.True);
      }

      [Test]
      [Description("Let the importer replace the full path in the database with a partial path")]
      public void TestReplaceFullPathWithPartialPath()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");
         SmtpClientSimulator.StaticSend(account.Address, account.Address, "Test message", "Test body");

         var folder = account.IMAPFolders.get_ItemByName("Inbox");
         CustomAsserts.AssertFolderMessageCount(folder, 1);

         var message = account.IMAPFolders.get_ItemByName("Inbox").Messages[0];

         // Now nothing should happen.
         Assert.That(_application.Utilities.ImportMessageFromFile(message.Filename, account.ID), Is.True);

         // Move the message file to another folder.
         var domainPath = Path.Combine(_application.Settings.Directories.DataDirectory, _domain.Name);
         var accountPath = Path.Combine(domainPath, "test");
         var fileName = Path.Combine(accountPath, "randomMail.eml");
         File.Move(message.Filename, fileName);

         // Update the database with the 'invalid' path.
         var sql = string.Format("update hm_messages set messagefilename = '{0}' where messageid = {1}",
            TestSetup.Escape(fileName), message.ID);
         SingletonProvider<TestSetup>.Instance.GetApp().Database.ExecuteSQL(sql);

         Assert.That(File.Exists(fileName), Is.True);
         // Now the file should be moved to the correct path.
         Assert.That(_application.Utilities.ImportMessageFromFile(fileName, account.ID), Is.True);

         Assert.That(File.Exists(fileName), Is.False);

         // Now nothing should happen because the file is no longer there.
         Assert.That(_application.Utilities.ImportMessageFromFile(fileName, account.ID), Is.False);

         var content = Pop3ClientSimulator.AssertGetFirstMessageText(account.Address, "test");

         Assert.That(content.Contains("Test message"), Is.True);
      }

      [Test]
      [Description("Let the importer replace the full path in the database with a partial path")]
      public void TestReplaceInvalidPathWithCorrectPath()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");
         SmtpClientSimulator.StaticSend(account.Address, account.Address, "Test message", "Test body");

         var folder = account.IMAPFolders.get_ItemByName("Inbox");
         CustomAsserts.AssertFolderMessageCount(folder, 1);

         var message = account.IMAPFolders.get_ItemByName("Inbox").Messages[0];

         var filename = message.Filename;
         // Now nothing should happen here.
         Assert.That(_application.Utilities.ImportMessageFromFile(filename, account.ID), Is.True);
         Assert.That(File.Exists(filename), Is.True);

         var sql = string.Format("update hm_messages set messagefilename = '{0}' where messageid = {1}",
            TestSetup.Escape(message.Filename), message.ID);

         SingletonProvider<TestSetup>.Instance.GetApp().Database.ExecuteSQL(sql);

         // Now the path should be replaced.
         Assert.That(_application.Utilities.ImportMessageFromFile(message.Filename, account.ID), Is.True);

         // Now nothing should happen.
         Assert.That(_application.Utilities.ImportMessageFromFile(message.Filename, account.ID), Is.True);
         Assert.That(File.Exists(message.Filename), Is.True);

         var content = Pop3ClientSimulator.AssertGetFirstMessageText(account.Address, "test");

         Assert.That(content.Contains("Test message"), Is.True);
      }
   }
}