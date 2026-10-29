// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

#include "stdafx.h"
#include "PropertySet.h"
#include "BackupSecrets.h"
#include "Property.h"
#include "../SQL/DALRecordset.h"
#include "../Util/Crypt.h"


#ifdef _DEBUG
#define DEBUG_NEW new(_NORMAL_BLOCK, __FILE__, __LINE__)
#define new DEBUG_NEW
#endif


using namespace std;


namespace HM
{
   PropertySet::PropertySet(void)
   {
   }

   PropertySet::~PropertySet(void)
   {
      
   }

   void 
   PropertySet::Refresh()
   {
      boost::lock_guard<boost::recursive_mutex> writes(checked_mutex_);
      SQLCommand command("select * from hm_settings");
      std::shared_ptr<DALRecordset> pRS = Application::Instance()->GetDBManager()->OpenRecordset(command);
   
      if (!pRS)
         return;

      std::map<String, std::shared_ptr<Property> > tmpMap;

      while (!pRS->IsEOF())
      {
         String sPropertyName = pRS->GetStringValue("settingname");
         long lPropertyLong = pRS->GetLongValue("settinginteger");
         String sPropertyString = pRS->GetStringValue("settingstring");

         bool bIsCrypted = false;
         if (IsCryptedProperty(sPropertyName))
         {
            // De-crypt the string after load from DB.
            sPropertyString = Crypt::Instance()->DeCrypt(sPropertyString, Crypt::ETSecret);
            bIsCrypted = true;
         }
      
         std::shared_ptr<Property> oProperty = std::shared_ptr<Property> (new Property(sPropertyName, lPropertyLong, sPropertyString));
         if (bIsCrypted)
            oProperty->SetIsCrypted();

         tmpMap[sPropertyName] = oProperty;

         pRS->MoveNext();
      }



      {
         boost::lock_guard<boost::recursive_mutex> cache(cache_mutex_);
         items_ = tmpMap;
      }

      auto iter = items_.begin();
      auto iterEnd = items_.end();
      for (; iter != iterEnd; iter++)
      {
         // Trigger an change-event for all options.
         OnPropertyChanged_((*iter).second);
      }
   }

   std::shared_ptr<Property>
   PropertySet::GetProperty_(const String & sPropertyName)
   {
      boost::lock_guard<boost::recursive_mutex> cache(cache_mutex_);
      auto iterProperty = items_.find(sPropertyName);
   
      if (iterProperty != items_.end())
         return (*iterProperty).second;

      String sErrorMessage;
      sErrorMessage.Format(_T("The property %s could not be found."), sPropertyName.c_str());
      ErrorManager::Instance()->ReportError(ErrorManager::Medium, 5015, "PropertySet::GetProperty_()", sErrorMessage);

      // Property was not found. Create one temporary in memory 
      // to avoid further problems.
      std::shared_ptr<Property> oProperty = std::shared_ptr<Property>(new Property());
      return oProperty;
   }

   long
   PropertySet::GetLong(const String &sPropertyName)
   {
      return GetProperty_(sPropertyName)->GetLongValue();
   }

   bool
   PropertySet::GetBool(const String &sPropertyName)
   {
      return GetProperty_(sPropertyName)->GetBoolValue();
   }

   String
   PropertySet::GetString(const String &sPropertyName)
   {
      return GetProperty_(sPropertyName)->GetStringValue();
   }

   void 
   PropertySet::SetLong(const String &sPropertyName, long lValue)
   {
      boost::lock_guard<boost::recursive_mutex> writes(checked_mutex_);
      std::shared_ptr<Property> pProperty = GetProperty_(sPropertyName);
      bool bChanged = lValue != pProperty->GetLongValue();
      pProperty->SetLongValue(lValue);

      if (bChanged)
         OnPropertyChanged_(pProperty);
   }

   void 
   PropertySet::SetBool(const String &sPropertyName, bool bValue)
   {
      boost::lock_guard<boost::recursive_mutex> writes(checked_mutex_);
      std::shared_ptr<Property> pProperty = GetProperty_(sPropertyName);
      bool bChanged = bValue != pProperty->GetBoolValue();
      pProperty->SetBoolValue(bValue);

      if (bChanged)
         OnPropertyChanged_(pProperty);
   }

   void 
   PropertySet::SetString(const String &sPropertyName, const String &sValue)
   {
      boost::lock_guard<boost::recursive_mutex> writes(checked_mutex_);
      std::shared_ptr<Property> pProperty = GetProperty_(sPropertyName);
      bool bChanged = sValue != pProperty->GetStringValue();
      pProperty->SetStringValue(sValue);

      if (bChanged)
         OnPropertyChanged_(pProperty);
   }

   PropertySet::CheckedResult
   PropertySet::ApplyChecked(const std::vector<CheckedChange> &changes)
   {
      if (changes.empty() || changes.size() > 128) return CheckedResult::Failure;
      std::vector<std::shared_ptr<Property>> notify;
      {
         boost::lock_guard<boost::recursive_mutex> guard(checked_mutex_);
         std::set<String> names;
         for (const auto &change : changes)
         {
            const auto item = items_.find(change.name);
            if (item == items_.end() || IsCryptedProperty(change.name) || !names.insert(change.name).second)
               return CheckedResult::Failure;
            if (change.text ? item->second->GetStringValue() != change.expectedString
                            : item->second->GetLongValue() != change.expectedNumber)
            {
               LOG_DEBUG("Management settings: cached precondition changed for " + change.name);
               return CheckedResult::Conflict;
            }
         }
         auto manager = Application::Instance()->GetDBManager();
         String error;
         auto connection = manager->BeginTransaction(error);
         if (!connection) return CheckedResult::Failure;
         bool pending = true;
         try
         {
            for (const auto &change : changes)
            {
               SQLCommand update(change.text
                  ? "update hm_settings set settingstring = @VALUE where settingname = @NAME and settingstring = @EXPECTED"
                  : "update hm_settings set settinginteger = @VALUE where settingname = @NAME and settinginteger = @EXPECTED");
               // Compact's ADO provider binds by ordinal even with NamedParameters=true.
               // Keep the binding order identical to the SQL, also for named providers.
               if (change.text)
                  update.AddParameter("@VALUE", change.value);
               else
                  update.AddParameter("@VALUE", change.number);
               update.AddParameter("@NAME", change.name);
               if (change.text)
                  update.AddParameter("@EXPECTED", change.expectedString);
               else
                  update.AddParameter("@EXPECTED", change.expectedNumber);
               // Do not reconnect/retry inside a transaction: losing the connection loses
               // the transaction. Verify the guarded update on that same connection.
               if (connection->TryExecute(update, error) != DALConnection::DALSuccess)
               {
                  pending = false;
                  manager->RollbackTransaction(connection, error);
                  return CheckedResult::Failure;
               }
               SQLCommand select("select settinginteger, settingstring from hm_settings where settingname = @NAME");
               select.AddParameter("@NAME", change.name);
               auto row = connection->CreateRecordset();
               if (!row || row->TryOpen(connection, select, error) != DALConnection::DALSuccess)
               {
                  pending = false;
                  manager->RollbackTransaction(connection, error);
                  return CheckedResult::Failure;
               }
               const bool matches = !row->IsEOF() && (change.text
                  ? row->GetStringValue("settingstring") == change.value
                  : row->GetLongValue("settinginteger") == change.number);
               if (!matches)
               {
                  LOG_DEBUG("Management settings: persisted precondition changed for " + change.name);
                  pending = false;
                  manager->RollbackTransaction(connection, error);
                  return CheckedResult::Conflict;
               }
            }
            pending = false; // CommitTransaction releases the leased connection even on failure.
            if (!manager->CommitTransaction(connection, error)) return CheckedResult::Failure;
            for (const auto &change : changes)
            {
               auto item = items_.at(change.name);
               if (change.text) item->SetStringValueWithoutSaving(change.value);
               else item->SetLongValueWithoutSaving(change.number);
               notify.push_back(item);
            }
         }
         catch (...)
         {
            if (pending) manager->RollbackTransaction(connection, error);
            throw;
         }
      }
      // Observers see all committed values; never run external callbacks while holding
      // the batch mutex. Database outages leave the previous effective cache intact.
      for (const auto &item : notify) OnPropertyChanged_(item);
      return CheckedResult::Success;
   }

   String
   PropertySet::InitializeStringIfEmpty(const String &sPropertyName, const String &sValue)
   {
      boost::lock_guard<boost::recursive_mutex> writes(checked_mutex_);
      bool bIsCrypted = IsCryptedProperty(sPropertyName);

      String sStoredValue = bIsCrypted ? Crypt::Instance()->EnCrypt(sValue, Crypt::ETSecret) : sValue;

      // A single guarded statement, so that two servers sharing a database and starting at
      // the same time cannot both believe they have set the value: whichever of them gets
      // there first is the one the write takes effect for, and the other updates no rows.
      // Encrypting leaves an empty string empty, so the guard holds for a crypted property
      // as well.
      SQLCommand command("update hm_settings set settingstring = @SETTINGSTRING where settingname = @SETTINGNAME and (settingstring is null or settingstring = '')");
      command.AddParameter("@SETTINGSTRING", sStoredValue);
      command.AddParameter("@SETTINGNAME", sPropertyName);

      if (!Application::Instance()->GetDBManager()->Execute(command))
         return "";

      // Read back rather than assume our own write is the one which took: a server signing
      // with a secret the database no longer holds cannot reverse what the other one hands
      // out, and the other one cannot reverse what it hands out either.
      SQLCommand selectCommand("select settingstring from hm_settings where settingname = @SETTINGNAME");
      selectCommand.AddParameter("@SETTINGNAME", sPropertyName);

      std::shared_ptr<DALRecordset> pRS = Application::Instance()->GetDBManager()->OpenRecordset(selectCommand);

      if (!pRS || pRS->IsEOF())
         return "";

      String sResult = pRS->GetStringValue("settingstring");

      if (bIsCrypted)
         sResult = Crypt::Instance()->DeCrypt(sResult, Crypt::ETSecret);

      // Only this one property is brought up to date. A full Refresh() would re-fire the
      // change handler of every property, and this runs in the middle of the configuration
      // being loaded.
      GetProperty_(sPropertyName)->SetStringValueWithoutSaving(sResult);

      return sResult;
   }

   void 
   PropertySet::OnPropertyChanged_(std::shared_ptr<Property> pProperty)
   {
      // Notify configuration that a setting has changed.
      Configuration::Instance()->OnPropertyChanged(pProperty);
   }

   bool 
   PropertySet::IsCryptedProperty(const String &sPropertyName)
   {
      if (sPropertyName == PROPERTY_SMTPRELAYER_PASSWORD)
         return true;

      // The SRS secret is what stops someone from making up an address which relays mail
      // through this server, so it is stored the way the relay password is.
      if (sPropertyName == PROPERTY_SRS_SECRET)
         return true;

      // The passphrase backups are encrypted under.
      if (sPropertyName == PROPERTY_BACKUP_PASSPHRASE)
         return true;

      return false;
   }

   bool
   PropertySet::XMLStore(XNode *pBackupNode)
   {
      boost::lock_guard<boost::recursive_mutex> writes(checked_mutex_);
      XNode *pPropertiesNode = pBackupNode->AppendChild(_T("Properties"));
      auto iterProperty = items_.begin();

      while (iterProperty != items_.end())
      {
         std::shared_ptr<Property> oProperty = (*iterProperty).second;

         XNode *pNode = pPropertiesNode->AppendChild(String(oProperty->GetName()));

         pNode->AppendAttr(_T("LongValue"), StringParser::IntToString(oProperty->GetLongValue()));
         // A secret is written as BackupSecrets says: plain in an encrypted archive, protected
         // under the installation's key otherwise - never plain in an archive anyone can open.
         String value = oProperty->GetStringValue();
         if (IsCryptedProperty(oProperty->GetName()) && !value.IsEmpty())
            value = BackupSecrets::Store(value);

         pNode->AppendAttr(_T("StringValue"), value);

         iterProperty++;
      }      

      return true;
   }

   bool
   PropertySet::XMLLoad(XNode *pBackupNode)
   {
      boost::lock_guard<boost::recursive_mutex> writes(checked_mutex_);
      XNode *pPropertiesNode = pBackupNode->GetChild(_T("Properties"));
      if (!pPropertiesNode)
         return true;

      for (int i = 0; i < pPropertiesNode->GetChildCount(); i++)
      {
         XNode *pPropertyNode = pPropertiesNode->GetChild(i);
         String sName = pPropertyNode->name;
         String sStringValue = pPropertyNode->GetAttrValue(_T("StringValue"));
         if (IsCryptedProperty(sName) && !sStringValue.IsEmpty())
            sStringValue = BackupSecrets::LoadSetting(sStringValue);
         int iLongValue = _ttoi(pPropertyNode->GetAttrValue(_T("LongValue")));

         std::shared_ptr<Property> pProperty = GetProperty_(sName);
         if (pProperty)
         {
            pProperty->SetStringValue(sStringValue);
            pProperty->SetLongValue(iLongValue);
         }
      }

      return true;
   }
}
