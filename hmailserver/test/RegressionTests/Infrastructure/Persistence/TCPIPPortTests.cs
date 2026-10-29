using System.Runtime.InteropServices;
using hMailServer;
using NUnit.Framework;
using RegressionTests.Shared;

namespace RegressionTests.Infrastructure.Persistence
{
   [TestFixture]
   public class TCPIPPortTests : TestFixtureBase
   {
      [Test]
      public void FailedListenerDatabaseWriteDoesNotReportSuccess()
      {
         var ports = _settings.TCPIPPorts;
         int count = ports.Count;
         var port = ports.Add();
         port.Protocol = (eSessionType)int.MaxValue; // Cannot fit any supported protocol column.
         port.PortNumber = 45263;
         port.ConnectionSecurity = eConnectionSecurity.eCSNone;
         COMException failure = null;
         try
         {
            try { port.Save(); }
            catch (COMException error) { failure = error; }
            CustomAsserts.AssertReportedError("hm_tcpipports");
            Assert.That(failure, Is.Not.Null, "A rejected database write must fail the caller's save.");
            Assert.That(port.ID, Is.Zero, "An unsuccessful insert must not publish an identity.");
            Assert.That(ports.Count, Is.EqualTo(count), "Failed saves must not publish a transient listener in the collection.");
            port.Protocol = eSessionType.eSTSMTP;
            port.Save();
            Assert.That(port.ID, Is.GreaterThan(0), "A valid save after the failure can still persist.");
         }
         finally
         {
            if (port.ID > 0) ports.DeleteByDBID(port.ID);
            ports.Refresh();
         }
      }

      [Test]
      public void FailedCertificateDatabaseWriteDoesNotReportSuccess()
      {
         var certificates = _settings.SSLCertificates;
         int count = certificates.Count;
         var certificate = certificates.Add();
         certificate.Name = new string('C', 256); // Schema limit is 255; exercise provider failure.
         certificate.CertificateFile = "not-used-certificate.pem";
         certificate.PrivateKeyFile = "not-used-private-key.pem";
         COMException failure = null;
         try
         {
            try { certificate.Save(); }
            catch (COMException error) { failure = error; }
            CustomAsserts.AssertReportedError("hm_sslcertificates");
            Assert.That(failure, Is.Not.Null, "A rejected certificate write must fail the caller's save.");
            Assert.That(certificate.ID, Is.Zero, "An unsuccessful certificate insert must not publish an identity.");
            Assert.That(certificates.Count, Is.EqualTo(count), "A failed certificate is not published in the collection.");
            certificate.Name = "Persistence failure recovery fixture";
            certificate.Save();
            Assert.That(certificate.ID, Is.GreaterThan(0), "The certificate can be saved after correcting the rejected field.");
         }
         finally
         {
            // A permissive provider may truncate instead; do not leave that fixture behind.
            if (certificate.ID > 0) certificates.DeleteByDBID(certificate.ID);
            certificates.Refresh();
         }
      }

      [Test]
      public void CertificateIsRequiredForSSL()
      {
         var settings = SingletonProvider<TestSetup>.Instance.GetApp().Settings;
         var port = settings.TCPIPPorts[0];

         if (port.SSLCertificateID > 0)
            Assert.Inconclusive("Test cannot run using port with SSL cert.");

         port.ConnectionSecurity = eConnectionSecurity.eCSTLS;

         var ex = Assert.Throws<COMException>(() => port.Save());
         Assert.That(ex.Message, Does.Contain("Certificate must be specified."));
      }

      [Test]
      public void CertificateIsRequiredForStartTLSOptional()
      {
         var settings = SingletonProvider<TestSetup>.Instance.GetApp().Settings;
         var port = settings.TCPIPPorts[0];

         if (port.SSLCertificateID > 0)
            Assert.Inconclusive("Test cannot run using port with SSL cert.");

         port.ConnectionSecurity = eConnectionSecurity.eCSSTARTTLSOptional;

         var ex = Assert.Throws<COMException>(() => port.Save());
         Assert.That(ex.Message, Does.Contain("Certificate must be specified."));
      }

      [Test]
      public void CertificateIsRequiredForStartTLSRequired()
      {
         var settings = SingletonProvider<TestSetup>.Instance.GetApp().Settings;
         var port = settings.TCPIPPorts[0];

         if (port.SSLCertificateID > 0)
            Assert.Inconclusive("Test cannot run using port with SSL cert.");

         port.ConnectionSecurity = eConnectionSecurity.eCSSTARTTLSRequired;

         var ex = Assert.Throws<COMException>(() => port.Save());
         Assert.That(ex.Message, Does.Contain("Certificate must be specified."));
      }
   }
}
