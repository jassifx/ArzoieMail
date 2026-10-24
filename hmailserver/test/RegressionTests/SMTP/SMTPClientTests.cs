// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

using System.Collections.Generic;
using System.Threading;
using hMailServer;
using NUnit.Framework;
using RegressionTests.Infrastructure;
using RegressionTests.Shared;

namespace RegressionTests.SMTP
{
   [TestFixture]
   public class SMTPClientTests : TestFixtureBase
   {
      [SetUp]
      public new void SetUp()
      {
         _queue = _application.GlobalObjects.DeliveryQueue;
         _status = _application.Status;
         // add an account to send from
         _account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");
      }

      private DeliveryQueue _queue;
      private Status _status;
      private Account _account;

      [Test]
      [Description("Make sure that the bounce message doesn't include a SMTP auth password")]
      public void TestAuthFailurePasswordInBounce()
      {
         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));

         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["test@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.SimulatedError = SimulatedErrorType.ForceAuthenticationFailure;
            server.StartListen();

            // Add a route so we can connect to localhost.
            var route = TestSetup.AddRoutePointingAtLocalhost(5, smtpServerPort, false);
            route.RelayerRequiresAuth = true;
            route.RelayerAuthUsername = "user@example.com";
            route.SetRelayerAuthPassword("MySecretPassword");

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            var recipients = new List<string>();
            recipients.Add("test@dummy-example.com");
            smtp.Send("test@example.test", recipients, "Test", "Test message");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            CustomAsserts.AssertRecipientsInDeliveryQueue(0);

            var messageText = Pop3ClientSimulator.AssertGetFirstMessageText(_account.Address, "test");

            Assert.That(messageText.Contains("MySecretPassword"), Is.False);
            Assert.That(messageText.Contains("<Password removed>"), Is.True);
         }
      }

      [Test]
      [Description(
         "Test that hMailServer does not try to connect to itself, if a route is set up incorrectly or DNS records are pointing at localhost"
      )]
      public void TestDeliverToMyselfOnLocalPort()
      {
         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));

         // Add a route so we can connect to localhost.
         TestSetup.AddRoutePointingAtLocalhost(1, 25, false);

         // Send message to this route.
         SmtpClientSimulator.StaticSend("test@example.test", "test@dummy-example.com", "subject", "body");

         // Wait for the bounce message to be delivered.
         CustomAsserts.AssertRecipientsInDeliveryQueue(0, true);

         var message = Pop3ClientSimulator.AssertGetFirstMessageText("test@example.test", "test");
         Assert.That(message.Contains("Could not connect to 127.0.0.1 on port 25 since this would mean"), Is.True);
      }

      [Test]
      [Description("Issue 227, Restarting server doesn't refresh local address list")]
      public void TestDeliverToMyselfOnLocalPortAfterChangedLocalPort()
      {
         var ports = _application.Settings.TCPIPPorts;
         for (var i = 0; i < ports.Count; i++)
         {
            var testPort = ports[i];
            if (testPort.Protocol == eSessionType.eSTIMAP)
               testPort.PortNumber = 14300;
            else if (testPort.Protocol == eSessionType.eSTSMTP &&
                     testPort.PortNumber != 587) // Only change 1 of the 2 default SMTP ports.
               testPort.PortNumber = 11001;
            else if (testPort.Protocol == eSessionType.eSTPOP3)
               testPort.PortNumber = 2500;

            testPort.Save();
         }

         _application.Stop();
         _application.Start();

         // Set up a server and start listening.
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["user@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            // Add a route so we can connect to localhost.
            TestSetup.AddRoutePointingAtLocalhost(5, smtpServerPort, false);

            // Send message to this route.
            var sim = new SmtpClientSimulator(false, 11001);
            sim.Send("test@example.test", "user@dummy-example.com", "Test", "Test message");


            // Wait for the client to disconnect.
            server.WaitForCompletion();

            CustomAsserts.AssertRecipientsInDeliveryQueue(0, false);

            Assert.That(server.MessageData.Contains("Test message"), Is.True);
         }

         ports.SetDefault();
         _application.Stop();
         _application.Start();
      }

      [Test]
      public void TestDeliverySuccess250Recipients()
      {
         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));

         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         for (var i = 0; i < 250; i++)
            deliveryResults["user" + i + "@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(3, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.AddRecipientResult(deliveryResults);
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            // Add a route so we can connect to localhost.
            TestSetup.AddRoutePointingAtLocalhost(5, smtpServerPort, false);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            var recipients = new List<string>();

            for (var i = 0; i < 250; i++)
               recipients.Add("user" + i + "@dummy-example.com");

            smtp.Send("test@example.test", recipients, "Test", "Test message");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            CustomAsserts.AssertRecipientsInDeliveryQueue(0, false);

            Assert.That(server.MessageData.Contains("Test message"), Is.True);
         }
      }

      [Test]
      public void TestDeliverySuccess50Recipients()
      {
         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));


         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         for (var i = 0; i < 50; i++)
            deliveryResults["user" + i + "@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            // Add a route so we can connect to localhost.
            TestSetup.AddRoutePointingAtLocalhost(5, smtpServerPort, false);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            var recipients = new List<string>();

            for (var i = 0; i < 50; i++)
               recipients.Add("user" + i + "@dummy-example.com");

            smtp.Send("test@example.test", recipients, "Test", "Test message");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            CustomAsserts.AssertRecipientsInDeliveryQueue(0, false);

            Assert.That(server.MessageData.Contains("Test message"), Is.True);
         }
      }


      [Test]
      [Description(
         "Test delivery to a server which accepts the message but then disconnects immediately before we've sent QUIT."
      )]
      public void TestDeliverySuccessDisconnectAfterMessageAccept()
      {
         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));

         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["test@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.StartListen();
            server.SimulatedError = SimulatedErrorType.DisconnectAfterMessageAccept;

            // Add a route so we can connect to localhost.
            TestSetup.AddRoutePointingAtLocalhost(5, smtpServerPort, false);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            var recipients = new List<string>();
            recipients.Add("test@dummy-example.com");
            smtp.Send("test@example.test", recipients, "Test", "DisconnectAfterAcceptBeforeQuit");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            CustomAsserts.AssertRecipientsInDeliveryQueue(0, false);

            Assert.That(server.MessageData.Contains("DisconnectAfterAcceptBeforeQuit"), Is.True);
         }
      }

      [Test]
      public void TestDeliverySuccessSingleRecipient()
      {
         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));

         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["test@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            // Add a route so we can connect to localhost.
            TestSetup.AddRoutePointingAtLocalhost(5, smtpServerPort, false);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            var recipients = new List<string>();
            recipients.Add("test@dummy-example.com");
            smtp.Send("test@example.test", recipients, "Test", "Test message");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            Assert.That(server.Conversation.Contains("\r\nDATA\r\n"), Is.True);

            CustomAsserts.AssertRecipientsInDeliveryQueue(0, false);

            Assert.That(server.MessageData.Contains("Test message"), Is.True);
         }
      }

      [Test]
      public void TestLargeMessageSuccess()
      {
         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));

         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["test@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            // Add a route so we can connect to localhost.
            TestSetup.AddRoutePointingAtLocalhost(5, smtpServerPort, false);

            // Send message to this route.
            var messageBody = TestSetup.CreateLargeDummyMailBody();

            var smtp = new SmtpClientSimulator();
            var recipients = new List<string>();
            recipients.Add("test@dummy-example.com");
            smtp.Send("test@example.test", recipients, "Test", messageBody);

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            Assert.That(server.Conversation.Contains("\r\nDATA\r\n"), Is.True);

            CustomAsserts.AssertRecipientsInDeliveryQueue(0, false);

            Assert.That(server.MessageData.Contains(messageBody), Is.True);
         }
      }

      [Test]
      [Description("Test delivery to a server which accepts the message but does not respond to QUIT.")]
      public void TestDeliverySuccessSingleRecipientMissingQuitResponse()
      {
         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));

         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["test@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.StartListen();
            server.SimulatedError = SimulatedErrorType.DisconnectWithoutReplyOnQuit;

            // Add a route so we can connect to localhost.
            TestSetup.AddRoutePointingAtLocalhost(5, smtpServerPort, false);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            var recipients = new List<string>();
            recipients.Add("test@dummy-example.com");
            smtp.Send("test@example.test", recipients, "Test", "DeliverySuccessNoQuitResponse");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            CustomAsserts.AssertRecipientsInDeliveryQueue(0, false);

            Assert.That(server.MessageData.Contains("DeliverySuccessNoQuitResponse"), Is.True);
         }
      }

      [Test]
      [Description("In this test, the server will respond with a permanent-error on the MAIL FROM command.")]
      public void TestErrorOnMailFrom()
      {
         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));

         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["test@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.MailFromResult = 561;
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            // Add a route so we can connect to localhost.
            TestSetup.AddRoutePointingAtLocalhost(5, smtpServerPort, false);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            var recipients = new List<string>();
            recipients.Add("test@dummy-example.com");
            smtp.Send("test@example.test", recipients, "Test", "Test message");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            CustomAsserts.AssertRecipientsInDeliveryQueue(0);

            var bounceMessage = Pop3ClientSimulator.AssertGetFirstMessageText(_account.Address, "test");

            Assert.That(bounceMessage.Contains("MAIL FROM:<test@example.test>"), Is.True);
            Assert.That(bounceMessage.Contains("Remote server replied: 561"), Is.True);
         }
      }

      [Test]
      [Description("Test that an error after the message delivery has started causes a retry.")]
      public void TestFailureAfterConnect()
      {
         _application.Settings.SMTPNoOfTries = 3;
         _application.Settings.SMTPMinutesBetweenTry = 60;

         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));
         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["test@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.SimulatedError = SimulatedErrorType.DisconnectAfterSessionStart;
            server.StartListen();

            // Add a route so we can connect to localhost.
            TestSetup.AddRoutePointingAtLocalhost(1, smtpServerPort, false);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            var recipients = new List<string>();
            recipients.Add("test@dummy-example.com");
            smtp.Send(_account.Address, recipients, "Test", "Test message");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            // Force the message to be bounced.
            CustomAsserts.AssertRecipientsInDeliveryQueue(0, true);

            var bounce = Pop3ClientSimulator.AssertGetFirstMessageText(_account.Address, "test");

            Assert.That(bounce.Contains("test@dummy-example.com"), Is.True);
            Assert.That(bounce.Contains("Remote server closed connection."), Is.True);
            Assert.That(bounce.Contains("Tried 1 time(s)"), Is.True);
         }
      }


      [Test]
      [Description("Test that an error after the message delivery completion doesn't affect the delivery.")]
      public void TestFailureAfterDelivery()
      {
         _application.Settings.SMTPNoOfTries = 3;
         _application.Settings.SMTPMinutesBetweenTry = 60;

         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));
         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["test@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.QuitResult = 421;
            server.StartListen();

            // Add a route so we can connect to localhost.
            TestSetup.AddRoutePointingAtLocalhost(5, smtpServerPort, false);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            var recipients = new List<string>();
            recipients.Add("test@dummy-example.com");
            smtp.Send("test@example.test", recipients, "Test", "test of error after accepted delivery");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            CustomAsserts.AssertRecipientsInDeliveryQueue(0);

            Assert.That(server.MessageData.Contains("test of error after accepted delivery"), Is.True);
         }
      }

      [Test]
      [Description("Test that an error after the message delivery has started causes a retry.")]
      public void TestFailureAfterDeliveryStarted()
      {
         _application.Settings.SMTPNoOfTries = 3;
         _application.Settings.SMTPMinutesBetweenTry = 60;

         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));
         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["test@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.SimulatedError = SimulatedErrorType.DisconnectAfterDeliveryStarted;
            server.StartListen();

            // Add a route so we can connect to localhost.
            TestSetup.AddRoutePointingAtLocalhost(1, smtpServerPort, false);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            var recipients = new List<string>();
            recipients.Add("test@dummy-example.com");
            smtp.Send(_account.Address, recipients, "Test", "Test message");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            // Force the message to be bounced.
            CustomAsserts.AssertRecipientsInDeliveryQueue(0, true);

            var bounce = Pop3ClientSimulator.AssertGetFirstMessageText(_account.Address, "test");

            Assert.That(bounce.Contains("test@dummy-example.com"), Is.True);
            Assert.That(bounce.Contains("Remote server closed connection."), Is.True);
            Assert.That(bounce.Contains("Tried 1 time(s)"), Is.True);
         }
      }

      [Test]
      [Description("Test that an error after the message delivery has started causes a retry.")]
      public void TestFailureAfterReceivedHelloBanner()
      {
         _application.Settings.SMTPNoOfTries = 3;
         _application.Settings.SMTPMinutesBetweenTry = 60;

         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));
         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["test@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.SimulatedError = SimulatedErrorType.DisconnectAfterSessionStart;
            server.StartListen();

            // Add a route so we can connect to localhost.
            TestSetup.AddRoutePointingAtLocalhost(1, smtpServerPort, false);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            var recipients = new List<string>();
            recipients.Add("test@dummy-example.com");
            smtp.Send(_account.Address, recipients, "Test", "Test message");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            // Force the message to be bounced.
            CustomAsserts.AssertRecipientsInDeliveryQueue(0, true);

            var bounce = Pop3ClientSimulator.AssertGetFirstMessageText(_account.Address, "test");

            Assert.That(bounce.Contains("test@dummy-example.com"), Is.True);
            Assert.That(bounce.Contains("Remote server closed connection."), Is.True);
            Assert.That(bounce.Contains("Tried 1 time(s)"), Is.True);
         }
      }

      [Test]
      public void TestFatalDeliveryFailure()
      {
         _application.Settings.Logging.Enabled = true;
         _application.Settings.Logging.LogSMTP = true;
         _application.Settings.Logging.LogTCPIP = true;
         _application.Settings.Logging.LogDebug = true;

         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));

         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["test@dummy-example.com"] = 550;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            // Add a route so we can connect to localhost.
            TestSetup.AddRoutePointingAtLocalhost(0, smtpServerPort, false);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            var recipients = new List<string>();
            recipients.Add("test@dummy-example.com");
            smtp.Send("test@example.test", recipients, "Test", "Test message");

            var undeliveredMessages = _status.UndeliveredMessages;

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            // wait for the message to be delivered.
            for (var i = 1; i <= 40; i++)
            {
               Assert.That(i == 40, Is.False);

               if (_status.UndeliveredMessages.Contains("\ttest@example.test") ||
                   _status.UndeliveredMessages.Length == 0)
                  break;

               Thread.Sleep(250);
            }

            CustomAsserts.AssertRecipientsInDeliveryQueue(0, true);

            var bounceMessage = Pop3ClientSimulator.AssertGetFirstMessageText(_account.Address, "test");

            Assert.That(bounceMessage.Contains("Remote server (127.0.0.1) issued an error."), Is.True);
            Assert.That(bounceMessage.Contains("550 test@dummy-example.com"), Is.True);
         }
      }

      /// <summary>
      ///    3 recipients. On the first server, 2 recipients are successful and one temp fail.
      ///    On the second, the 3'rd one fails with a permanent error.
      /// </summary>
      [Test]
      public void TestMultipleHostsHalfDeliveryOnFirstPermanentOnSecond()
      {
         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));

         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["user1@dummy-example.com"] = 250;
         deliveryResults["user2@dummy-example.com"] = 250;
         deliveryResults["user3@dummy-example.com"] = 450;

         var deliveryResultsSecond = new Dictionary<string, int>();
         deliveryResultsSecond["user1@dummy-example.com"] = 550;
         deliveryResultsSecond["user2@dummy-example.com"] = 550;
         deliveryResultsSecond["user3@dummy-example.com"] = 500;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(2, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.AddRecipientResult(deliveryResultsSecond);
            server.StartListen();


            // Add a route so we can connect to localhost.
            TestSetup.AddRoutePointingAtLocalhostMultipleHosts(1, smtpServerPort);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            var recipients = new List<string>();

            recipients.Add("user1@dummy-example.com");
            recipients.Add("user2@dummy-example.com");
            recipients.Add("user3@dummy-example.com");

            smtp.Send("test@example.test", recipients, "Test", "Accepted message");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            // Trigger a sending of the bounce message.
            CustomAsserts.AssertRecipientsInDeliveryQueue(0, true);

            var bounceMessage = Pop3ClientSimulator.AssertGetFirstMessageText(_account.Address, "test");

            Assert.That(bounceMessage.Contains("RCPT TO:<user1@dummy-example.com>"), Is.False);
            Assert.That(bounceMessage.Contains("RCPT TO:<user2@dummy-example.com>"), Is.False);
            Assert.That(bounceMessage.Contains("RCPT TO:<user3@dummy-example.com>"), Is.True);
            Assert.That(bounceMessage.Contains("500 user3@dummy-example.com"), Is.True);
         }
      }

      [Test]
      public void TestMultipleHostsTemporaryFailure()
      {
         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));

         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["user1@dummy-example.com"] = 250;
         deliveryResults["user2@dummy-example.com"] = 250;
         deliveryResults["user3@dummy-example.com"] = 499;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(2, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            // Add a route so we can connect to localhost.
            var route = TestSetup.AddRoutePointingAtLocalhostMultipleHosts(2, smtpServerPort);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            var recipients = new List<string>();

            recipients.Add("user1@dummy-example.com");
            recipients.Add("user2@dummy-example.com");
            recipients.Add("user3@dummy-example.com");

            smtp.Send("test@example.test", recipients, "Test", "Test message");


            // Wait for the client to disconnect.
            server.WaitForCompletion();
         }


         CustomAsserts.AssertRecipientsInDeliveryQueue(1);

         // Check so that only user 3 remains in the queue.
         Assert.That(_status.UndeliveredMessages.IndexOf("user1@dummy-example.com"), Is.EqualTo(-1));
         Assert.That(_status.UndeliveredMessages.IndexOf("user2@dummy-example.com"), Is.EqualTo(-1));
         Assert.That(_status.UndeliveredMessages.IndexOf("user3@dummy-example.com"), Is.Not.EqualTo(-1));

         using (var server = new SmtpServerSimulator(2, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            CustomAsserts.AssertRecipientsInDeliveryQueue(0, true);

            server.WaitForCompletion();

            var bounceMessage = Pop3ClientSimulator.AssertGetFirstMessageText("test@example.test", "test");

            Assert.That(bounceMessage.Contains("user1@dummy-example.com"), Is.False);
            Assert.That(bounceMessage.Contains("user2@dummy-example.com"), Is.False);
            Assert.That(bounceMessage.Contains("user3@dummy-example.com"), Is.True);
            Assert.That(bounceMessage.Contains("499 user3@dummy-example.com"), Is.True);
            Assert.That(bounceMessage.Contains("Tried 2 time(s)"), Is.True);
         }
      }

      [Test]
      public void TestMultipleHostsTemporaryFailureDeliveryOnSecondServer()
      {
         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));

         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["user1@dummy-example.com"] = 250;
         deliveryResults["user2@dummy-example.com"] = 250;
         deliveryResults["user3@dummy-example.com"] = 450;

         var deliveryResultsSecond = new Dictionary<string, int>();
         deliveryResultsSecond["user1@dummy-example.com"] = 250;
         deliveryResultsSecond["user2@dummy-example.com"] = 250;
         deliveryResultsSecond["user3@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(2, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.AddRecipientResult(deliveryResultsSecond);
            server.StartListen();


            // Add a route so we can connect to localhost.
            TestSetup.AddRoutePointingAtLocalhostMultipleHosts(2, smtpServerPort);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            var recipients = new List<string>();

            recipients.Add("user1@dummy-example.com");
            recipients.Add("user2@dummy-example.com");
            recipients.Add("user3@dummy-example.com");

            smtp.Send("test@example.test", recipients, "Test", "Accepted message");


            // Wait for the client to disconnect.
            server.WaitForCompletion();

            // Trigger a sending of the bounce message.
            CustomAsserts.AssertRecipientsInDeliveryQueue(0);

            Assert.That(server.MessageData.Contains("Accepted message"), Is.True);
         }
      }

      [Test]
      public void TestPartialTemporaryErrorFailure()
      {
         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));

         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["user1@dummy-example.com"] = 250;
         deliveryResults["user2@dummy-example.com"] = 250;
         deliveryResults["user3@dummy-example.com"] = 400;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            // Add a route so we can connect to localhost.
            TestSetup.AddRoutePointingAtLocalhost(2, smtpServerPort, false);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            var recipients = new List<string>();

            recipients.Add("user1@dummy-example.com");
            recipients.Add("user2@dummy-example.com");
            recipients.Add("user3@dummy-example.com");

            smtp.Send("test@example.test", recipients, "Test", "Test message");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            // Trigger a sending of the bounce message.
            CustomAsserts.AssertRecipientsInDeliveryQueue(1);

            Assert.That(_status.UndeliveredMessages.IndexOf("user1@dummy-example.com"), Is.EqualTo(-1));
            Assert.That(_status.UndeliveredMessages.IndexOf("user2@dummy-example.com"), Is.EqualTo(-1));
            Assert.That(_status.UndeliveredMessages.IndexOf("user3@dummy-example.com"), Is.Not.EqualTo(-1));

            Assert.That(server.MessageData.Contains("Test message"), Is.True);
         }

         // Attempt to deliver the message again.
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.StartListen();
            // Force sending of messages
            CustomAsserts.AssertRecipientsInDeliveryQueue(0);
            server.WaitForCompletion();

            var bounceMessage = Pop3ClientSimulator.AssertGetFirstMessageText("test@example.test", "test");

            Assert.That(bounceMessage.Contains("400 user3@dummy-example.com"), Is.True);
            Assert.That(bounceMessage.Contains("Tried 2 time(s)"), Is.True);

            Assert.That(bounceMessage.Contains("user2@dummy-example.com"), Is.False);
            Assert.That(bounceMessage.Contains("user1@dummy-example.com"), Is.False);
         }
      }

      [Test]
      public void TestPermanentFailure()
      {
         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));

         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["test@dummy-example.com"] = 542;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            // Add a route so we can connect to localhost.
            TestSetup.AddRoutePointingAtLocalhost(5, smtpServerPort, false);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            var recipients = new List<string>();
            recipients.Add("test@dummy-example.com");
            smtp.Send("test@example.test", recipients, "Test", "Test message");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            Assert.That(server.Conversation.Contains("\r\nDATA\r\n"), Is.False);
         }

         CustomAsserts.AssertRecipientsInDeliveryQueue(0);

         var bounce = Pop3ClientSimulator.AssertGetFirstMessageText(_account.Address, "test");

         Assert.That(bounce.Contains("Remote server replied: 542 test@dummy-example.com"), Is.True);
      }


      [Test]
      public void TestTemporaryFailure()
      {
         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));

         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["test@dummy-example.com"] = 452;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            // Add a route so we can connect to localhost.
            TestSetup.AddRoutePointingAtLocalhost(2, smtpServerPort, false);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            var recipients = new List<string>();
            recipients.Add("test@dummy-example.com");
            smtp.Send("test@example.test", recipients, "Test", "Test message");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            Assert.That(_status.UndeliveredMessages.Length, Is.Not.EqualTo(0));
         }

         using (var server = new SmtpServerSimulator(1, smtpServerPort)) // Start to listen again.
         {
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            // Deliver the message to the server and then deliver the bounce message.
            CustomAsserts.AssertRecipientsInDeliveryQueue(0, true);

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            Assert.That(server.Conversation.Contains("\r\nDATA\r\n"), Is.False);
         }

         // Now the message has bounced.
         var message = Pop3ClientSimulator.AssertGetFirstMessageText(_account.Address, "test");

         Assert.That(message.Contains("452 test@dummy-example.com"), Is.True);
         Assert.That(message.Contains("Tried 2 time(s)"), Is.True);
      }

      [Test]
      public void TestDeliverToServerNotSupportingEHLO()
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
            TestSetup.AddRoutePointingAtLocalhost(1, smtpServerPort, false, eConnectionSecurity.eCSNone);


            SmtpClientSimulator.StaticSend("test@example.test", "user1@dummy-example.com", "Test", "Test message");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            CustomAsserts.AssertRecipientsInDeliveryQueue(0, false);

            Assert.That(server.MessageData.Contains("Test message"), Is.True);
         }
      }

      [Test]
      public void TestDeliverToServerNotSupportingHELO()
      {
         Assert.That(_status.UndeliveredMessages.Length, Is.EqualTo(0));

         var deliveryResults = new Dictionary<string, int>
         {
            { "user1@dummy-example.com", 250 }
         };

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.ServerSupportsHelo = false;
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            // Add a route so we can connect to localhost.
            TestSetup.AddRoutePointingAtLocalhost(1, smtpServerPort, false, eConnectionSecurity.eCSNone);

            // Send message to this route.

            SmtpClientSimulator.StaticSend("test@example.test", "user1@dummy-example.com", "Test", "Test message");


            // Wait for the client to disconnect.
            server.WaitForCompletion();

            CustomAsserts.AssertRecipientsInDeliveryQueue(0, true);

            var msg = Pop3ClientSimulator.AssertGetFirstMessageText("test@example.test", "test");
            Assert.That(msg.Contains("Remote server replied: 550 Command HELO not recognized."), Is.True);
         }
      }
   }
}