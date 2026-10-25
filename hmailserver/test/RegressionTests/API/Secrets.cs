// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

using System.Collections.Generic;
using NUnit.Framework;
using RegressionTests.Infrastructure;
using RegressionTests.Shared;

namespace RegressionTests.API
{
   // The secrets the server reads back - route and external account passwords, the relay
   // password, the database password - are protected under the installation's key with
   // AES-256-GCM. Earlier versions Blowfish-encrypted them with a key in the source code; the
   // server protects those again when it starts, and reads them in the meantime.
   [TestFixture]
   public class Secrets : TestFixtureBase
   {
      [Test]
      [Description("A route password an earlier version Blowfish-encrypted is protected again when the server starts, " +
                   "and the route still authenticates with it.")]
      public void TestBlowfishRoutePasswordIsMigratedAndStillWorks()
      {
         // ASCII: the SMTP client sends AUTH credentials in the system code page rather than
         // UTF-8, which is a matter of its own. SecretProtectorTester covers values that are not
         // ASCII.
         const string password = "Old password ' \\ ;--";

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(new Dictionary<string, int> { ["test@dummy-example.com"] = 250 });
            server.StartListen();

            var route = TestSetup.AddRoutePointingAtLocalhost(1, smtpServerPort, false);
            route.RelayerRequiresAuth = true;
            route.RelayerAuthUsername = "user@example.com";
            route.SetRelayerAuthPassword("set through the API, replaced below");
            route.Save();

            // What an earlier version stored: the password Blowfish-encrypted, hex digits only.
            var blowfish = _application.Utilities.BlowfishEncrypt(password);
            Assert.That(blowfish, Does.Match("^[0-9A-Fa-f]+$"));
            _application.Database.ExecuteSQL(
               $"update hm_routes set routeauthenticationpassword = '{blowfish}' where routeid = {route.ID}");

            // The migration runs when the server starts.
            _application.Reinitialize();

            // Among the class tests: every secret in the database, this one included, is now
            // protected under the installation's key.
            _application.Utilities.RunTestSuite("I know what I am doing.");

            var smtp = new SmtpClientSimulator();
            smtp.Send("test@example.test", new List<string> { "test@dummy-example.com" }, "Test", "Test message");

            server.WaitForCompletion();
            CustomAsserts.AssertRecipientsInDeliveryQueue(0);

            Assert.That(server.AuthPassword, Is.EqualTo(password));
         }
      }

      [Test]
      [Description("A route password set through the API is the one the route authenticates with.")]
      public void TestProtectedRoutePasswordAuthenticates()
      {
         // ASCII, as in the test above.
         const string password = "New ' \\ \" ;-- @VALUE";

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(new Dictionary<string, int> { ["test@dummy-example.com"] = 250 });
            server.StartListen();

            var route = TestSetup.AddRoutePointingAtLocalhost(1, smtpServerPort, false);
            route.RelayerRequiresAuth = true;
            route.RelayerAuthUsername = "user@example.com";
            route.SetRelayerAuthPassword(password);
            route.Save();

            var smtp = new SmtpClientSimulator();
            smtp.Send("test@example.test", new List<string> { "test@dummy-example.com" }, "Test", "Test message");

            server.WaitForCompletion();
            CustomAsserts.AssertRecipientsInDeliveryQueue(0);

            Assert.That(server.AuthPassword, Is.EqualTo(password));
         }
      }
   }
}
