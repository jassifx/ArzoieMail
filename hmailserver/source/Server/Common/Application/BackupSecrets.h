// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

#pragma once

namespace HM
{
   // How the secrets in a backup - the passwords of external accounts and routes, the relay
   // password, the SRS secret and the backup passphrase - are written into its XML and read
   // back, set by BackupExecuter for the backup or restore it runs. One runs at a time.
   class BackupSecrets
   {
   public:

      enum Mode
      {
         // A backup with a passphrase: the whole archive is encrypted under it, so the secrets
         // inside are plain, and the backup restores on any computer that has the passphrase.
         ProtectedByArchive,

         // A backup without one: the secrets are protected under the installation's secret key,
         // as in the database. Restoring them on another computer needs that key imported.
         ProtectedByInstallationKey,

         // A backup made by an earlier version, which says neither: passwords of accounts and
         // routes Blowfish-encrypted (or under the installation's key), settings plain.
         Legacy
      };

      static void SetMode(Mode mode);

      // The value to write into the XML for a secret.
      static String Store(const String &plain);

      // The secret a value read from the XML stands for. A setting was written plain by earlier
      // versions; the passwords of accounts and routes were encrypted.
      static String LoadPassword(const String &stored);
      static String LoadSetting(const String &stored);

      // The BackupInformation attribute that says how a backup holds its secrets.
      static String ModeName(Mode mode);
      static Mode ModeFromName(const String &name);

   private:

      static Mode mode_;
   };
}
