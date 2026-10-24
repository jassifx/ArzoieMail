// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using RegressionTests.Shared;

namespace RegressionTests.Security
{
   // The passwords the Administrator saves for its server connections are protected with DPAPI
   // for the Windows user, not with the key in the source code earlier versions used, which they
   // still read. Tested on the installed hMailAdmin.dll, as the Administrator has no tests of its
   // own.
   [TestFixture]
   public class AdministratorSavedPasswords : TestFixtureBase
   {
      private static Type GetEncryption()
      {
         var processes = Process.GetProcessesByName("hmailserver");
         Assert.That(processes.Length, Is.EqualTo(1), "Expected exactly one running hMailServer.exe.");

         var administrator = Path.Combine(Path.GetDirectoryName(processes[0].MainModule.FileName), "hMailAdmin.dll");
         var type = Assembly.LoadFrom(administrator).GetType("hMailServer.Administrator.Utilities.Encryption", true);
         return type;
      }

      private static object Call(Type type, string method, string argument)
      {
         var info = type.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
         Assert.That(info, Is.Not.Null, method);

         try
         {
            return info.Invoke(null, new object[] { argument });
         }
         catch (TargetInvocationException ex)
         {
            throw ex.InnerException;
         }
      }

      [Test]
      [Description("A saved password is protected with DPAPI and reads back as it was.")]
      public void TestSavedPasswordIsProtectedWithDpapi()
      {
         var encryption = GetEncryption();
         const string password = "Admin pässword ' \\ ✓";

         var saved = (string) Call(encryption, "Encrypt", password);

         Assert.That(saved, Does.StartWith("dpapi1:"));
         Assert.That((bool) Call(encryption, "IsSavedTheOldWay", saved), Is.False);
         Assert.That((string) Call(encryption, "Decrypt", saved), Is.EqualTo(password));

         // Every protection is a new blob, and a changed one does not unprotect.
         Assert.That((string) Call(encryption, "Encrypt", password), Is.Not.EqualTo(saved));

         var blob = Convert.FromBase64String(saved.Substring("dpapi1:".Length));
         blob[blob.Length - 1] ^= 0xff;
         Assert.Throws<CryptographicException>(() => Call(encryption, "Decrypt", "dpapi1:" + Convert.ToBase64String(blob)));
      }

      [Test]
      [Description("A password an earlier version saved under the key in the source code is still read.")]
      public void TestPasswordSavedTheOldWayIsRead()
      {
         var encryption = GetEncryption();
         const string password = "Old admin pässword";

         var saved = EncryptTheOldWay(password);

         Assert.That((bool) Call(encryption, "IsSavedTheOldWay", saved), Is.True);
         Assert.That((string) Call(encryption, "Decrypt", saved), Is.EqualTo(password));
      }

      // What earlier versions of the Administrator wrote.
      private static string EncryptTheOldWay(string plainText)
      {
#pragma warning disable SYSLIB0041
         var derived = new PasswordDeriveBytes("THIS_KEY_IS_NOT_SECRET",
            new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });
#pragma warning restore SYSLIB0041

         using (var aes = Aes.Create())
         {
            aes.Key = derived.GetBytes(32);
            aes.IV = derived.GetBytes(16);

            var plain = Encoding.Unicode.GetBytes(plainText);
            using (var encryptor = aes.CreateEncryptor())
               return Convert.ToBase64String(encryptor.TransformFinalBlock(plain, 0, plain.Length));
         }
      }
   }
}
