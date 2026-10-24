// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using hMailServer;
using NUnit.Framework;
using RegressionTests.Infrastructure;
using RegressionTests.Shared;

namespace RegressionTests.Security
{
   [TestFixture]
   public class PasswordMasking : TestFixtureBase
   {
      [SetUp]
      public new void SetUp()
      {
         _status = SingletonProvider<TestSetup>.Instance.GetApp().Status;
         var logFile = _settings.Logging.CurrentDefaultLog;
         if (File.Exists(logFile))
            File.Delete(logFile);
      }

      private Status _status;

      private const string _username = "NonSecretUser@example.test";
      private const string _password = "SecretPassword";

      private string EncodeBase64(string s)
      {
         var bytes = Encoding.UTF8.GetBytes(s);
         return Convert.ToBase64String(bytes);
      }

      private string GetUsername()
      {
         return _username;
      }

      private string GetPassword()
      {
         return _password;
      }


      private void EnsureNoPassword(bool usernameExpected = true)
      {
         var logFileName = _settings.Logging.CurrentDefaultLog;

         for (var i = 1; i <= 10; i++)
         {
            var text = string.Empty;

            try
            {
               using (var fileStream = File.Open(logFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
               using (var streamReader = new StreamReader(fileStream))
               {
                  text = streamReader.ReadToEnd();
               }
            }
            catch
            {
               if (i == 10)
                  throw;

               Thread.Sleep(1000);
               continue;
            }

            if (usernameExpected)
               Assert.That(text.Contains(_username) || text.Contains(EncodeBase64(_username)), Is.True, text);
            else
               Assert.That(text.Contains(_username) || text.Contains(EncodeBase64(_username)), Is.False, text);

            Assert.That(text.Contains(_password) || text.Contains(EncodeBase64(_password)), Is.False, text);
            Assert.That(text.Contains("***"), Is.True, text);
         }
      }

      internal Route AddRoutePointingAtLocalhostWithAuth(int numberOfTries, int port)
      {
         // Add a route pointing at localhost
         var settings = SingletonProvider<TestSetup>.Instance.GetApp().Settings;

         var route = settings.Routes.Add();
         route.DomainName = "dummy-example.com";
         route.TargetSMTPHost = "localhost";
         route.TargetSMTPPort = port;
         route.NumberOfTries = numberOfTries;
         route.RelayerRequiresAuth = true;
         route.RelayerAuthUsername = GetUsername();
         route.SetRelayerAuthPassword(GetPassword());
         route.MinutesBetweenTry = 5;
         route.Save();

         return route;
      }

      [Test]
      public void TestIMAPServerLiteral()
      {
         var sim = new ImapClientSimulator();
         sim.Connect();
         Assert.That(sim.Send("a01 login " + GetUsername() + " {4}").StartsWith("+"), Is.True);
         sim.Send(GetPassword());
         EnsureNoPassword();
      }

      [Test]
      public void TestIMAPServerLiteral2()
      {
         var sim = new ImapClientSimulator();
         sim.Connect();
         Assert.That(sim.Send("a01 login {" + GetUsername().Length + "} {4}").StartsWith("+"), Is.True);
         Assert.That(sim.Send(GetUsername() + " {" + GetPassword().Length + "}").StartsWith("+"), Is.True);
         sim.Send(GetPassword());
         EnsureNoPassword();
      }

      [Test]
      public void TestIMAPServerNormal()
      {
         var sim = new ImapClientSimulator();
         sim.ConnectAndLogon(GetUsername(), GetPassword());
         EnsureNoPassword();
      }

      [Test]
      public void TestPOP3Client()
      {
         var messages = new List<string>();

         var message = "From: Martin@example.com\r\n" +
                       "To: Martin@example.com\r\n" +
                       "Subject: Test\r\n" +
                       "\r\n" +
                       "Hello!";

         messages.Add(message);

         var port = TestSetup.GetNextFreePort();
         using (var pop3Server = new Pop3ServerSimulator(1, port, messages))
         {
            pop3Server.StartListen();

            var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "user@example.test", "test");
            var fa = account.FetchAccounts.Add();

            fa.Enabled = true;
            fa.MinutesBetweenFetch = 10;
            fa.Name = "Test";
            fa.Username = GetUsername();
            fa.Password = GetPassword();
            fa.UseSSL = false;
            fa.ServerAddress = "localhost";
            fa.Port = port;
            fa.ProcessMIMERecipients = false;
            fa.Save();

            fa.DownloadNow();

            pop3Server.WaitForCompletion();

            fa.Delete();

            var downloadedMessage = Pop3ClientSimulator.AssertGetFirstMessageText(account.Address, "test");
            Assert.That(downloadedMessage, Does.Contain("Hello!"));

            EnsureNoPassword();
         }
      }

      [Test]
      public void TestPOP3Server()
      {
         var sim = new Pop3ClientSimulator();
         sim.ConnectAndLogon(GetUsername(), GetPassword());
         EnsureNoPassword();
      }

      [Test]
      public void TestSMTPClient()
      {
         var account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

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
            AddRoutePointingAtLocalhostWithAuth(0, smtpServerPort);

            // Send message to this route.
            var smtp = new SmtpClientSimulator();
            smtp.Send("test@example.test", "test@dummy-example.com", "Test", "Test message");

            CustomAsserts.AssertRecipientsInDeliveryQueue(0);

            var undeliveredMessages = _status.UndeliveredMessages;

            // Wait for the client to disconnect.
            server.WaitForCompletion();

            CustomAsserts.AssertRecipientsInDeliveryQueue(0);

            EnsureNoPassword();
         }
      }

      [Test]
      public void TestSMTPServerAuthLogin()
      {
         var sim = new SmtpClientSimulator();
         string errorMsg;

         CustomAsserts.Throws<AuthenticationException>(() =>
            sim.ConnectAndLogon(GetUsername(), GetPassword(), out errorMsg));

         EnsureNoPassword();
      }

      [Test]
      public void TestSMTPServerAuthLoginUsernameAsThirdParameter()
      {
         _settings.AllowSMTPAuthPlain = true;

         var sock = new TcpConnection();
         sock.Connect(25);
         Assert.That(sock.Receive().StartsWith("220"), Is.True);
         sock.Send("EHLO test.com\r\n");
         Assert.That(sock.Receive().StartsWith("250"), Is.True);

         var base64EncodedUsername = EncodeBase64(GetUsername());
         sock.Send("AUTH LOGIN " + base64EncodedUsername + "\r\n");
         Assert.That(sock.Receive().StartsWith("334"), Is.True);

         sock.Send(EncodeBase64(GetPassword()) + "\r\n");
         Assert.That(sock.Receive().StartsWith("535"), Is.True);
         EnsureNoPassword();
      }

      [Test]
      public void TestSMTPServerAuthPlain()
      {
         _settings.AllowSMTPAuthPlain = true;

         var sock = new TcpConnection();
         sock.Connect(25);
         Assert.That(sock.Receive().StartsWith("220"), Is.True);
         sock.Send("EHLO test.com\r\n");
         Assert.That(sock.Receive().StartsWith("250"), Is.True);
         sock.Send("AUTH PLAIN\r\n");
         Assert.That(sock.Receive().StartsWith("334"), Is.True);

         var str = "\t" + GetUsername() + "\t" + GetPassword();

         sock.Send(EncodeBase64(str) + "\r\n");
         Assert.That(sock.Receive().StartsWith("535"), Is.True);
         EnsureNoPassword();
      }

      [Test]
      public void TestSMTPServerAuthPlainSingleLine()
      {
         _settings.AllowSMTPAuthPlain = true;

         var sock = new TcpConnection();
         sock.Connect(25);
         Assert.That(sock.Receive().StartsWith("220"), Is.True);
         sock.Send("EHLO test.com\r\n");
         Assert.That(sock.Receive().StartsWith("250"), Is.True);
         sock.Send("AUTH PLAIN 77+9dXNlcm5hbWVAZG9tYWluLmNvbe+/vVBAc3N3b3Jk\r\n");
         Assert.That(sock.Receive().StartsWith("535"), Is.True);

         EnsureNoPassword(false);
      }

      [Test]
      [Description("The authentication identity is logged, not the authorization identity.")]
      public void TestSMTPServerAuthPlainWithAuthorizationIdentity()
      {
         _settings.AllowSMTPAuthPlain = true;

         var sock = new TcpConnection();
         sock.Connect(25);
         Assert.That(sock.Receive().StartsWith("220"), Is.True);
         sock.Send("EHLO test.com\r\n");
         Assert.That(sock.Receive().StartsWith("250"), Is.True);
         sock.Send("AUTH PLAIN\r\n");
         Assert.That(sock.Receive().StartsWith("334"), Is.True);

         var str = GetUsername() + "\0" + GetUsername() + "\0" + GetPassword();

         sock.Send(EncodeBase64(str) + "\r\n");
         Assert.That(sock.Receive().StartsWith("535"), Is.True);

         Assert.That(LogHandler.DefaultLogContains("RECEIVED: " + EncodeBase64(GetUsername()) + " ***"), Is.True);
         EnsureNoPassword();
      }

      [Test]
      [Description("The authentication identity is logged, not the authorization identity.")]
      public void TestSMTPServerAuthPlainSingleLineWithAuthorizationIdentity()
      {
         _settings.AllowSMTPAuthPlain = true;

         var sock = new TcpConnection();
         sock.Connect(25);
         Assert.That(sock.Receive().StartsWith("220"), Is.True);
         sock.Send("EHLO test.com\r\n");
         Assert.That(sock.Receive().StartsWith("250"), Is.True);

         var str = GetUsername() + "\0" + GetUsername() + "\0" + GetPassword();

         sock.Send("AUTH PLAIN " + EncodeBase64(str) + "\r\n");
         Assert.That(sock.Receive().StartsWith("535"), Is.True);

         Assert.That(LogHandler.DefaultLogContains("RECEIVED: AUTH PLAIN " + EncodeBase64(GetUsername()) + " ***"), Is.True);
         EnsureNoPassword();
      }

      [Test]
      [Description("Issue 654: AUTH PLAIN with credentials, sent before EHLO.")]
      public void TestSMTPServerAuthPlainSingleLineBeforeEhlo()
      {
         _settings.AllowSMTPAuthPlain = true;

         var sock = new TcpConnection();
         sock.Connect(25);
         Assert.That(sock.Receive().StartsWith("220"), Is.True);

         var credentials = EncodeBase64("\0" + GetUsername() + "\0" + GetPassword());
         sock.Send("AUTH PLAIN " + credentials + "\r\n");
         Assert.That(sock.Receive().StartsWith("503"), Is.True);

         EnsureCredentialsNotLogged(sock, credentials);
      }

      [Test]
      [Description("Issue 654: AUTH PLAIN with credentials, followed by a trailing space.")]
      public void TestSMTPServerAuthPlainSingleLineWithTrailingSpace()
      {
         _settings.AllowSMTPAuthPlain = true;

         var sock = ConnectAndSayHello();

         var credentials = EncodeBase64("\0" + GetUsername() + "\0" + GetPassword());
         sock.Send("AUTH PLAIN " + credentials + " \r\n");
         sock.Receive();

         EnsureCredentialsNotLogged(sock, credentials);
      }

      [Test]
      [Description("Issue 654: AUTH PLAIN with credentials which lack base64 padding.")]
      public void TestSMTPServerAuthPlainSingleLineWithoutPadding()
      {
         _settings.AllowSMTPAuthPlain = true;

         var sock = ConnectAndSayHello();

         var credentials = EncodeBase64(GetUsername() + "\0" + GetUsername() + "\0" + GetPassword()).TrimEnd('=');
         sock.Send("AUTH PLAIN " + credentials + "\r\n");
         sock.Receive();

         EnsureCredentialsNotLogged(sock, credentials);
      }

      [Test]
      [Description("Issue 654: Client sends AUTH PLAIN credentials although AUTH PLAIN is disabled.")]
      public void TestSMTPServerAuthPlainDisabled()
      {
         _settings.AllowSMTPAuthPlain = false;

         var sock = ConnectAndSayHello();
         sock.Send("AUTH PLAIN\r\n");
         Assert.That(sock.Receive().StartsWith("504"), Is.True);

         var credentials = EncodeBase64("\0" + GetUsername() + "\0" + GetPassword());
         sock.Send(credentials + "\r\n");
         Assert.That(sock.Receive().StartsWith("503"), Is.True);

         EnsureCredentialsNotLogged(sock, credentials);
      }

      [Test]
      [Description("Client sends AUTH LOGIN credentials although the AUTH command was refused.")]
      public void TestSMTPServerAuthLoginRefused()
      {
         var sock = new TcpConnection();
         sock.Connect(25);
         Assert.That(sock.Receive().StartsWith("220"), Is.True);

         // AUTH is refused before EHLO.
         sock.Send("AUTH LOGIN\r\n");
         Assert.That(sock.Receive().StartsWith("503"), Is.True);

         sock.Send(EncodeBase64(GetUsername()) + "\r\n");
         Assert.That(sock.Receive().StartsWith("503"), Is.True);

         var credentials = EncodeBase64(GetPassword());
         sock.Send(credentials + "\r\n");
         Assert.That(sock.Receive().StartsWith("503"), Is.True);

         EnsureCredentialsNotLogged(sock, credentials);

         // The user name is not secret, so it is logged, as for an accepted AUTH LOGIN.
         Assert.That(LogHandler.DefaultLogContains("RECEIVED: " + EncodeBase64(GetUsername())), Is.True);
      }

      [Test]
      [Description("Client sends the AUTH LOGIN password although the AUTH command, with the user name, was refused.")]
      public void TestSMTPServerAuthLoginWithUsernameRefused()
      {
         var sock = new TcpConnection();
         sock.Connect(25);
         Assert.That(sock.Receive().StartsWith("220"), Is.True);

         // AUTH is refused before EHLO.
         sock.Send("AUTH LOGIN " + EncodeBase64(GetUsername()) + "\r\n");
         Assert.That(sock.Receive().StartsWith("503"), Is.True);

         // The user name was on the AUTH line, so the next line is the password.
         var credentials = EncodeBase64(GetPassword());
         sock.Send(credentials + "\r\n");
         Assert.That(sock.Receive().StartsWith("503"), Is.True);

         EnsureCredentialsNotLogged(sock, credentials);
      }

      [Test]
      [Description("An unknown command is logged as is, when no AUTH command precedes it.")]
      public void TestSMTPServerUnknownCommandIsLogged()
      {
         var sock = ConnectAndSayHello();

         sock.Send("XFOO\r\n");
         Assert.That(sock.Receive().StartsWith("503"), Is.True);

         sock.Send("NOOP\r\n");
         Assert.That(sock.Receive().StartsWith("250"), Is.True);
         Assert.That(LogHandler.DefaultLogContains("RECEIVED: NOOP"), Is.True);

         var log = LogHandler.ReadCurrentDefaultLog();
         Assert.That(log.Contains("RECEIVED: XFOO"), Is.True, log);
      }

      private TcpConnection ConnectAndSayHello()
      {
         var sock = new TcpConnection();
         sock.Connect(25);
         Assert.That(sock.Receive().StartsWith("220"), Is.True);
         sock.Send("EHLO test.com\r\n");
         Assert.That(sock.Receive().StartsWith("250"), Is.True);
         return sock;
      }

      private void EnsureCredentialsNotLogged(TcpConnection sock, string credentials)
      {
         // Wait until the commands sent so far have been logged.
         sock.Send("NOOP\r\n");
         Assert.That(sock.Receive().StartsWith("250"), Is.True);
         Assert.That(LogHandler.DefaultLogContains("RECEIVED: NOOP"), Is.True);

         var log = LogHandler.ReadCurrentDefaultLog();
         Assert.That(log.Contains(credentials), Is.False, log);
         Assert.That(log.Contains(GetPassword()), Is.False, log);
      }
   }
}