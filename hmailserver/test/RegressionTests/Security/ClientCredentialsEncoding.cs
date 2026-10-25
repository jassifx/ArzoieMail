// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RegressionTests.Infrastructure;
using RegressionTests.Shared;

namespace RegressionTests.Security
{
   // The credentials the server sends as a client - AUTH LOGIN to the server a route relays
   // through, USER and PASS to the POP3 server of an external account - go as UTF-8, as servers
   // take them. They went in the system code page, so a password with an a-umlaut reached the
   // other server as a byte it did not take for one, and a character outside the page not at all.
   [TestFixture]
   public class ClientCredentialsEncoding : TestFixtureBase
   {
      private const string Username = "üser@example.com";
      private const string Password = "Pässword ✓ 漢 ' \\";

      [Test]
      [Description("A route authenticates with its user name and password as UTF-8.")]
      public void TestRouteAuthenticatesInUtf8()
      {
         SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(new Dictionary<string, int> { ["test@dummy-example.com"] = 250 });
            server.StartListen();

            var route = TestSetup.AddRoutePointingAtLocalhost(1, smtpServerPort, false);
            route.RelayerRequiresAuth = true;
            route.RelayerAuthUsername = Username;
            route.SetRelayerAuthPassword(Password);
            route.Save();

            var smtp = new SmtpClientSimulator();
            smtp.Send("test@example.test", new List<string> { "test@dummy-example.com" }, "Test", "Test message");

            server.WaitForCompletion();
            CustomAsserts.AssertRecipientsInDeliveryQueue(0);

            Assert.That(server.AuthUsername, Is.EqualTo(Username));
            Assert.That(server.AuthPassword, Is.EqualTo(Password));
         }
      }

      [Test]
      [Description("An external account logs on to its POP3 server with its user name and password as UTF-8.")]
      public void TestExternalAccountLogsOnInUtf8()
      {
         var messages = new List<string> { "From: a@example.com\r\nTo: b@example.com\r\nSubject: Test\r\n\r\nHello!" };

         var port = TestSetup.GetNextFreePort();
         using (var pop3Server = new Pop3ServerSimulator(1, port, messages))
         {
            pop3Server.StartListen();

            var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "user@example.test", "test");
            var fa = account.FetchAccounts.Add();
            fa.Enabled = true;
            fa.MinutesBetweenFetch = 10;
            fa.Name = "Test";
            fa.Username = Username;
            fa.Password = Password;
            fa.UseSSL = false;
            fa.ServerAddress = "localhost";
            fa.Port = port;
            fa.ProcessMIMERecipients = false;
            fa.Save();

            fa.DownloadNow();
            pop3Server.WaitForCompletion();
            fa.Delete();

            // Wait for delivery, so the next test's setup does not delete the account mid-delivery.
            Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

            // The simulator reads a plain connection as ASCII, so each byte above 127 reads as '?':
            // the UTF-8 bytes leave as many as UTF-8 has - two for the u-umlaut, three for the check
            // mark - where the system code page left one, or none.
            Assert.That(pop3Server.ReceivedUsername, Is.EqualTo(AsReadAsAscii(Username)));
            Assert.That(pop3Server.ReceivedPassword, Is.EqualTo(AsReadAsAscii(Password)));
         }
      }

      private static string AsReadAsAscii(string text)
      {
         return new string(Encoding.UTF8.GetBytes(text).Select(b => b < 128 ? (char) b : '?').ToArray());
      }
   }
}
