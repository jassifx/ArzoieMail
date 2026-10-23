using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using NUnit.Framework;
using RegressionTests.Shared;

namespace RegressionTests.Infrastructure
{
   /// <summary>
   /// The product is shown as ArzoieMail, while the identifiers existing installations and
   /// scripts depend on - the service name, the COM API, the executable - stay hMailServer.
   /// </summary>
   [TestFixture]
   public class ProductIdentity : TestFixtureBase
   {
      [Test]
      [Description("The service keeps its name, so net start/stop and scripts keep working")]
      public void ServiceNameShouldBeUnchanged()
      {
         using (var service = new ServiceController("hMailServer"))
         {
            Assert.AreEqual(ServiceControllerStatus.Running, service.Status);
         }
      }

      [Test]
      [Description("The Services console shows the service as ArzoieMail")]
      public void ServiceDisplayNameShouldBeArzoieMail()
      {
         using (var service = new ServiceController("hMailServer"))
         {
            Assert.AreEqual("ArzoieMail", service.DisplayName);
         }
      }

      [Test]
      [Description("The server executable reports ArzoieMail as its product name")]
      public void ExecutableProductNameShouldBeArzoieMail()
      {
         var processes = Process.GetProcessesByName("hmailserver");
         Assert.AreEqual(1, processes.Length, "Expected exactly one running hMailServer.exe.");

         var versionInfo = FileVersionInfo.GetVersionInfo(processes[0].MainModule.FileName);
         Assert.AreEqual("ArzoieMail", versionInfo.ProductName);
      }

      [Test]
      [Description("A new installation keeps its ini file in ProgramData\\ArzoieMail")]
      public void NewInstallationShouldUseArzoieMailProgramData()
      {
         var legacyIniFile = Path.Combine(
            IniFileLocator.GetProgramDataDirectory(IniFileLocator.LegacyProgramDataFolderName), "hMailServer.ini");

         if (File.Exists(legacyIniFile))
            Assert.Inconclusive("This installation was upgraded from hMailServer and keeps ProgramData\\hMailServer.");

         var iniFile = Path.Combine(IniFileLocator.GetProgramDataDirectory(), "hMailServer.ini");
         Assert.AreEqual(iniFile, IniFileLocator.GetIniFileName());
      }
   }
}
