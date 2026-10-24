// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

#include "stdafx.h"

#include "SecretMigration.h"

#include "../Application/PropertySet.h"
#include "../SQL/SQLCommand.h"
#include "../Util/Crypt.h"
#include "../Util/SecretProtector.h"

#ifdef _DEBUG
#define DEBUG_NEW new(_NORMAL_BLOCK, __FILE__, __LINE__)
#define new DEBUG_NEW
#endif

namespace HM
{
   namespace
   {
      struct SecretColumn
      {
         const wchar_t *table;
         const wchar_t *idColumn;
         const wchar_t *valueColumn;
         const wchar_t *nameColumn;
      };

      // Table and column names, not values: they are the server's own.
      const SecretColumn SecretColumns[] =
      {
         { L"hm_settings", L"settingid", L"settingstring", L"settingname" },
         { L"hm_fetchaccounts", L"faid", L"fapassword", L"" },
         { L"hm_routes", L"routeid", L"routeauthenticationpassword", L"" },
      };
   }

   void
   SecretMigration::Run()
   {
      SecretProtector *protector = SecretProtector::Instance();

      // Without a key there is nothing to protect them under. SecretProtector has said why.
      if (!protector->HasKey())
         return;

      int migrated = 0;
      int failed = 0;

      if (IniFileSettings::Instance()->GetPasswordNeedsProtecting())
      {
         // Written back the way SetPassword writes it now.
         IniFileSettings::Instance()->SetPassword(IniFileSettings::Instance()->GetPassword());

         if (IniFileSettings::Instance()->GetPasswordNeedsProtecting())
            failed++;
         else
            migrated++;
      }

      for (const SecretColumn &column : SecretColumns)
         migrated += MigrateColumn_(column.table, column.idColumn, column.valueColumn, column.nameColumn, failed);

      if (migrated > 0)
         LOG_APPLICATION(Formatter::Format("SecretMigration: {0} secrets written by an earlier version were protected under the installation's secret key.", migrated));

      if (failed > 0)
      {
         ErrorManager::Instance()->ReportError(ErrorManager::High, 5740, "SecretMigration::Run",
            Formatter::Format("{0} secrets written by an earlier version could not be protected under the installation's secret key, and are still Blowfish-encrypted.", failed));
      }
   }

   int
   SecretMigration::MigrateColumn_(const String &table, const String &idColumn, const String &valueColumn,
                                   const String &nameColumn, int &failed)
   {
      SecretProtector *protector = SecretProtector::Instance();

      String columns = idColumn + _T(", ") + valueColumn;
      if (!nameColumn.IsEmpty())
         columns += _T(", ") + nameColumn;

      SQLCommand select(_T("select ") + columns + _T(" from ") + table);

      std::shared_ptr<DALRecordset> recordset = Application::Instance()->GetDBManager()->OpenRecordset(select);
      if (!recordset)
      {
         failed++;
         return 0;
      }

      struct Pending { __int64 id; String stored; };
      std::vector<Pending> pending;

      while (!recordset->IsEOF())
      {
         String stored = recordset->GetStringValue(AnsiString(valueColumn));

         bool include = nameColumn.IsEmpty() || PropertySet::IsCryptedProperty(recordset->GetStringValue(AnsiString(nameColumn)));

         // What an earlier version wrote, and what was protected under a key an import has
         // replaced. A value under another installation's key is not this one's to change;
         // reading it reports that.
         bool needed = include && !stored.IsEmpty() && !protector->IsProtectedWithCurrentKey(stored) &&
                       (SecretProtector::IsBlowfishValue(stored) || protector->IsProtectedWithPreviousKey(stored));

         if (needed)
            pending.push_back({ recordset->GetInt64Value(AnsiString(idColumn)), stored });

         recordset->MoveNext();
      }

      // Updated once the recordset is read, rather than while it is open on the connection.
      int migrated = 0;
      for (const Pending &row : pending)
      {
         String plain;
         String reason;
         if (!protector->TryUnprotect(row.stored, plain, reason))
         {
            failed++;
            continue;
         }

         String protectedValue = protector->Protect(plain);

         if (!protector->IsProtectedWithCurrentKey(protectedValue))
         {
            failed++;
            continue;
         }

         // Guarded by the old value, so that a value changed in the meantime - by another
         // server sharing the database - is left as that server wrote it.
         SQLCommand update(_T("update ") + table + _T(" set ") + valueColumn + _T(" = @VALUE where ") + idColumn +
                           _T(" = @ID and ") + valueColumn + _T(" = @OLDVALUE"));
         update.AddParameter("@VALUE", protectedValue);
         update.AddParameter("@ID", row.id);
         update.AddParameter("@OLDVALUE", row.stored);

         if (Application::Instance()->GetDBManager()->Execute(update))
            migrated++;
         else
            failed++;
      }

      return migrated;
   }

   void
   SecretMigrationTester::Test()
   {
      SecretProtector *protector = SecretProtector::Instance();

      for (const SecretColumn &column : SecretColumns)
      {
         String columns = String(column.valueColumn);
         String nameColumn = column.nameColumn;
         if (!nameColumn.IsEmpty())
            columns += _T(", ") + nameColumn;

         SQLCommand select(_T("select ") + columns + _T(" from ") + column.table);
         std::shared_ptr<DALRecordset> recordset = Application::Instance()->GetDBManager()->OpenRecordset(select);
         if (!recordset)
            throw std::logic_error(("SecretMigration: " + AnsiString(column.table) + " could not be read.").c_str());

         while (!recordset->IsEOF())
         {
            String stored = recordset->GetStringValue(AnsiString(column.valueColumn));
            bool include = nameColumn.IsEmpty() || PropertySet::IsCryptedProperty(recordset->GetStringValue(AnsiString(nameColumn)));

            if (include && !stored.IsEmpty() && !protector->IsProtectedWithCurrentKey(stored))
            {
               throw std::logic_error(("SecretMigration: a secret in " + AnsiString(column.table) +
                  " is not protected under the installation's key: " + AnsiString(stored.Left(12)) + "...").c_str());
            }

            recordset->MoveNext();
         }
      }

      if (IniFileSettings::Instance()->GetPasswordNeedsProtecting())
         throw std::logic_error("SecretMigration: the database password in hMailServer.ini is not protected under the installation's key.");
   }
}
