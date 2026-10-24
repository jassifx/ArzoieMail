// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

using System;
using hMailServer;
using NUnit.Framework;
using RegressionTests.Shared;

namespace RegressionTests.IMAP
{
   [TestFixture]
   public class Folders : TestFixtureBase
   {
      [Test]
      public void TestCreateDeepFolder()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder4@example.test", "test");

         var simulator = new ImapClientSimulator();

         simulator.Connect();
         simulator.LogonWithLiteral(account.Address, "test");
         var result =
            simulator.Send("A01 CREATE 1.2.3.4.5.6.7.8.9.10.11.12.13.14.15.16.17.18.19.20.21.22.23.24.25.26");
         Assert.That(result.Contains("A01 NO"), Is.True);

         simulator.Disconnect();
      }


      [Test]
      public void TestCreateFolderWithHash()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder6@example.test", "test");

         var simulator = new ImapClientSimulator();

         simulator.Connect();
         simulator.Logon(account.Address, "test");
         Assert.That(simulator.CreateFolder("#Test"), Is.False);
         Assert.That(simulator.CreateFolder("Test.#Testar"), Is.True);
         simulator.Disconnect();
      }


      [Test]
      public void TestCreateFolderWithQuote()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "quote@example.test", "test");


         const string folderName = "ABC\"123";

         var simulator = new ImapClientSimulator();
         simulator.Connect();
         simulator.Logon(account.Address, "test");
         Assert.That(simulator.CreateFolder(folderName), Is.True);
         Assert.That(simulator.List().Contains(folderName), Is.True);
         Assert.That(simulator.SelectFolder(folderName), Is.True);
         simulator.Disconnect();
      }

      [Test]
      public void TestCreateFolderWithSlash()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "quote@example.test", "test");

         const string folderName = "ABC\\123";

         var simulator = new ImapClientSimulator();
         simulator.Connect();
         simulator.Logon(account.Address, "test");
         Assert.That(simulator.CreateFolder(folderName), Is.True);
         Assert.That(simulator.List().Contains(folderName), Is.True);
         Assert.That(simulator.SelectFolder(folderName), Is.True);
         simulator.Disconnect();
      }

      [Test]
      public void TestCreateLongFolder()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder9@example.test", "test");

         var simulator = new ImapClientSimulator();

         simulator.Connect();
         simulator.LogonWithLiteral(account.Address, "test");

         var folderName = "";
         for (var i = 0; i < 255; i++)
            folderName = folderName + "A";

         var result = simulator.Send("A01 CREATE " + folderName);
         Assert.That(result.Contains("A01 OK"), Is.True);
         Assert.That(simulator.SelectFolder(folderName), Is.True);

         folderName = "";
         for (var i = 0; i < 256; i++)
            folderName = folderName + "A";

         result = simulator.Send("A01 CREATE " + folderName);
         Assert.That(result.Contains("A01 NO"), Is.True);
         Assert.That(simulator.SelectFolder(folderName), Is.False);

         simulator.Disconnect();
      }

      [Test]
      public void TestCreateParallelFolder()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder5@example.test", "test");

         var simulator = new ImapClientSimulator();

         simulator.Connect();
         simulator.LogonWithLiteral(account.Address, "test");
         var result = simulator.Send("A01 CREATE 1.2.3");
         Assert.That(result.Contains("A01 OK"), Is.True);
         result = simulator.Send("A01 CREATE 1.2.4");
         Assert.That(result.Contains("A01 OK"), Is.True);

         simulator.Disconnect();
      }

      [Test]
      public void TestCreateUnnamedSubFolder()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder7@example.test", "test");

         var simulator = new ImapClientSimulator();

         simulator.Connect();
         simulator.LogonWithLiteral(account.Address, "test");
         var result = simulator.Send("A01 CREATE 1..3");
         Assert.That(result.Contains("A01 NO"), Is.True);
         result = simulator.Send("A01 CREATE 1....3");
         Assert.That(result.Contains("A01 NO"), Is.True);
         result = simulator.Send("A01 CREATE 1.......3");
         Assert.That(result.Contains("A01 NO"), Is.True);


         simulator.Disconnect();
      }

      [Test]
      public void TestFolderCaseInLIST()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "quote@example.test", "test");

         var folderName = "ABC.def.GHI";

         var simulator = new ImapClientSimulator();
         simulator.Connect();
         simulator.Logon(account.Address, "test");
         Assert.That(simulator.CreateFolder(folderName), Is.True);
         Assert.That(simulator.List("ABC.DEF.*").Contains("ABC.def.GHI"), Is.False);
         Assert.That(simulator.List("ABC.DEF.*").Contains("ABC.DEF.GHI"), Is.True);
         Assert.That(simulator.List("ABC.def.*").Contains("ABC.DEF"), Is.False);
         Assert.That(simulator.List("ABC.def.*").Contains("ABC.def.GHI"), Is.True);
         Assert.That(simulator.SelectFolder(folderName), Is.True);
         simulator.Disconnect();
      }

      [Test]
      public void TestFolderCaseInLSUB()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "quote@example.test", "test");

         var folderName = "ABC.def.GHI";

         var simulator = new ImapClientSimulator();
         var sWelcomeMessage = simulator.Connect();
         simulator.Logon(account.Address, "test");
         Assert.That(simulator.CreateFolder(folderName), Is.True);
         Assert.That(simulator.Subscribe(folderName), Is.True);
         Assert.That(simulator.LSUB("ABC.DEF.*").Contains("ABC.def.GHI"), Is.False);
         Assert.That(simulator.LSUB("ABC.DEF.*").Contains("ABC.DEF.GHI"), Is.True);
         Assert.That(simulator.LSUB("ABC.def.*").Contains("ABC.DEF"), Is.False);
         Assert.That(simulator.LSUB("ABC.def.*").Contains("ABC.def.GHI"), Is.True);
         Assert.That(simulator.SelectFolder(folderName), Is.True);
         simulator.Disconnect();
      }

      /// <summary>
      ///    Tests that the LSUB response doesn't contain an empty line if a folder is not subscribed to.
      /// </summary>
      [Test]
      public void TestFolderLSUBUnsubscribedFolder()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "quote@example.test", "test");

         const string folderName = "ABC.def.GHI";

         var simulator = new ImapClientSimulator();
         simulator.Connect();
         simulator.Logon(account.Address, "test");
         Assert.That(simulator.CreateFolder(folderName), Is.True);
         Assert.That(simulator.LSUB().Contains("\r\n\r\n"), Is.False);
         simulator.Disconnect();
      }


      /// <summary>
      ///    Test that folder names in the LSUB response are put in quotes.
      ///    This is how gmail acts, and Thunderbird crashes if a folder name
      ///    is * without quotes.
      /// </summary>
      [Test]
      public void TestFolderQuote()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "quote@example.test", "test");

         const string folderName = "Test*";

         var simulator = new ImapClientSimulator();
         var sWelcomeMessage = simulator.Connect();
         simulator.Logon(account.Address, "test");
         Assert.That(simulator.CreateFolder(folderName), Is.True);
         Assert.That(simulator.Subscribe(folderName), Is.True);
         Assert.That(simulator.LSUB().Contains("\"" + folderName + "\""), Is.True);
         simulator.Disconnect();
      }

      [Test]
      public void TestStatusFolderWithBackslashDelimiter()
      {
         _settings.IMAPHierarchyDelimiter = "\\";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "status-delimiter@example.test", "test");

         var simulator = new ImapClientSimulator();
         simulator.Connect();
         simulator.LogonWithLiteral(account.Address, "test");
         simulator.CreateFolder("Test\\HelloWorld");

         string result = simulator.Status("Test\\\\HelloWorld", "MESSAGES");
         Assert.That(result.Contains("\"Test\\\\HelloWorld\""), Is.True, "STATUS response should escape backslashes. Got: " + result);
         Assert.That(result.Contains("A08 OK"), Is.True);
         simulator.Disconnect();
      }

      [Test]
      public void TestStatusFolderWithBackslashDelimiter_Literal()
      {
         _settings.IMAPHierarchyDelimiter = "\\";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "status-delimiter@example.test", "test");

         string folderName = "Test\\HelloWorld";

         var simulator = new ImapClientSimulator();
         simulator.Connect();
         simulator.LogonWithLiteral(account.Address, "test");
         simulator.CreateFolder(folderName);

         simulator.SendRaw($"A08 STATUS {{{folderName.Length}}}\r\n");
         simulator.Receive();
         simulator.SendRaw(folderName + Environment.NewLine);
         var statusResponse = simulator.Receive();

         Assert.That(statusResponse.Contains("\"Test\\\\HelloWorld\""), Is.True, "STATUS response should escape backslashes. Got: " + statusResponse);
         Assert.That(statusResponse.Contains("A08 OK"), Is.True);
         simulator.Disconnect();
      }

      [Test]
      public void TestListCommandChangedHierarchyDelimiter()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder1@example.test", "test");
         _settings.IMAPHierarchyDelimiter = "\\";

         var simulator = new ImapClientSimulator();

         simulator.Connect();
         simulator.LogonWithLiteral(account.Address, "test");
         simulator.CreateFolder("Test\\HelloWorld\\Test2");

         var response = simulator.List("%");
         Assert.That(response.Contains("\"Test\""), Is.True, response);

         response = simulator.List("%\\%");
         Assert.That(response.Contains("\"Test\\HelloWorld\""), Is.True, response);

         response = simulator.List("%\\%\\%%");
         Assert.That(response.Contains("\"Test\\HelloWorld\\Test2\""), Is.True, response);
      }


      [Test]
      [Description("Test that the namespace is included in an empty list response.")]
      public void TestListSpecial()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "quote@example.test", "test");

         var simulator = new ImapClientSimulator();
         var sWelcomeMessage = simulator.Connect();
         simulator.Logon(account.Address, "test");
         var response = simulator.List("");
         Assert.That(response.StartsWith("* LIST (\\Noselect) \".\" \"\""), Is.True);
         simulator.Disconnect();

         _settings.IMAPHierarchyDelimiter = "/";

         simulator = new ImapClientSimulator();
         sWelcomeMessage = simulator.Connect();
         simulator.Logon(account.Address, "test");
         response = simulator.List("");
         Assert.That(response.StartsWith("* LIST (\\Noselect) \"/\" \"\""), Is.True);
         simulator.Disconnect();

         _settings.IMAPHierarchyDelimiter = "\\";

         simulator = new ImapClientSimulator();
         sWelcomeMessage = simulator.Connect();
         simulator.Logon(account.Address, "test");
         response = simulator.List("", false);
         var expectedResponse = "* LIST (\\Noselect) \"\\\\\" \"\"";
         Assert.That(response.StartsWith(expectedResponse), Is.True);
         simulator.Disconnect();
      }

      [Test]
      [Description("Test to include reference in LIST command (issue 163).")]
      public void TestListWithReference()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "quote@example.test", "test");

         var simulator = new ImapClientSimulator();
         simulator.ConnectAndLogon(account.Address, "test");
         simulator.CreateFolder("Main.Sub1.Sub2.Sub3");
         simulator.CreateFolder("SomeOtherFolder");

         simulator.Subscribe("Main");
         simulator.Subscribe("Main.Sub1");
         simulator.Subscribe("Main.Sub1.Sub2");
         simulator.Subscribe("Main.Sub1.Sub2.Sub3");
         simulator.Subscribe("SomeOtherFolder");

         var response = simulator.List("Main", "*", true);
         Assert.That(response.Contains("INBOX"), Is.False);
         Assert.That(response.Contains("SomeOtherFolder"), Is.False);
         Assert.That(response.Contains("* LIST (\\HasChildren) \".\" \"Main.Sub1\""), Is.True);
         Assert.That(response.Contains("* LIST (\\HasChildren) \".\" \"Main.Sub1.Sub2\""), Is.True);
         Assert.That(response.Contains("* LIST (\\HasNoChildren) \".\" \"Main.Sub1.Sub2.Sub3\""), Is.True);

         response = simulator.List("Main.Sub1", "*", true);
         Assert.That(response.Contains("INBOX"), Is.False);
         Assert.That(response.Contains("SomeOtherFolder"), Is.False);
         Assert.That(response.Contains("* LIST (\\HasChildren) \".\" \"Main.Sub1.Sub2\""), Is.True);
         Assert.That(response.Contains("* LIST (\\HasNoChildren) \".\" \"Main.Sub1.Sub2.Sub3\""), Is.True);

         response = simulator.LSUB("Main", "*");
         Assert.That(response.Contains("INBOX"), Is.False);
         Assert.That(response.Contains("SomeOtherFolder"), Is.False);
         Assert.That(response.Contains("* LSUB (\\HasChildren) \".\" \"Main.Sub1\""), Is.True);
         Assert.That(response.Contains("* LSUB (\\HasChildren) \".\" \"Main.Sub1.Sub2\""), Is.True);
         Assert.That(response.Contains("* LSUB (\\HasNoChildren) \".\" \"Main.Sub1.Sub2.Sub3\""), Is.True);

         response = simulator.LSUB("Main.Sub1", "*");
         Assert.That(response.Contains("INBOX"), Is.False);
         Assert.That(response.Contains("SomeOtherFolder"), Is.False);
         Assert.That(response.Contains("* LSUB (\\HasChildren) \".\" \"Main.Sub1.Sub2\""), Is.True);
         Assert.That(response.Contains("* LSUB (\\HasNoChildren) \".\" \"Main.Sub1.Sub2.Sub3\""), Is.True);

         simulator.Disconnect();
      }

      [Test]
      [Description("Test to include reference in LIST command (test case from issue 165).")]
      public void TestListWithReferenceTestCase2()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "quote@example.test", "test");

         var simulator = new ImapClientSimulator();
         simulator.ConnectAndLogon(account.Address, "test");
         simulator.CreateFolder("INBOX.MyApp.SubFolder1");
         simulator.CreateFolder("INBOX.MyApp.SubFolder2");
         simulator.CreateFolder("INBOX.SomeOtherFolder");


         var response = simulator.List("INBOX.MyApp", "%.%", true);
         Assert.That(response.Contains("\"INBOX.MyApp\""), Is.False);
         Assert.That(response.Contains("\"INBOX.SomeOtherFolder\""), Is.False);
         Assert.That(response.Contains("* LIST (\\HasNoChildren) \".\" \"INBOX.MyApp.SubFolder1\""), Is.True);
         Assert.That(response.Contains("* LIST (\\HasNoChildren) \".\" \"INBOX.MyApp.SubFolder2\""), Is.True);

         simulator.Disconnect();
      }

      /// <summary>
      ///    Tests that a folder which is not subscribed to isn't included and vice versa.
      /// </summary>
      [Test]
      public void TestLsubInclusion()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "quote@example.test", "test");

         var folderName = "Folder1";

         var simulator = new ImapClientSimulator();
         var sWelcomeMessage = simulator.Connect();
         simulator.Logon(account.Address, "test");
         Assert.That(simulator.CreateFolder(folderName), Is.True);
         Assert.That(simulator.LSUB().Contains(folderName), Is.False);
         Assert.That(simulator.Subscribe(folderName), Is.True);
         Assert.That(simulator.LSUB().Contains(folderName), Is.True);

         simulator.Disconnect();
      }

      [Test]
      public void TestRenameIncorrectParameters()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder8@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.LogonWithLiteral(account.Address, "test");
         var result = simulator.Send("A01 CREATE A");

         result = simulator.Send("A02 RENAME A B C");
         Assert.That(result.Contains("A02 BAD"), Is.True);

         simulator.Disconnect();
      }

      [Test]
      public void TestRenameLongFolder()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder2@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.LogonWithLiteral(account.Address, "test");
         Assert.That(simulator.CreateFolder("1.2.3.4.5.6.7.8.9.10.11.12.13.14.15.16.17.18.19.20.21.22.23.24.25"), Is.True);
         Assert.That(simulator.CreateFolder("A"), Is.True);

         Assert.That(simulator.RenameFolder("1", "A.1"), Is.False);
         Assert.That(simulator.RenameFolder("1.2.3", "A.1"), Is.True);
         Assert.That(simulator.SelectFolder("A.1.4"), Is.True);


         simulator.Disconnect();
      }

      [Test]
      public void TestRenameRootPublicFolder()
      {
         var folders = _application.Settings.PublicFolders;
         var folder = folders.Add("ShareA");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionType = eACLPermissionType.ePermissionTypeAnyone;
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.set_Permission(eACLPermission.ePermissionRead, true);
         permission.set_Permission(eACLPermission.ePermissionDeleteMailbox, true);
         permission.Save();

         var folder2 = folders.Add("ShareB");
         folder2.Save();

         var permission2 = folder2.Permissions.Add();
         permission2.PermissionType = eACLPermissionType.ePermissionTypeAnyone;
         permission2.set_Permission(eACLPermission.ePermissionLookup, true);
         permission2.set_Permission(eACLPermission.ePermissionRead, true);
         permission2.set_Permission(eACLPermission.ePermissionDeleteMailbox, true);
         permission2.Save();

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "shared@example.test", "test");

         var simulator1 = new ImapClientSimulator();
         simulator1.ConnectAndLogon(account.Address, "test");
         Assert.That(simulator1.SelectFolder("#Public.ShareA"), Is.True);
         Assert.That(simulator1.SelectFolder("#Public.ShareB"), Is.True);
         Assert.That(simulator1.RenameFolder("#Public.ShareA", "#Public.ShareB.ShareA"), Is.True);
         Assert.That(simulator1.SelectFolder("#Public.ShareA"), Is.False);
         Assert.That(simulator1.SelectFolder("#Public.ShareB"), Is.True);
         Assert.That(simulator1.SelectFolder("#Public.ShareB.ShareA"), Is.True);
      }

      [Test]
      [Description("Issue 274, IMAP: Moving folder to sub-folder")]
      public void TestRenameSubFolderToChildSub()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder1@example.test", "test");

         var simulator = new ImapClientSimulator();
         Assert.That(simulator.ConnectAndLogon(account.Address, "test"), Is.True);
         Assert.That(simulator.CreateFolder("A.B"), Is.True);

         string result;
         Assert.That(simulator.RenameFolder("A.B", "A.B.C", out result), Is.False);
         Assert.That(result.Contains("A folder cannot be moved into one of its subfolders."), Is.True);

         Assert.That(simulator.SelectFolder("A.B"), Is.True);

         simulator.Disconnect();
      }

      [Test]
      [Description("Issue 274, Test side effect of fix for 274")]
      public void TestRenameSubFolderToMatchingName()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder1@example.test", "test");

         var simulator = new ImapClientSimulator();
         Assert.That(simulator.ConnectAndLogon(account.Address, "test"), Is.True);
         Assert.That(simulator.CreateFolder("Folder1"), Is.True);
         Assert.That(simulator.SelectFolder("Folder1"), Is.True);

         var result = string.Empty;
         Assert.That(simulator.RenameFolder("Folder1", "Folder1.Sub1", out result), Is.False);
         Assert.That(result.Contains("A folder cannot be moved into one of its subfolders."), Is.True);
         Assert.That(simulator.SelectFolder("Folder1"), Is.True);

         result = string.Empty;
         Assert.That(simulator.RenameFolder("Folder1", "Folder1Test", out result), Is.True);
         Assert.That(simulator.SelectFolder("Folder1Test"), Is.True);

         simulator.Disconnect();
      }

      [Test]
      public void TestRenameToAndFromINBOX()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder3@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.LogonWithLiteral(account.Address, "test");
         simulator.Send("A01 CREATE A\r\n");
         Assert.That(simulator.RenameFolder("A", "INBOX"), Is.False);
         Assert.That(simulator.RenameFolder("INBOX", "B"), Is.False);

         simulator.Disconnect();
      }

      [Test]
      [Description("Test renaming sub folder to a new sub folder where the new parent does not exist.")]
      public void TestRenameToParallelFolder()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder1@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.LogonWithLiteral(account.Address, "test");
         Assert.That(simulator.CreateFolder("Main.Sub"), Is.True);
         Assert.That(simulator.RenameFolder("Main.Sub", "Second.Sub"), Is.True);

         var listResponse = simulator.List();
         Assert.That(listResponse.Contains("Main.Sub"), Is.False);
         Assert.That(listResponse.Contains("Second.Sub"), Is.True);


         simulator.Disconnect();
      }

      [Test]
      public void TestRenameToSubFolder()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder1@example.test", "test");

         var simulator = new ImapClientSimulator();

         simulator.Connect();
         simulator.LogonWithLiteral(account.Address, "test");
         Assert.That(simulator.CreateFolder("1.2.3.4.5"), Is.True);
         Assert.That(simulator.CreateFolder("A"), Is.True);
         Assert.That(simulator.RenameFolder("1", "A.1"), Is.True);
         Assert.That(simulator.SelectFolder("A.1"), Is.True);
         Assert.That(simulator.SelectFolder("A.1.2.3.4.5"), Is.True);
         Assert.That(simulator.RenameFolder("A.1", "1"), Is.True);
         Assert.That(simulator.SelectFolder("A.1"), Is.False);
         Assert.That(simulator.SelectFolder("A.1.2.3.4.5"), Is.False);
         Assert.That(simulator.SelectFolder("1"), Is.True);
         Assert.That(simulator.SelectFolder("1.2.3.4.5"), Is.True);

         simulator.Disconnect();
      }

      [Test]
      public void TestTryCreateInvalidStructure()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder1@example.test", "test");

         var simulator = new ImapClientSimulator();

         simulator.Connect();
         simulator.LogonWithLiteral(account.Address, "test");
         Assert.That(simulator.CreateFolder("1.2.3"), Is.True);
         Assert.That(simulator.CreateFolder("1.2.3.4"), Is.True);

         // Should fail because name taken.
         Assert.That(simulator.RenameFolder("1.2.3.4", "1.2.3"), Is.False);
         Assert.That(simulator.RenameFolder("1.2.3.4", "1.2"), Is.False);
         Assert.That(simulator.RenameFolder("1.2.3.4", "1"), Is.False);

         // Should fail because invalid destination name.
         Assert.That(simulator.RenameFolder("1.2.3.4", ""), Is.False);

         // Should fail because destination name taken.
         Assert.That(simulator.RenameFolder("1.2.3", "1.2.3.4"), Is.False);
         Assert.That(simulator.RenameFolder("1.2", "1.2.3.4"), Is.False);
         Assert.That(simulator.RenameFolder("1", "1.2.3.4"), Is.False);

         Assert.That(simulator.RenameFolder("1.2.3", "1.2"), Is.False);
         Assert.That(simulator.RenameFolder("1.2.3", "1"), Is.False);

         Assert.That(simulator.RenameFolder("1.2.3", "A"), Is.True);

         simulator.Disconnect();
      }
   }
}