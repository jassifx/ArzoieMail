// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

using System;
using System.Collections.Generic;
using System.Windows.Forms;
using hMailServer.Shared;

namespace DataDirectorySynchronizer
{
   static class Program
   {
      /// <summary>
      /// The main entry point for the application.
      /// </summary>
      [STAThread]
      static void Main()
      {
         Application.EnableVisualStyles();
         Application.SetCompatibleTextRenderingDefault(false);
         // .NET made Segoe UI 9pt the default; the forms are laid out for the .NET Framework one.
         Application.SetDefaultFont(new System.Drawing.Font(new System.Drawing.FontFamily("Microsoft Sans Serif"), 8.25f));

         CommandLineParser.Parse();

         hMailServer.Application application = new hMailServer.Application();
         if (!Authenticator.AuthenticateUser(application))
            return;

         Globals.SetApp(application);

         Application.Run(new formMain());
      }
   }
}