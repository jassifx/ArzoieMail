using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RegressionTests.Shared;

namespace RegressionTests.Infrastructure
{
   /// <summary>
   /// The installer requires Windows 10, so it ships the newest Visual C++ runtime beside
   /// hMailServer.exe and none of the Universal CRT, which Windows 10 carries itself.
   /// </summary>
   [TestFixture]
   public class InstallationLayout : TestFixtureBase
   {
      private static string GetBinDirectory()
      {
         var processes = Process.GetProcessesByName("hmailserver");
         Assert.AreEqual(1, processes.Length, "Expected exactly one running hMailServer.exe.");

         return Path.GetDirectoryName(processes[0].MainModule.FileName);
      }

      [Test]
      [Description("The Visual C++ runtime hMailServer.exe imports is installed beside it")]
      public void VisualCppRuntimeShouldBeInstalled()
      {
         var bin = GetBinDirectory();

         foreach (var file in new[] { "msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll" })
            Assert.IsTrue(File.Exists(Path.Combine(bin, file)), file + " is missing from " + bin);
      }

      [Test]
      [Description("The runtime is not older than the 14.29 toolset that compiles hMailServer.exe")]
      public void VisualCppRuntimeShouldNotBeOlderThanTheToolset()
      {
         var versionInfo = FileVersionInfo.GetVersionInfo(Path.Combine(GetBinDirectory(), "vcruntime140.dll"));
         var version = new Version(versionInfo.FileMajorPart, versionInfo.FileMinorPart);

         Assert.GreaterOrEqual(version, new Version(14, 29));
      }

      [Test]
      [Description("The Universal CRT is not shipped, since Windows 10 and later carry it")]
      public void UniversalCrtShouldNotBeInstalled()
      {
         var bin = GetBinDirectory();

         var ucrtFiles = Directory.GetFiles(bin, "api-ms-win-*.dll")
            .Concat(Directory.GetFiles(bin, "ucrtbase.dll"))
            .Select(Path.GetFileName)
            .ToList();

         CollectionAssert.IsEmpty(ucrtFiles);
      }
   }
}
