// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

using System;
using System.IO;
using hMailServer;
using NUnit.Framework;
using RegressionTests.Infrastructure;

namespace RegressionTests.Shared
{
   public class TestFixtureBase
   {
      protected Application _application;
      protected Domain _domain;
      protected Settings _settings;

      [OneTimeSetUp]
      public void TestFixtureSetUp()
      {
         SingletonProvider<TestSetup>.Instance.Authenticate();

         _application = SingletonProvider<TestSetup>.Instance.GetApp();
         _settings = _application.Settings;
      }

      [SetUp]
      public void SetUp()
      {
         ServiceRestartDetector.ValidateProcessId();

         // Logged after the previous test was checked, for example by background delivery. Only
         // a warning here, since this test didn't cause it.
         if (File.Exists(LogHandler.GetErrorLogFileName()))
            Assert.Warn("Errors were logged before this test started:" + Environment.NewLine + LogHandler.ReadAndDeleteErrorLog());

         _domain = SingletonProvider<TestSetup>.Instance.PerformBasicSetup();

         LogHandler.DeleteCurrentDefaultLog();

         // make sure we have internet access.
         TestSetup.GetLocalIpAddress();
      }

      [TearDown]
      public void TearDown()
      {
         if (TestContext.CurrentContext.Result.FailCount > 0)
         {
            // The console runner only prints the failures once the whole run is over. Printed
            // here as well, a run that is cut short - a job timeout - still says why tests failed.
            Console.WriteLine("Failure: " + TestContext.CurrentContext.Result.Message);
            Console.WriteLine(TestContext.CurrentContext.Result.StackTrace);

            Console.WriteLine("hMailServer log:");
            Console.WriteLine(LogHandler.ReadCurrentDefaultLog());
            Console.WriteLine();
         }

         // Deleted, so an error fails only the test that caused it.
         if (File.Exists(LogHandler.GetErrorLogFileName()))
            Assert.Fail("Errors were logged during the test:" + Environment.NewLine + LogHandler.ReadAndDeleteErrorLog());
      }
   }
}