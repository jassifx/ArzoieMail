using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace RegressionTests.Shared
{
   /// <summary>
   /// Locates hMailServer.ini the same way the server does. New installations keep
   /// the file in ProgramData\ArzoieMail, installations upgraded from hMailServer in
   /// ProgramData\hMailServer, and older ones in the Bin directory.
   /// </summary>
   public static class IniFileLocator
   {
      public const string ProgramDataFolderName = "ArzoieMail";
      public const string LegacyProgramDataFolderName = "hMailServer";

      public static string GetIniFileName()
      {
         foreach (var folderName in new[] { ProgramDataFolderName, LegacyProgramDataFolderName })
         {
            var programDataFile = Path.Combine(GetProgramDataDirectory(folderName), "hMailServer.ini");

            if (File.Exists(programDataFile))
               return programDataFile;
         }

         return Path.Combine(GetBinDirectory(), "hMailServer.ini");
      }

      public static string GetProgramDataDirectory(string folderName = ProgramDataFolderName)
      {
         return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            folderName);
      }

      private static string GetBinDirectory()
      {
         // The installer writes the install location to the 32-bit registry view, so
         // read that view explicitly regardless of the bitness of the test process.
         using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
         using (var key = baseKey.OpenSubKey(@"SOFTWARE\hMailServer"))
         {
            var installLocation = key?.GetValue("InstallLocation") as string;

            if (!string.IsNullOrEmpty(installLocation))
               return Path.Combine(installLocation, "Bin");
         }

         var processes = Process.GetProcessesByName("hmailserver");

         if (processes.Length != 1)
            throw new InvalidOperationException("Expected exactly one running hMailServer.exe.");

         return Path.GetDirectoryName(processes[0].MainModule.FileName);
      }
   }
}
