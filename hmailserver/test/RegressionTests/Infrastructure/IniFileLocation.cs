// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RegressionTests.Shared;

namespace RegressionTests.Infrastructure
{
   /// <summary>
   /// The server looks for hMailServer.ini in ProgramData\ArzoieMail, then in
   /// ProgramData\hMailServer, and falls back to the Bin directory. These tests confirm
   /// that the file the server actually loaded is the one that lookup finds.
   /// </summary>
   [TestFixture]
   public class IniFileLocation : TestFixtureBase
   {
      [Test]
      [Description("The ini file the server uses should exist where the lookup expects it")]
      public void IniFileShouldExist()
      {
         Assert.That(File.Exists(IniFileLocator.GetIniFileName()), Is.True);
      }

      [Test]
      [Description("The directories the server reports should come from the located ini file")]
      public void DirectoriesShouldMatchLocatedIniFile()
      {
         var directories = SingletonProvider<TestSetup>.Instance.GetApp().Settings.Directories;

         Assert.That(directories.DataDirectory.TrimEnd('\\'), Is.EqualTo(ReadIniSetting("DataFolder")));
         Assert.That(directories.LogDirectory.TrimEnd('\\'), Is.EqualTo(ReadIniSetting("LogFolder")));
         Assert.That(directories.EventDirectory.TrimEnd('\\'), Is.EqualTo(ReadIniSetting("EventFolder")));
      }

      [Test]
      [Description("An ini file in ProgramData takes precedence over the one in Bin")]
      public void ProgramDataShouldTakePrecedenceOverBin()
      {
         var iniFile = IniFileLocator.GetIniFileName();
         var programDataFile = Path.Combine(IniFileLocator.GetProgramDataDirectory(), "hMailServer.ini");
         var legacyProgramDataFile = Path.Combine(
            IniFileLocator.GetProgramDataDirectory(IniFileLocator.LegacyProgramDataFolderName), "hMailServer.ini");

         if (File.Exists(programDataFile))
            Assert.That(iniFile, Is.EqualTo(programDataFile));
         else if (File.Exists(legacyProgramDataFile))
            Assert.That(iniFile, Is.EqualTo(legacyProgramDataFile));
         else
            Assert.Inconclusive("This installation keeps hMailServer.ini in the Bin directory.");
      }

      private static string ReadIniSetting(string key)
      {
         // Read without taking a lock, so that the server can keep writing to the file.
         using (var fileStream = new FileStream(IniFileLocator.GetIniFileName(), FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite))
         using (var reader = new StreamReader(fileStream))
         {
            var line = reader.ReadToEnd()
               .Split(new[] {"\r\n", "\n"}, StringSplitOptions.None)
               .FirstOrDefault(l => l.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));

            if (line == null)
               throw new InvalidOperationException(string.Format("{0} is not set in hMailServer.ini.", key));

            return line.Substring(key.Length + 1).TrimEnd('\\');
         }
      }
   }
}
