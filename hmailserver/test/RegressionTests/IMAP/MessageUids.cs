// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

using System;
using System.IO;
using hMailServer;
using NUnit.Framework;
using RegressionTests.Infrastructure;
using RegressionTests.Shared;

namespace RegressionTests.IMAP
{
   [TestFixture]
   public class MessageUids : TestFixtureBase
   {
      private void CreateMessageModificationRule(hMailServer.Rules ruleContainer)
      {
         var rule = ruleContainer.Add();
         rule.Name = "Criteria test";
         rule.Active = true;

         var ruleCriteria = rule.Criterias.Add();
         ruleCriteria.UsePredefined = true;
         ruleCriteria.PredefinedField = eRulePredefinedField.eFTMessageSize;
         ruleCriteria.MatchType = eRuleMatchType.eMTGreaterThan;
         ruleCriteria.MatchValue = "0";
         ruleCriteria.Save();

         var ruleAction = rule.Actions.Add();
         ruleAction.Type = eRuleActionType.eRARunScriptFunction;
         ruleAction.ScriptFunction = "ModifyMessage";
         ruleAction.Save();

         rule.Save();

         File.WriteAllText(_settings.Scripting.CurrentScriptFile,
            "Sub ModifyMessage(message)" + Environment.NewLine +
            "message.Subject = \"[Spam] \" + CStr(message.Subject)" + Environment.NewLine +
            "message.Save" + Environment.NewLine +
            "End Sub");

         _settings.Scripting.Reload();
      }

      private void CreateMoveRule(hMailServer.Rules ruleContainer, string foldername)
      {
         var rule = ruleContainer.Add();
         rule.Name = "Criteria test";
         rule.Active = true;

         var ruleCriteria = rule.Criterias.Add();
         ruleCriteria.UsePredefined = true;
         ruleCriteria.PredefinedField = eRulePredefinedField.eFTMessageSize;
         ruleCriteria.MatchType = eRuleMatchType.eMTGreaterThan;
         ruleCriteria.MatchValue = "0";
         ruleCriteria.Save();

         var ruleAction = rule.Actions.Add();
         ruleAction.Type = eRuleActionType.eRAMoveToImapFolder;
         ruleAction.IMAPFolder = foldername;
         ruleAction.Save();

         rule.Save();
      }

      [Test]
      [Description("Confirm that new messages receive new UIDs")]
      public void TestBasicIncrements()
      {
         var testAccount = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "Test'Account@example.test",
            "test");

         SmtpClientSimulator.StaticSend(testAccount.Address, testAccount.Address, "Test", "Test");
         Pop3ClientSimulator.AssertMessageCount(testAccount.Address, "test", 1);

         var inboxFolder = testAccount.IMAPFolders[0];

         Assert.That(inboxFolder.CurrentUID, Is.EqualTo(1));
         Assert.That(inboxFolder.Messages[0].UID, Is.EqualTo(1));

         SmtpClientSimulator.StaticSend(testAccount.Address, testAccount.Address, "Test", "Test");
         Pop3ClientSimulator.AssertMessageCount(testAccount.Address, "test", 2);

         Assert.That(inboxFolder.CurrentUID, Is.EqualTo(2));
         Assert.That(inboxFolder.Messages[1].UID, Is.EqualTo(2));

         SmtpClientSimulator.StaticSend(testAccount.Address, testAccount.Address, "Test", "Test");
         Pop3ClientSimulator.AssertMessageCount(testAccount.Address, "test", 3);

         Assert.That(inboxFolder.CurrentUID, Is.EqualTo(3));
         Assert.That(inboxFolder.Messages[2].UID, Is.EqualTo(3));
      }

      [Test]
      [Description("Confirm that deletion of messages does not effect UID sequence")]
      public void TestBasicIncrementsWithDeletion()
      {
         var testAccount = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "Test'Account@example.test",
            "test");

         SmtpClientSimulator.StaticSend(testAccount.Address, testAccount.Address, "Test", "Test");
         Pop3ClientSimulator.AssertMessageCount(testAccount.Address, "test", 1);

         var inboxFolder = testAccount.IMAPFolders[0];

         Assert.That(inboxFolder.CurrentUID, Is.EqualTo(1));
         Assert.That(inboxFolder.Messages[0].UID, Is.EqualTo(1));
         Pop3ClientSimulator.AssertGetFirstMessageText(testAccount.Address, "test");

         SmtpClientSimulator.StaticSend(testAccount.Address, testAccount.Address, "Test", "Test");
         Pop3ClientSimulator.AssertMessageCount(testAccount.Address, "test", 1);

         Assert.That(inboxFolder.CurrentUID, Is.EqualTo(2));
         Assert.That(inboxFolder.Messages[0].UID, Is.EqualTo(2));
         Pop3ClientSimulator.AssertGetFirstMessageText(testAccount.Address, "test");


         SmtpClientSimulator.StaticSend(testAccount.Address, testAccount.Address, "Test", "Test");
         Pop3ClientSimulator.AssertMessageCount(testAccount.Address, "test", 1);

         Assert.That(inboxFolder.CurrentUID, Is.EqualTo(3));
         Assert.That(inboxFolder.Messages[0].UID, Is.EqualTo(3));
      }

      [Test]
      [Description("Confirm that moving a message to a new folder generates an UID specific to that folder.")]
      public void TestMoveMessageWithAccountLevelRule()
      {
         var testAccount = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "Test'Account@example.test",
            "test");

         // First deliver two messages to the inbox.
         SmtpClientSimulator.StaticSend(testAccount.Address, testAccount.Address, "Test", "Test");
         Pop3ClientSimulator.AssertMessageCount(testAccount.Address, "test", 1);
         var inboxFolder = testAccount.IMAPFolders[0];
         Assert.That(inboxFolder.CurrentUID, Is.EqualTo(1));
         Assert.That(inboxFolder.Messages[0].UID, Is.EqualTo(1));

         SmtpClientSimulator.StaticSend(testAccount.Address, testAccount.Address, "Test", "Test");
         Pop3ClientSimulator.AssertMessageCount(testAccount.Address, "test", 2);
         Assert.That(inboxFolder.CurrentUID, Is.EqualTo(2));
         Assert.That(inboxFolder.Messages[1].UID, Is.EqualTo(2));

         CreateMoveRule(testAccount.Rules, "TestFolder");

         // This message will be moved into the test folder.
         SmtpClientSimulator.StaticSend(testAccount.Address, testAccount.Address, "Test", "Test");

         // Wait for the message to arrive.
         CustomAsserts.AssertFolderExists(testAccount.IMAPFolders, "TestFolder");

         var testFolder = testAccount.IMAPFolders.get_ItemByName("TestFolder");

         // Since the message is placed in a new folder, it should receive a unique UID.
         CustomAsserts.AssertFolderMessageCount(testFolder, 1);
         Assert.That(testFolder.CurrentUID, Is.EqualTo(1));
         Assert.That(testFolder.Messages[0].UID, Is.EqualTo(1));
      }

      [Test]
      [Description("Confirm that moving a message to a new folder generates an UID specific to that folder.")]
      public void TestMoveMessageWithGlobalRule()
      {
         var testAccount = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "Test'Account@example.test",
            "test");

         // First deliver two messages to the inbox.
         SmtpClientSimulator.StaticSend(testAccount.Address, testAccount.Address, "Test", "Test");
         Pop3ClientSimulator.AssertMessageCount(testAccount.Address, "test", 1);
         var inboxFolder = testAccount.IMAPFolders[0];
         Assert.That(inboxFolder.CurrentUID, Is.EqualTo(1));
         Assert.That(inboxFolder.Messages[0].UID, Is.EqualTo(1));

         SmtpClientSimulator.StaticSend(testAccount.Address, testAccount.Address, "Test", "Test");
         Pop3ClientSimulator.AssertMessageCount(testAccount.Address, "test", 2);
         Assert.That(inboxFolder.CurrentUID, Is.EqualTo(2));
         Assert.That(inboxFolder.Messages[1].UID, Is.EqualTo(2));

         CreateMoveRule(_application.Rules, "TestFolder");

         // This message will be moved into the test folder.
         SmtpClientSimulator.StaticSend(testAccount.Address, testAccount.Address, "Test", "Test");

         // Wait for the message to arrive.
         CustomAsserts.AssertFolderExists(testAccount.IMAPFolders, "TestFolder");

         var testFolder = testAccount.IMAPFolders.get_ItemByName("TestFolder");
         CustomAsserts.AssertFolderMessageCount(testFolder, 1);

         // Since the message is placed in a new folder, it should receive a unique UID.
         Assert.That(testFolder.Messages.Count, Is.EqualTo(1));
         Assert.That(testFolder.CurrentUID, Is.EqualTo(1));
         Assert.That(testFolder.Messages[0].UID, Is.EqualTo(1));
      }

      [Test]
      [Description("Issue 267, Invalid message UID generated. " +
                   " Confirm that moving a message to a new folder generates an UID specific to that folder, even if the message is saved using an account rule."
      )]
      public void TestSaveMessageWithScriptAndMoveMessageWithAccountRule()
      {
         _settings.Scripting.Enabled = true;
         _settings.Scripting.Reload();

         var testAccount = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "Test'Account@example.test",
            "test");

         var sim = new ImapClientSimulator();
         Assert.That(sim.ConnectAndLogon(testAccount.Address, "test"), Is.True);

         // First deliver two messages to the inbox.
         SmtpClientSimulator.StaticSend(testAccount.Address, testAccount.Address, "Test", "Test");
         Pop3ClientSimulator.AssertMessageCount(testAccount.Address, "test", 1);
         var inboxFolder = testAccount.IMAPFolders[0];
         Assert.That(inboxFolder.CurrentUID, Is.EqualTo(1));
         Assert.That(inboxFolder.Messages[0].UID, Is.EqualTo(1));
         Assert.That(sim.Status("INBOX", "UIDNEXT").Contains("UIDNEXT 2"), Is.True);

         SmtpClientSimulator.StaticSend(testAccount.Address, testAccount.Address, "Test", "Test");
         Pop3ClientSimulator.AssertMessageCount(testAccount.Address, "test", 2);
         Assert.That(sim.Status("INBOX", "UIDNEXT").Contains("UIDNEXT 3"), Is.True);
         Assert.That(inboxFolder.CurrentUID, Is.EqualTo(2));
         Assert.That(inboxFolder.Messages[1].UID, Is.EqualTo(2));

         CreateMessageModificationRule(testAccount.Rules);
         CreateMoveRule(testAccount.Rules, "TestFolder");

         // This message will be moved into the test folder.
         SmtpClientSimulator.StaticSend(testAccount.Address, testAccount.Address, "Test", "Test");

         // Wait for the message to arrive.
         CustomAsserts.AssertFolderExists(testAccount.IMAPFolders, "TestFolder");

         var testFolder = testAccount.IMAPFolders.get_ItemByName("TestFolder");
         CustomAsserts.AssertFolderMessageCount(testFolder, 1);

         // The UID for the inbox should be the same as before.
         Assert.That(sim.Status("INBOX", "UIDNEXT").Contains("UIDNEXT 3"), Is.True);

         // Since the message is placed in a new folder, it should receive a unique UID.
         Assert.That(sim.Status("TestFolder", "UIDNEXT").Contains("UIDNEXT 2"), Is.True);
         Assert.That(testFolder.Messages.Count, Is.EqualTo(1));
         Assert.That(testFolder.CurrentUID, Is.EqualTo(1));
         Assert.That(testFolder.Messages[0].UID, Is.EqualTo(1));
      }

      [Test]
      [Description("Issue 267, Invalid message UID generated. " +
                   " Confirm that moving a message to a new folder generates an UID specific to that folder, even if the message is saved using an account rule."
      )]
      public void TestSaveMessageWithScriptAndMoveMessageWithGlobalRule()
      {
         _settings.Scripting.Enabled = true;

         var testAccount = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "Test'Account@example.test",
            "test");

         var sim = new ImapClientSimulator();
         Assert.That(sim.ConnectAndLogon(testAccount.Address, "test"), Is.True);

         // First deliver two messages to the inbox.
         SmtpClientSimulator.StaticSend(testAccount.Address, testAccount.Address, "Test", "Test");
         Pop3ClientSimulator.AssertMessageCount(testAccount.Address, "test", 1);
         var inboxFolder = testAccount.IMAPFolders[0];
         Assert.That(inboxFolder.CurrentUID, Is.EqualTo(1));
         Assert.That(inboxFolder.Messages[0].UID, Is.EqualTo(1));
         Assert.That(sim.Status("INBOX", "UIDNEXT").Contains("UIDNEXT 2"), Is.True);

         SmtpClientSimulator.StaticSend(testAccount.Address, testAccount.Address, "Test", "Test");
         Pop3ClientSimulator.AssertMessageCount(testAccount.Address, "test", 2);
         Assert.That(inboxFolder.CurrentUID, Is.EqualTo(2));
         Assert.That(inboxFolder.Messages[1].UID, Is.EqualTo(2));
         Assert.That(sim.Status("INBOX", "UIDNEXT").Contains("UIDNEXT 3"), Is.True);

         CreateMessageModificationRule(_application.Rules);
         CreateMoveRule(_application.Rules, "TestFolder");


         // This message will be moved into the test folder.
         SmtpClientSimulator.StaticSend(testAccount.Address, testAccount.Address, "Test", "Test");

         // Wait for the message to arrive.
         CustomAsserts.AssertFolderExists(testAccount.IMAPFolders, "TestFolder");
         var testFolder = testAccount.IMAPFolders.get_ItemByName("TestFolder");
         CustomAsserts.AssertFolderMessageCount(testFolder, 1);

         // Inbox UID should not have changed since nothing has been added to the inbox.
         Assert.That(sim.Status("INBOX", "UIDNEXT").Contains("UIDNEXT 3"), Is.True);

         // Since the message is placed in a new folder, it should receive a unique UID.
         var status = sim.Status("TestFolder", "UIDNEXT");
         Assert.That(status.Contains("UIDNEXT 2"), Is.True, status);
         Assert.That(testFolder.CurrentUID, Is.EqualTo(1));
         Assert.That(testFolder.Messages[0].UID, Is.EqualTo(1));
      }
   }
}