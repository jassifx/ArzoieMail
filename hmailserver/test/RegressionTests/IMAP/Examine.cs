using NUnit.Framework;
using RegressionTests.Shared;

namespace RegressionTests.IMAP
{
   [TestFixture]
   public class Examine : TestFixtureBase
   {
      [Test]
      [Description("Assert that it's not possible to change flags while in READONLY-mode")]
      public void TestChangeFlags()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "examine@example.test", "test");

         SmtpClientSimulator.StaticSend("test@example.test", account.Address, "Test", "test");
         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         var simulator = new ImapClientSimulator();
         simulator.ConnectAndLogon(account.Address, "test");
         simulator.ExamineFolder("Inbox");
         Assert.That(simulator.SetFlagOnMessage(1, true, @"\Deleted"), Is.False);
      }

      [Test]
      [Description(
         "Assert that the \\RECENT flag isn't automatically changed when accessing a folder in READONLY-mode")]
      public void TestChangeRecentFlag()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "examine@example.test", "test");

         SmtpClientSimulator.StaticSend("test@example.test", account.Address, "Test", "test");
         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         var simulator = new ImapClientSimulator();
         simulator.ConnectAndLogon(account.Address, "test");
         var result = simulator.ExamineFolder("Inbox");
         Assert.That(result.Contains("* 1 RECENT"), Is.True, result);
         simulator.Close();
         simulator.Disconnect();

         simulator = new ImapClientSimulator();
         simulator.ConnectAndLogon(account.Address, "test");
         Assert.That(simulator.SelectFolder("Inbox", out result), Is.True);
         Assert.That(result.Contains("* 1 RECENT"), Is.True, result);
         simulator.Close();
         simulator.Disconnect();

         simulator = new ImapClientSimulator();
         simulator.ConnectAndLogon(account.Address, "test");
         result = simulator.ExamineFolder("Inbox");
         Assert.That(result.Contains("* 0 RECENT"), Is.True, result);
         simulator.Close();
         simulator.Disconnect();
      }

      [Test]
      [Description("Assert that the \\SEEN flag isn't automatically changed when accessing a message in READONLY-mode"
      )]
      public void TestChangeSeenFlag()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "examine@example.test", "test");

         SmtpClientSimulator.StaticSend("test@example.test", account.Address, "Test", "test");
         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         var simulator = new ImapClientSimulator();
         simulator.ConnectAndLogon(account.Address, "test");
         simulator.ExamineFolder("Inbox");
         var flags = simulator.GetFlags(1);
         var body = simulator.Fetch("1 RFC822");
         var flagsAfter = simulator.GetFlags(1);
         simulator.Close();
         simulator.Disconnect();

         Assert.That(flagsAfter, Is.EqualTo(flags));

         var secondSimulator = new ImapClientSimulator();
         secondSimulator.ConnectAndLogon(account.Address, "test");
         secondSimulator.SelectFolder("Inbox");
         var secondFlags = secondSimulator.GetFlags(1);
         var secondBody = secondSimulator.Fetch("1 RFC822");
         var secondFlagsAfter = secondSimulator.GetFlags(1);
         secondSimulator.Close();
         secondSimulator.Disconnect();

         Assert.That(secondFlagsAfter, Is.Not.EqualTo(secondFlags));
      }

      [Test]
      public void TestExamine()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "examine@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon(account.Address, "test");
         Assert.That(simulator.CreateFolder("TestFolder"), Is.True);
         var result = simulator.ExamineFolder("TestFolder");

         Assert.That(result.Contains("[PERMANENTFLAGS ()]"), Is.True, result);
         Assert.That(result.Contains("[READ-ONLY]"), Is.True, result);
      }

      [Test]
      [Description("Assert that it's not possible to EXPUNGE while in READONLY-mode")]
      public void TestExpunge()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "examine@example.test", "test");

         SmtpClientSimulator.StaticSend("test@example.test", account.Address, "Test", "test");
         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         var simulator = new ImapClientSimulator();
         simulator.ConnectAndLogon(account.Address, "test");
         simulator.SelectFolder("Inbox");
         Assert.That(simulator.SetFlagOnMessage(1, true, @"\Deleted"), Is.True);

         var secondSimulator = new ImapClientSimulator();
         secondSimulator.ConnectAndLogon(account.Address, "test");
         var result = secondSimulator.ExamineFolder("INBOX");
         Assert.That(result.Contains("1 EXISTS"), Is.True, result);
         Assert.That(secondSimulator.Expunge(), Is.False);

         simulator.SelectFolder("INBOX");
         Assert.That(simulator.Expunge(), Is.True);

         simulator.Close();
         secondSimulator.Close();
      }
   }
}