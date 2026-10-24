// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

#pragma once

namespace HM
{
   // Protects again, under the installation's secret key, the secrets that earlier versions
   // Blowfish-encrypted with the key in the source code: the database password in
   // hMailServer.ini, the crypted settings, and the passwords of external accounts and routes.
   //
   // Run once the database is connected, before the configuration is loaded, at every start.
   // A secret already protected under the key is left alone, so after the first start it does
   // nothing but read; a value under another installation's key is left alone as well.
   class SecretMigration
   {
   public:

      static void Run();

   private:

      // Re-protects the Blowfish values of one column. With a nameColumn, only the rows whose
      // name is a crypted setting are. Returns the number re-protected, and adds the ones that
      // could not be to failed.
      static int MigrateColumn_(const String &table, const String &idColumn, const String &valueColumn,
                                const String &nameColumn, int &failed);
   };

   // Checks what the migration leaves behind: every secret in the database protected under
   // the installation's key.
   class SecretMigrationTester
   {
   public:
      void Test();
   };
}
