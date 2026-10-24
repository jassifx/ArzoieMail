using System;
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
         Assert.That(processes.Length, Is.EqualTo(1), "Expected exactly one running hMailServer.exe.");

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
            Assert.That(File.Exists(Path.Combine(bin, tool + suffix)), Is.True, tool + suffix + " is missing from " + bin);
      }

      [TestCase("hMailAdmin")]
      [TestCase("DBSetup")]
      [TestCase("DBSetupQuick")]
      [TestCase("DBUpdater")]
      [Description("The tool runs on the .NET 10 Windows Desktop runtime")]
      public void ToolShouldTargetDotNet10WindowsDesktop(string tool)
      {
         var runtimeConfig = File.ReadAllText(Path.Combine(GetBinDirectory(), tool + ".runtimeconfig.json"));

         Assert.That(runtimeConfig, Does.Match("\"name\"\\s*:\\s*\"Microsoft.WindowsDesktop.App\"\\s*,\\s*\"version\"\\s*:\\s*\"10\\."));
      }

      [Test]
      [Description("The .NET Framework configuration files of earlier versions are removed on upgrade")]
      public void NoDotNetFrameworkConfigFilesShouldRemain()
      {
         Assert.That(Directory.GetFiles(GetBinDirectory(), "*.exe.config").Select(Path.GetFileName).ToList(), Is.Empty);
      }

      [Test]
      [Description("A .NET 10 Desktop Runtime is installed where the .NET host looks for it")]
      public void DotNet10DesktopRuntimeShouldBeInstalled()
      {
         // The .NET host resolves the framework from here, whichever way it was installed.
         var programFiles = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
         var frameworkDirectory = Path.Combine(programFiles, @"dotnet\shared\Microsoft.WindowsDesktop.App");

         var versions = Directory.Exists(frameworkDirectory)
            ? Directory.GetDirectories(frameworkDirectory).Select(Path.GetFileName).ToList()
            : new System.Collections.Generic.List<string>();

         Assert.That(versions.Any(v => Regex.IsMatch(v, @"^10\.")),
            Is.True,
            "No .NET 10 Desktop Runtime in " + frameworkDirectory + ". Found: " + string.Join(", ", versions));
      }

      [Test]
      [Description("The .NET installers record x64 runtimes in the 32-bit registry view, which the setup reads")]
      public void DotNetRuntimeRegistrationShouldBeInThe32BitRegistryView()
      {
         using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
         using (var key = baseKey.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App"))
         {
            if (key == null)
               Assert.Inconclusive("The .NET Desktop Runtime here was not installed by a .NET installer, so it is not registered.");

            Assert.That(key.GetValueNames().Any(v => Regex.IsMatch(v, @"^10\.")),
               Is.True,
               "No .NET 10 Desktop Runtime is registered. Found: " + string.Join(", ", key.GetValueNames()));
         }
      }
   }
}
