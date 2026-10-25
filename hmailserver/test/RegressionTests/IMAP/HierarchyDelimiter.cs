// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

using System.Runtime.InteropServices;
using NUnit.Framework;
using RegressionTests.Shared;

namespace RegressionTests.IMAP
{
   [TestFixture]
   public class HierarchyDelimiter : TestFixtureBase
   {
      [Test]
      [Description("Test that the hierchary delimiter has effect on the Create command.")]
      public void TestHierarchyDelimiterCreate()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();
         var settings = _settings;
         settings.IMAPHierarchyDelimiter = "/";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "quote@example.test", "test");

         var folderName = "Test.Test";

         var simulator = new ImapClientSimulator();
         var sWelcomeMessage = simulator.Connect();
         simulator.Logon(account.Address, "test");
         Assert.That(simulator.CreateFolder(folderName), Is.True);
         var listResponse = simulator.List();
         Assert.That(listResponse.Contains("\"" + folderName + "\""), Is.True);
         Assert.That(!listResponse.Contains("\"Test\""), Is.True);
         simulator.Disconnect();
      }

      [Test]
      [Description("Test that the hierchary delimiter has effect on the Delete command.")]
      public void TestHierarchyDelimiterDelete()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();
         var settings = _settings;
         settings.IMAPHierarchyDelimiter = "\\";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "quote@example.test", "test");

         var folderName = "Test\\Test";

         var simulator = new ImapClientSimulator();
         var sWelcomeMessage = simulator.Connect();
         simulator.Logon(account.Address, "test");
         Assert.That(simulator.CreateFolder(folderName), Is.True);
         var listResponse = simulator.List();
         Assert.That(listResponse.Contains("\"Test\\Test\""), Is.True);
         Assert.That(listResponse.Contains("\"Test\""), Is.True);
         Assert.That(simulator.DeleteFolder("Test\\Test"), Is.True);
         listResponse = simulator.List();
         Assert.That(listResponse.Contains("Test\\Test"), Is.False);
         Assert.That(listResponse.Contains("Test"), Is.True);
         simulator.Disconnect();
      }

      [Test]
      [Description("Test that the hierchary delimiter has effect on the List response.")]
      public void TestHierarchyDelimiterListResponse()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();
         var settings = _settings;
         settings.IMAPHierarchyDelimiter = "\\";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "quote@example.test", "test");

         var folderName = "Test\\Test";

         var simulator = new ImapClientSimulator();
         var sWelcomeMessage = simulator.Connect();
         simulator.Logon(account.Address, "test");
         Assert.That(simulator.CreateFolder(folderName), Is.True);
         var listResponse = simulator.List();
         Assert.That(listResponse.Contains("\"Test\\Test\""), Is.True);
         Assert.That(listResponse.Contains("\"Test\""), Is.True);
         simulator.Disconnect();
      }

      [Test]
      [Description("Test that the hierchary delimiter has effect on the LSUB response.")]
      public void TestHierarchyDelimiterLsubResponse()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();
         var settings = _settings;
         settings.IMAPHierarchyDelimiter = "/";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "quote@example.test", "test");

         var folderName = "Test/Test";

         var simulator = new ImapClientSimulator();
         var sWelcomeMessage = simulator.Connect();
         simulator.Logon(account.Address, "test");
         Assert.That(simulator.CreateFolder(folderName), Is.True);
         Assert.That(simulator.Subscribe("Test"), Is.True);
         Assert.That(simulator.Subscribe("Test/Test"), Is.True);
         var lsubResponse = simulator.LSUB();
         Assert.That(lsubResponse.Contains("\"Test/Test\""), Is.True);
         Assert.That(lsubResponse.Contains("\"Test\""), Is.True);
         simulator.Disconnect();
      }

      [Test]
      [Description("Test that the hierchary delimiter has effect on the Namespace response.")]
      public void TestHierarchyDelimiterNamespaceResponse()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();
         var settings = _settings;
         settings.IMAPHierarchyDelimiter = "\\";
         var publicFolderName = _settings.IMAPPublicFolderName;

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "quote@example.test", "test");

         var simulator = new ImapClientSimulator();
         simulator.ConnectAndLogon(account.Address, "test");
         var result = simulator.Send("A01 NAMESPACE");
         var correctNamespaceSetting = "* NAMESPACE ((\"\" \"\\\\\")) NIL ((\"" + publicFolderName +
                                       "\" \"\\\\\"))";
         Assert.That(result.Contains(correctNamespaceSetting), Is.True, result);
         simulator.Disconnect();

         settings.IMAPHierarchyDelimiter = ".";

         simulator = new ImapClientSimulator();
         simulator.ConnectAndLogon(account.Address, "test");

         result = simulator.Send("A01 NAMESPACE");
         correctNamespaceSetting = "* NAMESPACE ((\"\" \".\")) NIL ((\"" + publicFolderName + "\" \".\"))";
         Assert.That(result.Contains(correctNamespaceSetting), Is.True, result);
         simulator.Disconnect();

         settings.IMAPHierarchyDelimiter = "/";

         simulator = new ImapClientSimulator();
         simulator.ConnectAndLogon(account.Address, "test");

         result = simulator.Send("A01 NAMESPACE");
         correctNamespaceSetting = "* NAMESPACE ((\"\" \"/\")) NIL ((\"" + publicFolderName + "\" \"/\"))";
         Assert.That(result.Contains(correctNamespaceSetting), Is.True, result);
         simulator.Disconnect();
      }

      [Test]
      [Description("Test that the hierchary delimiter has effect on the Rename command.")]
      public void TestHierarchyDelimiterRename()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();
         var settings = _settings;
         settings.IMAPHierarchyDelimiter = "/";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "quote@example.test", "test");

         var folderName = "Test/Test";
         var newFolderName = "Apa/Test";

         var simulator = new ImapClientSimulator();
         var sWelcomeMessage = simulator.Connect();
         simulator.Logon(account.Address, "test");
         Assert.That(simulator.CreateFolder(folderName), Is.True);
         var listResponse = simulator.List();
         Assert.That(listResponse.Contains("\"" + folderName + "\""), Is.True);
         Assert.That(listResponse.Contains("\"Test\""), Is.True);
         Assert.That(simulator.RenameFolder("Test", "Apa"), Is.True);
         listResponse = simulator.List();
         Assert.That(listResponse.Contains("\"" + newFolderName + "\""), Is.True);
         Assert.That(listResponse.Contains("\"Apa\""), Is.True);
         simulator.Disconnect();
      }

      [Test]
      [Description("Test that the hierchary delimiter has effect on the LSUB response.")]
      public void TestHierarchyDelimiterSlash()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();
         var settings = _settings;
         settings.IMAPHierarchyDelimiter = "\\";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "quote@example.test", "test");

         var folderName = "Test\\Test";

         var simulator = new ImapClientSimulator();
         var sWelcomeMessage = simulator.Connect();
         simulator.Logon(account.Address, "test");
         Assert.That(simulator.CreateFolder(folderName), Is.True);
         Assert.That(simulator.Subscribe("Test"), Is.True);
         Assert.That(simulator.Subscribe("Test\\Test"), Is.True);
         var lsubResponse = simulator.LSUB();
         Assert.That(lsubResponse.Contains("\"Test\\Test\""), Is.True);
         Assert.That(lsubResponse.Contains("\"Test\""), Is.True);
         simulator.Disconnect();
      }

      [Test]
      [Description("The delimiter cannot become a character an IMAP folder name contains - a backslash included, " +
                   "which MySQL read as the escape of a LIKE pattern, so that the folder was never found there.")]
      public void TestDelimiterChangeIsRefusedWhenAFolderContainsIt()
      {
         _settings.IMAPHierarchyDelimiter = ".";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "delimiter@example.test", "test");
         var folder = account.IMAPFolders.Add("Back\\slash");
         folder.Save();

         Assert.Throws<COMException>(() => _settings.IMAPHierarchyDelimiter = "\\");
         Assert.That(_settings.IMAPHierarchyDelimiter, Is.EqualTo("."));

         // No folder contains a slash, so that change goes through.
         _settings.IMAPHierarchyDelimiter = "/";
         Assert.That(_settings.IMAPHierarchyDelimiter, Is.EqualTo("/"));
      }

      [Test]
      [Description("The delimiter cannot become a character the IMAP folder of a rule action contains.")]
      public void TestDelimiterChangeIsRefusedWhenARuleFolderContainsIt()
      {
         _settings.IMAPHierarchyDelimiter = ".";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "delimiter@example.test", "test");

         var rule = account.Rules.Add();
         rule.Name = "Move";
         rule.Active = true;

         var criteria = rule.Criterias.Add();
         criteria.UsePredefined = true;
         criteria.PredefinedField = hMailServer.eRulePredefinedField.eFTMessageSize;
         criteria.MatchType = hMailServer.eRuleMatchType.eMTGreaterThan;
         criteria.MatchValue = "0";
         criteria.Save();

         var action = rule.Actions.Add();
         action.Type = hMailServer.eRuleActionType.eRAMoveToImapFolder;
         action.IMAPFolder = "Back\\slash";
         action.Save();

         rule.Save();

         Assert.Throws<COMException>(() => _settings.IMAPHierarchyDelimiter = "\\");
         Assert.That(_settings.IMAPHierarchyDelimiter, Is.EqualTo("."));
      }
   }
}