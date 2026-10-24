// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

using System.Text.RegularExpressions;
using hMailServer;
using NUnit.Framework;
using RegressionTests.Infrastructure;
using RegressionTests.Shared;

namespace RegressionTests.IMAP
{
   /// <summary>
   ///    UIDVALIDITY must be higher for a folder re-created under the same name (RFC 3501 2.3.1.1).
   /// </summary>
   [TestFixture]
   public class UidValidity : TestFixtureBase
   {
      private const string Password = "test";

      [Test]
      [Description("A folder deleted and re-created, even within the same second, gets a higher UIDVALIDITY.")]
      public void RecreatedFolderGetsHigherUidValidity()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "uidvalidity@example.test", Password);
         var simulator = ConnectAndLogon(account);

         long previous = 0;

         for (var i = 0; i < 5; i++)
         {
            Assert.That(simulator.CreateFolder("Recreated"), Is.True);
            var current = GetUidValidity(simulator, "Recreated");
            Assert.That(current, Is.GreaterThan(previous));
            Assert.That(simulator.DeleteFolder("Recreated"), Is.True);

            previous = current;
         }

         simulator.Disconnect();
      }

      [Test]
      [Description("After a restart, a re-created folder still gets a higher UIDVALIDITY.")]
      public void UidValidityKeepsIncreasingAfterRestart()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "uidvalidity@example.test", Password);

         var simulator = ConnectAndLogon(account);
         Assert.That(simulator.CreateFolder("Restart"), Is.True);
         var before = GetUidValidity(simulator, "Restart");
         Assert.That(simulator.DeleteFolder("Restart"), Is.True);
         simulator.Disconnect();

         var application = SingletonProvider<TestSetup>.Instance.GetApp();
         application.Stop();
         application.Start();

         simulator = ConnectAndLogon(account);
         Assert.That(simulator.CreateFolder("Restart"), Is.True);
         Assert.That(GetUidValidity(simulator, "Restart"), Is.GreaterThan(before));
         simulator.Disconnect();
      }

      [Test]
      [Description("Each account has its own counter, so a user can't learn how many folders other accounts create.")]
      public void OtherAccountsDoNotAffectUidValidity()
      {
         var observer = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "observer@example.test", Password);
         var other = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "other@example.test", Password);

         var observerSimulator = ConnectAndLogon(observer);
         Assert.That(observerSimulator.CreateFolder("First"), Is.True);
         var first = GetUidValidity(observerSimulator, "First");

         var otherSimulator = ConnectAndLogon(other);
         for (var i = 0; i < 5; i++)
            Assert.That(otherSimulator.CreateFolder("Folder" + i), Is.True);
         otherSimulator.Disconnect();

         Assert.That(observerSimulator.CreateFolder("Second"), Is.True);
         Assert.That(GetUidValidity(observerSimulator, "Second"), Is.EqualTo(first + 1));

         observerSimulator.Disconnect();
      }

      private static ImapClientSimulator ConnectAndLogon(Account account)
      {
         var simulator = new ImapClientSimulator();
         Assert.That(simulator.ConnectAndLogon(account.Address, Password), Is.True);
         return simulator;
      }

      private static long GetUidValidity(ImapClientSimulator simulator, string folderName)
      {
         var response = simulator.Status(folderName, "UIDVALIDITY");
         var match = Regex.Match(response, @"UIDVALIDITY (\d+)");
         Assert.That(match.Success, Is.True, response);
         return long.Parse(match.Groups[1].Value);
      }
   }
}
