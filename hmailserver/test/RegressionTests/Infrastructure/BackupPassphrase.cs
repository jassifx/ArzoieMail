// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using hMailServer;
using NUnit.Framework;
using RegressionTests.Shared;

namespace RegressionTests.Infrastructure
{
   // A backup holds the secrets of the installation: route and external account passwords, the
   // relay password, the SRS secret. With a passphrase the whole archive is encrypted under it -
   // AES-256, file names included - and the backup restores on any computer that has the
   // passphrase. Without one the secrets stay protected under the installation's key, as in the
   // database, rather than plain in an archive anyone can open.
   [TestFixture]
   public class BackupPassphrase : TestFixtureBase
   {
      private const string Passphrase = "correct horse battery staple";

      // ASCII: until the SMTP client sends credentials as UTF-8, it sends them in the system
      // code page.
      private const string RoutePassword = "Route ' \\ \" ;-- secret";

      private string _backupDir;

      [SetUp]
      public new void SetUp()
      {
         _backupDir = Path.Combine(TestSetup.GetSharedTempDirectory(), TestSetup.UniqueString());
         Directory.CreateDirectory(_backupDir);

         _application.Settings.Backup.Passphrase = "";
      }

      [TearDown]
      public new void TearDown()
      {
         _application.Settings.Backup.Passphrase = "";

         if (Directory.Exists(_backupDir))
            Directory.Delete(_backupDir, true);
      }

      [Test]
      [Description("A passphrase shorter than the one for exporting the secret key is refused; " +
                   "it cannot be read back, only whether there is one.")]
      public void TestShortPassphraseIsRefused()
      {
         var backupSettings = _application.Settings.Backup;

         var ex = Assert.Throws<COMException>(() => backupSettings.Passphrase = "too short");
         Assert.That(ex.Message, Does.Contain("at least 12 characters"));
         Assert.That(backupSettings.PassphraseIsSet, Is.False);

         backupSettings.Passphrase = Passphrase;
         Assert.That(backupSettings.PassphraseIsSet, Is.True);

         backupSettings.Passphrase = "";
         Assert.That(backupSettings.PassphraseIsSet, Is.False);
      }

      [Test]
      [Description("A backup with a passphrase restores with it, its route password included.")]
      public void TestEncryptedBackupRestoresWithPassphrase()
      {
         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(new Dictionary<string, int> { ["test@dummy-example.com"] = 250 });
            server.StartListen();

            AddRouteWithPassword(smtpServerPort);

            _application.Settings.Backup.Passphrase = Passphrase;
            var backupFile = BackupSettings();

            DeleteRoutes();
            Restore(_application.BackupManager.LoadEncryptedBackup(backupFile, Passphrase));

            AssertRouteAuthenticates(server);
         }
      }

      [Test]
      [Description("A backup with a passphrase cannot be opened without it: not by 7-Zip, " +
                   "not by the server with another passphrase or none.")]
      public void TestEncryptedBackupDoesNotOpenWithoutPassphrase()
      {
         AddRouteWithPassword(TestSetup.GetNextFreePort());

         _application.Settings.Backup.Passphrase = Passphrase;
         var backupFile = BackupSettings();

         Assert.That(Run7Zip($"t \"{backupFile}\" -p\"{Passphrase}\""), Is.EqualTo(0));
         Assert.That(Run7Zip($"t \"{backupFile}\" -p\"wrong passphrase\""), Is.Not.EqualTo(0));

         // Without the passphrase there is nothing in it to restore.
         var backup = _application.BackupManager.LoadBackup(backupFile);
         Assert.That(backup.ContainsSettings, Is.False);
         Assert.That(backup.ContainsDomains, Is.False);
         Assert.That(backup.ContainsMessages, Is.False);

         var ex = Assert.Throws<COMException>(() => _application.BackupManager.LoadEncryptedBackup(backupFile, "wrong passphrase"));
         Assert.That(ex.Message, Does.Contain("Check the passphrase"));
      }

      [Test]
      [Description("A backup without a passphrase holds its secrets protected under the installation's key, " +
                   "not plain, and restores them on the same installation.")]
      public void TestBackupWithoutPassphraseKeepsSecretsProtected()
      {
         const string relayPassword = "Relay password secret";

         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(new Dictionary<string, int> { ["test@dummy-example.com"] = 250 });
            server.StartListen();

            AddRouteWithPassword(smtpServerPort);
            _application.Settings.SetSMTPRelayerPassword(relayPassword);

            var backupFile = BackupSettings();

            var xml = ReadBackupXml(backupFile, out _);
            // The part of the route password XML leaves as it is.
            Assert.That(xml, Does.Not.Contain(";-- secret"));
            Assert.That(xml, Does.Not.Contain(relayPassword));
            Assert.That(xml, Does.Contain("Secrets=\"InstallationKey\""));
            Assert.That(xml, Does.Contain("aes1:"));

            DeleteRoutes();
            Restore(_application.BackupManager.LoadBackup(backupFile));

            AssertRouteAuthenticates(server);
         }
      }

      [Test]
      [Description("A backup made by an earlier version - route passwords Blowfish-encrypted, " +
                   "nothing saying how the secrets are held - still restores them.")]
      public void TestBackupOfEarlierVersionRestoresRoutePassword()
      {
         var smtpServerPort = TestSetup.GetNextFreePort();
         using (var server = new SmtpServerSimulator(1, smtpServerPort))
         {
            server.AddRecipientResult(new Dictionary<string, int> { ["test@dummy-example.com"] = 250 });
            server.StartListen();

            AddRouteWithPassword(smtpServerPort);
            var backupFile = BackupSettings();

            // Rewrite the backup as an earlier version wrote it.
            var xml = ReadBackupXml(backupFile, out var encoding);
            xml = xml.Replace(" Secrets=\"InstallationKey\"", "");
            xml = Regex.Replace(xml, "Password=\"aes1:[^\"]*\"",
               "Password=\"" + _application.Utilities.BlowfishEncrypt(RoutePassword) + "\"");

            // Its secret settings were plain; there are none to restore here.
            xml = Regex.Replace(xml, "StringValue=\"aes1:[^\"]*\"", "StringValue=\"\"");
            Assert.That(xml, Does.Not.Contain("aes1:"));

            var xmlDir = Path.Combine(_backupDir, "Legacy");
            Directory.CreateDirectory(xmlDir);
            var xmlFile = Path.Combine(xmlDir, "hMailServerBackup.xml");
            File.WriteAllText(xmlFile, xml, encoding);

            File.Delete(backupFile);
            Assert.That(Run7Zip($"a \"{backupFile}\" \"{xmlFile}\""), Is.EqualTo(0));

            DeleteRoutes();
            Restore(_application.BackupManager.LoadBackup(backupFile));

            AssertRouteAuthenticates(server);
         }
      }

      private void AddRouteWithPassword(int port)
      {
         var route = TestSetup.AddRoutePointingAtLocalhost(1, port, false);
         route.RelayerRequiresAuth = true;
         route.RelayerAuthUsername = "user@example.com";
         route.SetRelayerAuthPassword(RoutePassword);
         route.Save();
      }

      private void DeleteRoutes()
      {
         var routes = _application.Settings.Routes;
         while (routes.Count > 0)
            routes[0].Delete();
      }

      private static void AssertRouteAuthenticates(SmtpServerSimulator server)
      {
         var smtp = new SmtpClientSimulator();
         smtp.Send("test@example.test", new List<string> { "test@dummy-example.com" }, "Test", "Test message");

         server.WaitForCompletion();
         CustomAsserts.AssertRecipientsInDeliveryQueue(0);

         Assert.That(server.AuthPassword, Is.EqualTo(RoutePassword));
      }

      /// <summary>
      /// Backs up the settings, compressed, and returns the archive.
      /// </summary>
      private string BackupSettings()
      {
         var backupSettings = _application.Settings.Backup;
         backupSettings.BackupSettings = true;
         backupSettings.BackupDomains = false;
         backupSettings.BackupMessages = false;
         backupSettings.CompressDestinationFiles = true;
         backupSettings.Destination = _backupDir;

         CustomAsserts.AssertDeleteFile(backupSettings.LogFile);

         _application.BackupManager.StartBackup();

         for (var i = 0; i < 80; i++)
         {
            try
            {
               var contents = TestSetup.ReadExistingTextFile(backupSettings.LogFile);

               if (contents.Contains("Backup completed successfully"))
                  return Directory.GetFiles(_backupDir, "HMBackup*.7z")[0];

               if (contents.Contains("BACKUP ERROR:"))
                  Assert.Fail(contents);
            }
            catch (IOException)
            {
               // The server is still writing the log.
            }

            Thread.Sleep(250);
         }

         Assert.Fail("The backup did not complete.");
         return null;
      }

      private void Restore(Backup backup)
      {
         var startTime = _application.Status.StartTime;

         Assert.That(backup.ContainsSettings, Is.True);
         backup.RestoreSettings = true;
         backup.RestoreDomains = false;
         backup.RestoreMessages = false;
         backup.StartRestore();

         // The server restarts once the restore is done.
         for (var i = 0; i < 600; i++)
         {
            var newStartTime = _application.Status.StartTime;
            if (newStartTime.Length > 0 && newStartTime != startTime)
               return;

            Thread.Sleep(100);
         }

         Assert.Fail("The restore did not complete.");
      }

      private string ReadBackupXml(string backupFile, out System.Text.Encoding encoding)
      {
         var extractDir = Path.Combine(_backupDir, "Extracted");
         Assert.That(Run7Zip($"e \"{backupFile}\" -o\"{extractDir}\" hMailServerBackup.xml -y"), Is.EqualTo(0));

         using (var reader = new StreamReader(Path.Combine(extractDir, "hMailServerBackup.xml"), true))
         {
            var xml = reader.ReadToEnd();
            encoding = reader.CurrentEncoding;
            return xml;
         }
      }

      private int Run7Zip(string arguments)
      {
         var executable = Path.Combine(_application.Settings.Directories.ProgramDirectory, "Bin", "7za.exe");
         Assert.That(File.Exists(executable), Is.True, executable);

         var startInfo = new ProcessStartInfo(executable, arguments)
         {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
         };

         using (var process = Process.Start(startInfo))
         {
            // Nothing to answer a prompt with: 7za fails instead of waiting.
            process.StandardInput.Close();
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode;
         }
      }
   }
}
