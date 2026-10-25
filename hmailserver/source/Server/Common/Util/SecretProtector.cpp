// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

#include "stdafx.h"

#include "SecretProtector.h"
#include "Crypt.h"
#include "Unicode.h"

#include <wincrypt.h>

#include <openssl/evp.h>
#include <openssl/rand.h>

#ifdef _DEBUG
#define DEBUG_NEW new(_NORMAL_BLOCK, __FILE__, __LINE__)
#define new DEBUG_NEW
#endif

namespace HM
{
   namespace
   {
      const char Prefix[] = "aes1:";
      const size_t KeyLength = 32;
      const size_t NonceLength = 12;
      const size_t TagLength = 16;

      // Mixed into the DPAPI protection of the key, so that it unprotects only for this purpose.
      const wchar_t KeyEntropy[] = L"ArzoieMail installation secret key, version 1";

      // Unicode::WideToMultiByte leaves its buffer's terminating null in the string it returns.
      bool ToUtf8(const String &input, AnsiString &output)
      {
         if (!Unicode::WideToMultiByte(input, output))
            return false;

         while (!output.empty() && output.back() == '\0')
            output.pop_back();

         return true;
      }

      AnsiString ToBase64(const std::vector<unsigned char> &data)
      {
         std::vector<unsigned char> encoded(4 * ((data.size() + 2) / 3) + 1);
         int length = EVP_EncodeBlock(encoded.data(), data.data(), (int) data.size());
         return AnsiString((const char *) encoded.data(), length);
      }

      bool FromBase64(const AnsiString &text, std::vector<unsigned char> &data)
      {
         if (text.empty() || text.size() % 4 != 0)
            return false;

         data.resize(3 * (text.size() / 4));
         int length = EVP_DecodeBlock(data.data(), (const unsigned char *) text.data(), (int) text.size());
         if (length < 0)
            return false;

         // EVP_DecodeBlock counts the bytes the padding stands for as well. Indexed through
         // std::string, whose operator[] takes a size_t, which AnsiString's overloads make
         // ambiguous.
         const std::string &chars = text;
         size_t padding = 0;
         if (chars[chars.size() - 1] == '=') padding++;
         if (chars[chars.size() - 2] == '=') padding++;

         data.resize(length - padding);
         return true;
      }

      AnsiString KeyIdOf(const std::vector<unsigned char> &key)
      {
         unsigned char digest[EVP_MAX_MD_SIZE];
         unsigned int digestLength = 0;
         EVP_Digest(key.data(), key.size(), digest, &digestLength, EVP_sha256(), nullptr);

         const char hex[] = "0123456789abcdef";
         AnsiString id;
         for (int i = 0; i < 4; i++)
         {
            id += hex[digest[i] >> 4];
            id += hex[digest[i] & 0x0f];
         }

         return id;
      }

      bool Dpapi(bool protect, const std::vector<unsigned char> &input, std::vector<unsigned char> &output)
      {
         DATA_BLOB in = { (DWORD) input.size(), (BYTE *) input.data() };
         DATA_BLOB entropy = { (DWORD) (sizeof(KeyEntropy) - sizeof(wchar_t)), (BYTE *) KeyEntropy };
         DATA_BLOB out = { 0, nullptr };

         // Machine scope: the service account and the administrator on this computer can both
         // unprotect it, and nobody elsewhere can.
         DWORD flags = CRYPTPROTECT_UI_FORBIDDEN | (protect ? CRYPTPROTECT_LOCAL_MACHINE : 0);

         BOOL done = protect ?
            CryptProtectData(&in, L"ArzoieMail secret key", &entropy, nullptr, nullptr, flags, &out) :
            CryptUnprotectData(&in, nullptr, &entropy, nullptr, nullptr, flags, &out);

         if (!done)
            return false;

         output.assign(out.pbData, out.pbData + out.cbData);
         SecureZeroMemory(out.pbData, out.cbData);
         LocalFree(out.pbData);
         return true;
      }
   }

   SecretProtector::SecretProtector() :
      key_loaded_(false),
      key_available_(false)
   {

   }

   bool
   SecretProtector::LoadOrCreateKey_()
   {
      // Called with mutex_ held.
      if (key_loaded_)
         return key_available_;

      key_loaded_ = true;

      AnsiString stored = AnsiString(IniFileSettings::Instance()->GetSecretKey());

      if (stored.empty())
      {
         // The installation's first secret: a new key, kept only if it can be kept.
         std::vector<unsigned char> key(KeyLength);

         if (RAND_bytes(key.data(), (int) key.size()) != 1)
         {
            ReportOnce_("create", "A secret key for the installation could not be created. Secrets are Blowfish-encrypted, as by earlier versions.");
            return false;
         }

         bool stored_ok = StoreKey_(key);
         SecureZeroMemory(key.data(), key.size());

         if (!stored_ok)
         {
            ReportOnce_("store", "The secret key of the installation could not be stored in hMailServer.ini. Secrets are Blowfish-encrypted, as by earlier versions, until the server can write to it.");
            return false;
         }

         stored = AnsiString(IniFileSettings::Instance()->GetSecretKey());
      }

      std::vector<unsigned char> blob;
      std::vector<unsigned char> key;
      if (!FromBase64(stored, blob) || !Dpapi(false, blob, key) || key.size() != KeyLength)
      {
         ReportOnce_("unprotect", "The secret key in hMailServer.ini cannot be unprotected on this computer. If the file comes from another computer, import the key exported there. Until then, secrets protected under it cannot be read, and new ones are Blowfish-encrypted.");
         return false;
      }

      key_ = key;
      SecureZeroMemory(key.data(), key.size());
      key_id_ = KeyIdOf(key_);
      key_available_ = true;
      return true;
   }

   bool
   SecretProtector::StoreKey_(const std::vector<unsigned char> &key)
   {
      std::vector<unsigned char> blob;
      if (!Dpapi(true, key, blob))
         return false;

      AnsiString encoded = ToBase64(blob);
      IniFileSettings::Instance()->SetSecretKey(String(encoded));

      // Read back: a key that did not reach hMailServer.ini would be lost at the next start,
      // and everything protected under it with it.
      return AnsiString(IniFileSettings::Instance()->GetSecretKey()) == encoded;
   }

   const std::vector<unsigned char> *
   SecretProtector::KeyById_(const AnsiString &keyId)
   {
      // Called with mutex_ held, after LoadOrCreateKey_.
      if (key_available_ && keyId == key_id_)
         return &key_;

      auto previous = previous_keys_.find(keyId);
      return previous == previous_keys_.end() ? nullptr : &previous->second;
   }

   bool
   SecretProtector::HasKey()
   {
      std::lock_guard<std::mutex> lock(mutex_);
      return LoadOrCreateKey_();
   }

   AnsiString
   SecretProtector::GetKeyId()
   {
      std::lock_guard<std::mutex> lock(mutex_);
      return LoadOrCreateKey_() ? key_id_ : AnsiString();
   }

   namespace
   {
      // An exported key reads arzoiemail-key1:<base64 of salt, iterations, nonce, ciphertext
      // and tag>. The header - salt and iterations - is authenticated as associated data.
      const char ExportPrefix[] = "arzoiemail-key1:";
      const size_t SaltLength = 16;
      const unsigned int ExportIterations = 600000;   // OWASP's figure for PBKDF2-HMAC-SHA256

      bool DeriveExportKey(const String &passphrase, const unsigned char *salt, unsigned int iterations, unsigned char *derived)
      {
         AnsiString utf8;
         if (!ToUtf8(passphrase, utf8))
            return false;

         bool done = PKCS5_PBKDF2_HMAC(utf8.data(), (int) utf8.size(), salt, (int) SaltLength, (int) iterations,
                                       EVP_sha256(), (int) KeyLength, derived) == 1;
         SecureZeroMemory(utf8.data(), utf8.size());
         return done;
      }
   }

   bool
   SecretProtector::ExportKey(const String &passphrase, String &exported, String &error)
   {
      exported.clear();

      if (passphrase.GetLength() < MinimumPassphraseLength)
      {
         error.Format(_T("The passphrase must have at least %d characters."), MinimumPassphraseLength);
         return false;
      }

      std::vector<unsigned char> key;
      {
         std::lock_guard<std::mutex> lock(mutex_);
         if (!LoadOrCreateKey_())
         {
            error = _T("The installation has no secret key that can be exported.");
            return false;
         }
         key = key_;
      }

      // salt | iterations (big-endian) | nonce | ciphertext | tag
      std::vector<unsigned char> sealed(SaltLength + 4 + NonceLength + KeyLength + TagLength);
      unsigned char *salt = sealed.data();
      unsigned char *iterations = salt + SaltLength;
      unsigned char *nonce = iterations + 4;
      unsigned char *ciphertext = nonce + NonceLength;
      unsigned char *tag = ciphertext + KeyLength;

      iterations[0] = (unsigned char) (ExportIterations >> 24);
      iterations[1] = (unsigned char) (ExportIterations >> 16);
      iterations[2] = (unsigned char) (ExportIterations >> 8);
      iterations[3] = (unsigned char) ExportIterations;

      unsigned char derived[KeyLength];
      bool done = RAND_bytes(salt, (int) SaltLength) == 1 && RAND_bytes(nonce, (int) NonceLength) == 1 &&
                  DeriveExportKey(passphrase, salt, ExportIterations, derived);

      EVP_CIPHER_CTX *context = EVP_CIPHER_CTX_new();
      int length = 0;
      done = done && context &&
         EVP_EncryptInit_ex(context, EVP_aes_256_gcm(), nullptr, derived, nonce) == 1 &&
         EVP_EncryptUpdate(context, nullptr, &length, salt, (int) (SaltLength + 4)) == 1 &&
         EVP_EncryptUpdate(context, ciphertext, &length, key.data(), (int) KeyLength) == 1 &&
         EVP_EncryptFinal_ex(context, ciphertext + length, &length) == 1 &&
         EVP_CIPHER_CTX_ctrl(context, EVP_CTRL_GCM_GET_TAG, (int) TagLength, tag) == 1;
      EVP_CIPHER_CTX_free(context);

      SecureZeroMemory(derived, sizeof(derived));
      SecureZeroMemory(key.data(), key.size());

      if (!done)
      {
         error = _T("The secret key could not be encrypted for export.");
         return false;
      }

      exported = String(AnsiString(ExportPrefix) + ToBase64(sealed));
      return true;
   }

   bool
   SecretProtector::ImportKey(const String &exported, const String &passphrase, String &error)
   {
      AnsiString value = AnsiString(exported);
      value.Trim();

      const size_t prefixLength = sizeof(ExportPrefix) - 1;
      std::vector<unsigned char> sealed;

      if (value.compare(0, prefixLength, ExportPrefix) != 0 || !FromBase64(value.substr(prefixLength), sealed) ||
          sealed.size() != SaltLength + 4 + NonceLength + KeyLength + TagLength)
      {
         error = _T("This is not an exported ArzoieMail secret key.");
         return false;
      }

      const unsigned char *salt = sealed.data();
      const unsigned char *iterationBytes = salt + SaltLength;
      const unsigned char *nonce = iterationBytes + 4;
      const unsigned char *ciphertext = nonce + NonceLength;
      unsigned char *tag = sealed.data() + SaltLength + 4 + NonceLength + KeyLength;

      unsigned int iterations = ((unsigned int) iterationBytes[0] << 24) | ((unsigned int) iterationBytes[1] << 16) |
                                ((unsigned int) iterationBytes[2] << 8) | (unsigned int) iterationBytes[3];

      // Bounded, so that a made-up export cannot keep the server busy for hours.
      if (iterations < 100000 || iterations > 10000000)
      {
         error = _T("This is not an exported ArzoieMail secret key.");
         return false;
      }

      unsigned char derived[KeyLength];
      std::vector<unsigned char> key(KeyLength + 16);

      bool done = DeriveExportKey(passphrase, salt, iterations, derived);

      EVP_CIPHER_CTX *context = EVP_CIPHER_CTX_new();
      int length = 0;
      int total = 0;
      done = done && context &&
         EVP_DecryptInit_ex(context, EVP_aes_256_gcm(), nullptr, derived, nonce) == 1 &&
         EVP_DecryptUpdate(context, nullptr, &length, salt, (int) (SaltLength + 4)) == 1 &&
         EVP_DecryptUpdate(context, key.data(), &length, ciphertext, (int) KeyLength) == 1;
      total = length;
      done = done &&
         EVP_CIPHER_CTX_ctrl(context, EVP_CTRL_GCM_SET_TAG, (int) TagLength, tag) == 1 &&
         EVP_DecryptFinal_ex(context, key.data() + total, &length) == 1;
      EVP_CIPHER_CTX_free(context);
      SecureZeroMemory(derived, sizeof(derived));

      if (!done || total + length != (int) KeyLength)
      {
         SecureZeroMemory(key.data(), key.size());
         error = _T("The passphrase is wrong, or the exported key has been changed.");
         return false;
      }

      key.resize(KeyLength);

      std::lock_guard<std::mutex> lock(mutex_);

      bool hadKey = LoadOrCreateKey_();
      if (hadKey && key == key_)
      {
         // Already this installation's key.
         SecureZeroMemory(key.data(), key.size());
         return true;
      }

      if (!StoreKey_(key))
      {
         SecureZeroMemory(key.data(), key.size());
         error = _T("The secret key could not be stored in hMailServer.ini.");
         return false;
      }

      // The key it replaces is kept for this run, so that what was protected under it can be
      // read once more and protected under the new one.
      if (hadKey)
         previous_keys_[key_id_] = key_;

      key_ = key;
      SecureZeroMemory(key.data(), key.size());
      key_id_ = KeyIdOf(key_);
      key_loaded_ = true;
      key_available_ = true;

      // A key that could not be used before, now replaced, is reported anew if it happens again.
      reported_.clear();
      return true;
   }

   bool
   SecretProtector::IsProtectedWithPreviousKey(const String &stored)
   {
      AnsiString value = AnsiString(stored);
      const size_t prefixLength = sizeof(Prefix) - 1;

      size_t separator = value.find(':', prefixLength);
      if (value.compare(0, prefixLength, Prefix) != 0 || separator == std::string::npos)
         return false;

      std::lock_guard<std::mutex> lock(mutex_);
      return previous_keys_.count(value.substr(prefixLength, separator - prefixLength)) > 0;
   }

   void
   SecretProtector::ReportOnce_(const AnsiString &reason, const String &message)
   {
      // The fetch and delivery threads unprotect the same values again and again, so each
      // reason is reported once a run rather than on every attempt.
      if (!reported_.insert(reason).second)
         return;

      ErrorManager::Instance()->ReportError(ErrorManager::High, 5739, "SecretProtector", message);
   }

   bool
   SecretProtector::IsBlowfishValue(const String &stored)
   {
      // BlowFishEncryptor writes whole 8-byte blocks as hex digits.
      if (stored.IsEmpty() || stored.GetLength() % 16 != 0)
         return false;

      for (int i = 0; i < stored.GetLength(); i++)
      {
         if (!iswxdigit(stored[i]))
            return false;
      }

      return true;
   }

   String
   SecretProtector::Protect(const String &plain)
   {
      if (plain.IsEmpty())
         return "";

      std::lock_guard<std::mutex> lock(mutex_);

      if (!LoadOrCreateKey_())
         return Crypt::Instance()->EnCrypt(plain, Crypt::ETBlowFish);

      AnsiString utf8;
      if (!ToUtf8(plain, utf8))
      {
         ReportOnce_("convert", "A secret could not be converted to UTF-8 to be protected.");
         return "";
      }

      AnsiString associated = AnsiString(Prefix) + key_id_;

      std::vector<unsigned char> sealed(NonceLength + utf8.size() + TagLength);
      unsigned char *nonce = sealed.data();
      unsigned char *ciphertext = nonce + NonceLength;
      unsigned char *tag = ciphertext + utf8.size();

      bool done = RAND_bytes(nonce, (int) NonceLength) == 1;

      EVP_CIPHER_CTX *context = EVP_CIPHER_CTX_new();
      int length = 0;
      done = done && context &&
         EVP_EncryptInit_ex(context, EVP_aes_256_gcm(), nullptr, key_.data(), nonce) == 1 &&
         EVP_EncryptUpdate(context, nullptr, &length, (const unsigned char *) associated.data(), (int) associated.size()) == 1 &&
         EVP_EncryptUpdate(context, ciphertext, &length, (const unsigned char *) utf8.data(), (int) utf8.size()) == 1 &&
         EVP_EncryptFinal_ex(context, ciphertext + length, &length) == 1 &&
         EVP_CIPHER_CTX_ctrl(context, EVP_CTRL_GCM_GET_TAG, (int) TagLength, tag) == 1;
      EVP_CIPHER_CTX_free(context);

      SecureZeroMemory(utf8.data(), utf8.size());

      if (!done)
      {
         ReportOnce_("encrypt", "A secret could not be encrypted.");
         return "";
      }

      return String(associated + ":" + ToBase64(sealed));
   }

   bool
   SecretProtector::TryUnprotect(const String &stored, String &plain, String &reason)
   {
      plain.clear();

      if (stored.IsEmpty())
         return true;

      if (IsBlowfishValue(stored))
      {
         plain = Crypt::Instance()->DeCrypt(stored, Crypt::ETBlowFish);
         return true;
      }

      AnsiString value = AnsiString(stored);
      const size_t prefixLength = sizeof(Prefix) - 1;

      size_t separator = value.find(':', prefixLength);
      if (value.compare(0, prefixLength, Prefix) != 0 || separator == std::string::npos)
      {
         reason = "The value is neither protected nor Blowfish-encrypted.";
         return false;
      }

      AnsiString keyId = value.substr(prefixLength, separator - prefixLength);

      std::lock_guard<std::mutex> lock(mutex_);

      if (!LoadOrCreateKey_())
      {
         reason = "The installation has no secret key it can use.";
         return false;
      }

      const std::vector<unsigned char> *key = KeyById_(keyId);
      if (!key)
      {
         reason = "The value was protected under the secret key of another installation (" + String(keyId) +
            "), not this one's (" + String(key_id_) + "). Import that installation's key to read it.";
         return false;
      }

      std::vector<unsigned char> sealed;
      if (!FromBase64(value.substr(separator + 1), sealed) || sealed.size() < NonceLength + TagLength)
      {
         reason = "The protected value is malformed.";
         return false;
      }

      AnsiString associated = AnsiString(Prefix) + keyId;

      size_t ciphertextLength = sealed.size() - NonceLength - TagLength;
      const unsigned char *nonce = sealed.data();
      const unsigned char *ciphertext = nonce + NonceLength;
      unsigned char *tag = sealed.data() + NonceLength + ciphertextLength;

      std::vector<unsigned char> utf8(ciphertextLength + 1);

      EVP_CIPHER_CTX *context = EVP_CIPHER_CTX_new();
      int length = 0;
      int total = 0;
      bool done = context &&
         EVP_DecryptInit_ex(context, EVP_aes_256_gcm(), nullptr, key->data(), nonce) == 1 &&
         EVP_DecryptUpdate(context, nullptr, &length, (const unsigned char *) associated.data(), (int) associated.size()) == 1 &&
         EVP_DecryptUpdate(context, utf8.data(), &length, ciphertext, (int) ciphertextLength) == 1;
      total = length;
      done = done &&
         EVP_CIPHER_CTX_ctrl(context, EVP_CTRL_GCM_SET_TAG, (int) TagLength, tag) == 1 &&
         EVP_DecryptFinal_ex(context, utf8.data() + total, &length) == 1;
      EVP_CIPHER_CTX_free(context);

      if (!done)
      {
         SecureZeroMemory(utf8.data(), utf8.size());
         reason = "The protected value does not decrypt: it has been changed, or was not protected under this key.";
         return false;
      }

      total += length;
      AnsiString decrypted((const char *) utf8.data(), total);
      SecureZeroMemory(utf8.data(), utf8.size());

      if (!Unicode::MultiByteToWide(decrypted, plain))
      {
         reason = "The decrypted value is not UTF-8.";
         return false;
      }

      SecureZeroMemory(decrypted.data(), decrypted.size());
      return true;
   }

   bool
   SecretProtector::Unprotect(const String &stored, String &plain)
   {
      String reason;
      if (TryUnprotect(stored, plain, reason))
         return true;

      std::lock_guard<std::mutex> lock(mutex_);
      ReportOnce_(AnsiString(reason), "A secret could not be read. " + reason);
      return false;
   }

   String
   SecretProtector::Unprotect(const String &stored)
   {
      String plain;
      Unprotect(stored, plain);
      return plain;
   }

   bool
   SecretProtector::IsProtectedWithCurrentKey(const String &stored)
   {
      std::lock_guard<std::mutex> lock(mutex_);

      if (!LoadOrCreateKey_())
         return false;

      AnsiString current = AnsiString(Prefix) + key_id_ + ":";
      return AnsiString(stored).compare(0, current.size(), current) == 0;
   }

   void
   SecretProtectorTester::Test()
   {
      SecretProtector *protector = SecretProtector::Instance();

      auto fail = [](const AnsiString &message) { throw std::logic_error(("SecretProtector: " + message).c_str()); };

      if (!protector->HasKey())
         fail("the installation has no secret key.");

      // Round trips, including an empty value and one that is not ASCII.
      const String values[] = { _T(""), _T("secret"), _T("päss wörd ✓ 漢 ' \\ \" ;--") };
      for (const String &value : values)
      {
         String stored = protector->Protect(value);
         String plain;
         String reason;

         if (!value.IsEmpty() && (stored.Find(_T("aes1:")) != 0 || !protector->IsProtectedWithCurrentKey(stored)))
            fail("a protected value has the wrong format: " + AnsiString(stored));

         if (!value.IsEmpty() && stored.Find(value) >= 0)
            fail("the protected value contains the plain value.");

         if (!protector->TryUnprotect(stored, plain, reason) || plain != value)
            fail("a value does not unprotect to itself: " + AnsiString(reason));
      }

      // Every protection gets its own nonce.
      if (protector->Protect(_T("same")) == protector->Protect(_T("same")))
         fail("two protections of one value are the same.");

      // A changed value, and one claiming another installation's key, are refused.
      {
         // A character inside the nonce, where all six bits it stands for are used - not one
         // next to the padding, whose spare bits do not change what it decodes to.
         String stored = protector->Protect(_T("secret"));
         int at = stored.Find(_T(":"), 5) + 5;
         String changed = stored.Left(at) + (stored[at] == 'A' ? _T("B") : _T("A")) + stored.Mid(at + 1);

         String plain, reason;
         if (protector->TryUnprotect(changed, plain, reason))
            fail("a changed value was unprotected.");

         String otherKey = _T("aes1:00000000") + stored.Mid(stored.Find(_T(":"), 5));
         if (protector->IsProtectedWithCurrentKey(otherKey) || protector->TryUnprotect(otherKey, plain, reason))
            fail("a value under another key was unprotected.");
      }

      // Values Blowfish-encrypted by earlier versions are still read.
      {
         String legacy = Crypt::Instance()->EnCrypt(_T("old secret"), Crypt::ETBlowFish);
         String plain, reason;

         if (!SecretProtector::IsBlowfishValue(legacy) || protector->IsProtectedWithCurrentKey(legacy))
            fail("a Blowfish value is not recognised as one.");

         if (!protector->TryUnprotect(legacy, plain, reason) || plain != _T("old secret"))
            fail("a Blowfish value does not unprotect.");
      }

      // Anything else is refused rather than taken for a secret.
      {
         String plain, reason;
         if (protector->TryUnprotect(_T("not a secret"), plain, reason))
            fail("an unencrypted value was taken for a secret.");
      }

      // Export and import: a short passphrase is refused, a wrong one and a changed export do
      // not import, and importing the installation's own key changes nothing.
      {
         AnsiString keyId = protector->GetKeyId();
         String exported, error;

         if (protector->ExportKey(_T("too short"), exported, error))
            fail("a key was exported under a short passphrase.");

         if (!protector->ExportKey(_T("correct horse battery staple"), exported, error) || exported.Find(_T("arzoiemail-key1:")) != 0)
            fail("the key could not be exported: " + AnsiString(error));

         if (protector->ImportKey(exported, _T("wrong horse battery staple"), error))
            fail("a key was imported with the wrong passphrase.");

         int at = exported.GetLength() - 10;
         String changed = exported.Left(at) + (exported[at] == 'A' ? _T("B") : _T("A")) + exported.Mid(at + 1);
         if (protector->ImportKey(changed, _T("correct horse battery staple"), error))
            fail("a changed export was imported.");

         if (!protector->ImportKey(exported, _T("correct horse battery staple"), error) || protector->GetKeyId() != keyId)
            fail("importing the installation's own key changed it: " + AnsiString(error));
      }
   }
}
