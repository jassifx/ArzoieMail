// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

#pragma once

namespace HM
{
   class BlowFishEncryptor;
   
   class Crypt : public Singleton<Crypt>
   {
   public:
	   Crypt();
	   virtual ~Crypt();

      enum EncryptionType
      {
         ETNone = 0,
         ETBlowFish = 1,
         ETMD5 = 2,
         ETSHA256 = 3,
         ETPHC = 4,
         ETSecret = 5
      };
      // ETPHC means "the stored string describes itself" - see PasswordHasher.
      // Adding another key derivation function later does not require a new
      // value here, nor a database migration.
      //
      // ETSecret is a secret the server reads back, protected under the installation's key
      // (see SecretProtector). Decrypting it also reads a Blowfish value, which every such
      // secret was before. ETBlowFish itself is left for what still has to be Blowfish: the
      // BlowfishEncrypt and BlowfishDecrypt of the COM API, and account passwords that very
      // old versions stored Blowfish-encrypted, which PasswordValidator still accepts.

      EncryptionType GetHashType(const String &hash);

      String EnCrypt(const String &sInput, EncryptionType iType) const;
      String DeCrypt(const String &sInput, EncryptionType iType) const;

      bool Validate(const String &password, const String &originalHash, EncryptionType iType) const;

   private:

      BlowFishEncryptor *blow_fish_;

   };
}
