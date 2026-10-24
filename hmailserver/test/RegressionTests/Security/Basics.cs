// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

using System.Security.Authentication;
using NUnit.Framework;
using RegressionTests.Infrastructure;
using RegressionTests.Shared;

namespace RegressionTests.Security
{
   [TestFixture]
   public class Basics : TestFixtureBase
   {
      [Test]
      public void TestEmptyPassword()
      {
         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "");

         string message;
         var sim = new Pop3ClientSimulator();
         Assert.That(sim.ConnectAndLogon(account1.Address, "", out message), Is.False);


         var simIMAP = new ImapClientSimulator();
         Assert.That(simIMAP.ConnectAndLogon(account1.Address, "", out message), Is.False);
         Assert.That(message, Is.EqualTo("A01 NO Invalid user name or password.\r\n"));

         var simSMTP = new SmtpClientSimulator();
         CustomAsserts.Throws<AuthenticationException>(() =>
            simSMTP.ConnectAndLogon("dGVzdEB0ZXN0LmNvbQ==", "", out message));
         Assert.That(message, Is.EqualTo("535 Authentication failed. Restarting authentication process.\r\n"));
      }
   }
}