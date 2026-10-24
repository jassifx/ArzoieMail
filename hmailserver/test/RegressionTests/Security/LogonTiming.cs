// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

using System;
using System.Diagnostics;
using hMailServer;
using NUnit.Framework;
using RegressionTests.Infrastructure;
using RegressionTests.Shared;

namespace RegressionTests.Security
{
   [TestFixture]
   public class LogonTiming : TestFixtureBase
   {
      [Test]
      [Description("A logon to an account that does not exist fails as slowly as one with a wrong password, " +
                   "so the time a failed logon takes does not tell which accounts exist.")]
      public void TestUnknownAccountFailsAsSlowlyAsWrongPassword()
      {
         // The algorithm and cost new installations get. The other tests use the cheapest
         // there is, which leaves too little to measure.
         _settings.PasswordHashAlgorithm = ePasswordHashAlgorithm.ePWHashArgon2id;
         _settings.PasswordHashMemoryCost = 0;
         _settings.PasswordHashIterations = 0;

         SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "timing@example.test", "right password");

         // The fastest of a few attempts each, which the connections' own noise can only add to.
         var wrongPassword = FastestFailedLogon("timing@example.test", "wrong password");
         var unknownAccount = FastestFailedLogon("nobody@example.test", "wrong password");

         // Verifying an Argon2id hash with the default cost takes tens of milliseconds, a refused
         // logon without one a millisecond or two: without the fix the unknown account is many
         // times faster. Half is room for the noise.
         Assert.That(unknownAccount.TotalMilliseconds, Is.GreaterThanOrEqualTo(wrongPassword.TotalMilliseconds * 0.5),
            $"Wrong password: {wrongPassword.TotalMilliseconds} ms, unknown account: {unknownAccount.TotalMilliseconds} ms.");
      }

      private static TimeSpan FastestFailedLogon(string username, string password)
      {
         var fastest = TimeSpan.MaxValue;

         for (var i = 0; i < 5; i++)
         {
            var pop3 = new Pop3ClientSimulator();
            var stopwatch = Stopwatch.StartNew();
            Assert.That(pop3.ConnectAndLogon(username, password, out _), Is.False);
            stopwatch.Stop();
            pop3.Disconnect();

            if (stopwatch.Elapsed < fastest)
               fastest = stopwatch.Elapsed;
         }

         return fastest;
      }
   }
}
