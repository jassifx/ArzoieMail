using NUnit.Framework;

namespace RegressionTests.Shared
{
   [TestFixture]
   public class UtilityServices : TestFixtureBase
   {
      [OneTimeSetUp]
      public void SetUpFixture()
      {
         SingletonProvider<TestSetup>.Instance.Authenticate();
      }

      [Test]
      [Description("Test usage of algorithm MD5.")]
      public void TestMD5Hashing()
      {
         var utilities = _application.Utilities;

         var passwordHash = utilities.MD5("Password");
      }

      [Test]
      public void TestStrongPasswords()
      {
         var utilities = _application.Utilities;

         // The password should not be a part of a user name.
         Assert.That(utilities.IsStrongPassword("testar@example.test", "testar"), Is.False);

         // The password should be more than 4 characters long.
         Assert.That(utilities.IsStrongPassword("vaffe@example.test", "test"), Is.False);

         // If the password is 6 characters or less, it should contain a number or special char
         Assert.That(utilities.IsStrongPassword("vaffe@example.test", "testar"), Is.False);

         // Typically bad passwords...
         Assert.That(utilities.IsStrongPassword("vaffe@example.test", "secret"), Is.False);
         Assert.That(utilities.IsStrongPassword("vaffe@example.test", "password"), Is.False);
         Assert.That(utilities.IsStrongPassword("vaffe@example.test", "info"), Is.False);
         Assert.That(utilities.IsStrongPassword("vaffe@example.test", "webmaster"), Is.False);
         Assert.That(utilities.IsStrongPassword("vaffe@example.test", "admin"), Is.False);
         Assert.That(utilities.IsStrongPassword("vaffe@example.test", "12345"), Is.False);
         Assert.That(utilities.IsStrongPassword("vaffe@example.test", "qwerty"), Is.False);

         // Contains more than 6 characters - OK
         Assert.That(utilities.IsStrongPassword("vaffe@example.test", "testarp"), Is.True);

         // Contains more than 6 characters - OK
         Assert.That(utilities.IsStrongPassword("vaffe@example.test", "test_"), Is.True);
      }
   }
}