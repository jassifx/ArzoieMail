// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

using System;
using NUnit.Framework;
using RegressionTests.Infrastructure;
using RegressionTests.Shared;

namespace RegressionTests.Security
{
   [TestFixture]
   public class AutoBan : TestFixtureBase
   {
      [SetUp]
      public new void SetUp()
      {
         _settings.ClearLogonFailureList();
         LogHandler.DeleteCurrentDefaultLog();
      }

      [Test]
      public void TestDisabled()
      {
         _settings.AutoBanOnLogonFailure = false;
         _settings.MaxInvalidLogonAttempts = 3;
         _settings.MaxInvalidLogonAttemptsWithin = 5;
         _settings.AutoBanMinutes = 3;

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         var sim = new Pop3ClientSimulator();
         Assert.That(sim.ConnectAndLogon(account.Address, "test"), Is.True);
         sim.Disconnect();

         // confirm that we can retrieve welcome message.
         Assert.That(sim.GetWelcomeMessage().StartsWith("+OK"), Is.True);

         string errorMessage;
         // fail to log on 3 times.
         for (var i = 0; i < 5; i++)
         {
            Assert.That(sim.ConnectAndLogon(account.Address, "testA", out errorMessage), Is.False);
            sim.Disconnect();
         }

         Assert.That(sim.GetWelcomeMessage().StartsWith("+OK"), Is.True);

         var logText = LogHandler.ReadCurrentDefaultLog();
         Assert.That(logText.Contains("Blocked either by IP range or by connection limit."), Is.False, logText);
      }

      [Test]
      public void TestIMAPLogonFailure()
      {
         _settings.AutoBanOnLogonFailure = true;
         _settings.MaxInvalidLogonAttempts = 4;
         _settings.MaxInvalidLogonAttemptsWithin = 5;
         _settings.AutoBanMinutes = 3;

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         var sim = new ImapClientSimulator();
         Assert.That(sim.ConnectAndLogon(account.Address, "test"), Is.True);
         sim.Disconnect();

         // confirm that we can retrieve welcome message.
         Assert.That(sim.GetWelcomeMessage().StartsWith("* OK"), Is.True);

         // fail to log on 3 times.
         for (var i = 0; i < 4; i++)
         {
            string errorMessage;

            Assert.That(sim.ConnectAndLogon(account.Address, "testA", out errorMessage), Is.False);
            sim.Disconnect();

            if (i == 3) Assert.That(errorMessage.Contains("Too many invalid logon attempts."), Is.True);
         }

         Assert.That(sim.GetWelcomeMessage().Length == 0, Is.True);

         var logText = LogHandler.ReadCurrentDefaultLog();
         Assert.That(logText.Contains("Blocked either by IP range or by connection limit."), Is.True, logText);
      }

      [Test]
      public void TestIPRangeName()
      {
         _settings.AutoBanOnLogonFailure = true;
         _settings.MaxInvalidLogonAttempts = 3;
         _settings.MaxInvalidLogonAttemptsWithin = 5;
         _settings.AutoBanMinutes = 3;

         Assert.That(_settings.SecurityRanges.Count, Is.EqualTo(2));

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         var sim = new Pop3ClientSimulator();
         Assert.That(sim.ConnectAndLogon(account.Address, "test"), Is.True);
         sim.Disconnect();

         // confirm that we can retrieve welcome message.
         Assert.That(sim.GetWelcomeMessage().StartsWith("+OK"), Is.True);

         string errorMessage;
         // fail to log on 3 times.
         for (var i = 0; i < 3; i++)
         {
            Assert.That(sim.ConnectAndLogon(account.Address, "testA", out errorMessage), Is.False);
            sim.Disconnect();

            if (i == 2) Assert.That(errorMessage.Contains("Too many invalid logon attempts."), Is.True);
         }

         Assert.That(sim.GetWelcomeMessage().Length == 0, Is.True);

         Assert.That(_settings.SecurityRanges.Count, Is.EqualTo(3));
         Assert.That(_settings.SecurityRanges[2].Name, Is.EqualTo("Auto-ban: test@example.test"));
      }

      [Test]
      public void TestPOP3LogonFailure()
      {
         _settings.AutoBanOnLogonFailure = true;
         _settings.MaxInvalidLogonAttempts = 3;
         _settings.MaxInvalidLogonAttemptsWithin = 5;
         _settings.AutoBanMinutes = 3;

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "te'st@example.test", "test");

         var sim = new Pop3ClientSimulator(TestSetup.GetLocalIpAddress(), false, 110);
         Assert.That(sim.ConnectAndLogon(account.Address, "test"), Is.True);
         sim.Disconnect();

         // confirm that we can retrieve welcome message.
         Assert.That(sim.GetWelcomeMessage().StartsWith("+OK"), Is.True);

         string errorMessage;
         // fail to log on 3 times.
         for (var i = 0; i < 3; i++)
         {
            Assert.That(sim.ConnectAndLogon(account.Address, "testA", out errorMessage), Is.False);
            sim.Disconnect();

            if (i == 2) Assert.That(errorMessage.Contains("Too many invalid logon attempts."), Is.True);
         }

         Assert.That(sim.GetWelcomeMessage().Length == 0, Is.True);

         var logText = LogHandler.ReadCurrentDefaultLog();
         Assert.That(logText.Contains("Blocked either by IP range or by connection limit."), Is.True, logText);
      }

      [Test]
      public void TestSMTPLogonFailure()
      {
         _settings.AutoBanOnLogonFailure = true;
         _settings.MaxInvalidLogonAttempts = 2;
         _settings.MaxInvalidLogonAttemptsWithin = 5;
         _settings.AutoBanMinutes = 3;

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         var sim = new SmtpClientSimulator();

         //test@example.test / test
         string errorMessage;
         sim.ConnectAndLogon("dGVzdEBleGFtcGxlLnRlc3Q=", "dGVzdA==", out errorMessage);
         sim.Disconnect();

         // confirm that we can retrieve welcome message.
         Assert.That(sim.GetWelcomeMessage().StartsWith("220"), Is.True);

         // fail to log on 3 times.
         for (var i = 0; i < 2; i++)
         {
            CustomAsserts.Throws<Exception>(() =>
               sim.ConnectAndLogon("dGVzdEBleGFtcGxlLnRlc3Q=", "Vaffe==", out errorMessage));
            sim.Disconnect();

            if (i == 2) Assert.That(errorMessage.Contains("Too many invalid logon attempts."), Is.True);
         }

         Assert.That(sim.GetWelcomeMessage().Length == 0, Is.True);

         var logText = LogHandler.ReadCurrentDefaultLog();
         Assert.That(logText.Contains("Blocked either by IP range or by connection limit."), Is.True, logText);
      }

      [Test]
      [Description("The name of an auto-ban range carries the user name of the failed logons, quotes and all. " +
                   "A range of that name existing already, the ban takes the next free name.")]
      public void TestIPRangeNameWithQuoteInUserName()
      {
         _settings.AutoBanOnLogonFailure = true;
         _settings.MaxInvalidLogonAttempts = 3;
         _settings.MaxInvalidLogonAttemptsWithin = 5;
         _settings.AutoBanMinutes = 3;

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "o'brien@example.test", "test");

         // A range with the name the ban would take first, for an address no test connects from.
         var taken = _settings.SecurityRanges.Add();
         taken.Name = "Auto-ban: o'brien@example.test";
         taken.LowerIP = "10.255.255.1";
         taken.UpperIP = "10.255.255.1";
         taken.Priority = 10;
         taken.Save();

         var sim = new Pop3ClientSimulator();
         for (var i = 0; i < 3; i++)
         {
            Assert.That(sim.ConnectAndLogon(account.Address, "wrong", out _), Is.False);
            sim.Disconnect();
         }

         Assert.That(sim.GetWelcomeMessage().Length == 0, Is.True);

         var names = new System.Collections.Generic.List<string>();
         for (var i = 0; i < _settings.SecurityRanges.Count; i++)
            names.Add(_settings.SecurityRanges[i].Name);

         Assert.That(names, Does.Contain("Auto-ban: o'brien@example.test"));
         Assert.That(names, Does.Contain("Auto-ban: o'brien@example.test (1)"));
      }
   }
}