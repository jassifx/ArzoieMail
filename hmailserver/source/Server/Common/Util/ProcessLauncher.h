// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

#pragma once

namespace HM
{
   class ProcessLauncher
   {
   public:
      ProcessLauncher(const String &commandLine, const String &workingDirectory);
      ProcessLauncher(const String &commandLine);
      ~ProcessLauncher(void);

      bool Launch(unsigned int &exitCode);

      void SetErrorLogTimeout(unsigned int milliseconds);

      // What the error messages show as the command line, when the one run holds
      // something that must not be logged, such as a passphrase.
      void SetLoggedCommandLine(const String &commandLine);

   private:

      unsigned int error_log_timeout_;

      String command_line_;
      String logged_command_line_;
      String working_directory_;
   };
}