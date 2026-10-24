using System.Collections.Generic;
using hMailServer;
using NUnit.Framework;
using RegressionTests.Infrastructure;
using RegressionTests.Shared;

namespace RegressionTests.SMTP
{
   [TestFixture]
   public class SMTPClientStartTLSTests : TestFixtureBase
   {
      [SetUp]
      public new void SetUp()
      {
         _status = _application.Status;

         _account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");
      }

      private Status _status;
      private Account _account;

      [Test]
      public void UseStartTlsIfEnabledAndAvailable()
      {
         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["test@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort, eConnectionSecurity.eCSSTARTTLSOptional))
         {
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            var route = TestSetup.AddRoutePointingAtLocalhost(1, smtpServerPort, false,
               eConnectionSecurity.eCSSTARTTLSOptional);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            smtp.Send("test@example.test", "test@dummy-example.com", "Test", "Test message");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            CustomAsserts.AssertRecipientsInDeliveryQueue(0, false);

            Assert.That(server.MessageData, Is.Not.Null.And.Not.Empty);

            Assert.That(LogHandler.DefaultLogContains("220 Ready to start TLS"), Is.True);
         }
      }

      [Test]
      public void DoNotUseStartTlsIfEnabledButNotAvailable()
      {
         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["test@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort, eConnectionSecurity.eCSNone))
         {
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            var route = TestSetup.AddRoutePointingAtLocalhost(1, smtpServerPort, false,
               eConnectionSecurity.eCSSTARTTLSOptional);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            smtp.Send("test@example.test", "test@dummy-example.com", "Test", "Test message");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            CustomAsserts.AssertRecipientsInDeliveryQueue(0, false);

            Assert.That(server.MessageData, Is.Not.Null.And.Not.Empty);

            Assert.That(LogHandler.DefaultLogDoesNotContain("220 Ready to start TLS"), Is.True);
         }
      }

      [Test]
      public void DoNotUseStartTlsIfNotEnabledButAvailable()
      {
         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["test@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort, eConnectionSecurity.eCSSTARTTLSOptional))
         {
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            var route = TestSetup.AddRoutePointingAtLocalhost(1, smtpServerPort, false, eConnectionSecurity.eCSNone);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            smtp.Send("test@example.test", "test@dummy-example.com", "Test", "Test message");


            // Wait for the client to disconnect.
            server.WaitForCompletion();

            CustomAsserts.AssertRecipientsInDeliveryQueue(0, false);

            Assert.That(server.MessageData, Is.Not.Null.And.Not.Empty);

            Assert.That(LogHandler.DefaultLogDoesNotContain("220 Ready to start TLS"), Is.True);
         }
      }

      [Test]
      public void DoNotUseStartTlsIfNotEnabledAndNotAvailable()
      {
         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["test@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort, eConnectionSecurity.eCSNone))
         {
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            var route = TestSetup.AddRoutePointingAtLocalhost(1, smtpServerPort, false, eConnectionSecurity.eCSNone);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            smtp.Send("test@example.test", "test@dummy-example.com", "Test", "Test message");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            CustomAsserts.AssertRecipientsInDeliveryQueue(0, false);

            Assert.That(server.MessageData, Is.Not.Null.And.Not.Empty);

            Assert.That(LogHandler.DefaultLogDoesNotContain("220 Ready to start TLS"), Is.True);
         }
      }


      [Test]
      public void TestDelivertoServerNotSupportingEHLOOptionalConnectionSecurity()
      {
         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));

         var deliveryResults = new Dictionary<string, int>
         {
            { "user1@dummy-example.com", 250 }
         };

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.ServerSupportsEhlo = false;
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            // Add a route so we can connect to localhost.
            TestSetup.AddRoutePointingAtLocalhost(1, smtpServerPort, false, eConnectionSecurity.eCSSTARTTLSOptional);

            // Send message to this route.

            SmtpClientSimulator.StaticSend("test@example.test", "user1@dummy-example.com", "Test", "Test message");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            CustomAsserts.AssertRecipientsInDeliveryQueue(0, false);

            Assert.That(server.MessageData.Contains("Test message"), Is.True);
         }
      }

      [Test]
      public void TestDeliverToServerNotSupportingEHLORequiredConnectionSecurity()
      {
         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));

         var deliveryResults = new Dictionary<string, int>
         {
            { "user1@dummy-example.com", 250 }
         };

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.ServerSupportsEhlo = false;
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            // Add a route so we can connect to localhost.
            TestSetup.AddRoutePointingAtLocalhost(1, smtpServerPort, false, eConnectionSecurity.eCSSTARTTLSRequired);

            // Send message to this route.

            SmtpClientSimulator.StaticSend("test@example.test", "user1@dummy-example.com", "Test", "Test message");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            CustomAsserts.AssertRecipientsInDeliveryQueue(0, true);

            var msg = Pop3ClientSimulator.AssertGetFirstMessageText("test@example.test", "test");
            Assert.That(msg.Contains("Server does not support EHLO command."), Is.True);
         }
      }
   }
}