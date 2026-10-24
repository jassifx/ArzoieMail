// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

using hMailServer;
using NUnit.Framework;
using RegressionTests.Infrastructure;
using RegressionTests.Shared;

namespace RegressionTests.IMAP
{
   [TestFixture]
   public class ACL : TestFixtureBase
   {
      [Test]
      public void ExamineFolderRequiresReadPermission()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "reader@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.Save();

         var selectResult = string.Empty;

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.LogonWithLiteral(account1.Address, "test");
         var examineResult = imapClientSimulator.ExamineFolder("#Public.Share1");
         imapClientSimulator.Disconnect();

         Assert.That(examineResult.Contains("ACL: Read permission denied (Required for EXAMINE command)."),
            Is.True,
            examineResult);
      }

      [Test]
      public void FolderMarkedAsReadOnlyWhenUserHasReadOnlyRights()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "reader@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.set_Permission(eACLPermission.ePermissionRead, true);
         permission.Save();

         var selectResult = string.Empty;

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.LogonWithLiteral(account1.Address, "test");
         imapClientSimulator.SelectFolder("#Public.Share1", out selectResult);
         imapClientSimulator.Disconnect();

         Assert.That(selectResult.Contains("[READ-ONLY]"), Is.True, selectResult);
         Assert.That(selectResult.Contains("[READ-WRITE]"), Is.False, selectResult);
      }

      [Test]
      public void FolderMarkedAsReadWriteWhenUserHasChangeRights()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "reader@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.set_Permission(eACLPermission.ePermissionRead, true);
         permission.set_Permission(eACLPermission.ePermissionWriteOthers, true);
         permission.Save();

         var selectResult = string.Empty;

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.LogonWithLiteral(account1.Address, "test");
         imapClientSimulator.SelectFolder("#Public.Share1", out selectResult);
         imapClientSimulator.Disconnect();

         Assert.That(selectResult.Contains("[READ-WRITE]"), Is.True, selectResult);
         Assert.That(selectResult.Contains("[READ-ONLY]"), Is.False, selectResult);
      }

      [Test]
      public void TestAppendToPublicFolder()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "append-public@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.set_Permission(eACLPermission.ePermissionRead, true);
         permission.set_Permission(eACLPermission.ePermissionInsert, true);
         permission.set_Permission(eACLPermission.ePermissionPost, true);
         permission.Save();

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.LogonWithLiteral(account.Address, "test");
         simulator.SendSingleCommandWithLiteral("A01 APPEND #Public.Share1 {4}", "ABCD");
         Assert.That(simulator.GetMessageCount("#Public.Share1"), Is.EqualTo(1));
         simulator.Disconnect();
      }

      [Test]
      public void TestCopyMessageToPublicFolder()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account7@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionCreate, true);
         permission.Save();

         var folderName = "#Public.Share1";

         SmtpClientSimulator.StaticSend("test@example.test", account1.Address, "TestMessage", "Body");

         CustomAsserts.AssertFolderMessageCount(account1.IMAPFolders.get_ItemByName("INBOX"), 1);
         CustomAsserts.AssertFolderMessageCount(folder, 0);

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");
         imapClientSimulator.SelectFolder("INBOX");

         // This should fails since we don't have ACL Insert permission.
         Assert.That(imapClientSimulator.Copy(1, folderName), Is.False);

         // Give permission...
         permission.set_Permission(eACLPermission.ePermissionInsert, true);
         permission.Save();
         Assert.That(imapClientSimulator.Copy(1, folderName), Is.True);

         imapClientSimulator.Disconnect();

         CustomAsserts.AssertFolderMessageCount(folder, 1);
      }

      [Test]
      public void TestCreateFolderWithSameNameAsPublic()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();
         var origPublicFolderName = _settings.IMAPPublicFolderName;

         _settings.IMAPPublicFolderName = "MyPublic";

         try
         {
            var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account10@example.test",
               "test");

            var imapClientSimulator = new ImapClientSimulator();
            imapClientSimulator.Connect();
            imapClientSimulator.Logon(account1.Address, "test");
            Assert.That(imapClientSimulator.CreateFolder("MyPublic"), Is.False);
            imapClientSimulator.Disconnect();
         }
         finally
         {
            _settings.IMAPPublicFolderName = origPublicFolderName;
         }
      }


      [Test]
      public void TestCreateRootSharedFolder()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account1c@example.test", "test");
         var account2 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account2c@example.test", "test");

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");
         Assert.That(imapClientSimulator.CreateFolder("#Public.SharedFolder"), Is.False);
         imapClientSimulator.Disconnect();
      }


      [Test]
      public void TestDelete()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();


         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account9@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionCreate, true);
         permission.set_Permission(eACLPermission.ePermissionRead, true);
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.Save();

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.ConnectAndLogon("account9@example.test", "test");
         Assert.That(imapClientSimulator.SelectFolder("#public.Share1"), Is.True);
         Assert.That(imapClientSimulator.DeleteFolder("#public.Share1"), Is.False);

         permission.set_Permission(eACLPermission.ePermissionDeleteMailbox, true);
         permission.Save();

         Assert.That(imapClientSimulator.DeleteFolder("#public.Share1"), Is.True);
         Assert.That(imapClientSimulator.SelectFolder("#public.Share1"), Is.False);

         imapClientSimulator.Disconnect();
      }


      [Test]
      [Description("Test that when deleting an account, the corresponding permissions is removed as well.")]
      public void TestDeleteAccountHoldingGrant()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var publicFolders = _settings.PublicFolders;

         Assert.That(publicFolders.Count, Is.EqualTo(0));


         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account9@example.test", "test");

         var folder = publicFolders.Add("Share1");
         folder.Save();

         Assert.That(publicFolders.Count, Is.EqualTo(1));

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.Save();

         account1.Delete();

         Assert.That(folder.Permissions.Count, Is.EqualTo(0));
      }

      [Test]
      public void TestDeleteCheckAPI()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var publicFolders = _settings.PublicFolders;

         Assert.That(publicFolders.Count, Is.EqualTo(0));


         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account9@example.test", "test");

         var folder = publicFolders.Add("Share1");
         folder.Save();

         Assert.That(publicFolders.Count, Is.EqualTo(1));

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionCreate, true);
         permission.set_Permission(eACLPermission.ePermissionRead, true);
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.Save();

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.ConnectAndLogon("account9@example.test", "test");
         Assert.That(imapClientSimulator.SelectFolder("#public.Share1"), Is.True);
         Assert.That(imapClientSimulator.DeleteFolder("#public.Share1"), Is.False);

         Assert.That(publicFolders.Count, Is.EqualTo(1));

         permission.set_Permission(eACLPermission.ePermissionDeleteMailbox, true);
         permission.Save();

         Assert.That(imapClientSimulator.DeleteFolder("#public.Share1"), Is.True);
         Assert.That(imapClientSimulator.SelectFolder("#public.Share1"), Is.False);

         Assert.That(publicFolders.Count, Is.EqualTo(0));

         imapClientSimulator.Disconnect();
      }

      [Test]
      [Description("Test that when deleting an group, the corresponding permissions is removed as well.")]
      public void TestDeleteGroupHoldingGrant()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();


         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account1@example.test", "test");

         var group = SingletonProvider<TestSetup>.Instance.AddGroup("TestGroup");
         SingletonProvider<TestSetup>.Instance.AddGroupMember(group, account1);


         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionGroupID = group.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeGroup;
         permission.Save();

         group.Delete();

         Assert.That(folder.Permissions.Count, Is.EqualTo(0));
      }

      [Test]
      public void TestDeleteSubFolderCheckAPI()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var publicFolders = _settings.PublicFolders;

         Assert.That(publicFolders.Count, Is.EqualTo(0));


         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account9@example.test", "test");

         var folder = publicFolders.Add("Share1");
         folder.Save();

         Assert.That(publicFolders.Count, Is.EqualTo(1));

         var mySubFolders = folder.SubFolders;
         Assert.That(mySubFolders.Count, Is.EqualTo(0));

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionCreate, true);
         permission.set_Permission(eACLPermission.ePermissionRead, true);
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.Save();

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.ConnectAndLogon("account9@example.test", "test");
         Assert.That(imapClientSimulator.CreateFolder("#public.Share1.MySub"), Is.True);
         Assert.That(mySubFolders.Count, Is.EqualTo(1));

         permission.set_Permission(eACLPermission.ePermissionDeleteMailbox, true);
         permission.Save();

         Assert.That(imapClientSimulator.DeleteFolder("#public.Share1.MySub"), Is.True);
         Assert.That(imapClientSimulator.SelectFolder("#public.Share1.MySub"), Is.False);

         Assert.That(mySubFolders.Count, Is.EqualTo(0));
         Assert.That(publicFolders.Count, Is.EqualTo(1));

         imapClientSimulator.Disconnect();
      }

      [Test]
      [Description(
         "Test to create two groups with the same user account. The permissions for the FIRST group should be used.")
      ]
      public void TestDuplicateConflictingGroupPermission()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account1@example.test", "test");

         var group1 = SingletonProvider<TestSetup>.Instance.AddGroup("TestGroup1");
         SingletonProvider<TestSetup>.Instance.AddGroupMember(group1, account1);

         var group2 = SingletonProvider<TestSetup>.Instance.AddGroup("TestGroup2");
         SingletonProvider<TestSetup>.Instance.AddGroupMember(group2, account1);

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permissions = folder.Permissions;

         var permission = permissions.Add();
         permission.PermissionGroupID = group1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeGroup;
         permission.Save();

         var permission2 = permissions.Add();
         permission2.PermissionGroupID = group2.ID;
         permission2.PermissionType = eACLPermissionType.ePermissionTypeGroup;
         permission2.set_Permission(eACLPermission.ePermissionLookup, true);
         permission2.Save();

         var folderName = "#Public.Share1";

         // The account should not have permission since the first permission doesn't give him this.
         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");
         Assert.That(imapClientSimulator.List().Contains(folderName), Is.False);
         imapClientSimulator.Disconnect();

         // Now delete the permissions and in the first permission give the group the rights.
         while (permissions.Count > 0)
            permissions.Delete(0);

         permission = permissions.Add();
         permission.PermissionGroupID = group1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeGroup;
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.Save();

         permission2 = permissions.Add();
         permission2.PermissionGroupID = group2.ID;
         permission2.PermissionType = eACLPermissionType.ePermissionTypeGroup;
         permission2.Save();

         // The account should not have permission since the first permission does give him this.
         imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");
         Assert.That(imapClientSimulator.List().Contains(folderName), Is.True);
         imapClientSimulator.Disconnect();
      }

      [Test]
      public void TestExpunge()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account9@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionCreate, true);
         permission.set_Permission(eACLPermission.ePermissionInsert, true);
         permission.set_Permission(eACLPermission.ePermissionRead, true);
         permission.set_Permission(eACLPermission.ePermissionWriteDeleted, true);
         permission.Save();

         var folderName = "#Public.Share1";
         SmtpClientSimulator.StaticSend("test@example.test", account1.Address, "TestMessage", "Body");
         CustomAsserts.AssertFolderMessageCount(account1.IMAPFolders.get_ItemByName("INBOX"), 1);
         CustomAsserts.AssertFolderMessageCount(folder, 0);

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");
         imapClientSimulator.SelectFolder("INBOX");
         Assert.That(imapClientSimulator.Copy(1, folderName), Is.True);
         Assert.That(imapClientSimulator.SelectFolder(folderName), Is.True);
         Assert.That(imapClientSimulator.SetDeletedFlag(1), Is.True);


         Assert.That(imapClientSimulator.Expunge(), Is.False);

         permission.set_Permission(eACLPermission.ePermissionExpunge, true);
         permission.Save();
         Assert.That(imapClientSimulator.Expunge(), Is.True);

         imapClientSimulator.Disconnect();
      }

      [Test]
      public void TestGetACL()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account12@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionCreate, true);
         permission.set_Permission(eACLPermission.ePermissionAdminister, true);
         permission.Save();

         var folderName = "#Public.Share1";
         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.LogonWithLiteral(account1.Address, "test");
         var result = imapClientSimulator.GetACL(folderName);
         Assert.That(result.StartsWith("* ACL \"" + folderName + "\" " + account1.Address + " ka"), Is.True);


         permission.set_Permission(eACLPermission.ePermissionDeleteMailbox, true);
         permission.Save();
         result = imapClientSimulator.GetACL(folderName);
         Assert.That(result.StartsWith("* ACL \"" + folderName + "\" " + account1.Address + " kxa"), Is.True);

         permission.set_Permission(eACLPermission.ePermissionRead, true);
         permission.Save();
         result = imapClientSimulator.GetACL(folderName);
         Assert.That(result.StartsWith("* ACL \"" + folderName + "\" " + account1.Address + " rkxa"), Is.True);

         permission.set_Permission(eACLPermission.ePermissionExpunge, true);
         permission.Save();
         result = imapClientSimulator.GetACL(folderName);
         Assert.That(result.StartsWith("* ACL \"" + folderName + "\" " + account1.Address + " rkxea"), Is.True);

         imapClientSimulator.Disconnect();
      }

      /// <summary>
      ///    Test that if you create a folder named #public.Share1, both #public and #public.Share1
      ///    are listed in the LIST response.
      /// </summary>
      [Test]
      public void TestLISTPublicFolderParent()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account5@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionCreate, true);
         permission.Save();

         var folderName = "#Public.Share1";
         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");
         Assert.That(imapClientSimulator.List().Contains(folderName), Is.False);

         // Add permissions to select and lookup
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.Save();

         Assert.That(imapClientSimulator.List().Contains("#Public\"\r\n"), Is.True);
         Assert.That(imapClientSimulator.List().Contains(folderName), Is.True);

         imapClientSimulator.Disconnect();
      }

      /// <summary>
      ///    Test that if you create a folder named #public.Share1, both #public and #public.Share1
      ///    are listed in the LIST response.
      /// </summary>
      [Test]
      public void TestLSUBPublicFolderParent()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account5@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionCreate, true);
         permission.Save();

         var folderName = "#Public.Share1";
         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");
         Assert.That(imapClientSimulator.LSUB().Contains(folderName), Is.False);

         // Add permissions to select and lookup
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.Save();

         Assert.That(imapClientSimulator.LSUB().Contains("#Public\"\r\n"), Is.True);
         Assert.That(imapClientSimulator.LSUB().Contains(folderName), Is.True);

         imapClientSimulator.Disconnect();
      }

      /// <summary>
      ///    Test that a public folder is included in the LIST response.
      /// </summary>
      [Test]
      public void TestListPublicFolder()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account4@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.Save();

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.LogonWithLiteral(account1.Address, "test");

         var result = imapClientSimulator.List();

         Assert.That(result.Contains(folder.Name), Is.True);

         imapClientSimulator.Disconnect();
      }

      [Test]
      public void TestListPublicFolderAnyonePermission()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account1@example.test", "test");
         var account2 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account2@example.test", "test");
         var account3 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account3@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var folderName = "#Public.Share1";

         // account 1 should not have permission since they aren't added yet.
         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");
         Assert.That(imapClientSimulator.List().Contains(folderName), Is.False);
         imapClientSimulator.Disconnect();

         var permission = folder.Permissions.Add();
         permission.PermissionType = eACLPermissionType.ePermissionTypeAnyone;
         permission.set_Permission(eACLPermission.ePermissionCreate, true);
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.Save();


         // account 1 should not have permission since he's not in the group
         imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");
         Assert.That(imapClientSimulator.List().Contains(folderName), Is.True);
         imapClientSimulator.Disconnect();
      }

      [Test]
      public void TestListPublicFolderGroupPermission()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account1@example.test", "test");
         var account2 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account2@example.test", "test");
         var account3 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account3@example.test", "test");

         var group = SingletonProvider<TestSetup>.Instance.AddGroup("TestGroup");
         SingletonProvider<TestSetup>.Instance.AddGroupMember(group, account2);


         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionGroupID = group.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeGroup;
         permission.set_Permission(eACLPermission.ePermissionCreate, true);
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.Save();

         var folderName = "#Public.Share1";

         // account 1 should not have permission since he's not in the group
         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");
         Assert.That(imapClientSimulator.List().Contains(folderName), Is.False);
         imapClientSimulator.Disconnect();

         // account 2 should have permission since he is in the group
         imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account2.Address, "test");
         Assert.That(imapClientSimulator.List().Contains(folderName), Is.True);
         imapClientSimulator.Disconnect();

         // account 3 should not have permission since he's not in the group
         imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account3.Address, "test");
         Assert.That(imapClientSimulator.List().Contains(folderName), Is.False);
         imapClientSimulator.Disconnect();

         // add account 1 to the group to give him permission.
         var member = SingletonProvider<TestSetup>.Instance.AddGroupMember(group, account1);
         imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");
         Assert.That(imapClientSimulator.List().Contains(folderName), Is.True);
         imapClientSimulator.Disconnect();

         // delete account 1 from the group again to remove permissios.
         group.Members.DeleteByDBID(member.ID);
         imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");
         Assert.That(imapClientSimulator.List().Contains(folderName), Is.False);
         imapClientSimulator.Disconnect();
      }

      [Test]
      public void TestListPublicFolderOnly()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();


         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account1-p@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.Save();

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");

         Assert.That(imapClientSimulator.List("#Public").Contains("#Public"), Is.True);

         imapClientSimulator.Disconnect();
      }

      [Test]
      public void TestListPublicFolderSubFolderOnly()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();


         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account1-p@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.Save();

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");

         Assert.That(imapClientSimulator.List("#Public.Share1").Contains("#Public\r\n"), Is.False);

         imapClientSimulator.Disconnect();
      }

      [Test]
      public void TestListRights()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account1b@example.test", "test");
         var account2 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account2b@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionCreate, true);
         permission.set_Permission(eACLPermission.ePermissionAdminister, true);
         permission.set_Permission(eACLPermission.ePermissionExpunge, true);
         permission.set_Permission(eACLPermission.ePermissionDeleteMailbox, true);
         permission.set_Permission(eACLPermission.ePermissionRead, true);
         permission.Save();

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.LogonWithLiteral(account1.Address, "test");
         var folderName = "#Public.Share1";

         var rights = imapClientSimulator.ListRights(folderName, account1.Address);
         Assert.That(rights.StartsWith("* LISTRIGHTS #Public.Share1 account1b@example.test l r s w i k x t e a"), Is.True);
         imapClientSimulator.Disconnect();
      }

      [Test]
      public void TestMyRights()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account13@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionCreate, true);
         permission.set_Permission(eACLPermission.ePermissionAdminister, true);
         permission.set_Permission(eACLPermission.ePermissionExpunge, true);
         permission.set_Permission(eACLPermission.ePermissionDeleteMailbox, true);
         permission.set_Permission(eACLPermission.ePermissionRead, true);
         permission.Save();

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.LogonWithLiteral(account1.Address, "test");
         var folderName = "#Public.Share1";
         var result = imapClientSimulator.GetMyRights(folderName);
         Assert.That(result.StartsWith("* MYRIGHTS \"" + folderName + "\" rkxea"), Is.True);

         imapClientSimulator.Disconnect();
      }

      [Test]
      public void TestPublicFolderCreateDeep()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();


         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account1@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionCreate, true);
         permission.set_Permission(eACLPermission.ePermissionRead, true);
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.Save();

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");
         var folderName = "#Public.Share1.MySub1.MySub2.MySub3.MySub4";
         Assert.That(imapClientSimulator.CreateFolder(folderName), Is.True);

         imapClientSimulator.Disconnect();
      }

      [Test]
      public void TestPublicFolderNoSelectAttribute()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();


         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account1-p@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.Save();

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");

         Assert.That(imapClientSimulator.List("#Public").Contains("Noselect"), Is.True);
         Assert.That(imapClientSimulator.List("#Public.Share1").Contains("#Public.Share1"), Is.True);
         Assert.That(imapClientSimulator.List("#Public.Share1").Contains("Noselect"), Is.False);

         imapClientSimulator.Disconnect();
      }

      [Test]
      public void TestPublicFolderSubscriptionAPI()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account1e@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         Assert.That(folder.Subscribed, Is.True);
      }

      [Test]
      public void TestPublicFolderSubscriptionCreate()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account1f@example.test", "test");
         var account2 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account2f@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionCreate, true);
         permission.set_Permission(eACLPermission.ePermissionAdminister, true);
         permission.set_Permission(eACLPermission.ePermissionExpunge, true);
         permission.set_Permission(eACLPermission.ePermissionDeleteMailbox, true);
         permission.set_Permission(eACLPermission.ePermissionRead, true);
         permission.Save();

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");
         var folderName = "#Public.Share1.MySub";
         imapClientSimulator.CreateFolder(folderName);

         var theNewFolder = folder.SubFolders[0];
         Assert.That(theNewFolder.Name.Equals("MySub"), Is.True);
         Assert.That(theNewFolder.Subscribed, Is.True);

         imapClientSimulator.Disconnect();
      }

      [Test]
      public void TestPublicFolderUnsubscribe()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();


         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account1g@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionCreate, true);
         permission.set_Permission(eACLPermission.ePermissionAdminister, true);
         permission.set_Permission(eACLPermission.ePermissionExpunge, true);
         permission.set_Permission(eACLPermission.ePermissionDeleteMailbox, true);
         permission.set_Permission(eACLPermission.ePermissionRead, true);
         permission.Save();

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");
         var folderName = "#Public.Share1";
         Assert.That(imapClientSimulator.Unsubscribe(folderName), Is.False);


         imapClientSimulator.Disconnect();
      }

      [Test]
      public void TestRenameFolderToAndFromPublic()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account11@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionCreate, true);
         permission.set_Permission(eACLPermission.ePermissionDeleteMailbox, true);
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.Save();

         var oldFolderName = "#Public.Share1";
         var newFolderName = "AccountLevelFolder";

         // Test renaming from local folder name to shared folder.
         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");
         Assert.That(imapClientSimulator.RenameFolder(oldFolderName, newFolderName), Is.False);
         imapClientSimulator.Disconnect();


         // Test renaming from local folder name to shared folder.
         oldFolderName = "LocalFolder";
         newFolderName = "#Public.Share1";

         imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");
         imapClientSimulator.CreateFolder(oldFolderName);
         Assert.That(imapClientSimulator.RenameFolder(oldFolderName, newFolderName), Is.False);
         imapClientSimulator.Disconnect();
      }

      [Test]
      public void TestRenamePublicFolder()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account9@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionCreate, true);
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.Save();

         var oldFolderName = "#Public.Share1.MySub1";
         var newFolderName = "#Public.Share1.MySub2";
         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");
         Assert.That(imapClientSimulator.CreateFolder(oldFolderName), Is.True);

         Assert.That(imapClientSimulator.RenameFolder(oldFolderName, newFolderName), Is.False);

         // Add permissions to delete the mailbox.
         permission.set_Permission(eACLPermission.ePermissionDeleteMailbox, true);
         permission.Save();

         Assert.That(imapClientSimulator.RenameFolder(oldFolderName, newFolderName), Is.True);

         Assert.That(imapClientSimulator.List().Contains(oldFolderName), Is.False);
         Assert.That(imapClientSimulator.List().Contains(newFolderName), Is.True);

         imapClientSimulator.Disconnect();
      }

      [Test]
      public void TestRenamePublicFolderToRootPublicFolder()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account1d@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionCreate, true);
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.set_Permission(eACLPermission.ePermissionDeleteMailbox, true);
         permission.Save();

         var oldFolderName = "#Public.Share1.MySub1";
         var newFolderName = "#Public.Share2";

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");
         Assert.That(imapClientSimulator.CreateFolder(oldFolderName), Is.True);
         Assert.That(imapClientSimulator.RenameFolder(oldFolderName, newFolderName), Is.False);

         Assert.That(imapClientSimulator.List().Contains(oldFolderName), Is.True);
         Assert.That(imapClientSimulator.List().Contains(newFolderName), Is.False);

         imapClientSimulator.Disconnect();
      }

      [Test]
      public void TestSelectPublicFolder()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account6@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionCreate, true);
         permission.Save();

         var folderName = "#Public.Share1.MySub1";
         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.LogonWithLiteral(account1.Address, "test");
         Assert.That(imapClientSimulator.CreateFolder(folderName), Is.True);

         Assert.That(imapClientSimulator.SelectFolderWithoutLiteral(folderName), Is.False);

         imapClientSimulator.Disconnect();
      }

      [Test]
      public void TestSetACLOnAccountFolder()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account1@example.test", "test");
         var account2 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account2@example.test", "test");

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.LogonWithLiteral(account1.Address, "test");
         Assert.That(imapClientSimulator.CreateFolder("SharedFolder"), Is.True);
         Assert.That(imapClientSimulator.SetACL("SharedFolder", account2.Address, "lrswipkxtea"), Is.False);
         imapClientSimulator.Disconnect();
      }

      [Test]
      public void TestSetAclOnPublicFolderNormalUser()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account3@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.LogonWithLiteral(account1.Address, "test");
         Assert.That(imapClientSimulator.CreateFolder("#Public.Share1.MySub1"), Is.False);
         imapClientSimulator.Disconnect();
      }

      [Test]
      public void TestSetAclOnPublicFolderNormalUserWithPrivilegies()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account4@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionCreate, true);
         permission.Save();

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.LogonWithLiteral(account1.Address, "test");
         Assert.That(imapClientSimulator.CreateFolder("#Public.Share1.MySub1"), Is.True);
         imapClientSimulator.Disconnect();
      }

      [Test]
      public void TestSetDeletedFlag()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account8@example.test", "test");

         var publicFolders = _settings.PublicFolders;
         var folder = publicFolders.Add("Share1");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionCreate, true);
         permission.set_Permission(eACLPermission.ePermissionInsert, true);
         permission.set_Permission(eACLPermission.ePermissionRead, true);
         permission.Save();

         var folderName = "#Public.Share1";
         SmtpClientSimulator.StaticSend("test@example.test", account1.Address, "TestMessage", "Body");
         CustomAsserts.AssertFolderMessageCount(account1.IMAPFolders.get_ItemByName("INBOX"), 1);
         CustomAsserts.AssertFolderMessageCount(folder, 0);

         var imapClientSimulator = new ImapClientSimulator();
         imapClientSimulator.Connect();
         imapClientSimulator.Logon(account1.Address, "test");
         imapClientSimulator.SelectFolder("INBOX");
         Assert.That(imapClientSimulator.Copy(1, folderName), Is.True);

         CustomAsserts.AssertFolderMessageCount(folder, 1);
         imapClientSimulator.SelectFolder(folderName);
         Assert.That(imapClientSimulator.SetDeletedFlag(1), Is.False);

         permission.set_Permission(eACLPermission.ePermissionWriteDeleted, true);
         permission.Save();
         Assert.That(imapClientSimulator.SetDeletedFlag(1), Is.True);

         imapClientSimulator.Disconnect();
      }

      [Test]
      [Description("APPEND leaves out the flags the user lacks the right to set (RFC 4314 4).")]
      public void AppendWithoutWriteRightDropsFlags()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "flags@example.test", "test");
         var permission = CreateShare(account, eACLPermission.ePermissionLookup, eACLPermission.ePermissionRead,
            eACLPermission.ePermissionInsert);

         var simulator = new ImapClientSimulator();
         Assert.That(simulator.ConnectAndLogon(account.Address, "test"), Is.True);

         var append = AppendWithFlags(simulator, "#Public.Share", "(\\Draft \\Answered \\Flagged \\Deleted)");
         Assert.That(append.Contains("A40 OK"), Is.True, append);

         permission.set_Permission(eACLPermission.ePermissionWriteOthers, true);
         permission.set_Permission(eACLPermission.ePermissionWriteDeleted, true);
         permission.Save();

         append = AppendWithFlags(simulator, "#Public.Share", "(\\Draft \\Answered \\Flagged \\Deleted)");
         Assert.That(append.Contains("A40 OK"), Is.True, append);

         Assert.That(simulator.SelectFolder("#Public.Share"), Is.True);
         AssertFlags(simulator.Fetch("1 (FLAGS)"), false);
         AssertFlags(simulator.Fetch("2 (FLAGS)"), true);

         simulator.Disconnect();
      }

      [Test]
      [Description("COPY leaves out the flags the user lacks the right to set in the destination (RFC 4314 4).")]
      public void CopyWithoutWriteRightDropsFlags()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "flags@example.test", "test");
         CreateShare(account, eACLPermission.ePermissionLookup, eACLPermission.ePermissionRead,
            eACLPermission.ePermissionInsert);

         var simulator = new ImapClientSimulator();
         Assert.That(simulator.ConnectAndLogon(account.Address, "test"), Is.True);

         var append = AppendWithFlags(simulator, "INBOX", "(\\Draft \\Answered \\Flagged \\Deleted)");
         Assert.That(append.Contains("A40 OK"), Is.True, append);

         Assert.That(simulator.SelectFolder("INBOX"), Is.True);
         AssertFlags(simulator.Fetch("1 (FLAGS)"), true);
         Assert.That(simulator.Copy(1, "#Public.Share"), Is.True);

         Assert.That(simulator.SelectFolder("#Public.Share"), Is.True);
         AssertFlags(simulator.Fetch("1 (FLAGS)"), false);

         simulator.Disconnect();
      }

      [TestCase("SELECT")]
      [TestCase("STATUS")]
      [TestCase("STORE")]
      [TestCase("CREATE")]
      [TestCase("DELETE")]
      [TestCase("RENAME")]
      [TestCase("SETACL")]
      [TestCase("APPEND")]
      [TestCase("COPY")]
      [TestCase("UID EXPUNGE")]
      [Description("An ACL denial is NO [NOPERM] (RFC 5530 3, RFC 4314 4), not BAD.")]
      public void AclDenialIsNoPerm(string command)
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "noperm@example.test", "test");

         // Enough to see the folders and put a message in them, but nothing else.
         var permission = CreateShare(account, eACLPermission.ePermissionLookup, eACLPermission.ePermissionRead,
            eACLPermission.ePermissionInsert);

         var subFolder = _settings.PublicFolders.get_ItemByName("Share").SubFolders.Add("Sub");
         subFolder.Save();

         var simulator = new ImapClientSimulator();
         Assert.That(simulator.ConnectAndLogon(account.Address, "test"), Is.True);

         string response;
         switch (command)
         {
            case "SELECT":
            case "STATUS":
               permission.set_Permission(eACLPermission.ePermissionRead, false);
               permission.Save();
               response = command == "SELECT"
                  ? simulator.SendSingleCommand("A10 SELECT \"#Public.Share\"")
                  : simulator.SendSingleCommand("A10 STATUS \"#Public.Share\" (MESSAGES)");
               break;
            case "STORE":
               Assert.That(AppendWithFlags(simulator, "#Public.Share", null).Contains("A40 OK"), Is.True);
               Assert.That(simulator.SelectFolder("#Public.Share"), Is.True);
               response = simulator.SendSingleCommand("A10 STORE 1 +FLAGS (\\Flagged)");
               break;
            case "CREATE":
               response = simulator.SendSingleCommand("A10 CREATE \"#Public.Share.New\"");
               break;
            case "DELETE":
               response = simulator.SendSingleCommand("A10 DELETE \"#Public.Share.Sub\"");
               break;
            case "RENAME":
               response = simulator.SendSingleCommand("A10 RENAME \"#Public.Share.Sub\" \"#Public.Share.Renamed\"");
               break;
            case "APPEND":
            case "COPY":
               permission.set_Permission(eACLPermission.ePermissionInsert, false);
               permission.Save();

               if (command == "APPEND")
               {
                  response = AppendWithFlags(simulator, "#Public.Share", null).Replace("A40 ", "A10 ");
               }
               else
               {
                  Assert.That(AppendWithFlags(simulator, "INBOX", null).Contains("A40 OK"), Is.True);
                  Assert.That(simulator.SelectFolder("INBOX"), Is.True);
                  response = simulator.SendSingleCommand("A10 UID COPY 1 \"#Public.Share\"");
               }
               break;
            case "UID EXPUNGE":
               Assert.That(AppendWithFlags(simulator, "#Public.Share", "(\\Deleted)").Contains("A40 OK"), Is.True);
               Assert.That(simulator.SelectFolder("#Public.Share"), Is.True);
               response = simulator.SendSingleCommand("A10 UID EXPUNGE 1:*");
               break;
            default:
               response = simulator.SendSingleCommand("A10 SETACL \"#Public.Share\" " + account.Address + " lrswipkxtea");
               break;
         }

         Assert.That(response, Does.Contain("A10 NO [NOPERM]"));
         simulator.Disconnect();
      }

      private IMAPFolderPermission CreateShare(Account account, params eACLPermission[] rights)
      {
         var folder = _settings.PublicFolders.Add("Share");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;

         foreach (var right in rights)
            permission.set_Permission(right, true);

         permission.Save();
         return permission;
      }

      private static string AppendWithFlags(ImapClientSimulator simulator, string folderName, string flags)
      {
         const string message = "Subject: Flags\r\n\r\nBody\r\n";
         return simulator.SendSingleCommandWithLiteral(
            "A40 APPEND \"" + folderName + "\" " + (flags != null ? flags + " " : "") + "{" + message.Length + "}", message);
      }

      private static void AssertFlags(string fetch, bool expected)
      {
         foreach (var flag in new[] {"\\Draft", "\\Answered", "\\Flagged", "\\Deleted"})
            Assert.That(fetch.Contains(flag), Is.EqualTo(expected), flag + ": " + fetch);
      }
   }
}