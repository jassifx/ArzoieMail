// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

#include "stdafx.h"

#include "BackupSecrets.h"
#include "../Util/Crypt.h"

#ifdef _DEBUG
#define DEBUG_NEW new(_NORMAL_BLOCK, __FILE__, __LINE__)
#define new DEBUG_NEW
#endif

namespace HM
{
   BackupSecrets::Mode BackupSecrets::mode_ = BackupSecrets::ProtectedByInstallationKey;

   void
   BackupSecrets::SetMode(Mode mode)
   {
      mode_ = mode;
   }

   String
   BackupSecrets::Store(const String &plain)
   {
      if (mode_ == ProtectedByArchive)
         return plain;

      return Crypt::Instance()->EnCrypt(plain, Crypt::ETSecret);
   }

   String
   BackupSecrets::LoadPassword(const String &stored)
   {
      if (mode_ == ProtectedByArchive)
         return stored;

      // ETSecret reads both a value under the installation's key and a Blowfish one.
      return Crypt::Instance()->DeCrypt(stored, Crypt::ETSecret);
   }

   String
   BackupSecrets::LoadSetting(const String &stored)
   {
      if (mode_ == ProtectedByInstallationKey)
         return Crypt::Instance()->DeCrypt(stored, Crypt::ETSecret);

      // In an encrypted archive, or written plain by an earlier version.
      return stored;
   }

   String
   BackupSecrets::ModeName(Mode mode)
   {
      switch (mode)
      {
      case ProtectedByArchive:
         return _T("Archive");
      case ProtectedByInstallationKey:
         return _T("InstallationKey");
      default:
         return _T("");
      }
   }

   BackupSecrets::Mode
   BackupSecrets::ModeFromName(const String &name)
   {
      if (name.CompareNoCase(_T("Archive")) == 0)
         return ProtectedByArchive;

      if (name.CompareNoCase(_T("InstallationKey")) == 0)
         return ProtectedByInstallationKey;

      return Legacy;
   }
}
