using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;

namespace VMTestRunner.Console
{
   public class TestRunner
   {
      private const string NuGetPackagesRelativePath = @"..\..\..\..\packages\";
      private const string RegressionTestsBinRelativePath = @"..\..\..\..\RegressionTests\bin\x64\Debug\";
      private const string VolumeTestsBinRelativePath = @"..\..\..\..\VolumeTests\bin\x64\Debug\";
      // Directory.Build.props restores the packages into hmailserver\test\packages\<id>\<version>\.
      private const string NUnitConsoleRunnerPackageId = "nunit.consolerunner";
      private readonly string _nUnitConsolePath;

      private const string Username = "vmware";
      private const string Password = "Secret123";

      private const string RunTestScriptName = "RunTestsInHyperV.bat";

      private const string SetupLogPath = @"C:\setup.log";

      private readonly TestEnvironment _environment;

      private readonly string _softwareUnderTest;

      private readonly int _testIndex;

      private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

      public TestRunner(TestEnvironment environment, string softwareUnderTest, int testIndex)
      {
         _environment = environment;
         _softwareUnderTest = softwareUnderTest;
         _testIndex = testIndex;

         var packagePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, NuGetPackagesRelativePath));

         _nUnitConsolePath = FindNUnitConsole(Path.Combine(packagePath, NUnitConsoleRunnerPackageId));
      }

      /// <summary>
      /// The tools folder of the restored NUnit.ConsoleRunner package. Only the version
      /// Directory.Packages.props pins is restored, but an older one can be left behind.
      /// </summary>
      private static string FindNUnitConsole(string packageDirectory)
      {
         var toolsDirectory = Directory.Exists(packageDirectory)
            ? Directory.GetDirectories(packageDirectory)
               .Select(versionDirectory => Path.Combine(versionDirectory, "tools"))
               .Where(tools => File.Exists(Path.Combine(tools, "nunit3-console.exe")))
               .OrderByDescending(tools => Version.TryParse(Path.GetFileName(Path.GetDirectoryName(tools)), out var version) ? version : new Version())
               .FirstOrDefault()
            : null;

         if (toolsDirectory == null)
            throw new InvalidOperationException($"NUnit console not found in {packageDirectory}. Build hMailServer Tests.sln, which restores it.");

         return toolsDirectory;
      }

      public void Run()
      {
         RunInternal();
      }

      private void RunInternal()
      {
         var vm = new HyperV(_testIndex);

         var currentDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
         var testAssemblyDirectory = Path.Combine(currentDirectory, GetTestBinRelativePath());

         if (!Directory.Exists(testAssemblyDirectory))
            throw new InvalidOperationException($"The {_environment.TestSuite} assemblies were not found in " +
               $"{Path.GetFullPath(testAssemblyDirectory)}. Build them with build\\build-tests.ps1.");

         string guestTestPath = @"C:\Nunit";

         string softwareUnderTestFullPath = _softwareUnderTest;
         string softwareUnderTestName = Path.GetFileName(softwareUnderTestFullPath);

         // /SUPPRESSMSGBOXES, or a setup that fails stops at its error message, which nobody
         // sees in the guest, and the run waits for it forever instead of failing with the log.
         string softwareUnderTestSilentParmas = $"/SILENT /SUPPRESSMSGBOXES /LOG=\"{SetupLogPath}\"";

         string sslFolder = Path.Combine(TestSettings.GetTestFolder(), "SSL examples");

         vm.OpenVM(_environment.VMName);

         try
         {
            vm.RevertToSnapshot(_environment.SnapshotName);

            using (var guest = CreateGuestSession(vm))
            {
               guest.WaitUntilReady();

               // Set up test paths.
               guest.CreateDirectory(guestTestPath);
               guest.CreateDirectory(@"C:\Temp");

               foreach (var command in _environment.PreInstallCommands)
                  guest.RunProgram(command.Executable, command.Parameters);

               foreach (var copyOperation in _environment.PreInstallFileCopy)
                  guest.CopyFileToGuest(copyOperation.From, copyOperation.To);

               guest.CopyFolderToGuest(_nUnitConsolePath, guestTestPath);

               // The whole build output: a .NET test assembly needs its .deps.json and
               // .runtimeconfig.json, NUnit and the interop next to it. The volume tests' output
               // includes RegressionTests.dll, whose shared infrastructure they use.
               guest.CopyFolderToGuest(testAssemblyDirectory, guestTestPath);

               guest.CopyFileToGuest(Path.Combine(currentDirectory, RunTestScriptName), Path.Combine(guestTestPath, RunTestScriptName));

               // Other required stuff.
               guest.CopyFolderToGuest(sslFolder, @"C:\SSL examples");

               guest.CopyFileToGuest(softwareUnderTestFullPath, Path.Combine(guestTestPath, softwareUnderTestName));
               RunSetup(guest, Path.Combine(guestTestPath, softwareUnderTestName), softwareUnderTestSilentParmas);

               foreach (var copyOperation in _environment.PostInstallFileCopy)
                  guest.CopyFileToGuest(copyOperation.From, copyOperation.To);

               foreach (var command in _environment.PostInstallCommands)
                  guest.RunProgram(command.Executable, command.Parameters);

               bool useLocalVersion = false;

               if (useLocalVersion)
               {
                  CopyLocalVersion(guest);
               }

               if (_environment.EnablePageHeap)
                  EnablePageHeap(guest, guestTestPath);

               // Run NUnit
               string runTestParameters = $"{_environment.TestSuite}.dll";

               if (_environment.IncludeStressTests)
                  runTestParameters += " IncludeStress";

               guest.RunProgram(Path.Combine(guestTestPath, RunTestScriptName), runTestParameters);

               // Collect results. The NUnit result is kept next to the log file of this run.
               string localResultFile = RunContext.GetResultFilePath(_environment);
               string localLogFile = Path.GetTempFileName() + ".log";
               guest.CopyFileToHost(Path.Combine(guestTestPath, "TestResult.xml"), localResultFile);
               guest.CopyFileToHost(Path.Combine(guestTestPath, "TestResult.log"), localLogFile);

               Logger.Info($"Test {_testIndex} - NUnit result saved to {localResultFile}");

               var doc = new XmlDocument();
               doc.Load(localResultFile);

               var failedAttribute = doc.LastChild?.Attributes?["failed"]?.Value;
               int failedCount = failedAttribute != null ? Convert.ToInt32(failedAttribute) : 0;

               if (failedCount == 0)
                  return;

               string resultContent = File.ReadAllText(localResultFile);
               string logContent = File.ReadAllText(localLogFile);
               string failureSummary = NUnitResultParser.SummarizeFailures(doc);
               throw new TestFailedException($"{resultContent}\r\n\r\n{logContent}", failureSummary);
            }
         }
         finally
         {
            try
            {
               vm.PowerOff();
            }
            catch (Exception ex)
            {
               Logger.Error(ex, "Unable to power off VM. Maybe it's not powered on?");
            }
         }
      }

      /// <summary>
      /// Where the assemblies of the suite under test are built.
      /// </summary>
      private string GetTestBinRelativePath()
      {
         switch (_environment.TestSuite)
         {
            case TestSuite.VolumeTests:
               return VolumeTestsBinRelativePath;
            default:
               return RegressionTestsBinRelativePath;
         }
      }

      /// <summary>
      /// Turns page heap on for hMailServer.exe. gflags writes the setting to the registry,
      /// so the service has to be restarted for it to take effect.
      /// </summary>
      private void EnablePageHeap(IGuestSession guest, string guestTestPath)
      {
         string guestGFlagsPath = Path.Combine(guestTestPath, "gflags.exe");

         guest.CopyFileToGuest(_environment.GFlagsPath, guestGFlagsPath);
         guest.RunProgram(guestGFlagsPath, "/p /enable hMailServer.exe", true);

         guest.RunProgram(@"C:\Windows\System32\net.exe", "stop hMailServer");
         guest.RunProgram(@"C:\Windows\System32\net.exe", "start hMailServer");
      }

      /// <summary>
      /// Creates the session used to reach the inside of the guest. Guests older than
      /// Windows 10 have neither PowerShell Direct nor the guest service interface.
      /// </summary>
      private IGuestSession CreateGuestSession(HyperV vm)
      {
         switch (_environment.GuestTransport)
         {
            case GuestTransport.Network:
               return new NetworkGuestSession(_environment.GuestAddress, Username, Password, _testIndex);
            default:
               return new PowerShellDirectGuestSession(vm, _environment.VMName, Username, Password, _testIndex);
         }
      }

      /// <summary>
      /// Runs the setup program. Setup returns a non-zero exit code if the installation
      /// fails, for example if the database couldn't be upgraded.
      /// </summary>
      private void RunSetup(IGuestSession guest, string setupPath, string parameters)
      {
         try
         {
            guest.RunProgram(setupPath, parameters, true);
         }
         catch (Exception ex)
         {
            throw new Exception($"{ex.Message}{Environment.NewLine}{Environment.NewLine}{GetSetupLog(guest)}", ex);
         }
      }

      private string GetSetupLog(IGuestSession guest)
      {
         try
         {
            string localSetupLog = Path.GetTempFileName() + ".log";
            guest.CopyFileToHost(SetupLogPath, localSetupLog);

            return File.ReadAllText(localSetupLog);
         }
         catch (Exception ex)
         {
            return $"The setup log {SetupLogPath} could not be read: {ex.Message}";
         }
      }

      private void CopyLocalVersion(IGuestSession guest)
      {
         string currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

         var localExecutable = Path.Combine(currentDir,
            @"..\..\..\..\..\..\source\Server\hMailServer\x64\Release\hMailServer.exe");

         if (!File.Exists(localExecutable))
         {
            throw new Exception($"The executable {localExecutable} could not be found.");
         }

         guest.RunProgram(@"C:\Windows\System32\net.exe", "stop hMailServer");
         // New installations go to Program Files\ArzoieMail; an environment that upgrades an
         // hMailServer installation keeps its Program Files\hMailServer folder instead.
         guest.CopyFileToGuest(localExecutable, @"C:\Program Files\ArzoieMail\Bin\hMailServer.exe");
         guest.RunProgram(@"C:\Windows\System32\net.exe", "start hMailServer");
      }

   }
}
