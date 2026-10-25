// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

#pragma once

#include <mutex>
#include <set>

namespace HM
{
   // Encrypts the secrets the server has to be able to read back - the database password in
   // hMailServer.ini, the passwords of external accounts and routes, and the crypted settings.
   //
   // They used to be Blowfish-encrypted with a key that is in the source code, which anyone
   // could undo. They are encrypted with AES-256-GCM now, under a key generated for each
   // installation and kept in hMailServer.ini protected by DPAPI (machine scope): only this
   // computer can unprotect it, so a copy of the ini file, the database or a backup is of no
   // use elsewhere without it.
   //
   // A protected value reads aes1:<key id>:<base64 of nonce, ciphertext and tag>. The key id is
   // the start of the SHA-256 of the key, so that a value protected by another installation's
   // key - two servers sharing a database - is recognised as such rather than read as garbage.
   // A Blowfish value, hex digits only, is still read, so values written before stay readable
   // until they are protected again.
   class SecretProtector : public Singleton<SecretProtector>
   {
   public:

      SecretProtector();

      // The value protected under the installation's key; an empty string stays empty. Without
      // a key that could be kept in hMailServer.ini, the value is Blowfish-encrypted as before,
      // so that it is never encrypted under a key that would then be lost.
      String Protect(const String &plain);

      // The plain value of a protected or a Blowfish value. False, with the reason reported,
      // when the value cannot be read: another installation's key, a key that cannot be
      // unprotected on this computer, or a value that has been changed.
      bool Unprotect(const String &stored, String &plain);

      // As above, with an empty string for a value that cannot be read.
      String Unprotect(const String &stored);

      // As Unprotect, but reports nothing: reason says why the value could not be read.
      bool TryUnprotect(const String &stored, String &plain, String &reason);

      // True for a value protected under this installation's key, which needs no migration.
      bool IsProtectedWithCurrentKey(const String &stored);

      // True for a value protected under a key this server held before an import, which
      // SecretMigration protects again under the current one.
      bool IsProtectedWithPreviousKey(const String &stored);

      // True when the installation has a key it could keep: values are protected with AES.
      bool HasKey();

      // The id of the installation's key, as it appears in its values; empty without a key.
      // Two servers sharing a database need the same one.
      AnsiString GetKeyId();

      // The installation's key, encrypted with AES-256-GCM under a key derived from the
      // passphrase (PBKDF2-HMAC-SHA256), for ImportKey on another computer. The passphrase
      // must have at least MinimumPassphraseLength characters.
      bool ExportKey(const String &passphrase, String &exported, String &error);

      // Makes the exported key the installation's own, kept in hMailServer.ini under this
      // computer's DPAPI. A different key it held before is kept for this run, so that
      // SecretMigration, which the caller runs next, protects everything under the new key.
      bool ImportKey(const String &exported, const String &passphrase, String &error);

      static bool IsBlowfishValue(const String &stored);

      static const int MinimumPassphraseLength = 12;

   private:

      bool LoadOrCreateKey_();
      bool StoreKey_(const std::vector<unsigned char> &key);
      const std::vector<unsigned char> *KeyById_(const AnsiString &keyId);
      void ReportOnce_(const AnsiString &reason, const String &message);

      std::mutex mutex_;
      bool key_loaded_;
      bool key_available_;
      std::vector<unsigned char> key_;
      AnsiString key_id_;

      // Keys replaced by an import during this run, by id.
      std::map<AnsiString, std::vector<unsigned char>> previous_keys_;

      std::set<AnsiString> reported_;
   };

   class SecretProtectorTester
   {
   public:
      void Test();
   };
}
