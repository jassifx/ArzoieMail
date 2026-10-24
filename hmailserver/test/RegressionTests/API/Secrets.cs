// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
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

      private const string Passphrase = "correct horse battery staple";

      [Test]
      [Description("The installation's key exports under a passphrase and imports again unchanged; " +
                   "a short passphrase does not export, and a wrong one does not import.")]
      public void TestSecretKeyExportsAndImports()
      {
         var utilities = _application.Utilities;

         var keyId = utilities.SecretKeyId;
         Assert.That(keyId, Does.Match("^[0-9a-f]{8}$"));

         Assert.Throws<COMException>(() => utilities.ExportSecretKey("too short"));

         var exported = utilities.ExportSecretKey(Passphrase);
         Assert.That(exported, Does.StartWith("arzoiemail-key1:"));

         Assert.Throws<COMException>(() => utilities.ImportSecretKey(exported, "wrong horse battery staple"));

         utilities.ImportSecretKey(exported, Passphrase);
         Assert.That(utilities.SecretKeyId, Is.EqualTo(keyId));
      }

      [Test]
      [Description("Importing another installation's key makes it this one's, and protects every secret under it: " +
                   "a route still authenticates with its password.")]
      public void TestImportingAnotherKeyProtectsSecretsUnderIt()
      {
         const string password = "Röute password ' \\";

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

            // An export of a key no installation has used yet, made the way ExportSecretKey makes one.
            var otherKey = RandomNumberGenerator.GetBytes(32);
            _application.Utilities.ImportSecretKey(ExportKey(otherKey, Passphrase), Passphrase);

            Assert.That(_application.Utilities.SecretKeyId, Is.EqualTo(KeyIdOf(otherKey)));

            // Every secret is protected under the imported key now, the route password included.
            _application.Utilities.RunTestSuite("I know what I am doing.");

            var smtp = new SmtpClientSimulator();
            smtp.Send("test@example.test", new List<string> { "test@dummy-example.com" }, "Test", "Test message");

            server.WaitForCompletion();
            CustomAsserts.AssertRecipientsInDeliveryQueue(0);

            Assert.That(server.AuthPassword, Is.EqualTo(password));
         }
      }

      // arzoiemail-key1:<base64 of salt, iterations, nonce, ciphertext and tag>, the key encrypted
      // with AES-256-GCM under PBKDF2-HMAC-SHA256 of the passphrase, salt and iterations as
      // associated data - what SecretProtector::ExportKey writes.
      private static string ExportKey(byte[] key, string passphrase)
      {
         // A variable, not a constant: the byte conversions below keep only the low byte, which a
         // constant expression is not allowed to lose.
         int iterations = 600000;

         var header = new byte[20];
         RandomNumberGenerator.Fill(header.AsSpan(0, 16));
         header[16] = (byte) (iterations >> 24);
         header[17] = (byte) (iterations >> 16);
         header[18] = (byte) (iterations >> 8);
         header[19] = (byte) iterations;

         var derived = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), header.AsSpan(0, 16).ToArray(),
            iterations, HashAlgorithmName.SHA256, 32);

         var nonce = RandomNumberGenerator.GetBytes(12);
         var ciphertext = new byte[key.Length];
         var tag = new byte[16];

         using (var aes = new AesGcm(derived, 16))
            aes.Encrypt(nonce, key, ciphertext, tag, header);

         var sealedKey = new byte[header.Length + nonce.Length + ciphertext.Length + tag.Length];
         header.CopyTo(sealedKey, 0);
         nonce.CopyTo(sealedKey, header.Length);
         ciphertext.CopyTo(sealedKey, header.Length + nonce.Length);
         tag.CopyTo(sealedKey, header.Length + nonce.Length + ciphertext.Length);

         return "arzoiemail-key1:" + Convert.ToBase64String(sealedKey);
      }

      // The first eight hex digits of the SHA-256 of the key.
      private static string KeyIdOf(byte[] key)
      {
         return Convert.ToHexString(SHA256.HashData(key), 0, 4).ToLowerInvariant();
      }
   }
}
