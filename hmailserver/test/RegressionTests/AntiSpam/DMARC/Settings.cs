// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

using NUnit.Framework;
using RegressionTests.Shared;

namespace RegressionTests.AntiSpam.DMARC
{
   [TestFixture]
   public class Settings : TestFixtureBase
   {
      [Test]
      [Description("The DMARC settings should be readable and writable through the API.")]
      public void TestDMARCSettingsCanBeChanged()
      {
         var antiSpam = _application.Settings.AntiSpam;

         antiSpam.DMARCEnabled = true;
         antiSpam.DMARCFailureScore = 17;
         antiSpam.DMARCHonorPolicy = true;
         antiSpam.AddAuthenticationResultsHeader = true;

         Assert.That(antiSpam.DMARCEnabled, Is.True);
         Assert.That(antiSpam.DMARCFailureScore, Is.EqualTo(17));
         Assert.That(antiSpam.DMARCHonorPolicy, Is.True);
         Assert.That(antiSpam.AddAuthenticationResultsHeader, Is.True);

         antiSpam.DMARCEnabled = false;
         antiSpam.DMARCFailureScore = 5;
         antiSpam.DMARCHonorPolicy = false;
         antiSpam.AddAuthenticationResultsHeader = false;

         Assert.That(antiSpam.DMARCEnabled, Is.False);
         Assert.That(antiSpam.DMARCFailureScore, Is.EqualTo(5));
         Assert.That(antiSpam.DMARCHonorPolicy, Is.False);
         Assert.That(antiSpam.AddAuthenticationResultsHeader, Is.False);
      }
   }
}
