using System;
using System.IO;
using hMailServer;
using NUnit.Framework;
using RegressionTests.Shared;

namespace RegressionTests.Infrastructure
{
   [TestFixture]
   public class SessionLimitingTests : TestFixtureBase
   {
      private TimeSpan _readTimeout = TimeSpan.FromSeconds(5);

      [Test]
      public void TestLimitImapSessionCount()
      {
         _settings.MaxIMAPConnections = 2;

         using (var conn1 = new TcpConnection())
         using (var conn2 = new TcpConnection())
         using (var conn3 = new TcpConnection())
         {
            Assert.That(conn1.Connect(143), Is.True);
            conn1.Receive();
            Assert.That(conn2.Connect(143), Is.True);
            conn2.Receive();
            Assert.That(conn3.Connect(143), Is.True);
            Assert.That(conn3.Receive(), Is.Empty);
         }

         var log2 = LogHandler.ReadCurrentDefaultLog();
         Assert.That(log2.Contains("Blocked either by IP range or by connection limit."), Is.True);
      }

      [Test]
      public void TestLimitSMTPSessionCount()
      {
         _settings.MaxSMTPConnections = 2;

         using (var conn1 = new TcpConnection())
         using (var conn2 = new TcpConnection())
         using (var conn3 = new TcpConnection())
         {
            Assert.That(conn1.Connect(25), Is.True);
            conn1.Receive();
            Assert.That(conn2.Connect(25), Is.True);
            conn2.Receive();
            Assert.That(conn3.Connect(25), Is.True);
            Assert.That(conn3.Receive(), Is.Empty);
         }

         var log2 = LogHandler.ReadCurrentDefaultLog();
         Assert.That(log2.Contains("Blocked either by IP range or by connection limit."), Is.True);
      }

      [Test]
      public void TestLimitPOP3SessionCount()
      {
         _settings.MaxPOP3Connections = 2;

         using (var conn1 = new TcpConnection())
         using (var conn2 = new TcpConnection())
         using (var conn3 = new TcpConnection())
         {
            Assert.That(conn1.Connect(110), Is.True);
            conn1.Receive();
            Assert.That(conn2.Connect(110), Is.True);
            conn2.Receive();
            Assert.That(conn3.Connect(110), Is.True);
            Assert.That(conn3.Receive(), Is.Empty);
         }
      }


      [Test]
      public void TestConnectingToSmtpServerIncreasesSmtpSessionCount()
      {
         var app = SingletonProvider<TestSetup>.Instance.GetApp();
         var status = app.Status;
         var countBefore = status.get_SessionCount(eSessionType.eSTSMTP);

         using (var conn1 = new TcpConnection())
         {
            Assert.That(conn1.Connect(25), Is.True);

            CustomAsserts.AssertSessionCount(eSessionType.eSTSMTP, countBefore + 1);
         }
      }

      [Test]
      public void TestConnectingToImapServerIncreasesImapSessionCount()
      {
         var app = SingletonProvider<TestSetup>.Instance.GetApp();
         var status = app.Status;
         var countBefore = status.get_SessionCount(eSessionType.eSTIMAP);

         using (var conn1 = new TcpConnection())
         {
            Assert.That(conn1.Connect(143), Is.True);
            CustomAsserts.AssertSessionCount(eSessionType.eSTIMAP, countBefore + 1);
         }
      }

      [Test]
      public void TestConnectingToPop3ServerIncreasesPop3SessionCount()
      {
         var app = SingletonProvider<TestSetup>.Instance.GetApp();
         var status = app.Status;
         var countBefore = status.get_SessionCount(eSessionType.eSTPOP3);

         using (var conn1 = new TcpConnection())
         {
            Assert.That(conn1.Connect(110), Is.True);
            CustomAsserts.AssertSessionCount(eSessionType.eSTPOP3, countBefore + 1);
         }
      }


      [Test]
      public void TestDisconnectingFromSmtpServerDecreasesIMAPPSmtpSessionCount()
      {
         var app = SingletonProvider<TestSetup>.Instance.GetApp();
         var status = app.Status;

         var countBefore = status.get_SessionCount(eSessionType.eSTSMTP);
         using (var conn1 = new TcpConnection())
         {
            Assert.That(conn1.Connect(25), Is.True);
         }

         CustomAsserts.AssertSessionCount(eSessionType.eSTSMTP, countBefore);
      }

      [Test]
      public void TestDisconnectingFromImapServerDecreasesImapSessionCount()
      {
         var app = SingletonProvider<TestSetup>.Instance.GetApp();
         var status = app.Status;

         var countBefore = status.get_SessionCount(eSessionType.eSTIMAP);
         using (var conn1 = new TcpConnection())
         {
            Assert.That(conn1.Connect(143), Is.True);
         }

         AssertMaxSessionCount(eSessionType.eSTPOP3, countBefore);
      }

      [Test]
      public void TestDisconnectingFromPop3ServerDecreasesPop3SessionCount()
      {
         var app = SingletonProvider<TestSetup>.Instance.GetApp();
         var status = app.Status;

         var countBefore = status.get_SessionCount(eSessionType.eSTPOP3);
         using (var conn1 = new TcpConnection())
         {
            Assert.That(conn1.Connect(110), Is.True);
         }

         AssertMaxSessionCount(eSessionType.eSTPOP3, countBefore);
      }

      [Test]
      public void TestDisconnectingClientWithOnClientConnectEventShouldDecreaseConnectionCount()
      {
         var app = SingletonProvider<TestSetup>.Instance.GetApp();

         var status = app.Status;
         var countBefore = status.get_SessionCount(eSessionType.eSTPOP3);

         var scripting = app.Settings.Scripting;

         var script = "Sub OnClientConnect(oClient) " + Environment.NewLine +
                      "   Result.Value = 1" + Environment.NewLine +
                      "End Sub" + Environment.NewLine + Environment.NewLine;

         File.WriteAllText(scripting.CurrentScriptFile, script);
         scripting.Enabled = true;
         scripting.Reload();

         using (var socket = new TcpConnection())
         {
            Assert.That(socket.Connect(110), Is.True);
            Assert.That(socket.Receive(), Is.Empty);
         }

         AssertMaxSessionCount(eSessionType.eSTPOP3, countBefore);
      }


      public static void AssertMaxSessionCount(eSessionType sessionType, int maxExpectedCount)
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         RetryHelper.TryAction(TimeSpan.FromSeconds(10), () =>
         {
            var count = application.Status.get_SessionCount(sessionType);

            RetryableAssert.LessOrEqual(maxExpectedCount, count);
         });
      }
   }
}