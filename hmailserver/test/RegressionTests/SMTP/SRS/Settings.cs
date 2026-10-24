using System.Runtime.InteropServices;
using NUnit.Framework;

namespace RegressionTests.SMTP.SRS
{
   [TestFixture]
   public class Settings : SrsTestBase
   {
      [Test]
      [Description("The server generates a secret of its own, so that SRS works without anyone configuring one.")]
      public void ASecretIsGeneratedAutomatically()
      {
         // Not merely non-empty: a test which sets a secret of its own would leave one
         // behind, and the assertion would then hold without the server ever having
         // generated anything. TestSetup puts a generated secret back for every test, and
         // this is the shape of one - 32 random bytes in unpadded base64.
         Assert.That(Secret.Length, Is.EqualTo(SrsAddress.GeneratedSecretLength));
      }

      [Test]
      [Description("Rotating the secret replaces it with a new one, and is what has to be asked for to lose the old one.")]
      public void TheSecretIsOnlyReplacedWhenRotatingIsAskedFor()
      {
         var originalSecret = Secret;

         try
         {
            // Every address already handed out stops being reversible when the secret
            // changes, so clearing it is refused rather than quietly taken as a rotation.
            Assert.Throws<COMException>(() => _settings.SRSSecret = "");
            Assert.That(Secret, Is.EqualTo(originalSecret));

            _settings.RotateSRSSecret();

            Assert.That(Secret, Is.Not.Empty);
            Assert.That(Secret, Is.Not.EqualTo(originalSecret));

            // A secret which is set is kept as it is.
            _settings.SRSSecret = "a secret of my own";
            Assert.That(Secret, Is.EqualTo("a secret of my own"));
         }
         finally
         {
            // Whatever happened above, the server is not left signing with a secret which
            // is written down in the source tree.
            _settings.SRSSecret = originalSecret;
         }
      }

      [Test]
      [Description("A secret longer than the server accepts is refused, and one at the limit survives being stored.")]
      public void AnOverlongSecretIsRefused()
      {
         var originalSecret = Secret;

         try
         {
            var longest = new string('a', SrsAddress.MaxSecretLength);

            _settings.SRSSecret = longest;
            Assert.That(Secret, Is.EqualTo(longest));

            Assert.Throws<COMException>(() => _settings.SRSSecret = longest + "a");
            Assert.That(Secret, Is.EqualTo(longest));

            // The secret is encrypted on its way into the database and decrypted on the
            // way back out, so one at the limit is checked against a reload rather than
            // only against the copy the server is holding.
            _application.Reinitialize();
            Assert.That(_application.Settings.SRSSecret, Is.EqualTo(longest));
         }
         finally
         {
            _settings.SRSSecret = originalSecret;
         }
      }

      [Test]
      [Description("The settings are stored and read back.")]
      public void TheSettingsAreStoredAndReadBack()
      {
         Assert.That(_settings.SRSEnabled, Is.False);

         _settings.SRSEnabled = true;
         Assert.That(_settings.SRSEnabled, Is.True);

         _settings.SRSMaxAgeDays = 5;
         Assert.That(_settings.SRSMaxAgeDays, Is.EqualTo(5));

         _settings.SRSHashLength = 12;
         Assert.That(_settings.SRSHashLength, Is.EqualTo(12));
      }

      [Test]
      [Description("Zero selects the default rather than a value nothing could be validated with.")]
      public void ZeroSelectsTheDefaults()
      {
         _settings.SRSMaxAgeDays = 0;
         Assert.That(_settings.SRSMaxAgeDays, Is.EqualTo(SrsAddress.DefaultMaxAgeDays));

         _settings.SRSHashLength = 0;
         Assert.That(_settings.SRSHashLength, Is.EqualTo(SrsAddress.DefaultHashLength));
      }

      [Test]
      [Description("A setting outside the range the server goes by is reported as the value it goes by.")]
      public void OutOfRangeSettingsAreReportedAsTheyAreApplied()
      {
         _settings.SRSHashLength = 1;
         Assert.That(_settings.SRSHashLength, Is.EqualTo(SrsAddress.MinHashLength));

         _settings.SRSHashLength = 100;
         Assert.That(_settings.SRSHashLength, Is.EqualTo(SrsAddress.MaxHashLength));

         _settings.SRSMaxAgeDays = 100000;
         Assert.That(_settings.SRSMaxAgeDays, Is.EqualTo(SrsAddress.MaxMaxAgeDays));
      }
   }
}
