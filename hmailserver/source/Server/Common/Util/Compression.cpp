// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

#include "stdafx.h"

#include "Compression.h"
#include "ProcessLauncher.h"

#include <boost/thread/thread.hpp>

#ifdef _DEBUG
#define DEBUG_NEW new(_NORMAL_BLOCK, __FILE__, __LINE__)
#define new DEBUG_NEW
#endif

namespace HM
{

   Compression::Compression()
   {
      
   }

   Compression::~Compression(void)
   {

   }

   Compression::Compression(const String &passphrase) :
      passphrase_(passphrase)
   {

   }

   String
   Compression::GetPassphraseParameter_(bool writing) const
   {
      // Quoted as the C runtime reads a command line: a backslash only escapes when it is
      // followed by a quote, so backslashes before a quote - and before the closing one - are
      // doubled, and a quote is escaped. Any passphrase reaches 7za as it was given.
      //
      // Without a passphrase, reading still passes one: 7za would otherwise stop and ask for
      // it on an encrypted archive, and nobody would answer. A wrong one fails the command.
      String passphrase = passphrase_.IsEmpty() && !writing ? String(_T("no passphrase")) : passphrase_;
      if (passphrase.IsEmpty())
         return _T("");

      String quoted = _T("\"-p");
      int backslashes = 0;
      for (int i = 0; i < passphrase.GetLength(); i++)
      {
         wchar_t c = passphrase.GetAt(i);
         if (c == '\\')
         {
            backslashes++;
            continue;
         }

         if (c == '"')
            quoted += String(backslashes * 2 + 1, '\\') + _T("\"");
         else
            quoted += String(backslashes, '\\') + String(1, c);

         backslashes = 0;
      }

      quoted += String(backslashes * 2, '\\') + _T("\"");

      // -mhe: the file names are encrypted as well, so that nothing of the backup shows.
      if (writing)
         quoted += _T(" -mhe=on");

      return quoted;
   }

   bool
   Compression::AddDirectory(const String &zipFile, const String &directoryToAdd)
   {
      // -r = recurse -t = type 7z -mmt = number of threads -mx1 = lowest compression
      String commandLine = Formatter::Format("\"{0}\" a \"{1}\" \"{2}\" -r -t7z {3} -mx1  -w\"{4}\"",
         GetExecutableFullPath_(), zipFile, directoryToAdd, GetMultithreadingParameter_(), IniFileSettings::Instance()->GetTempDirectory()) + _T(" ") + GetPassphraseParameter_(true);

      return LaunchCommand_(commandLine);
   }

   bool
   Compression::AddFile(const String &zipFile, const String &fileToAdd)
   {
      // -t = type 7z -mmt = number of threads -mx1 = lowest compression
      String commandLine = Formatter::Format("\"{0}\" a \"{1}\" \"{2}\" -t7z {3} -mx1 -w\"{4}\"",
         GetExecutableFullPath_(), zipFile, fileToAdd, GetMultithreadingParameter_(), IniFileSettings::Instance()->GetTempDirectory()) + _T(" ") + GetPassphraseParameter_(true);

      return LaunchCommand_(commandLine);
   }

   bool
   Compression::Uncompress(const String &zipFile, const String &targetDirectory)
   {
      return Uncompress(zipFile, targetDirectory, "*");
   }

   bool
   Compression::Uncompress(const String &zipFile, const String &targetDirectory, const String &wildCard)
   {
      // -o = output directory -mmt = number of threads -y = assume yes on all queries
      String commandLine = Formatter::Format("\"{0}\" x \"{1}\" \"{2}\" -o\"{3}\" {4} -y", 
         GetExecutableFullPath_(), zipFile, wildCard, targetDirectory, GetMultithreadingParameter_()) + _T(" ") + GetPassphraseParameter_(false);

      return LaunchCommand_(commandLine);
   }

   bool 
   Compression::LaunchCommand_(const String &commandLine)
   {
      unsigned int exitCode = 0;
      ProcessLauncher processLauncher(commandLine);
      processLauncher.SetLoggedCommandLine(MaskPassphrase_(commandLine));

      if (!processLauncher.Launch(exitCode))
         return false;

      if (exitCode != 0 && exitCode != 1)
         return false;

      return true;
   }

   String
   Compression::MaskPassphrase_(const String &commandLine) const
   {
      // What the error log may show of a command line: the passphrase, if there is one, as ***.
      String passphraseParameter = GetPassphraseParameter_(true);
      if (passphrase_.IsEmpty() || passphraseParameter.IsEmpty())
         return commandLine;

      String masked = commandLine;
      masked.Replace(passphraseParameter, _T("\"-p***\" -mhe=on"));
      masked.Replace(GetPassphraseParameter_(false), _T("\"-p***\""));
      return masked;
   }

   String
   Compression::GetMultithreadingParameter_()
   {
      // Backup and restore both run while the server is processing email, so 7za is given at most
      // half of the logical processors rather than all of them. This matters on restore too:
      // an archive written with several threads is split into blocks that 7za will otherwise
      // decode on every core at once.
      unsigned int processorCount = boost::thread::hardware_concurrency();
      unsigned int threadCount = processorCount / 2;

      if (threadCount < 2)
         return "-mmt=off";

      return Formatter::Format("-mmt={0}", threadCount);
   }

   String 
   Compression::GetExecutableFullPath_()
   {
      const String ZipExecutable = "7za.exe";

      String binDir = IniFileSettings::Instance()->GetBinDirectory();

      return FileUtilities::Combine(binDir, ZipExecutable);
   }
}