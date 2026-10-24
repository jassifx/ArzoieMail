// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using hMailServer;
using NUnit.Framework;
using RegressionTests.Infrastructure;
using RegressionTests.Shared;

namespace RegressionTests.SMTP
{
   [TestFixture]
   public class Basics : TestFixtureBase
   {
      // helper
      private static void Send50Messages()
      {
         // Runs on its own thread, where an unhandled exception would end the test process on
         // .NET. A failed send shows up in the message count the test checks.
         try
         {
            for (var i = 0; i < 50; i++)
            {
               var smtpClientSimulator = new SmtpClientSimulator();
               smtpClientSimulator.Send("test@example.test", "bigaccount@example.test", "Big account email",
                  "Body av email");
            }
         }
         catch (Exception ex)
         {
            Console.WriteLine($"Sending failed: {ex}");
         }
      }

      private string EncodeBase64(string s)
      {
         var bytes = Encoding.UTF8.GetBytes(s);
         return Convert.ToBase64String(bytes);
      }

      [Test]
      [Description("Issue 325, Last logon time being updated at wrong point")]
      public void AuthShouldUpdateLastLogonTime()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");
         var lastLogonTimeBefore = Convert.ToDateTime(account.LastLogonTime);

         Thread.Sleep(1000);
         var sock = new TcpConnection();
         sock.Connect(25);
         Assert.That(sock.Receive().StartsWith("220"), Is.True);
         sock.Send("EHLO test.com\r\n");
         Assert.That(sock.Receive().StartsWith("250"), Is.True);

         var base64EncodedUsername = EncodeBase64("test@example.test");
         sock.Send("AUTH LOGIN " + base64EncodedUsername + "\r\n");
         Assert.That(sock.Receive().StartsWith("334"), Is.True);

         sock.Send(EncodeBase64("test") + "\r\n");
         Assert.That(sock.Receive().StartsWith("235"), Is.True);

         var lastLogonTimeAfter =
            Convert.ToDateTime(SingletonProvider<TestSetup>.Instance.GetApp().Domains[0].Accounts[0].LastLogonTime);
         Assert.That(lastLogonTimeAfter, Is.Not.EqualTo(lastLogonTimeBefore));
      }

      [Test]
      public void AuthLoginShouldOnlyBeAllowedOnce()
      {
         SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         var sock = new TcpConnection();
         sock.Connect(25);
         Assert.That(sock.Receive().StartsWith("220"), Is.True);
         sock.Send("EHLO test.com\r\n");
         Assert.That(sock.Receive().StartsWith("250"), Is.True);

         // Login a first time
         var base64EncodedUsername = EncodeBase64("test@example.test");
         sock.Send("AUTH LOGIN " + base64EncodedUsername + "\r\n");
         Assert.That(sock.Receive().StartsWith("334"), Is.True);
         sock.Send(EncodeBase64("test") + "\r\n");
         Assert.That(sock.Receive().StartsWith("235"), Is.True);

         // Login a second time
         sock.Send("AUTH LOGIN " + base64EncodedUsername + "\r\n");
         Assert.That(sock.Receive().StartsWith("503 Already authenticated."), Is.True);
      }


      [Test]
      [Category("SMTP")]
      [Description("Issue 228. Bounce messages does not contain Subject or Date.")]
      public void BounceMessageShouldContainSubjectAndDate()
      {
         var senderAccount = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "sender@example.test", "test");
         var recipientAccount = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "list@example.test",
            "test");

         recipientAccount.MaxSize = 1;
         recipientAccount.Save();

         // Make sure that no bounce is sent.
         SmtpClientSimulator.StaticSend(senderAccount.Address, recipientAccount.Address, "MySubject", "Test");
         Pop3ClientSimulator.AssertGetFirstMessageText(recipientAccount.Address, "test");

         // Build a 2MB string.
         var builder = new StringBuilder();
         for (var i = 0; i < 11000; i++)
            builder.Append(
               "1234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890\r\n");
         var text = builder.ToString();

         SmtpClientSimulator.StaticSend(senderAccount.Address, recipientAccount.Address, "Test subject", text);

         // Make sure the list did not receive it.
         CustomAsserts.AssertRecipientsInDeliveryQueue(0);
         Pop3ClientSimulator.AssertMessageCount(recipientAccount.Address, "test", 0);
         CustomAsserts.AssertFilesInUserDirectory(recipientAccount, 0);

         // Make sure it bounced.
         var content = Pop3ClientSimulator.AssertGetFirstMessageText(senderAccount.Address, "test");
         Assert.That(content.Contains("Inbox is full"), Is.True);
         Assert.That(content.Contains("Subject: Test subject"), Is.True);

         // Make sure body contains year.
         var bodyStartPos = content.IndexOf("\r\n\r\n");
         var yearPos = content.IndexOf(DateTime.Now.Year.ToString(), bodyStartPos);
         Assert.That(yearPos >= 0, Is.True);
      }

      [Test]
      [Category("SMTP")]
      public void BounceMessageShouldReferenceOriginalMessageID()
      {
         var senderAccount = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "sender@example.test", "test");
         var recipientAccount = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "list@example.test",
            "test");

         recipientAccount.MaxSize = 1;
         recipientAccount.Save();

         const string messageID = "<dsn-threading@example.test>";
         const string previousReference = "<previous-message@example.test>";
         var builder = new StringBuilder();
         for (var i = 0; i < 11000; i++)
            builder.Append(
               "1234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890\r\n");

         var content = "Message-ID: " + messageID + "\r\n" +
                       "References: " + previousReference + "\r\n" +
                       "Subject: Test subject\r\n" +
                       "\r\n" +
                       builder;
         SmtpClientSimulator.StaticSendRaw(senderAccount.Address, recipientAccount.Address, content);

         CustomAsserts.AssertRecipientsInDeliveryQueue(0);
         var bounce = Pop3ClientSimulator.AssertGetFirstMessageText(senderAccount.Address, "test");
         Assert.That(bounce.Contains("In-Reply-To: " + messageID), Is.True);
         Assert.That(bounce.Contains("References: " + previousReference + " " + messageID), Is.True);
      }

      [Test]
      [Description("Issue 226. Domain alias rewrites sender address.")]
      public void DomainAliasesShouldNotRewriteRecipientList()
      {
         var domainAlias = _domain.DomainAliases.Add();
         domainAlias.AliasName = "dummy-example.com";
         domainAlias.Save();

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "otherguy@example.test", "test");

         // Set up a server listening on port 250 which accepts email for test@otherdomain.com
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["test@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.StartListen();


            var route = TestSetup.AddRoutePointingAtLocalhost(1, smtpServerPort, true, eConnectionSecurity.eCSNone);

            // Now send a message from test@example.test to test@otherdomain.com.
            // Error scenario:
            // In the recipientlist, otherdomain.com gets rewritten to test.com 
            // since there's a domain alias named otherdomain.co on the domain 
            // test.com.
            //
            // This should not happen. Otherdomain.com is an alias for test.com,
            // but we shouldn't actually modify the list address just because
            // of this.
            var smtpClient = new SmtpClientSimulator();
            smtpClient.Send(account.Address, "test@dummy-example.com", "Test", "Test message");

            CustomAsserts.AssertRecipientsInDeliveryQueue(0);

            Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 0);

            // This should now be processed via the rule -> route -> external server we've set up.
            server.WaitForCompletion();

            Assert.That(server.MessageData.Contains("Test message"), Is.True);
         }
      }

      [Test]
      [Description("Issue 325, Last logon time being updated at wrong point")]
      public void MailFromShouldNotUpdatedLastLogonTime()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "us'er@example.test", "test");
         var lastLogonTimeBefore = Convert.ToDateTime(account.LastLogonTime);

         Thread.Sleep(1000);
         SmtpClientSimulator.StaticSend("someone@example.test", "us'er@example.test", "Test", "Test");

         CustomAsserts.AssertRecipientsInDeliveryQueue(0);
         var lastLogonTimeAfter =
            Convert.ToDateTime(SingletonProvider<TestSetup>.Instance.GetApp().Domains[0].Accounts[0].LastLogonTime);
         Assert.That(lastLogonTimeAfter, Is.EqualTo(lastLogonTimeBefore));
      }

      [Test]
      public void TestAntiVirusEnabled()
      {
         var range =
            SingletonProvider<TestSetup>.Instance.GetApp().Settings.SecurityRanges.get_ItemByName("My computer");
         range.EnableAntiVirus = true;
         range.Save();

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "user@example.test", "test");

         SmtpClientSimulator.StaticSend("user@example.test", "user@example.test", "Test", "Test");
         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         var message = account.IMAPFolders.get_ItemByName("INBOX").Messages[0];
         Assert.That(message.get_Flag(eMessageFlag.eMFVirusScan), Is.True);
      }


      [Test]
      [Description("Issue 291, Sloppy non-delivery report generated")]
      public void TestBounceMessageSyntax()
      {
         var senderAccount = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "sender@example.test", "test");
         var recipientAccount = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "list@example.test",
            "test");

         recipientAccount.MaxSize = 1;
         recipientAccount.Save();

         // Build a 2MB string.
         var builder = new StringBuilder();
         for (var i = 0; i < 11000; i++)
            builder.Append(
               "1234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890\r\n");

         var text = builder.ToString();

         SmtpClientSimulator.StaticSend(senderAccount.Address, recipientAccount.Address, "", text);

         // Make sure the list did not receive it.
         CustomAsserts.AssertRecipientsInDeliveryQueue(0);

         // Check the syntax in the bounce message.
         var content = Pop3ClientSimulator.AssertGetFirstMessageText(senderAccount.Address, "test");

         // The bounce message should contain the MIME-version.
         Assert.That(content.Contains("MIME-Version: 1.0"), Is.True);

         // Lines may not end with space if they are QP-encoded.
         Assert.That(content.Contains("  Subject:=20"), Is.True);
      }


      [Test]
      [Category("SMTP")]
      [Description("Issue 181: Make sure that duplicate Message-ID's aren't added.")]
      public void TestDuplicateMessageIDs()
      {
         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         var content = "SomeHeader: Text" + Environment.NewLine +
                       "Message-Id: <200903121212246.SM01264@server03>" + Environment.NewLine +
                       "" + Environment.NewLine +
                       "Test";

         SmtpClientSimulator.StaticSendRaw("test@example.test", "test@example.test", content);

         var test = Pop3ClientSimulator.AssertGetFirstMessageText("test@example.test", "test");

         Assert.That(test.Contains("Message-Id"), Is.True);
         Assert.That(test.Contains("Message-ID"), Is.False);
      }

      [Test]
      [Category("SMTP")]
      [Description("Issue 536: A missing Message-ID should be added for authenticated senders, since we act as a submission server for them.")]
      public void TestMessageIDAddedForAuthenticatedSender()
      {
         SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         // The simulator does not add a Message-ID of its own, so any Message-ID in the
         // delivered message was added by the server.
         string errorMessage;
         var simulator = new SmtpClientSimulator();
         simulator.Send(false, "test@example.test", "test", "test@example.test", "test@example.test",
                        "Test subject", "Test body", out errorMessage);

         var test = Pop3ClientSimulator.AssertGetFirstMessageText("test@example.test", "test");

         Assert.That(test.Contains("Message-ID: <"), Is.True);
      }

      [Test]
      [Category("SMTP")]
      [Description("Issue 536: A missing Message-ID should be added for unauthenticated senders which are allowed to send as one of our domains, since we act as a submission server for them as well.")]
      public void TestMessageIDAddedForLocalSenderNotRequiredToAuthenticate()
      {
         // The "My computer" IP range allows the local computer to send as one of our
         // domains without authenticating. Web sites and scripts submitting mail over
         // localhost rely on this.
         var range =
            SingletonProvider<TestSetup>.Instance.GetApp().Settings.SecurityRanges.get_ItemByName("My computer");
         Assert.That(range.RequireSMTPAuthLocalToLocal, Is.False);

         SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         SmtpClientSimulator.StaticSend("test@example.test", "test@example.test", "Test subject", "Test body");

         var test = Pop3ClientSimulator.AssertGetFirstMessageText("test@example.test", "test");

         Assert.That(test.Contains("Message-ID: <"), Is.True);
      }

      [Test]
      [Category("SMTP")]
      [Description("Issue 536: A missing Message-ID should not be added to relayed messages, since that hides from spam filters that the original message had none.")]
      public void TestMessageIDNotAddedForRelayedMessage()
      {
         SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         // The sender is not one of our domains, so we're relaying rather than submitting.
         SmtpClientSimulator.StaticSend("someone@example.com", "test@example.test", "Test subject", "Test body");

         var test = Pop3ClientSimulator.AssertGetFirstMessageText("test@example.test", "test");

         Assert.That(test.Contains("Message-ID"), Is.False);
      }

      [Test]
      public void TestEHLOKeywords()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();
         var settings = _settings;
         settings.HostName = "examplify.com";

         var socket = new TcpConnection();
         Assert.That(socket.Connect(25), Is.True);
         var result = socket.Receive();
         socket.Send("EHLO example.com\r\n");
         result = socket.Receive();
         socket.Disconnect();
         Assert.That(result.Contains("250-" + settings.HostName), Is.True);
      }

      [Test]
      public void TestHelo()
      {
         var simulator = new TcpConnection();

         simulator.Connect(25);

         var sWelcome = simulator.Receive();

         simulator.Send("HELO\r\n");
         var sResponse = simulator.Receive();

         if (!sResponse.StartsWith("501"))
            throw new Exception("Invalid response to HELO");

         simulator.Send("HELO   \r\n");
         sResponse = simulator.Receive();

         if (!sResponse.StartsWith("501"))
            throw new Exception("Invalid response to HELO");

         simulator.Send("HELO TEST.COM\r\n");
         sResponse = simulator.Receive();

         if (!sResponse.StartsWith("250"))
            throw new Exception("Invalid response to HELO");


         simulator.Send("HELO   TEST.COM\r\n");
         sResponse = simulator.Receive();

         if (!sResponse.StartsWith("250"))
            throw new Exception("Invalid response to HELO");

         simulator.Send("EHLO TEST.COM\r\n");
         sResponse = simulator.Receive();

         if (!sResponse.StartsWith("250"))
            throw new Exception("Invalid response to HELO");

         simulator.Send("EHLO    TEST.COM\r\n");
         sResponse = simulator.Receive();

         if (!sResponse.StartsWith("250"))
            throw new Exception("Invalid response to HELO");

         simulator.Disconnect();
      }

      [Test]
      [Category("SMTP")]
      [Description("Confirm that it's OK to send MAIL FROM without the < and >")]
      public void TestMailFromSyntaxValidation()
      {
         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         var smtpClientSimulator = new TcpConnection();
         smtpClientSimulator.Connect(25);

         Assert.That(smtpClientSimulator.Receive().StartsWith("220"), Is.True);
         smtpClientSimulator.Send("HELO test\r\n");
         Assert.That(smtpClientSimulator.Receive().StartsWith("250"), Is.True);

         // A few tests of invalid syntax.
         Assert.That(smtpClientSimulator.SendAndReceive("MAIL FROM: <test@example.test\r\n").StartsWith("250"), Is.False);
         Assert.That(smtpClientSimulator.SendAndReceive("MAIL FROM: test@example.test>\r\n").StartsWith("250"), Is.False);
         Assert.That(smtpClientSimulator.SendAndReceive("MAIL FROM: <    test@example.test    \r\n")
            .StartsWith("250"), Is.False);
         Assert.That(smtpClientSimulator.SendAndReceive("MAIL FROM: <        \r\n").StartsWith("250"), Is.False);
         Assert.That(smtpClientSimulator.SendAndReceive("MAIL FROM: >        \r\n").StartsWith("250"), Is.False);
         Assert.That(smtpClientSimulator.SendAndReceive("MAIL FROM: <sdsdfs@sdsdf.csd\r\n").StartsWith("250"), Is.False);
         Assert.That(smtpClientSimulator.SendAndReceive("MAIL FROM <sdsdfs@sdsdf.csd>\r\n").StartsWith("250"), Is.False);
         Assert.That(smtpClientSimulator.SendAndReceive("MAIL FROM  sdsdfs@sdsdf.csd\r\n").StartsWith("250"), Is.False);

         // Valid syntax, < and >
         Assert.That(smtpClientSimulator.SendAndReceive("MAIL FROM: <test@example.test>\r\n").StartsWith("250"), Is.True);
         Assert.That(smtpClientSimulator.SendAndReceive("RSET\r\n").StartsWith("250"), Is.True);

         Assert.That(smtpClientSimulator.SendAndReceive("MAIL FROM: test@example.test\r\n").StartsWith("250"), Is.True);
         Assert.That(smtpClientSimulator.SendAndReceive("RSET\r\n").StartsWith("250"), Is.True);

         Assert.That(smtpClientSimulator.SendAndReceive("MAIL FROM:    test@example.test   \r\n").StartsWith("250"), Is.True);
         Assert.That(smtpClientSimulator.SendAndReceive("RSET\r\n").StartsWith("250"), Is.True);

         Assert.That(smtpClientSimulator.SendAndReceive("MAIL FROM:test@example.test\r\n").StartsWith("250"), Is.True);
         Assert.That(smtpClientSimulator.SendAndReceive("RSET\r\n").StartsWith("250"), Is.True);

         Assert.That(smtpClientSimulator.SendAndReceive("MAIL FROM:<test@example.test>\r\n").StartsWith("250"), Is.True);
         Assert.That(smtpClientSimulator.SendAndReceive("RSET\r\n").StartsWith("250"), Is.True);

         smtpClientSimulator.Disconnect();
      }

      [Test]
      [Category("SMTP")]
      [Description("Test account max size limitation.")]
      public void TestMaxSizeLimitation()
      {
         var senderAccount = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "sender@example.test", "test");
         var recipientAccount = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "list@example.test",
            "test");

         recipientAccount.MaxSize = 1;
         recipientAccount.Save();

         // Make sure that no bounce is sent.
         SmtpClientSimulator.StaticSend(senderAccount.Address, recipientAccount.Address, "MySubject", "Test");
         Pop3ClientSimulator.AssertGetFirstMessageText(recipientAccount.Address, "test");

         // Build a 2MB string.
         var builder = new StringBuilder();
         for (var i = 0; i < 11000; i++)
            builder.Append(
               "1234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890\r\n");
         var text = builder.ToString();

         SmtpClientSimulator.StaticSend(senderAccount.Address, recipientAccount.Address, "MySubject", text);

         // Make sure the list did not receive it.
         CustomAsserts.AssertRecipientsInDeliveryQueue(0);
         Pop3ClientSimulator.AssertMessageCount(recipientAccount.Address, "test", 0);
         CustomAsserts.AssertFilesInUserDirectory(recipientAccount, 0);

         // Make sure it bounced.
         var content = Pop3ClientSimulator.AssertGetFirstMessageText(senderAccount.Address, "test");
         Assert.That(content.Contains("Inbox is full"), Is.True);
      }

      [Test]
      [Category("SMTP")]
      [Description("Test account max size limitation.")]
      public void TestMaxSizeLimitation_AnnouncedText()
      {
         _settings.MaxMessageSize = 102400;

         // Make sure that no bounce is sent.
         var smtpClient = new SmtpClientSimulator();
         smtpClient.Connect();
         smtpClient.Receive(); // Receive banner

         var ehloResponse = smtpClient.SendAndReceive("EHLO example.com\r\n");

         Assert.That(ehloResponse, Does.Contain("250-SIZE 104857600"), ehloResponse);
      }

      [Test]
      [Category("SMTP")]
      [Description("Test account max size limitation.")]
      public void TestMaxSizeLimitationMultipleSmallMessages()
      {
         var senderAccount = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "sender@example.test", "test");
         var recipientAccount = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "list@example.test",
            "test");

         recipientAccount.MaxSize = 1;
         recipientAccount.Save();

         // Make sure we can send several messages.
         SmtpClientSimulator.StaticSend(senderAccount.Address, recipientAccount.Address, "Test", "Test");
         SmtpClientSimulator.StaticSend(senderAccount.Address, recipientAccount.Address, "Test", "Test");

         Pop3ClientSimulator.AssertMessageCount(recipientAccount.Address, "test", 2);
      }

      [Test]
      public void TestMediumVolume()
      {
         // Fetch default domain
         var application = SingletonProvider<TestSetup>.Instance.GetApp();

         SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "bigaccount@example.test", "test");

         var smtpClientSimulator = new SmtpClientSimulator();


         var oThreads = new ArrayList();
         for (var i = 0; i < 5; i++)
         {
            var oThread = new Thread(Send50Messages);
            oThreads.Add(oThread);
            oThread.Start();
         }

         // Join the threads
         for (var i = 0; i < 5; i++)
         {
            var oThread = (Thread)oThreads[i];
            oThread.Join();
         }

         ImapClientSimulator.AssertMessageCount("bigaccount@example.test", "test", "Inbox", 250);
      }

      [Test]
      public void TestMultipleHostsLimitMXHosts()
      {
         _settings.MaxNumberOfMXHosts = 1;

         // No valid recipients...
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["user1@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            // Add a route so we can connect to localhost.
            var route = TestSetup.AddRoutePointingAtLocalhostMultipleHosts(2, smtpServerPort);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            var recipients = new List<string>();

            recipients.Add("user1@dummy-example.com");

            SmtpClientSimulator.StaticSend("test@example.test", "user1@dummy-example.com", "Test", "Test");

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            var log = LogHandler.ReadCurrentDefaultLog();
            Assert.That(log.Contains("Truncating MX server list."), Is.True);
         }
      }

      [Test]
      public void TestPostmasterAddressIsAlias()
      {
         _domain.Postmaster = "alias@example.test";
         _domain.Save();

         var alias = SingletonProvider<TestSetup>.Instance.AddAlias(_domain, "alias@example.test", "test@example.test");

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");
         SmtpClientSimulator.StaticSend("someone@example.com", "someone@example.test", "Test", "Test");
         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);
      }

      [Test]
      public void TestPostmasterAddressIsAliasButAliasMissing()
      {
         _domain.Postmaster = "alias@example.test";
         _domain.Save();

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");
         CustomAsserts.Throws<DeliveryFailedException>(() =>
            SmtpClientSimulator.StaticSend("someone@example.com", "someone@example.test", "Test", "Test"));
      }

      [Test]
      public void TestPostmasterInternalSender()
      {
         _domain.Postmaster = "test@example.test";
         _domain.Save();

         SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "sender@example.test", "test");

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");
         SmtpClientSimulator.StaticSend("sender@example.test", "someone@example.test", "Test", "Test");

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);
      }

      [Test]
      public void TestPostmasterOnSecondDomain()
      {
         _domain.Postmaster = "test@otherDomain.com";
         _domain.Save();

         var otherDomain =
            SingletonProvider<TestSetup>.Instance.AddDomain(SingletonProvider<TestSetup>.Instance.GetApp().Domains,
               "otherDomain.com");

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(otherDomain, "test@otherDomain.com",
            "test");
         SmtpClientSimulator.StaticSend("sender@example.test", "someone@example.test", "Test", "Test");

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);
      }

      [Test]
      public void TestPostmasterOnSecondDomainWithDomainAlias()
      {
         var otherDomain =
            SingletonProvider<TestSetup>.Instance.AddDomain(SingletonProvider<TestSetup>.Instance.GetApp().Domains,
               "otherDomain.com");
         var otherDomainAlias = otherDomain.DomainAliases.Add();
         otherDomainAlias.AliasName = "name2.com";
         otherDomainAlias.Save();

         _domain.Postmaster = "test@name2.com";
         _domain.Save();

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(otherDomain, "test@otherDomain.com",
            "test");
         SmtpClientSimulator.StaticSend("sender@example.test", "someone@example.test", "Test", "Test");

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);
      }

      [Test]
      [Description("Test that we don't mess up external deliveries with the postmaster address.")]
      public void TestPostmasterRecipientExternal()
      {
         _domain.Postmaster = "test@example.test";
         _domain.Save();

         SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "sender@example.test", "test");

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");
         CustomAsserts.Throws<DeliveryFailedException>(() =>
            SmtpClientSimulator.StaticSend("sender@example.com", "someone@example.com", "Test", "Test"));
      }

      [Test]
      public void TestPostmasterToPostmasterOnOtherDomain()
      {
         _domain.Postmaster = "nonexistent@otherDomain.com";
         _domain.Save();

         var otherDomain =
            SingletonProvider<TestSetup>.Instance.AddDomain(SingletonProvider<TestSetup>.Instance.GetApp().Domains,
               "otherDomain.com");
         otherDomain.Postmaster = "account@otherDomain.com";
         otherDomain.Save();

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(otherDomain, "account@otherDomain.com",
            "test");
         SmtpClientSimulator.StaticSend("sender@example.test", "someone@example.test", "Test", "Test");

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);
      }


      [Test]
      [Category("SMTP")]
      public void MailFromWithAuthParameterShouldBeAccepted()
      {
         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         var smtpClientSimulator = new TcpConnection();
         smtpClientSimulator.Connect(25);

         Assert.That(smtpClientSimulator.Receive().StartsWith("220"), Is.True);
         smtpClientSimulator.Send("HELO test\r\n");
         Assert.That(smtpClientSimulator.Receive().StartsWith("250"), Is.True);

         // A few tests of invalid syntax.
         Assert.That(smtpClientSimulator.SendAndReceive("MAIL FROM: <test@example.test> AUTH=<>\r\n")
            .StartsWith("250"),
            Is.True);

         smtpClientSimulator.Disconnect();
      }


      [Test]
      [Category("SMTP")]
      [Description("Confirm that it's OK to send MAIL FROM without the < and >")]
      public void TestRcptToSyntax()
      {
         SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         var smtpClientSimulator = new TcpConnection();
         smtpClientSimulator.Connect(25);

         Assert.That(smtpClientSimulator.Receive().StartsWith("220"), Is.True);
         smtpClientSimulator.Send("HELO test\r\n");
         Assert.That(smtpClientSimulator.Receive().StartsWith("250"), Is.True);

         // A few tests of invalid syntax.
         Assert.That(smtpClientSimulator.SendAndReceive("MAIL FROM: <test@example.test>\r\n").StartsWith("250"), Is.True);
         Assert.That(smtpClientSimulator.SendAndReceive("RCPT TO: test@example.test>\r\n").StartsWith("250"), Is.False);
         Assert.That(smtpClientSimulator.SendAndReceive("RCPT TO: <test@example.test\r\n").StartsWith("250"), Is.False);
         Assert.That(smtpClientSimulator.SendAndReceive("RCPT TO <test@example.test\r\n").StartsWith("250"), Is.False);
         Assert.That(smtpClientSimulator.SendAndReceive("RCPT TO<test@example.test\r\n").StartsWith("250"), Is.False);

         Assert.That(smtpClientSimulator.SendAndReceive("RCPT TO: <test@example.test>\r\n").StartsWith("250"), Is.True);
         Assert.That(smtpClientSimulator.SendAndReceive("RCPT TO: test@example.test\r\n").StartsWith("250"), Is.True);

         smtpClientSimulator.Disconnect();
      }

      [Test]
      [Category("SMTP")]
      [Description("Confirm that it's OK to add the same list multiple times.")]
      public void TestSameRecipientMultipleTimes()
      {
         var logging = SingletonProvider<TestSetup>.Instance.GetApp().Settings.Logging;
         logging.AWStatsEnabled = true;
         logging.Enabled = true;

         if (File.Exists(logging.CurrentAwstatsLog))
            File.Delete(logging.CurrentAwstatsLog);

         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         var smtpClientSimulator = new TcpConnection();
         smtpClientSimulator.Connect(25);
         Assert.That(smtpClientSimulator.Receive().StartsWith("220"), Is.True);
         smtpClientSimulator.Send("HELO test\r\n");
         Assert.That(smtpClientSimulator.Receive().StartsWith("250"), Is.True);
         smtpClientSimulator.Send("MAIL FROM: test@example.test\r\n");
         Assert.That(smtpClientSimulator.Receive().StartsWith("250"), Is.True);
         smtpClientSimulator.Send("RCPT TO: knafve@gmail.com\r\n");
         Assert.That(smtpClientSimulator.Receive().StartsWith("250"), Is.True);
         smtpClientSimulator.Send("RCPT TO: knafve@gmail.com\r\n");
         Assert.That(smtpClientSimulator.Receive().StartsWith("250"), Is.True);

         smtpClientSimulator.Disconnect();
      }

      [Test]
      [Description(
         "Test send email from internal address to external, using no authentication on an IP range with does not require authentication"
      )]
      public void TestSendExternalToExternalPermitted()
      {
         var range =
            SingletonProvider<TestSetup>.Instance.GetApp().Settings.SecurityRanges.get_ItemByName("My computer");
         range.RequireSMTPAuthExternalToExternal = false;
         range.Save();


         // Set up a server listening on port 250 which accepts email for test@otherdomain.com
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["test2@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            var route = TestSetup.AddRoutePointingAtLocalhost(1, smtpServerPort, false, eConnectionSecurity.eCSNone);

            var smtpClientSimulator = new SmtpClientSimulator();
            smtpClientSimulator.Send("test@sdag532sdfagdsa12fsdafdsa1.com",
               "test2@dummy-example.com", "Mail 1", "Test message");


            // This should now be processed via the rule -> route -> external server we've set up.
            server.WaitForCompletion();

            Assert.That(server.MessageData.Contains("Test message"), Is.True);
         }
      }

      [Test]
      [Description(
         "Test send email from internal address to external. When not allowed, error message should state this (and not bring up authentication).")]
      public void TestSendExternalToExternalNotPermitted_ErrorMessage()
      {
         var range =
            SingletonProvider<TestSetup>.Instance.GetApp().Settings.SecurityRanges.get_ItemByName("My computer");
         range.AllowDeliveryFromRemoteToRemote = false;
         range.Save();

         var smtpClientSimulator = new SmtpClientSimulator();
         var ex = Assert.Throws<DeliveryFailedException>(() => smtpClientSimulator.Send(
            "test@sdag532sdfagdsa12fsdafdsa1.com",
            "test2@dummy-example.com", "Mail 1", "Test message"));

         Assert.That(ex.Message, Does.Contain("550 Delivery is not allowed to this address."), ex.Message);
      }

      [Test]
      public void TestSendExternalMailToMailboxContainingQuote()
      {
         var range = SingletonProvider<TestSetup>.Instance.GetApp().Settings.SecurityRanges
            .get_ItemByName("My computer");
         range.RequireSMTPAuthExternalToExternal = false;
         range.Save();


         // Set up a server listening on port 250 which accepts email for test@otherdomain.com
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["\"John Smith\"@dummy-example.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            TestSetup.AddRoutePointingAtLocalhost(1, smtpServerPort, false, eConnectionSecurity.eCSNone);

            var smtpClientSimulator = new SmtpClientSimulator();
            smtpClientSimulator.Send("\"John Smith\"@example.com",
               "\"John Smith\"@dummy-example.com", "Mail 1", "Test message");


            // This should now be processed via the rule -> route -> external server we've set up.
            server.WaitForCompletion();

            Assert.That(server.MessageData.Contains("Test message"), Is.True);
         }
      }

      [Test]
      public void TestSendToAddressWithQuote()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "us'er@example.test", "test");
         SmtpClientSimulator.StaticSend("someone@example.test", "us'er@example.test", "Test", "Test");
         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);
      }

      [Test]
      public void TestSendToMultipleAccounts()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();
         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "multi1@example.test", "test");
         var account2 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "multi2@example.test", "test");
         var account3 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "multi3@example.test", "test");

         var smtpClientSimulator = new SmtpClientSimulator();

         var lstRecipients = new List<string>();
         lstRecipients.Add("multi1@example.test");
         lstRecipients.Add("multi2@example.test");
         lstRecipients.Add("multi3@example.test");

         var sBody = "Test of sending same email to multiple accounts.";

         smtpClientSimulator.Send(account1.Address, lstRecipients, "Multi test", sBody);

         var pop3ClientSimulator = new Pop3ClientSimulator();

         var sMessageData = Pop3ClientSimulator.AssertGetFirstMessageText(account1.Address, "test");
         if (sMessageData.IndexOf(sBody) < 0)
            throw new Exception("E-mail not found");

         sMessageData = Pop3ClientSimulator.AssertGetFirstMessageText(account2.Address, "test");
         if (sMessageData.IndexOf(sBody) < 0)
            throw new Exception("E-mail not found");

         sMessageData = Pop3ClientSimulator.AssertGetFirstMessageText(account3.Address, "test");
         if (sMessageData.IndexOf(sBody) < 0)
            throw new Exception("E-mail not found");
      }

      [Test]
      public void TestSendToPostmasterAddress()
      {
         _domain.Postmaster = "test@example.test";
         _domain.Save();

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");
         SmtpClientSimulator.StaticSend("someone@example.com", "someone@example.test", "Test", "Test");
         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);
      }


      [Test]
      public void TestSendToPostmasterAddressDomainAlias()
      {
         var alias = _domain.DomainAliases.Add();
         alias.AliasName = "alias.com";
         alias.Save();

         _domain.Postmaster = "test@alias.com";
         _domain.Save();

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");
         SmtpClientSimulator.StaticSend("someone@example.com", "someone@example.test", "Test", "Test");

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);
      }

      [Test]
      [Category("SMTP")]
      [Description("Issue 188, SMTP: Permanent error response if disk full ")]
      public void TestTempErrorIfDiskFull()
      {
         var senderAccount = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "sender@example.test", "test");
         var recipientAccount = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "list@example.test",
            "test");

         var directories = SingletonProvider<TestSetup>.Instance.GetApp().Settings.Directories;

         var origDataDir = directories.DataDirectory;

         try
         {
            directories.DataDirectory = "C:\\FolderWhichDoesNotExist";

            var result = "";

            var sim = new SmtpClientSimulator();
            CustomAsserts.Throws<DeliveryFailedException>(() =>
               sim.Send(senderAccount.Address, recipientAccount.Address, "MySubject", "Test", out result));

            Assert.That(result.StartsWith("4"), Is.True, "Expected temporary error, but was: " + result);

            CustomAsserts.AssertReportedError("Failed to write to the file",
               "Rejected message because no mail data has been saved in file");
         }
         finally
         {
            directories.DataDirectory = origDataDir;
         }
      }

      [Test]
      public void TestTooManyInvalidCommandsAUTH()
      {
         var settings = _settings;

         settings.DisconnectInvalidClients = true;
         settings.MaxNumberOfInvalidCommands = 3;

         var sim = new TcpConnection();
         sim.Connect(25);
         sim.Send("EHLO test.com\r\n");
         sim.ReadUntil("250 HELP\r\n");

         for (var i = 1; i <= 5; i++)
         {
            sim.Send("AUTH LOGIN\r\n");

            // Send invalid username/password
            var usernamePrompt = sim.Receive();

            // Send a invalid username
            sim.Send("YWNhZGVtaWE=\r\n");
            var passwordPrompt = sim.Receive();
            Assert.That(passwordPrompt, Does.Contain("334 UGFzc3dvcmQ6")); // Base64 encoded "Password" prompt

            // Send a invalid password
            sim.Send("abc\r\n");
            var loginResult = sim.Receive();


            if (i == 4)
            {
               Assert.That(loginResult, Does.Contain("Too many invalid commands"));
               return;
            }

            Assert.That(loginResult, Does.Contain("535 Authentication failed. Restarting authentication process."));
         }

         Assert.Fail("Wasn't disconnected");
      }

      [Test]
      public void TestTooManyInvalidCommandsHELO()
      {
         var settings = _settings;
         settings.DisconnectInvalidClients = true;
         settings.MaxNumberOfInvalidCommands = 3;

         var sim = new TcpConnection();
         sim.Connect(25);
         sim.Receive(); // banner

         sim.SendAndReceive("HELO\r\n");
         sim.SendAndReceive("HELO\r\n");
         sim.SendAndReceive("HELO\r\n");
         var result = sim.SendAndReceive("HELO\r\n");
         Assert.That(result.Contains("Too many invalid commands"), Is.True, result);
      }

      [Test]
      [Description("RSET must not take the place of a HELO/EHLO greeting.")]
      public void RsetBeforeGreetingShouldNotAllowMailFrom()
      {
         var sim = new TcpConnection();
         sim.Connect(25);
         sim.Receive(); // banner

         var rsetResult = sim.SendAndReceive("RSET\r\n");
         Assert.That(rsetResult, Does.Contain("250 OK"));

         var result = sim.SendAndReceive("MAIL FROM:<test@example.com>\r\n");
         Assert.That(result, Does.Contain("503 Bad sequence of commands"));
      }

      [Test]
      [Description("Closing the RSET greeting bypass must not break RSET itself.")]
      public void RsetAfterGreetingShouldStillAllowMailFrom()
      {
         var sim = new TcpConnection();
         sim.Connect(25);
         sim.Receive(); // banner

         Assert.That(sim.SendAndReceive("HELO example.com\r\n"), Does.Contain("250 Hello."));
         Assert.That(sim.SendAndReceive("RSET\r\n"), Does.Contain("250 OK"));
         Assert.That(sim.SendAndReceive("MAIL FROM:<test@example.com>\r\n"), Does.Contain("250 OK"));
      }

      [Test]
      [Description("Closing the RSET greeting bypass must not break RSET itself.")]
      public void RsetShouldAbortTransactionInProgress()
      {
         var sim = new TcpConnection();
         sim.Connect(25);
         sim.Receive(); // banner

         Assert.That(sim.SendAndReceive("HELO example.com\r\n"), Does.Contain("250 Hello."));
         Assert.That(sim.SendAndReceive("MAIL FROM:<test@example.com>\r\n"), Does.Contain("250 OK"));

         // Without the RSET, a second MAIL FROM is rejected as a bad sequence.
         Assert.That(sim.SendAndReceive("RSET\r\n"), Does.Contain("250 OK"));
         Assert.That(sim.SendAndReceive("MAIL FROM:<test@example.com>\r\n"), Does.Contain("250 OK"));
      }

      [Test]
      public void TestTooManyInvalidCommandsHELOLastCommandOK()
      {
         var settings = _settings;
         settings.DisconnectInvalidClients = true;
         settings.MaxNumberOfInvalidCommands = 3;

         var sim = new TcpConnection();
         sim.Connect(25);
         sim.Receive(); // banner

         sim.SendAndReceive("HELO\r\n");
         sim.SendAndReceive("HELO\r\n");
         sim.SendAndReceive("HELO\r\n");
         var result = sim.SendAndReceive("HELO test.com\r\n");
         Assert.That(result.Contains("250 Hello."), Is.True, result);
      }

      [Test]
      public void TestTooManyInvalidCommandsUnknownRcptShouldBeCounted()
      {
         var settings = _settings;
         settings.DisconnectInvalidClients = true;
         settings.MaxNumberOfInvalidCommands = 3;

         var sim = new TcpConnection();
         sim.Connect(25);
         sim.Receive(); // banner

         sim.SendAndReceive("HELO example.com\r\n");
         sim.SendAndReceive("MAIL FROM: example@example.com\r\n");
         var result = sim.SendAndReceive("RCPT TO: unknown@example.test\r\n");
         Assert.That(result.Contains("550 Unknown user"), Is.True, result);
         result = sim.SendAndReceive("RCPT TO: unknown@example.test\r\n");
         Assert.That(result.Contains("550 Unknown user"), Is.True, result);
         result = sim.SendAndReceive("RCPT TO: unknown@example.test\r\n");
         Assert.That(result.Contains("550 Unknown user"), Is.True, result);
         result = sim.SendAndReceive("RCPT TO: unknown@example.test\r\n");
         Assert.That(result.Contains("Too many invalid commands"), Is.True, result);
      }

      [TestCase("", "VRFY test@example.test", "502 VRFY disallowed.")]
      [TestCase("", "TURN", "502 TURN disallowed.")]
      [TestCase("", "RCPT TO:<test@example.test>", "503 Must have sender first.")]
      [TestCase("", "DATA", "503 Must have sender and recipient first.")]
      [TestCase("MAIL FROM:<test@example.com>", "DATA", "503 Must have sender and recipient first.")]
      [TestCase("MAIL FROM:<test@example.com>", "MAIL FROM:<test@example.com>", "503 Issue a reset if you want to start over")]
      [Description("Issue #653: All 5xx replies to client errors should count towards the invalid commands limit.")]
      public void TestTooManyInvalidCommandsShouldCountAllClientErrors(string setupCommand, string invalidCommand, string expectedError)
      {
         _settings.DisconnectInvalidClients = true;
         _settings.MaxNumberOfInvalidCommands = 3;

         var sim = new TcpConnection();
         sim.Connect(25);
         sim.Receive(); // banner

         sim.SendAndReceive("HELO example.com\r\n");
         if (setupCommand != "")
            Assert.That(sim.SendAndReceive(setupCommand + "\r\n"), Does.Contain("250"));

         for (var i = 0; i < 3; i++)
            Assert.That(sim.SendAndReceive(invalidCommand + "\r\n"), Does.Contain(expectedError));

         var result = sim.SendAndReceive(invalidCommand + "\r\n");
         Assert.That(result, Does.Contain("Too many invalid commands"));
      }

      [Test]
      [Description("Issue #653: Too long lines should count towards the invalid commands limit.")]
      public void TestTooManyInvalidCommandsLineTooLongShouldBeCounted()
      {
         _settings.DisconnectInvalidClients = true;
         _settings.MaxNumberOfInvalidCommands = 3;

         var sim = new TcpConnection();
         sim.Connect(25);
         sim.Receive(); // banner

         sim.SendAndReceive("HELO example.com\r\n");

         var longLine = "NOOP " + new string('A', 600) + "\r\n";
         for (var i = 0; i < 3; i++)
            Assert.That(sim.SendAndReceive(longLine), Does.Contain("500 Line too long."));

         var result = sim.SendAndReceive(longLine);
         Assert.That(result, Does.Contain("Too many invalid commands"));
      }

      [Test]
      [Description("Issue #653: Rejections due to the SIZE parameter should count towards the invalid commands limit.")]
      public void TestTooManyInvalidCommandsSizeParameterShouldBeCounted()
      {
         _settings.MaxMessageSize = 1;

         AssertDisconnectedAfterRepeatedRejections(
            sim => sim.SendAndReceive("MAIL FROM:<test@example.com> SIZE=100000\r\n"),
            "552 Message size exceeds fixed maximum message size");
      }

      [Test]
      [Description("Issue #653: Rejections due to message size should count towards the invalid commands limit.")]
      public void TestTooManyInvalidCommandsMessageSizeShouldBeCounted()
      {
         SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");
         _settings.MaxMessageSize = 1;

         var body = new string('A', 70) + "\r\n";
         for (var i = 0; i < 5; i++) body += body;

         AssertDisconnectedAfterRepeatedRejections(
            sim => SendMessageAndReceive(sim, "test@example.com", "test@example.test", body),
            "554 Rejected - Message size exceeds fixed maximum message size");
      }

      [Test]
      [Description("Issue #653: Rejections due to bare LF's should count towards the invalid commands limit.")]
      public void TestTooManyInvalidCommandsBareLineFeedsShouldBeCounted()
      {
         SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         var previous = _settings.AllowIncorrectLineEndings;
         _settings.AllowIncorrectLineEndings = false;

         try
         {
            AssertDisconnectedAfterRepeatedRejections(
               sim => SendMessageAndReceive(sim, "test@example.com", "test@example.test", "Line 1\nLine 2"),
               "554 Rejected - Message containing bare LF's.");
         }
         finally
         {
            _settings.AllowIncorrectLineEndings = previous;
         }
      }

      [Test]
      [Description("Issue #653: Cancelled transmissions should count towards the invalid commands limit.")]
      public void TestTooManyInvalidCommandsCancelledTransmissionShouldBeCounted()
      {
         SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         AssertDisconnectedAfterRepeatedRejections(
            sim => SendMessageAndReceive(sim, "test@example.com", "test@example.test", new string('A', 100001)),
            "554 Too long line was received. Transmission aborted.");
      }

      [Test]
      [Description("Issue #653: Spam rejections should count towards the invalid commands limit.")]
      public void TestTooManyInvalidCommandsSpamRejectionShouldBeCounted()
      {
         SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         var antiSpam = _settings.AntiSpam;
         antiSpam.SpamDeleteThreshold = 1;
         antiSpam.CheckHostInHelo = true;
         antiSpam.CheckHostInHeloScore = 5;

         // Behind an incoming relay, the HELO address literal in the Received header is compared
         // to the originating IP address without DNS lookups.
         var incomingRelay = _settings.IncomingRelays.Add();
         incomingRelay.LowerIP = "127.0.0.1";
         incomingRelay.UpperIP = "127.0.0.1";
         incomingRelay.Name = "Test";
         incomingRelay.Save();

         var message = "Received: from [198.51.100.7] (unknown [203.0.113.99])\r\n" +
                       "\tby mail.example.test with ESMTP\r\n" +
                       "\t; Fri, 06 May 2016 03:49:14 +0200\r\n" +
                       "Subject: Test\r\n" +
                       "\r\n" +
                       "Body";

         AssertDisconnectedAfterRepeatedRejections(
            sim => SendMessageAndReceive(sim, "test@example.com", "test@example.test", message),
            "550 The host name specified in HELO does not match IP address.");
      }

      [TestCase("OnHELO(oClient)")]
      [TestCase("OnSMTPData(oClient, oMessage)")]
      [TestCase("OnAcceptMessage(oClient, oMessage)")]
      [Description("Issue #653: Script rejections should count towards the invalid commands limit.")]
      public void TestTooManyInvalidCommandsScriptRejectionShouldBeCounted(string eventSignature)
      {
         SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         var scripting = _settings.Scripting;
         File.WriteAllText(scripting.CurrentScriptFile,
            "Sub " + eventSignature + "\r\n" +
            "   Result.Value = 2\r\n" +
            "   Result.Message = \"Rejected by script\"\r\n" +
            "End Sub");
         scripting.Enabled = true;
         scripting.Reload();

         Func<TcpConnection, string> attempt;
         if (eventSignature.StartsWith("OnHELO"))
            attempt = sim => sim.SendAndReceive("HELO example.com\r\n");
         else
            attempt = sim => SendMessageAndReceive(sim, "test@example.com", "test@example.test", "Body");

         AssertDisconnectedAfterRepeatedRejections(attempt, "554 Rejected by script", !eventSignature.StartsWith("OnHELO"));
      }

      private void AssertDisconnectedAfterRepeatedRejections(Func<TcpConnection, string> attempt, string expectedError, bool sendHelo = true)
      {
         _settings.DisconnectInvalidClients = true;
         _settings.MaxNumberOfInvalidCommands = 3;

         var sim = new TcpConnection();
         sim.Connect(25);
         sim.Receive(); // banner

         if (sendHelo)
            sim.SendAndReceive("HELO example.com\r\n");

         for (var i = 0; i < 3; i++)
         {
            Assert.That(attempt(sim), Does.Contain(expectedError));
            sim.SendAndReceive("RSET\r\n");
         }

         Assert.That(attempt(sim), Does.Contain("Too many invalid commands"));
      }

      // Returns the first reply which isn't a positive one.
      private static string SendMessageAndReceive(TcpConnection sim, string from, string to, string body)
      {
         var result = sim.SendAndReceive("MAIL FROM:<" + from + ">\r\n");
         if (!result.StartsWith("250"))
            return result;

         result = sim.SendAndReceive("RCPT TO:<" + to + ">\r\n");
         if (!result.StartsWith("250"))
            return result;

         result = sim.SendAndReceive("DATA\r\n");
         if (!result.StartsWith("354"))
            return result;

         sim.Send(body + "\r\n.\r\n");
         return sim.ReadUntil("\r\n");
      }

      [Test]
      public void TestTooManyInvalidCommandsHELOSuccesfullCommandDoesNotResetCounter()
      {
         var settings = _settings;
         settings.DisconnectInvalidClients = true;
         settings.MaxNumberOfInvalidCommands = 3;

         var sim = new TcpConnection();
         sim.Connect(25);
         sim.Receive(); // banner

         sim.SendAndReceive("HELO\r\n");
         sim.SendAndReceive("HELO\r\n");
         sim.SendAndReceive("HELO\r\n");
         var result = sim.SendAndReceive("HELO test.com\r\n");
         Assert.That(result.Contains("250 Hello."), Is.True, result);

         result = sim.SendAndReceive("HELO\r\n");
         Assert.That(result.Contains("Too many invalid commands"), Is.True, result);
      }

      /// <summary>
      ///    If an error is rejected with a temp-error (4xx), that should not be counted
      ///    as an error which makes the client get disconnected.
      /// </summary>
      [Test]
      [Description("Issue 189, Greylisting rejections (try again) are treated as errors ")]
      public void TestTooManyInvalidCommandsTempError()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         for (var i = 0; i < 10; i++)
            SingletonProvider<TestSetup>.Instance.AddAccount(_domain, string.Format("test{0}@example.test", i), "test");

         var application = SingletonProvider<TestSetup>.Instance.GetApp();
         var settings = _settings;

         settings.DisconnectInvalidClients = true;
         settings.MaxNumberOfInvalidCommands = 5;

         settings.AntiSpam.GreyListingEnabled = true;

         var sim = new TcpConnection();
         sim.Connect(25);
         var res = sim.Receive();
         sim.Send("EHLO test.com\r\n");
         res = sim.Receive();
         sim.Send("MAIL FROM: <test@example.test>\r\n");
         res = sim.Receive();
         for (var i = 1; i < 10; i++)
         {
            var address = string.Format("test{0}@example.test", i);

            sim.Send("RCPT TO: " + address + "\r\n");

            res = sim.Receive();

            Assert.That(res, Is.EqualTo("451 Please try again later.\r\n"));
         }

         sim.Disconnect();
      }

      [Test]
      [Description("Issue 291, Sloppy non-delivery report generated")]
      public void TestValidEmailAddress()
      {
         var senderAccount = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "sender@example.test", "test");
         var recipientAccount = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "list@example.test",
            "test");

         recipientAccount.MaxSize = 1;
         recipientAccount.Save();

         // Build a 2MB string.
         var builder = new StringBuilder();
         for (var i = 0; i < 11000; i++)
            builder.Append(
               "1234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890\r\n");

         var text = builder.ToString();

         SmtpClientSimulator.StaticSend(senderAccount.Address, recipientAccount.Address, "", text);

         // Make sure the list did not receive it.
         CustomAsserts.AssertRecipientsInDeliveryQueue(0);

         // Check the syntax in the bounce message.
         var content = Pop3ClientSimulator.AssertGetFirstMessageText(senderAccount.Address, "test");

         // The bounce message should contain the MIME-version.
         Assert.That(content.Contains("MIME-Version: 1.0"), Is.True);

         // Lines may not end with space if they are QP-encoded.
         Assert.That(content.Contains("  Subject:=20"), Is.True);
      }

      [Test]
      public void TestTooLongEmailAddress()
      {
         var senderAccount = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "sender@example.test", "test");

         var tooLongAddress = new string('i', 260) + "@example.com";
         var ex = Assert.Throws<DeliveryFailedException>(() =>
            SmtpClientSimulator.StaticSend(senderAccount.Address, tooLongAddress, "", "foobar"));

         Assert.That(ex.Message, Does.Contain("550 A valid address is required."));
      }

      [Test]
      public void TestWelcomeMessage()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();
         _settings.WelcomeSMTP = "HOWDYHO";

         var simulator = new SmtpClientSimulator();

         var sWelcomeMessage = simulator.GetWelcomeMessage();

         if (sWelcomeMessage != "220 HOWDYHO ESMTP\r\n")
            throw new Exception("ERROR - Wrong welcome message.");
      }

      [Test]
      [Description(
         "If a message with 4 recipients on different domains, but all are to be sent through the same SMTP relay, only one message should be sent")]
      public void SmtpRelayShouldConsolidateRecipients()
      {
         // Set up a server listening on port 250 which accepts email for test@example.test
         var deliveryResults = new Dictionary<string, int>();
         deliveryResults["user1@test1.com"] = 250;
         deliveryResults["user2@test2.com"] = 250;
         deliveryResults["user3@test3.com"] = 250;
         deliveryResults["user4@test4.com"] = 250;

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            _application.Settings.SMTPRelayer = "localhost";
            _application.Settings.SMTPRelayerPort = smtpServerPort;

            server.AddRecipientResult(deliveryResults);
            server.StartListen();

            var smtpClient = new SmtpClientSimulator();

            var recipients = new List<string>
            {
               "user1@test1.com",
               "user2@test2.com",
               "user3@test3.com",
               "user4@test4.com"
            };

            var senderAccount =
               SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "sender@example.test", "test");
            smtpClient.Send(senderAccount.Address, recipients, "Test", "Test message");
            CustomAsserts.AssertRecipientsInDeliveryQueue(0);

            server.WaitForCompletion();

            Assert.That(server.MessageData.Contains("Test message"), Is.True);
            Assert.That(server.RcptTosReceived, Is.EqualTo(deliveryResults.Count));
         }
      }
   }
}