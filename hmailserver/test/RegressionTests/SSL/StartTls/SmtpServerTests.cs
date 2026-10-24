using System.Threading;
using NUnit.Framework;
using RegressionTests.Infrastructure;
using RegressionTests.Shared;

namespace RegressionTests.SSL.StartTls
{
   [TestFixture]
   public class SmtpServerTests : TestFixtureBase
   {
      [OneTimeSetUp]
      public new void TestFixtureSetUp()
      {
         SslSetup.SetupSSLPorts(_application);
      }

      [Test]
      public void IfStartTlsNotEnabledStartTlsShouldNotBeShownInEhloResponse()
      {
         var smtpClientSimulator = new TcpConnection();
         smtpClientSimulator.Connect(25);
         var data1 = smtpClientSimulator.Receive();
         var data = smtpClientSimulator.SendAndReceive("EHLO example.com\r\n");

         Assert.That(data.Contains("STARTTLS"), Is.False);
      }

      [Test]
      public void IfStartTlsIsEnabledStartTlsShouldBeShownInEhloResponse()
      {
         var smtpClientSimulator = new TcpConnection();
         smtpClientSimulator.Connect(25002);
         var data1 = smtpClientSimulator.Receive();
         var data = smtpClientSimulator.SendAndReceive("EHLO example.com\r\n");

         Assert.That(data.Contains("STARTTLS"), Is.True);
      }

      [Test]
      public void StartTlsCommandShouldSwithToTls()
      {
         var smtpClientSimulator = new TcpConnection();
         smtpClientSimulator.Connect(25002);
         var banner = smtpClientSimulator.Receive();
         var capabilities1 = smtpClientSimulator.SendAndReceive("EHLO example.com\r\n");
         Assert.That(capabilities1.Contains("STARTTLS"), Is.True);

         smtpClientSimulator.SendAndReceive("STARTTLS\r\n");
         smtpClientSimulator.HandshakeAsClient();

         // Send a command over TLS.
         var capabilities2 = smtpClientSimulator.SendAndReceive("EHLO example.com\r\n");
         Assert.That(capabilities2.Contains("STARTTLS"), Is.False);

         // We're now on SSL.
      }

      [Test]
      [Description("A trailing space should be allowed to be compatible with malfunctioning servers.")]
      public void StartTlsWithTrailingSpaceShouldBeAccepted()
      {
         var smtpClientSimulator = new TcpConnection();
         smtpClientSimulator.Connect(25002);
         var banner = smtpClientSimulator.Receive();
         var capabilities1 = smtpClientSimulator.SendAndReceive("EHLO example.com\r\n");
         Assert.That(capabilities1.Contains("STARTTLS"), Is.True);

         var startTlsResultText = smtpClientSimulator.SendAndReceive("STARTTLS \r\n");
         Assert.That(startTlsResultText, Does.Contain("220 Ready to start TLS"));

         smtpClientSimulator.HandshakeAsClient();

         // Send a command over TLS.
         var capabilities2 = smtpClientSimulator.SendAndReceive("EHLO example.com\r\n");
         Assert.That(capabilities2.Contains("STARTTLS"), Is.False);
      }

      [Test]
      public void StartTlsWithParametersShouldBeRejected()
      {
         var smtpClientSimulator = new TcpConnection();
         smtpClientSimulator.Connect(25002);
         var banner = smtpClientSimulator.Receive();
         var capabilities1 = smtpClientSimulator.SendAndReceive("EHLO example.com\r\n");
         Assert.That(capabilities1.Contains("STARTTLS"), Is.True);

         var startTlsResultText = smtpClientSimulator.SendAndReceive("STARTTLS A=B\r\n");
         Assert.That(startTlsResultText, Does.Contain("501 Syntax error (no parameters allowed)"));
      }


      [Test]
      public void HandshakeCompletionShouldBeLoggedWithCipherDetails()
      {
         var smtpClientSimulator = new TcpConnection();
         smtpClientSimulator.Connect(25002);
         var banner = smtpClientSimulator.Receive();
         var capabilities1 = smtpClientSimulator.SendAndReceive("EHLO example.com\r\n");
         Assert.That(capabilities1.Contains("STARTTLS"), Is.True);

         smtpClientSimulator.SendAndReceive("STARTTLS\r\n");
         smtpClientSimulator.HandshakeAsClient();

         var capabilities2 = smtpClientSimulator.SendAndReceive("EHLO example.com\r\n");

         var default_log = LogHandler.ReadCurrentDefaultLog();

         Assert.That(default_log.Contains("Version: TLS"), Is.True);
         Assert.That(default_log.Contains("Cipher: "), Is.True);
         Assert.That(default_log.Contains("Bits: "), Is.True);
      }

      [Test]
      public void IfStlsRequiredLogonShouldSucceedIfStls()
      {
         var smtpClientSimulator = new TcpConnection();
         smtpClientSimulator.Connect(25003);
         var banner = smtpClientSimulator.Receive();
         var capabilities1 = smtpClientSimulator.SendAndReceive("EHLO example.com\r\n");
         Assert.That(capabilities1.Contains("STARTTLS"), Is.True);

         smtpClientSimulator.SendAndReceive("STARTTLS\r\n");
         smtpClientSimulator.HandshakeAsClient();

         smtpClientSimulator.SendAndReceive("EHLO example.com\r\n");

         var loginResult = smtpClientSimulator.SendAndReceive("AUTH LOGIN\r\n");
         Assert.That(loginResult.StartsWith("334"), Is.True);
      }

      [Test]
      public void IfStlsRequiredLogonShouldFailIfNoStls()
      {
         var smtpClientSimulator = new TcpConnection();
         smtpClientSimulator.Connect(25003);
         var banner = smtpClientSimulator.Receive();
         var capabilities1 = smtpClientSimulator.SendAndReceive("EHLO example.com\r\n");
         Assert.That(capabilities1.Contains("STARTTLS"), Is.True);

         var loginResult = smtpClientSimulator.SendAndReceive("AUTH LOGIN\r\n");
         Assert.That(loginResult.StartsWith("530 Must issue STARTTLS first."), Is.True);
      }

      [Test]
      public void IfStlsOptionalButSslRequiredByIpRangeForAuthThenAuthShouldFail()
      {
         var range = SingletonProvider<TestSetup>.Instance.GetApp().Settings.SecurityRanges
            .get_ItemByName("My computer");
         range.RequireSSLTLSForAuth = true;
         range.Save();

         var smtpClientSimulator = new TcpConnection();
         smtpClientSimulator.Connect(25002);
         var banner = smtpClientSimulator.Receive();
         var capabilities1 = smtpClientSimulator.SendAndReceive("EHLO example.com\r\n");
         Assert.That(capabilities1.Contains("STARTTLS"), Is.True);

         var loginResult = smtpClientSimulator.SendAndReceive("AUTH LOGIN\r\n");
         Assert.That(
            loginResult.StartsWith(
               "530 A SSL/TLS-connection is required for authentication."),
            Is.True); // must run starttls first.
      }


      [Test]
      [Description("RFC 3207: after the handshake the session is back in the initial state, " +
                   "so the client has to greet the server again.")]
      public void AfterStartTlsMailFromShouldRequireNewEhlo()
      {
         var smtpClientSimulator = new TcpConnection();
         smtpClientSimulator.Connect(25002);
         smtpClientSimulator.Receive(); // banner
         var capabilities = smtpClientSimulator.SendAndReceive("EHLO example.com\r\n");
         Assert.That(capabilities.Contains("STARTTLS"), Is.True);

         smtpClientSimulator.SendAndReceive("STARTTLS\r\n");
         smtpClientSimulator.HandshakeAsClient();

         var result = smtpClientSimulator.SendAndReceive("MAIL FROM:<test@example.com>\r\n");
         Assert.That(result, Does.Contain("503 Bad sequence of commands"));
      }

      [Test]
      [Description("RSET must not take the place of the EHLO the client owes us after STARTTLS.")]
      public void AfterStartTlsRsetShouldNotAllowMailFrom()
      {
         var smtpClientSimulator = new TcpConnection();
         smtpClientSimulator.Connect(25002);
         smtpClientSimulator.Receive(); // banner
         var capabilities = smtpClientSimulator.SendAndReceive("EHLO example.com\r\n");
         Assert.That(capabilities.Contains("STARTTLS"), Is.True);

         smtpClientSimulator.SendAndReceive("STARTTLS\r\n");
         smtpClientSimulator.HandshakeAsClient();

         Assert.That(smtpClientSimulator.SendAndReceive("RSET\r\n"), Does.Contain("250 OK"));

         var result = smtpClientSimulator.SendAndReceive("MAIL FROM:<test@example.com>\r\n");
         Assert.That(result, Does.Contain("503 Bad sequence of commands"));
      }

      [Test]
      public void AfterStartTlsMailFromShouldBeAcceptedOnceEhloHasBeenReissued()
      {
         var smtpClientSimulator = new TcpConnection();
         smtpClientSimulator.Connect(25002);
         smtpClientSimulator.Receive(); // banner
         var capabilities = smtpClientSimulator.SendAndReceive("EHLO example.com\r\n");
         Assert.That(capabilities.Contains("STARTTLS"), Is.True);

         smtpClientSimulator.SendAndReceive("STARTTLS\r\n");
         smtpClientSimulator.HandshakeAsClient();

         smtpClientSimulator.SendAndReceive("EHLO example.com\r\n");

         var result = smtpClientSimulator.SendAndReceive("MAIL FROM:<test@example.com>\r\n");
         Assert.That(result, Does.Contain("250 OK"));
      }

      [Test]
      public void TestPlaintextCommandInjection()
      {
         var smtpClientSimulator = new TcpConnection();
         smtpClientSimulator.Connect(25002);
         var banner = smtpClientSimulator.Receive();
         var capabilities1 = smtpClientSimulator.SendAndReceive("EHLO example.com\r\n");
         Assert.That(capabilities1.Contains("STARTTLS"), Is.True);

         var resp = smtpClientSimulator.SendAndReceive("STARTTLS\r\nRSET\r\n");
         Assert.That(resp, Is.EqualTo("220 Ready to start TLS\r\n"));
         smtpClientSimulator.HandshakeAsClient();

         var quitResponse = smtpClientSimulator.SendAndReceive("QUIT\r\n");
         Assert.That(quitResponse, Is.EqualTo("221 goodbye\r\n"));

         var default_log = LogHandler.ReadCurrentDefaultLog();
         Assert.That(default_log.Contains("RSET"), Is.False);
      }
   }
}