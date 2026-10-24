// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

using System;
using System.Collections.Generic;
using System.Windows.Forms;
using hMailServer.Shared;

namespace DBUpdater
{
   static class Program
   {
      /// <summary>
      /// The main entry point for the application.
      /// </summary>
      [STAThread]
      static int Main()
      {
         Application.EnableVisualStyles();
         Application.SetCompatibleTextRenderingDefault(false);
         // .NET made Segoe UI 9pt the default; the forms are laid out for the .NET Framework one.
         Application.SetDefaultFont(new System.Drawing.Font(new System.Drawing.FontFamily("Microsoft Sans Serif"), 8.25f));

         string databaseOldErrorMessage = "The database is too old for this version of ArzoieMail.";

         try
         {
            CommandLineParser.Parse();

            hMailServer.Application application = new hMailServer.Application();

             try
             {
                 application.Connect();
             }
             catch (Exception ex)
             {
                 if (!ex.Message.Contains(databaseOldErrorMessage))
                     throw;

             }


            int from = application.Database.CurrentVersion;
            int to = application.Database.RequiredVersion;

            if (from == to)
            {
               if (!CommandLineParser.ContainsArgument("/SilentIfOk") && !CommandLineParser.IsSilent())
                  MessageBox.Show("Your ArzoieMail database is already up to date.", "ArzoieMail Administrator");

               return ExitCodes.Success;
            }

            bool silent = CommandLineParser.IsSilent();

            string password = CommandLineParser.ContainsArgument("password")
                                 ? CommandLineParser.GetArgument("password")
                                 : null;

            if (!Authenticator.AuthenticateUser(application, password, !silent))
               return ExitCodes.AuthenticationFailed;

            formMain main = new formMain(application);

            if (!main.LoadSettings())
               return ExitCodes.UpgradeFailed;

            if (!main.CreateUpgradePath())
               return ExitCodes.UpgradeFailed;

            if (silent)
            {
               // Silently perform the upgrade
               if (!main.DoUpgrade())
                  return ExitCodes.UpgradeFailed;

               return ExitCodes.Success;
            }

            // Do it the default way.
            Application.Run(main);

            return ExitCodes.Success;
         }
         catch (Exception ex)
         {
            // Never show a dialog when running unattended - there may be no desktop
            // to show it on, and the installer would hang or the process crash.
            if (!CommandLineParser.IsSilent())
               MessageBox.Show(ex.Message + Environment.NewLine + Environment.NewLine + "Please check the ArzoieMail error log for further details.", "ArzoieMail Administrator", MessageBoxButtons.OK, MessageBoxIcon.Error);

            return ExitCodes.UnexpectedError;
         }
      }
   }
}
