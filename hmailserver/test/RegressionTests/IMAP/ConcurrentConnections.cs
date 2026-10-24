using NUnit.Framework;
using RegressionTests.Shared;

namespace RegressionTests.IMAP
{
   [TestFixture]
   public class ConcurrentConnections : TestFixtureBase
   {
      [Test]
      [Description("Bug: SendCachedNotifications passed lastExists to SendRECENT_ instead of lastRecent")]
      public void NoopRecentCountReflectsRecentMessagesNotExistsCount()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         // SELECT an empty inbox; the connection's recent-messages set starts empty (size=0).
         var sim = new ImapClientSimulator();
         sim.ConnectAndLogon(account.Address, "test");
         sim.SelectFolder("INBOX");

         // Deliver two messages after SELECT. They are not added to this connection's
         // recent-messages set, so lastRecent remains 0 while lastExists becomes 2.
         SmtpClientSimulator.StaticSend(account.Address, account.Address, "Test 1", "Body 1");
         SmtpClientSimulator.StaticSend(account.Address, account.Address, "Test 2", "Body 2");

         // Wait for delivery without opening a second IMAP SELECT (which would claim \Recent).
         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 2);

         // NOOP flushes cached notifications. EXISTS must be 2, RECENT must be 0.
         var response = sim.NOOP();
         Assert.That(response.Contains("* 2 EXISTS"), Is.True, response);
         Assert.That(response.Contains("* 0 RECENT"), Is.True, response);

         sim.Disconnect();
      }


      [Test]
      public void ChangingFlagShouldAffectAllConnections()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");
         SmtpClientSimulator.StaticSend(account.Address, account.Address, "Test", "Test");

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         var sim1 = new ImapClientSimulator();
         Assert.That(sim1.ConnectAndLogon(account.Address, "test"), Is.True);
         Assert.That(sim1.SelectFolder("Inbox"), Is.True);

         var sim2 = new ImapClientSimulator();
         Assert.That(sim2.ConnectAndLogon(account.Address, "test"), Is.True);
         Assert.That(sim2.SelectFolder("Inbox"), Is.True);

         sim1.SetFlagOnMessage(1, true, "\\Deleted");

         var flags1 = sim1.GetFlags(1);
         var flags2 = sim2.GetFlags(1);

         Assert.That(flags2.Contains(@"* 1 FETCH (FLAGS (\Deleted))"), Is.True, flags2);
         Assert.That(flags2.Contains(@"* 1 FETCH (FLAGS (\Deleted) UID 1)"), Is.True, flags2);
      }
   }
}