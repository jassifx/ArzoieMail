using System.Threading;
using hMailServer;
using NUnit.Framework;
using RegressionTests.Shared;

namespace RegressionTests.SSL.StartTls
{
   [TestFixture]
   public class Pop3ServerTests : TestFixtureBase
   {
      [OneTimeSetUp]
      public new void TestFixtureSetUp()
      {
         SslSetup.SetupSSLPorts(_application);
      }

      [SetUp]
      public new void SetUp()
      {
         _account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");
      }

      private Account _account;

      [Test]
      public void IfStartTlsNotEnabledStartTlsShouldNotBeShownInEhloResponse()
      {
         var pop3Simulator = new Pop3ClientSimulator(false, 110);
         pop3Simulator.Connect();
         string banner;

         pop3Simulator.ReceiveBanner(out banner);

         var data = pop3Simulator.CAPA();

         Assert.That(data.Contains("STLS"), Is.False);
      }

      [Test]
      public void IfStartTlsIsEnabledStartTlsShouldBeShownInEhloResponse()
      {
         var pop3Simulator = new Pop3ClientSimulator(false, 11002);
         pop3Simulator.Connect();
         string banner;

         pop3Simulator.ReceiveBanner(out banner);

         var data = pop3Simulator.CAPA();

         Assert.That(data.Contains("STLS"), Is.True);
      }

      [Test]
      public void StlsCommandShouldSwithToTls()
      {
         var pop3Simulator = new Pop3ClientSimulator(false, 11002);
         pop3Simulator.Connect();
         string banner;
         pop3Simulator.ReceiveBanner(out banner);
         pop3Simulator.STLS();
         pop3Simulator.Handshake();

         // run over TLS.
         pop3Simulator.HELP();
      }

      [Test]
      public void IfStlsRequiredLogonShouldSucceedIfStls()
      {
         var pop3Simulator = new Pop3ClientSimulator(false, 11003);
         pop3Simulator.Connect();
         string banner;
         pop3Simulator.ReceiveBanner(out banner);
         pop3Simulator.STLS();
         pop3Simulator.Handshake();

         // run over TLS.
         var result = pop3Simulator.User("test@example.test");
         Assert.That(result.StartsWith("+OK"), Is.True);
      }

      [Test]
      public void IfStlsRequiredLogonShouldFailIfNoStls()
      {
         var pop3Simulator = new Pop3ClientSimulator(false, 11003);
         pop3Simulator.Connect();
         string banner;
         pop3Simulator.ReceiveBanner(out banner);

         // run over TLS.
         var result = pop3Simulator.User("test@example.test");
         Assert.That(result.StartsWith("-ERR STLS is required."), Is.True);
      }

      [Test]
      public void IfStlsOptionalButSslRequiredByIpRangeForAuthThenAuthShouldFail()
      {
         var range = SingletonProvider<TestSetup>.Instance.GetApp().Settings.SecurityRanges
            .get_ItemByName("My computer");
         range.RequireSSLTLSForAuth = true;
         range.Save();

         var pop3Simulator = new Pop3ClientSimulator(false, 11002);
         pop3Simulator.Connect();
         string banner;
         pop3Simulator.ReceiveBanner(out banner);

         // run over TLS.
         var result = pop3Simulator.User("test@example.test");
         Assert.That(result.StartsWith("-ERR A SSL/TLS-connection is required for authentication."), Is.True);
      }
   }
}