// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

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
   }
}