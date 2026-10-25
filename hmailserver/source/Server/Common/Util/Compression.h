// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

#pragma once

namespace HM
{
   class Compression
   {
   public:
      Compression();

      // Archives written are AES-256-encrypted under the passphrase, the names of the files
      // in them included, and archives read are decrypted with it. Empty: not encrypted.
      explicit Compression(const String &passphrase);
      ~Compression(void);

      bool AddDirectory(const String &zipFile, const String &directoryToAdd);
      bool AddFile(const String &zipFile, const String &fileToAdd);

      bool Uncompress(const String &zipFile, const String &targetDirectory);
      bool Uncompress(const String &zipFile, const String &targetDirectory, const String &wildCard);

   private:
 
      bool LaunchCommand_(const String &commandLine);
      String MaskPassphrase_(const String &commandLine) const;
      String GetExecutableFullPath_();
      String GetMultithreadingParameter_();
      String GetPassphraseParameter_(bool writing) const;

      String passphrase_;

   };
}