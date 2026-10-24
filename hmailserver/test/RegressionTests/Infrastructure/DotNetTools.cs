using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using NUnit.Framework;
using RegressionTests.Shared;

namespace RegressionTests.Infrastructure
{
   /// <summary>
   /// The administration tools are .NET 10 Windows Forms applications. Each is installed as an
   /// .exe host with its .dll, .runtimeconfig.json and .deps.json beside it, and the setup
   /// installs the .NET 10 Desktop Runtime they run on when it is missing.
   /// </summary>
   [TestFixture]
   public class DotNetTools : TestFixtureBase
   {
      private static string GetBinDirectory()
      {
         var processes = Process.GetProcessesByName("hmailserver");
         Assert.AreEqual(1, processes.Length, "Expected exactly one running hMailServer.exe.");

         return Path.GetDirectoryName(processes[0].MainModule.FileName);
      }

      [TestCase("hMailAdmin")]
      [TestCase("DBSetup")]
      [TestCase("DBSetupQuick")]
      [TestCase("DBUpdater")]
      [Description("The tool is installed with the files the .NET host needs to start it")]
      public void ToolShouldBeInstalledWithItsDotNetFiles(string tool)
      {
         var bin = GetBinDirectory();

         foreach (var suffix in new[] { ".exe", ".dll", ".runtimeconfig.json", ".deps.json" })
            Assert.IsTrue(File.Exists(Path.Combine(bin, tool + suffix)), tool + suffix + " is missing from " + bin);
      }

      [TestCase("hMailAdmin")]
      [TestCase("DBSetup")]
      [TestCase("DBSetupQuick")]
      [TestCase("DBUpdater")]
      [Description("The tool runs on the .NET 10 Windows Desktop runtime")]
      public void ToolShouldTargetDotNet10WindowsDesktop(string tool)
      {
         var runtimeConfig = File.ReadAllText(Path.Combine(GetBinDirectory(), tool + ".runtimeconfig.json"));

         StringAssert.IsMatch("\"name\"\\s*:\\s*\"Microsoft.WindowsDesktop.App\"\\s*,\\s*\"version\"\\s*:\\s*\"10\\.", runtimeConfig);
      }

      [Test]
      [Description("The .NET Framework configuration files of earlier versions are removed on upgrade")]
      public void NoDotNetFrameworkConfigFilesShouldRemain()
      {
         CollectionAssert.IsEmpty(Directory.GetFiles(GetBinDirectory(), "*.exe.config").Select(Path.GetFileName).ToList());
      }

      [Test]
      [Description("A .NET 10 Desktop Runtime is installed, which the setup does when it is missing")]
      public void DotNet10DesktopRuntimeShouldBeInstalled()
      {
         using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
         using (var key = baseKey.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App"))
         {
            Assert.IsNotNull(key, "No .NET Desktop Runtime is registered.");

            var versions = key.GetValueNames();
            Assert.IsTrue(versions.Any(v => Regex.IsMatch(v, @"^10\.")),
               "No .NET 10 Desktop Runtime is installed. Found: " + string.Join(", ", versions));
         }
      }
   }
}
