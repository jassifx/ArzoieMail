// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using hMailServer;
using NUnit.Framework;
using RegressionTests.Shared;

namespace RegressionTests.Infrastructure
{
   [TestFixture]
   public class BackupRestore : TestFixtureBase
   {
      [SetUp]
      public new void SetUp()
      {
         InitializeBackupSettings();
      }

      private string _backupDir;
      private static string _folderCreationTime;
      private bool _backupMessages = true;
      private readonly int _fetchAccountPort = TestSetup.GetNextFreePort();

      public void InitializeBackupSettings()
      {
         _application = SingletonProvider<TestSetup>.Instance.GetApp();
         SetBackupDir(Path.Combine(TestSetup.GetSharedTempDirectory(), TestSetup.UniqueString()));

         var dirInfo = new DirectoryInfo(_backupDir);
         dirInfo.Create();

         if (File.Exists(_application.Settings.Backup.LogFile))
            File.Delete(_application.Settings.Backup.LogFile);
      }

      public void SetBackupDir(string backupDir)
      {
         _backupDir = backupDir;
      }

      internal bool Execute()
      {
         CustomAsserts.AssertDeleteFile(_application.Settings.Backup.LogFile);

         SetupEnvironment();
         if (!BackupEnvironment())
            return false;

         DeleteEnvironment();
         RestoreEnvironment();
         ConfirmRestore();

         Directory.Delete(_backupDir, true);

         return true;
      }

      private bool WaitForBackupCompletion()
      {
         Assert.That(_application, Is.Not.Null);

         for (var i = 0; i < 40; i++)
         {
            try
            {
               var logFile = _application.Settings.Backup.LogFile;

               var contents = TestSetup.ReadExistingTextFile(logFile);

               if (contents.IndexOf("Backup completed successfully") > 0)
                  return true;

               if (contents.IndexOf("BACKUP ERROR:") > 0) return false;
            }
            catch (Exception)
            {
               // probably a share access violation.
            }

            Thread.Sleep(250);
         }

         return false;
      }

      private void WaitForRestoreCompletion(string lastServerStartTime)
      {
         Assert.That(_application, Is.Not.Null);

         for (var i = 0; i < 600; i++)
         {
            try
            {
               var startTime = _application.Status.StartTime;

               if (startTime.Length > 0 && startTime != lastServerStartTime)
                  return;
            }
            catch (Exception)
            {
               // probably a share access violation.
            }

            Thread.Sleep(100);
         }

         throw new Exception("Timeout while waiting for backup or restore completion");
      }

      public void SetupEnvironment()
      {
         // Set up the domain. It's already set-up...
         var domain = _application.Domains[0];
         Assert.That(domain, Is.Not.Null);
         Assert.That(domain.Name, Is.EqualTo("example.test"));

         SetupDomainObject(domain);
         SetupDomainObjects(domain);
         SetupSettings();
      }

      private void SetupSettings()
      {
         SetupBlockedAttachment();
         SetupGreyListingWhiteList();
         SetupWhiteList();
         SetupRoutes();
         SetupDNSBlackLists();
         SetupSURBLServers();
         SetupSSLCertificates();
         SetupGroupObject();
         SetupPublicFolders();
         SetupIncomingRelay();
      }

      private void SetupSSLCertificates()
      {
         var cert = _application.Settings.SSLCertificates.Add();
         cert.CertificateFile = "file1.txt";
         cert.Name = "name1";
         cert.PrivateKeyFile = "pk1";
         cert.Save();

         cert = _application.Settings.SSLCertificates.Add();
         cert.CertificateFile = "file2.txt";
         cert.Name = "name2";
         cert.PrivateKeyFile = "pk2";
         cert.Save();
      }

      private void SetupBlockedAttachment()
      {
         var ba = _application.Settings.AntiVirus.BlockedAttachments.Add();
         ba.Description = "My description";
         ba.Wildcard = "*.my";
         ba.Save();
      }

      private void SetupDNSBlackLists()
      {
         var black = _application.Settings.AntiSpam.DNSBlackLists.Add();
         black.Active = true;
         black.DNSHost = "127.0.0.1";
         black.ExpectedResult = "127.5.2.1";
         black.RejectMessage = "Test";
         black.Score = 4;
         black.Save();

         black = _application.Settings.AntiSpam.DNSBlackLists.Add();
         black.Active = true;
         black.DNSHost = "127.0.0.2";
         black.ExpectedResult = "127.5.2.2";
         black.RejectMessage = "Test2";
         black.Score = 5;
         black.Save();
      }

      private void SetupIncomingRelay()
      {
         var relay = _application.Settings.IncomingRelays.Add();
         relay.Name = "Test";
         relay.LowerIP = "1.2.3.4";
         relay.UpperIP = "4.3.2.1";
         relay.Save();
      }


      private void SetupSURBLServers()
      {
         var black = _application.Settings.AntiSpam.SURBLServers.Add();
         black.Active = true;
         black.DNSHost = "127.0.0.1";
         black.RejectMessage = "Test";
         black.Score = 4;
         black.Save();

         black = _application.Settings.AntiSpam.SURBLServers.Add();
         black.Active = true;
         black.DNSHost = "127.0.0.2";
         black.RejectMessage = "Test2";
         black.Score = 5;
         black.Save();
      }

      private void SetupGreyListingWhiteList()
      {
         var addresses = _application.Settings.AntiSpam.GreyListingWhiteAddresses;

         var address = addresses.Add();
         address.Description = "helo1";
         address.IPAddress = "1.1.1.1";
         address.Save();

         address = addresses.Add();
         address.Description = "helo2";
         address.IPAddress = "2.2.2.2";
         address.Save();
      }


      private void SetupWhiteList()
      {
         var addresses = _application.Settings.AntiSpam.WhiteListAddresses;

         var address = addresses.Add();
         address.Description = "Desc1";
         address.EmailAddress = "Email1";
         address.LowerIPAddress = "1.1.1.1";
         address.UpperIPAddress = "2.2.2.2";
         address.Save();

         address = addresses.Add();
         address.Description = "Desc2";
         address.EmailAddress = "Email2";
         address.LowerIPAddress = "2.2.2.2";
         address.UpperIPAddress = "21.21.21.21";
         address.Save();
      }


      private void SetupRoutes()
      {
         var routes = _application.Settings.Routes;

         var route = routes.Add();
         route.DomainName = "test1.com";
         route.Description = "description";
         route.UseSSL = true;
         route.TreatSenderAsLocalDomain = true;
         route.TreatRecipientAsLocalDomain = false;
         route.Save();

         route = routes.Add();
         route.DomainName = "test2.com";
         route.Description = "description2";
         route.UseSSL = false;
         route.TreatSenderAsLocalDomain = false;
         route.TreatRecipientAsLocalDomain = true;
         route.Save();

         var addresses = route.Addresses;

         var address1 = addresses.Add();
         address1.Address = "address1@test2.com";
         address1.Save();

         var address2 = addresses.Add();
         address2.Address = "address2@test2.com";
         address2.Save();
      }

      private void SetupDomainObject(Domain domain)
      {
         domain.Postmaster = "someone@example.test";
         domain.SignatureEnabled = true;
         domain.AddSignaturesToLocalMail = true;
         domain.AddSignaturesToReplies = true;

         domain.SignaturePlainText = "PLS";
         domain.SignatureHTML = "HTML";
         domain.Save();

         var da = domain.DomainAliases.Add();
         da.AliasName = "test1.com";
         da.Save();

         var da2 = domain.DomainAliases.Add();
         da2.AliasName = "test2.com";
         da2.Save();
      }

      private void SetupDomainObjects(Domain domain)
      {
         SetupAccountObject(domain);
         SetupAliasObject(domain);
         SetupDistributionListObject(domain);
      }

      private void SetupPublicFolders()
      {
         var folders = _application.Settings.PublicFolders;
         var folder = folders.Add("Test1");
         folder.Save();

         folder = folders.Add("Test2");
         folder.Save();

         folder = folders.Add("Test3");
         folder.Save();

         folder = folders.Add("ACL");
         folder.Save();

         var domain = _application.Domains[0];
         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(domain, "acltest-1@example.test", "test");

         var permission = folder.Permissions.Add();
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.PermissionAccountID = account1.ID;
         permission.set_Permission(eACLPermission.ePermissionAdminister, true);
         permission.set_Permission(eACLPermission.ePermissionInsert, true);
         permission.Save();

         folder = folder.SubFolders.Add("MySubFolder");
         folder.Save();

         var group1 = _application.Settings.Groups.Add();
         group1.Name = "ACLTestGroup";
         group1.Save();

         permission = folder.Permissions.Add();
         permission.PermissionType = eACLPermissionType.ePermissionTypeGroup;
         permission.PermissionGroupID = group1.ID;
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.set_Permission(eACLPermission.ePermissionDeleteMailbox, true);
         permission.Save();
      }

      private void ConfirmPublicFolders()
      {
         if (_backupMessages)
         {
            var domain = _application.Domains[0];

            var publicFolders = _application.Settings.PublicFolders;

            Assert.That(publicFolders.get_ItemByName("Test1"), Is.Not.Null);
            Assert.That(publicFolders.get_ItemByName("Test2"), Is.Not.Null);
            Assert.That(publicFolders.get_ItemByName("Test3"), Is.Not.Null);
            Assert.That(publicFolders.get_ItemByName("ACL"), Is.Not.Null);

            var permissions = publicFolders.get_ItemByName("ACL").Permissions;
            Assert.That(permissions.Count, Is.EqualTo(1));

            var account1 = domain.Accounts.get_ItemByAddress("acltest-1@example.test");

            var permission = permissions[0];

            Assert.That(permission.PermissionType, Is.EqualTo(eACLPermissionType.ePermissionTypeUser));
            Assert.That(permission.PermissionAccountID, Is.EqualTo(account1.ID));

            Assert.That(permission.get_Permission(eACLPermission.ePermissionAdminister), Is.True);
            Assert.That(permission.get_Permission(eACLPermission.ePermissionInsert), Is.True);
            Assert.That(permission.get_Permission(eACLPermission.ePermissionRead), Is.False);
            Assert.That(permission.get_Permission(eACLPermission.ePermissionWriteSeen), Is.False);

            var group1 = _application.Settings.Groups.get_ItemByName("ACLTestGroup");

            permissions = publicFolders.get_ItemByName("ACL").SubFolders.get_ItemByName("MySubFolder").Permissions;
            permission = permissions[0];

            Assert.That(permission.PermissionType, Is.EqualTo(eACLPermissionType.ePermissionTypeGroup));
            Assert.That(permission.PermissionGroupID, Is.EqualTo(group1.ID));

            Assert.That(permission.get_Permission(eACLPermission.ePermissionLookup), Is.True);
            Assert.That(permission.get_Permission(eACLPermission.ePermissionDeleteMailbox), Is.True);
            Assert.That(permission.get_Permission(eACLPermission.ePermissionRead), Is.False);
            Assert.That(permission.get_Permission(eACLPermission.ePermissionWriteSeen), Is.False);
         }
      }

      private void SetupDistributionListObject(Domain domain)
      {
         var listRecipients = new List<string>();
         listRecipients.Add("member1@example.test");
         listRecipients.Add("member2@example.test");
         listRecipients.Add("member3@example.test");

         var list = SingletonProvider<TestSetup>.Instance.AddDistributionList(domain, "list@example.test",
            listRecipients);
      }

      private void SetupGroupObject()
      {
         var group = _application.Settings.Groups.Add();
         group.Name = "TestGroup";
         group.Save();

         var gm1 = SingletonProvider<TestSetup>.Instance.AddAccount(_application.Domains[0], "gm1@example.test",
            "test");
         var gm2 = SingletonProvider<TestSetup>.Instance.AddAccount(_application.Domains[0], "gm2@example.test",
            "test");
         var gm3 = SingletonProvider<TestSetup>.Instance.AddAccount(_application.Domains[0], "gm3@example.test",
            "test");

         var gm = group.Members.Add();
         gm.AccountID = gm1.ID;
         gm.Save();

         gm = group.Members.Add();
         gm.AccountID = gm2.ID;
         gm.Save();

         gm = group.Members.Add();
         gm.AccountID = gm3.ID;
         gm.Save();
      }

      private void SetupAliasObject(Domain domain)
      {
         var alias = SingletonProvider<TestSetup>.Instance.AddAlias(domain, "alias@example.test",
            "someone@example.test");
      }

      private void SetupAccountObject(Domain domain)
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(domain, "test@example.test", "test");

         // Make sure the inbox contains two messages which should be backed up.
         SmtpClientSimulator.StaticSend(account.Address, account.Address, "Message 1 Subject",
            "Message 1 Body");

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         SmtpClientSimulator.StaticSend(account.Address, account.Address, "Message 2 Subject",
            "Message 2 Body");
         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 2);

         SmtpClientSimulator.StaticSend(account.Address, account.Address, "Message 3 Subject",
            "Message 3 Body");
         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 3);

         var sim = new ImapClientSimulator();
         Assert.That(sim.ConnectAndLogon(account.Address, "test"), Is.True);
         Assert.That(sim.SelectFolder("Inbox"), Is.True);
         Assert.That(sim.SetDeletedFlag(2), Is.True);
         Assert.That(sim.Expunge(), Is.True);
         sim.Disconnect();

         _folderCreationTime = account.IMAPFolders.get_ItemByName("INBOX").CreationTime;

         account.Active = true;
         account.ADDomain = "AD";
         account.AdminLevel = eAdminLevel.hAdminLevelDomainAdmin;
         account.ADUsername = "AU";
         account.ForwardAddress = "FA";
         account.ForwardEnabled = false;
         account.ForwardKeepOriginal = true;
         account.IsAD = false;
         account.MaxSize = 1250;
         account.PersonFirstName = "First";
         account.PersonLastName = "Last";
         account.SignatureEnabled = true;
         account.SignatureHTML = "HTML";
         account.SignaturePlainText = "PLAIN";
         account.VacationMessage = "VAC";
         account.VacationMessageExpires = true;
         account.VacationMessageExpiresDate = "2040-01-01";
         account.VacationMessageIsOn = true;
         account.VacationSubject = "SUBJ";
         account.Password = "test";
         account.Save();

         // Set up fetch account
         var fa = account.FetchAccounts.Add();
         fa.DaysToKeepMessages = 5;
         fa.Enabled = true;
         fa.MinutesBetweenFetch = 10;
         fa.Name = "test";
         fa.Port = _fetchAccountPort;
         fa.ProcessMIMEDate = true;
         fa.ProcessMIMERecipients = true;
         fa.ServerAddress = "127.0.0.1";
         fa.Username = "test";
         fa.UseSSL = false;
         fa.UseAntiSpam = true;
         fa.UseAntiVirus = true;
         fa.Save();

         DownloadFromExternalAccount(account, fa);

         var rule = account.Rules.Add();
         rule.Name = "MyRule";
         var criteria = rule.Criterias.Add();
         criteria.MatchType = eRuleMatchType.eMTGreaterThan;
         criteria.PredefinedField = eRulePredefinedField.eFTMessageSize;
         criteria.MatchValue = "0";
         criteria.Save();

         var action = rule.Actions.Add();
         action.Type = eRuleActionType.eRAForwardEmail;
         action.To = "someone@example.test";
         action.Body = "Test";
         action.Filename = "File";
         action.FromAddress = "T";
         action.FromName = "N";
         action.HeaderName = "H";
         action.IMAPFolder = "Folder";
         action.ScriptFunction = "Script";
         action.Subject = "Subj";
         action.Value = "Value";
         action.Save();

         rule.Save();
      }

      private void DownloadFromExternalAccount(Account account, FetchAccount fa)
      {
         if (!_backupMessages)
            return;

         // Download messages from the external account.
         var messages = new List<string>();

         messages.Add("Subject: Message 1\r\n");
         messages.Add("Subject: Message 2\r\n");
         messages.Add("Subject: Message 3\r\n");

         using (var pop3Server = new Pop3ServerSimulator(1, fa.Port, messages))
         {
            pop3Server.StartListen();
            fa.DownloadNow();
            pop3Server.WaitForCompletion();
         }

         CustomAsserts.AssertRecipientsInDeliveryQueue(0);
         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 5);
      }

      private bool BackupEnvironment()
      {
         var oBackupSettings = _application.Settings.Backup;
         oBackupSettings.BackupDomains = true;
         oBackupSettings.BackupMessages = _backupMessages;
         oBackupSettings.BackupSettings = true;
         oBackupSettings.CompressDestinationFiles = true;

         oBackupSettings.Destination = _backupDir;

         _application.BackupManager.StartBackup();

         return WaitForBackupCompletion();
      }

      private void DeleteEnvironment()
      {
         while (_application.Domains.Count > 0)
            _application.Domains[0].Delete();

         var addresses = _application.Settings.AntiSpam.WhiteListAddresses;
         while (addresses.Count > 0)
            addresses[0].Delete();

         var routes = _application.Settings.Routes;
         while (routes.Count > 0)
            routes[0].Delete();

         var attachments = _application.Settings.AntiVirus.BlockedAttachments;
         while (attachments.Count > 0)
            attachments[0].Delete();

         var blackLists = _application.Settings.AntiSpam.DNSBlackLists;
         while (blackLists.Count > 0)
            blackLists[0].Delete();

         var surblServers = _application.Settings.AntiSpam.SURBLServers;
         while (surblServers.Count > 0)
            surblServers[0].Delete();

         var sslCertificates = _application.Settings.SSLCertificates;
         while (sslCertificates.Count > 0)
            sslCertificates[0].Delete();

         var incomingRelays = _application.Settings.IncomingRelays;
         while (incomingRelays.Count > 0)
            incomingRelays[0].Delete();
      }

      private void RestoreEnvironment()
      {
         var startTime = _application.Status.StartTime;

         // locate backup file
         var dirInfo = new DirectoryInfo(_backupDir);
         var files = dirInfo.GetFiles();

         var backupFile = files[0].FullName;

         var backup = _application.BackupManager.LoadBackup(backupFile);

         backup.RestoreDomains = true;
         backup.RestoreMessages = _backupMessages;
         backup.RestoreSettings = true;
         backup.StartRestore();

         WaitForRestoreCompletion(startTime);
      }

      private void ConfirmRestore()
      {
         ConfirmSettings();
         ConfirmDomainObject(_application.Domains[0]);
         ConfirmDomainObjects();
      }

      private void ConfirmSettings()
      {
         ConfirmGroupObject();
         ConfirmBlockedAttachments();
         ConfirmRoutes();
         ConfirmGreyListingWhiteList();
         ConfirmWhiteList();
         ConfirmDNSBlackLists();
         ConfirmSURBLServers();
         ConfirmSSLCertificates();
         ConfirmPublicFolders();
         ConfirmIncomingRelay();
      }

      private void ConfirmSSLCertificates()
      {
         var cert = _application.Settings.SSLCertificates[0];
         Assert.That(cert.CertificateFile, Is.EqualTo("file1.txt"));
         Assert.That(cert.Name, Is.EqualTo("name1"));
         Assert.That(cert.PrivateKeyFile, Is.EqualTo("pk1"));

         cert = _application.Settings.SSLCertificates[1];
         Assert.That(cert.CertificateFile, Is.EqualTo("file2.txt"));
         Assert.That(cert.Name, Is.EqualTo("name2"));
         Assert.That(cert.PrivateKeyFile, Is.EqualTo("pk2"));
      }

      private void ConfirmDNSBlackLists()
      {
         var black = _application.Settings.AntiSpam.DNSBlackLists.get_ItemByDNSHost("127.0.0.1");
         Assert.That(black.Active, Is.True);
         Assert.That(black.ExpectedResult, Is.EqualTo("127.5.2.1"));
         Assert.That(black.RejectMessage, Is.EqualTo("Test"));
         Assert.That(black.Score, Is.EqualTo(4));

         black = _application.Settings.AntiSpam.DNSBlackLists.get_ItemByDNSHost("127.0.0.2");
         Assert.That(black.Active, Is.True);
         Assert.That(black.ExpectedResult, Is.EqualTo("127.5.2.2"));
         Assert.That(black.RejectMessage, Is.EqualTo("Test2"));
         Assert.That(black.Score, Is.EqualTo(5));
      }

      private void ConfirmIncomingRelay()
      {
         var relays = _application.Settings.IncomingRelays;
         Assert.That(relays.Count, Is.EqualTo(1));

         var relay = relays[0];
         Assert.That(relay.Name, Is.EqualTo("Test"));
         Assert.That(relay.LowerIP, Is.EqualTo("1.2.3.4"));
         Assert.That(relay.UpperIP, Is.EqualTo("4.3.2.1"));
      }

      private void ConfirmSURBLServers()
      {
         var black = _application.Settings.AntiSpam.SURBLServers.get_ItemByDNSHost("127.0.0.1");
         Assert.That(black.Active, Is.True);
         Assert.That(black.RejectMessage, Is.EqualTo("Test"));
         Assert.That(black.Score, Is.EqualTo(4));

         black = _application.Settings.AntiSpam.SURBLServers.get_ItemByDNSHost("127.0.0.2");
         Assert.That(black.Active, Is.True);
         Assert.That(black.RejectMessage, Is.EqualTo("Test2"));
         Assert.That(black.Score, Is.EqualTo(5));
      }

      private void ConfirmBlockedAttachments()
      {
         var attachments = _application.Settings.AntiVirus.BlockedAttachments;
         Assert.That(attachments.Count, Is.GreaterThan(0));

         for (var i = 0; i < attachments.Count; i++)
         {
            var ba = attachments[i];

            if (ba.Description == "My description" && ba.Wildcard == "*.my")
               return;
         }

         Assert.Fail("Blocked attachment not found");
      }

      private void ConfirmRoutes()
      {
         var routes = _application.Settings.Routes;

         Assert.That(routes.Count, Is.EqualTo(2));

         var route = routes[0];
         Assert.That(route.DomainName, Is.EqualTo("test1.com"));
         Assert.That(route.Description, Is.EqualTo("description"));
         Assert.That(route.UseSSL, Is.EqualTo(true));
         Assert.That(route.TreatSenderAsLocalDomain, Is.EqualTo(true));
         Assert.That(route.TreatRecipientAsLocalDomain, Is.EqualTo(false));
         Assert.That(route.TreatSecurityAsLocalDomain, Is.EqualTo(false));

         route = routes[1];
         Assert.That(route.DomainName, Is.EqualTo("test2.com"));
         Assert.That(route.Description, Is.EqualTo("description2"));
         Assert.That(route.UseSSL, Is.EqualTo(false));
         Assert.That(route.TreatSenderAsLocalDomain, Is.EqualTo(false));
         Assert.That(route.TreatRecipientAsLocalDomain, Is.EqualTo(true));
         Assert.That(route.TreatSecurityAsLocalDomain, Is.EqualTo(true));

         Assert.That(route.Addresses.Count, Is.EqualTo(2));

         var addresses = new List<string>();
         addresses.Add(route.Addresses[0].Address);
         addresses.Add(route.Addresses[1].Address);

         Assert.That(addresses.Contains("address1@test2.com"), Is.True);
         Assert.That(addresses.Contains("address2@test2.com"), Is.True);
      }

      private void ConfirmGreyListingWhiteList()
      {
         var addresses = _application.Settings.AntiSpam.GreyListingWhiteAddresses;

         Assert.That(addresses.Count, Is.EqualTo(2));

         var address = addresses[0];
         Assert.That(address.Description, Is.EqualTo("helo1"));
         Assert.That(address.IPAddress, Is.EqualTo("1.1.1.1"));

         address = addresses[1];
         Assert.That(address.Description, Is.EqualTo("helo2"));
         Assert.That(address.IPAddress, Is.EqualTo("2.2.2.2"));
      }

      private void ConfirmWhiteList()
      {
         var addresses = _application.Settings.AntiSpam.WhiteListAddresses;

         Assert.That(addresses.Count, Is.EqualTo(2));

         var address = addresses[0];
         Assert.That(address.Description, Is.EqualTo("Desc1"));
         Assert.That(address.EmailAddress, Is.EqualTo("Email1"));
         Assert.That(address.LowerIPAddress, Is.EqualTo("1.1.1.1"));
         Assert.That(address.UpperIPAddress, Is.EqualTo("2.2.2.2"));

         address = addresses[1];
         Assert.That(address.Description, Is.EqualTo("Desc2"));
         Assert.That(address.EmailAddress, Is.EqualTo("Email2"));
         Assert.That(address.LowerIPAddress, Is.EqualTo("2.2.2.2"));
         Assert.That(address.UpperIPAddress, Is.EqualTo("21.21.21.21"));
      }

      private void ConfirmDomainObject(Domain domain)
      {
         Assert.That(domain.Postmaster, Is.EqualTo("someone@example.test"));
         Assert.That(domain.SignatureEnabled, Is.True);
         Assert.That(domain.AddSignaturesToLocalMail, Is.True);
         Assert.That(domain.AddSignaturesToReplies, Is.True);
         Assert.That(domain.SignaturePlainText, Is.EqualTo("PLS"));
         Assert.That(domain.SignatureHTML, Is.EqualTo("HTML"));

         Assert.That(_application.Domains[0].Name, Is.EqualTo("example.test"));
         Assert.That(_application.Domains[0].DomainAliases.Count, Is.EqualTo(2));
         Assert.That(_application.Domains[0].DomainAliases[0].AliasName, Is.EqualTo("test1.com"));
         Assert.That(_application.Domains[0].DomainAliases[1].AliasName, Is.EqualTo("test2.com"));
      }

      private void ConfirmDomainObjects()
      {
         ConfirmAccountObject();
         ConfirmAliasObject();
         ConfirmDistributionListObject();
      }

      private void ConfirmDistributionListObject()
      {
         var list = _application.Domains[0].DistributionLists[0];
         Assert.That(list.Address, Is.EqualTo("list@example.test"));
         Assert.That(list.Recipients.Count, Is.EqualTo(3));
         Assert.That(list.Recipients[0].RecipientAddress, Is.EqualTo("member1@example.test"));
         Assert.That(list.Recipients[1].RecipientAddress, Is.EqualTo("member2@example.test"));
         Assert.That(list.Recipients[2].RecipientAddress, Is.EqualTo("member3@example.test"));
      }

      private void ConfirmAliasObject()
      {
         var alias = _application.Domains[0].Aliases[0];
         Assert.That(alias.Name, Is.EqualTo("alias@example.test"));
         Assert.That(alias.Value, Is.EqualTo("someone@example.test"));
      }

      private void ConfirmAccountObject()
      {
         var account = _application.Domains[0].Accounts.get_ItemByAddress("test@example.test");


         Assert.That(account.Active, Is.True);
         Assert.That(account.ADDomain, Is.EqualTo("AD"));
         Assert.That(account.AdminLevel, Is.EqualTo(eAdminLevel.hAdminLevelDomainAdmin));
         Assert.That(account.ADUsername, Is.EqualTo("AU"));
         Assert.That(account.ForwardAddress, Is.EqualTo("FA"));
         Assert.That(account.ForwardEnabled, Is.False);
         Assert.That(account.ForwardKeepOriginal, Is.True);
         Assert.That(account.IsAD, Is.False);
         Assert.That(account.MaxSize, Is.EqualTo(1250));
         Assert.That(account.PersonFirstName, Is.EqualTo("First"));
         Assert.That(account.PersonLastName, Is.EqualTo("Last"));
         Assert.That(account.SignatureEnabled, Is.True);
         Assert.That(account.SignatureHTML, Is.EqualTo("HTML"));
         Assert.That(account.SignaturePlainText, Is.EqualTo("PLAIN"));
         Assert.That(account.VacationMessage, Is.EqualTo("VAC"));
         Assert.That(account.VacationMessageExpires, Is.True);
         Assert.That(account.VacationMessageExpiresDate.Substring(0, 10), Is.EqualTo("2040-01-01"));
         Assert.That(account.VacationMessageIsOn, Is.True);
         Assert.That(account.VacationSubject, Is.EqualTo("SUBJ"));

         // Confirm fetch account
         var fa = account.FetchAccounts.get_Item(0);
         Assert.That(fa.DaysToKeepMessages, Is.EqualTo(5));
         Assert.That(fa.Enabled, Is.True);
         Assert.That(fa.MinutesBetweenFetch, Is.EqualTo(10));
         Assert.That(fa.Name, Is.EqualTo("test"));
         Assert.That(fa.Port, Is.EqualTo(_fetchAccountPort));
         Assert.That(fa.ProcessMIMEDate, Is.EqualTo(true));
         Assert.That(fa.ProcessMIMERecipients, Is.True);
         Assert.That(fa.Username, Is.EqualTo("test"));
         Assert.That(fa.UseSSL, Is.False);
         Assert.That(fa.ServerAddress, Is.EqualTo("127.0.0.1"));
         Assert.That(fa.UseAntiSpam, Is.True);
         Assert.That(fa.UseAntiVirus, Is.True);

         // Make sur no additional mail is downloaded. We have already downloaded it.
         DownloadFromExternalAccount(account, fa);

         var rule = account.Rules[0];
         Assert.That(rule.Name, Is.EqualTo("MyRule"));

         var criteria = rule.Criterias[0];
         Assert.That(criteria.MatchType, Is.EqualTo(eRuleMatchType.eMTGreaterThan));
         Assert.That(criteria.PredefinedField, Is.EqualTo(eRulePredefinedField.eFTMessageSize));
         Assert.That(criteria.MatchValue, Is.EqualTo("0"));

         var action = rule.Actions[0];
         Assert.That(action.Type, Is.EqualTo(eRuleActionType.eRAForwardEmail));
         Assert.That(action.To, Is.EqualTo("someone@example.test"));
         Assert.That(action.Body, Is.EqualTo("Test"));
         Assert.That(action.Filename, Is.EqualTo("File"));
         Assert.That(action.FromAddress, Is.EqualTo("T"));
         Assert.That(action.FromName, Is.EqualTo("N"));
         Assert.That(action.HeaderName, Is.EqualTo("H"));
         Assert.That(action.IMAPFolder, Is.EqualTo("Folder"));
         Assert.That(action.ScriptFunction, Is.EqualTo("Script"));
         Assert.That(action.Subject, Is.EqualTo("Subj"));
         Assert.That(action.Value, Is.EqualTo("Value"));

         var inbox = account.IMAPFolders.get_ItemByName("INBOX");

         var messages = inbox.Messages;

         if (_backupMessages)
         {
            Assert.That(inbox.CurrentUID, Is.EqualTo(6));
            Assert.That(inbox.CreationTime, Is.EqualTo(_folderCreationTime));

            Assert.That(messages.Count, Is.EqualTo(5));
            Assert.That(messages[0].Subject, Is.EqualTo("Message 1 Subject"));
            Assert.That(messages[1].Body.Contains("Message 3 Body"), Is.True);

            Assert.That(messages[0].UID, Is.EqualTo(1));
            Assert.That(messages[1].UID, Is.EqualTo(3));
         }
         else
         {
            Assert.That(messages.Count, Is.EqualTo(0));
         }
      }

      private void ConfirmGroupObject()
      {
         var group = _application.Settings.Groups.get_ItemByName("TestGroup");

         Assert.That(group.Name, Is.EqualTo("TestGroup"));
         Assert.That(group.Members.Count, Is.EqualTo(3));

         Assert.That(group.Members[0].Account.Address, Is.EqualTo("gm1@example.test"));
         Assert.That(group.Members[1].Account.Address, Is.EqualTo("gm2@example.test"));
         Assert.That(group.Members[2].Account.Address, Is.EqualTo("gm3@example.test"));
      }

      [Test]
      public void TestWithMessages()
      {
         _backupMessages = true;
         Assert.That(Execute(), Is.True);
      }

      [Test]
      public void TestWithoutMessages()
      {
         _backupMessages = false;
         Assert.That(Execute(), Is.True);
      }

      [Test]
      [Description("Issue #163. A message file deleted while the backup copies the data directory made " +
                   "the backup task throw. The backup never finished and the backup manager stayed " +
                   "running, so no later backup could start.")]
      public void TestBackupWhenFilesAreDeletedDuringBackup()
      {
         _backupMessages = true;

         LogHandler.DeleteErrorLog();

         // The directory listing is fetched in batches, so a file that is deleted after its batch
         // was read is still handed to the copy. Deleting from the end while the backup copies from
         // the start guarantees that the two meet inside a batch.
         var sourceDir = Path.Combine(_application.Settings.Directories.DataDirectory,
            "BackupTest-" + TestSetup.UniqueString());
         Directory.CreateDirectory(sourceDir);

         var sourceFiles = new List<string>();
         for (var i = 0; i < 2000; i++)
         {
            var file = Path.Combine(sourceDir, i.ToString("D5") + ".eml");
            File.WriteAllText(file, "Test");
            sourceFiles.Add(file);
         }

         var copiedDir = Path.Combine(_backupDir, "DataBackup", Path.GetFileName(sourceDir));

         try
         {
            var backupSettings = _application.Settings.Backup;
            backupSettings.BackupDomains = true;
            backupSettings.BackupMessages = true;
            backupSettings.BackupSettings = false;
            backupSettings.CompressDestinationFiles = false;
            backupSettings.Destination = _backupDir;

            CustomAsserts.AssertDeleteFile(backupSettings.LogFile);

            _application.BackupManager.StartBackup();

            var waitUntil = DateTime.Now.AddSeconds(30);
            while (!Directory.Exists(copiedDir) || Directory.GetFiles(copiedDir).Length == 0)
            {
               Assert.That(DateTime.Now, Is.LessThan(waitUntil), "The backup has not started copying files.");
               Thread.Sleep(1);
            }

            for (var i = sourceFiles.Count - 1; i >= 0; i--)
            {
               try
               {
                  File.Delete(sourceFiles[i]);
               }
               catch (IOException)
               {
                  // The backup is copying this file right now.
               }
            }

            Assert.That(WaitForBackupCompletion(),
               Is.True,
               TestSetup.ReadExistingTextFile(backupSettings.LogFile));

            CustomAsserts.AssertNoReportedError();

            // The backup manager must be free for a new backup.
            CustomAsserts.AssertDeleteFile(backupSettings.LogFile);
            Assert.That(BackupEnvironment(), Is.True);
         }
         finally
         {
            Directory.Delete(sourceDir, true);
         }
      }

      [Test]
      [Description("Issue #163. When the backup task threw, the backup manager was never told that the " +
                   "task had stopped. Every later backup was refused as already started.")]
      public void TestBackupCanBeStartedAfterBackupTaskFails()
      {
         _backupMessages = true;

         LogHandler.DeleteErrorLog();

         // A directory the server cannot list makes the data directory copy throw.
         var sourceDir = Path.Combine(_application.Settings.Directories.DataDirectory,
            "BackupTest-" + TestSetup.UniqueString());
         var lockedDir = Path.Combine(sourceDir, "Locked");
         Directory.CreateDirectory(lockedDir);

         var denyEveryone = new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.FullControl, AccessControlType.Deny);
         var lockedDirInfo = new DirectoryInfo(lockedDir);
         DenyAccess(lockedDirInfo, denyEveryone);

         try
         {
            var backupSettings = _application.Settings.Backup;
            backupSettings.BackupDomains = true;
            backupSettings.BackupMessages = true;
            backupSettings.BackupSettings = false;
            backupSettings.CompressDestinationFiles = false;
            backupSettings.Destination = _backupDir;

            CustomAsserts.AssertDeleteFile(backupSettings.LogFile);

            _application.BackupManager.StartBackup();

            Assert.That(WaitForBackupCompletion(), Is.False);

            var backupLog = TestSetup.ReadExistingTextFile(backupSettings.LogFile);
            Assert.That(backupLog.Contains("BACKUP ERROR:"), Is.True, backupLog);

            LogHandler.DeleteErrorLog();

            AllowAccess(lockedDirInfo, denyEveryone);
            Directory.Delete(sourceDir, true);

            // The failed backup must not block the next one.
            CustomAsserts.AssertDeleteFile(backupSettings.LogFile);
            Assert.That(BackupEnvironment(), Is.True, TestSetup.ReadExistingTextFile(backupSettings.LogFile));
            CustomAsserts.AssertNoReportedError();
         }
         finally
         {
            if (Directory.Exists(sourceDir))
            {
               AllowAccess(lockedDirInfo, denyEveryone);
               Directory.Delete(sourceDir, true);
            }
         }
      }

      [Test]
      [Description("Issue #234. An uncompressed backup leaves the DataBackup directory in the destination. " +
                   "The next backup to the same destination failed on the first file that already existed.")]
      public void TestUncompressedBackupTwiceToSameDirectory()
      {
         _backupMessages = true;

         LogHandler.DeleteErrorLog();

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_application.Domains[0], "test@example.test", "test");
         SmtpClientSimulator.StaticSend(account.Address, account.Address, "Message 1 Subject", "Message 1 Body");
         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         var backupSettings = _application.Settings.Backup;
         backupSettings.BackupDomains = true;
         backupSettings.BackupMessages = true;
         backupSettings.BackupSettings = false;
         backupSettings.CompressDestinationFiles = false;
         backupSettings.Destination = _backupDir;

         for (var run = 1; run <= 2; run++)
         {
            CustomAsserts.AssertDeleteFile(backupSettings.LogFile);

            _application.BackupManager.StartBackup();

            Assert.That(WaitForBackupCompletion(),
               Is.True,
               "Backup run " + run + ": " + TestSetup.ReadExistingTextFile(backupSettings.LogFile));
         }

         Assert.That(Directory.Exists(Path.Combine(_backupDir, "DataBackup", "example.test")), Is.True);

         CustomAsserts.AssertNoReportedError();
      }

      [Test]
      public void TestBackupToInaccessibleDirectory()
      {
         _backupMessages = false;

         LogHandler.DeleteErrorLog();

         // The server checks that the backup directory exists by opening it, and falls back to
         // reading its attributes - which the parent directory answers. Both must be denied for
         // the directory to be unreachable, which is how a directory below another user's
         // profile behaves.
         var inaccessibleParent = Path.Combine(TestSetup.GetSharedTempDirectory(), TestSetup.UniqueString());
         var inaccessibleBackupDir = Path.Combine(inaccessibleParent, "Backup");
         Directory.CreateDirectory(inaccessibleBackupDir);

         var denyEveryone = new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.FullControl, AccessControlType.Deny);

         var parentInfo = new DirectoryInfo(inaccessibleParent);
         var backupDirInfo = new DirectoryInfo(inaccessibleBackupDir);

         DenyAccess(backupDirInfo, denyEveryone);
         DenyAccess(parentInfo, denyEveryone);

         try
         {
            // The owner of a directory keeps the right to change its permissions, so the deny
            // rules don't take effect everywhere - Windows Vista lets the owner read the
            // directory anyway. There is nothing to test when the server can reach it.
            if (!IsInaccessible(inaccessibleParent, inaccessibleBackupDir))
               Assert.Ignore("The backup directory could not be made inaccessible on this version of Windows.");

            var backupSettings = _application.Settings.Backup;
            backupSettings.BackupDomains = true;
            backupSettings.BackupMessages = _backupMessages;
            backupSettings.BackupSettings = true;
            backupSettings.Destination = inaccessibleBackupDir;

            CustomAsserts.AssertDeleteFile(backupSettings.LogFile);

            _application.BackupManager.StartBackup();

            Assert.That(WaitForBackupCompletion(), Is.False);

            var backupLog = TestSetup.ReadExistingTextFile(backupSettings.LogFile);
            Assert.That(backupLog.Contains("BACKUP ERROR: The specified backup directory is not accessible"),
               Is.True,
               backupLog);

            CustomAsserts.AssertReportedError("HM5014", "The specified backup directory is not accessible");

            // The failed backup must not leave the backup manager running, so a backup to a
            // directory the server can reach still works.
            CustomAsserts.AssertDeleteFile(backupSettings.LogFile);
            Assert.That(BackupEnvironment(), Is.True);
         }
         finally
         {
            AllowAccess(parentInfo, denyEveryone);
            AllowAccess(backupDirInfo, denyEveryone);

            Directory.Delete(inaccessibleParent, true);
         }
      }

      [Test]
      [Description("The backup log reported a failed backup before the backup manager was told that " +
                   "the backup had stopped. A backup started at that point was refused as already started.")]
      public void TestBackupCanBeStartedWhenFailureIsLogged()
      {
         _backupMessages = false;

         LogHandler.DeleteErrorLog();
         EnableSlowBackupFailedEvent();

         var backupSettings = _application.Settings.Backup;
         backupSettings.Destination = Path.Combine(_backupDir, "Missing");

         CustomAsserts.AssertDeleteFile(backupSettings.LogFile);

         _application.BackupManager.StartBackup();
         Assert.That(WaitForBackupCompletion(), Is.False);

         CustomAsserts.AssertReportedError("HM5014", "The specified backup directory is not accessible");

         CustomAsserts.AssertDeleteFile(backupSettings.LogFile);
         Assert.That(BackupEnvironment(), Is.True, TestSetup.ReadExistingTextFile(backupSettings.LogFile));
      }

      /// <summary>
      /// Makes the OnBackupFailed event take a few seconds, so the backup is still finishing when
      /// the backup log says that it failed.
      /// </summary>
      private void EnableSlowBackupFailedEvent()
      {
         var script =
            "Sub OnBackupFailed(reason)\r\n" +
            "   Dim start : start = Timer\r\n" +
            "   Do While Timer < start + 3 : Loop\r\n" +
            "End Sub\r\n";

         var scripting = _application.Settings.Scripting;
         File.WriteAllText(scripting.CurrentScriptFile, script);
         scripting.Enabled = true;
         scripting.Reload();
      }

      /// <summary>
      /// Tells whether Windows denies access to the directory, which is what the server
      /// is expected to run into.
      /// </summary>
      private static bool IsInaccessible(string parent, string backupDirectory)
      {
         try
         {
            Directory.GetFiles(parent);
            return false;
         }
         catch (UnauthorizedAccessException)
         {
         }

         try
         {
            File.GetAttributes(backupDirectory);
            return false;
         }
         catch (UnauthorizedAccessException)
         {
         }

         return true;
      }

      private static void DenyAccess(DirectoryInfo directory, FileSystemAccessRule rule)
      {
         var security = directory.GetAccessControl();
         security.AddAccessRule(rule);
         directory.SetAccessControl(security);
      }

      private static void AllowAccess(DirectoryInfo directory, FileSystemAccessRule rule)
      {
         var security = directory.GetAccessControl();
         security.RemoveAccessRule(rule);
         directory.SetAccessControl(security);
      }
   }
}
