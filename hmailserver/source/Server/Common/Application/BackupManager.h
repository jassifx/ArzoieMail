// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

#pragma once

namespace HM
{
   class Backup;

   class BackupManager
   {
   public:
      BackupManager(void);
      ~BackupManager(void);

      bool StartBackup();
      // Backups the server according to the settings
      // specified in the configuration.

      std::shared_ptr<Backup> LoadBackup(const String &sZipFile) const;

      // A backup encrypted under the passphrase. Its backup file is empty when it could not be
      // read: a wrong passphrase, or no backup at all.
      std::shared_ptr<Backup> LoadBackup(const String &sZipFile, const String &passphrase) const;
      // Loads a backup from an XML stream and returns
      // an backup object that contains backup information.

      bool StartRestore(std::shared_ptr<Backup> pBackup);
      // Starts a restore of the backup. Restores the part of the
      // backup specified by iRestoreMode.

      void OnThreadStopped();
      // Called by backup/restore thread when operation has finished.

      void SetStatus(const String &sStatus);
      String GetStatus();

      void OnBackupCompleted();
      void OnBackupFailed(const String &sReason);
 
   private:

      boost::recursive_mutex mutex_;
      String log_;
      bool is_running_;
   };
}