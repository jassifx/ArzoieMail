// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

#pragma once

namespace HM
{
   class DALRecordset;
   class Property;
      
   class PropertySet
   {
   public:
      PropertySet(void);
      ~PropertySet(void);

      void Refresh();


      long GetLong(const String &sPropertyName);
      bool GetBool(const String &sPropertyName);
      String GetString(const String &sPropertyName);

      void SetLong(const String &sPropertyName, long lValue);
      void SetBool(const String &sPropertyName, bool lValue);
      void SetString(const String &sPropertyName, const String &lValue);

      struct CheckedChange
      {
         String name;
         bool text;
         long expectedNumber;
         long number;
         String expectedString;
         String value;
      };
      enum class CheckedResult { Success, Conflict, Failure };
      // Management allowlists choose the names. Commit all values before changing the cache
      // or firing notifications. A stale persisted/cache value rejects the complete patch.
      CheckedResult ApplyChecked(const std::vector<CheckedChange> &changes);

      String InitializeStringIfEmpty(const String &sPropertyName, const String &sValue);
      // Writes sValue only if the property is empty in the database, and returns the value
      // the database holds afterwards - which is another server's if it got there first,
      // and an empty string if the property could not be written or read.

      // True for the settings stored as secrets (Crypt::ETSecret): the relay password and the
      // SRS secret.
      static bool IsCryptedProperty(const String &sPropertyName);

      bool XMLStore(XNode *pBackupNode);
      bool XMLLoad(XNode *pBackupNode);

   private:

      void OnPropertyChanged_(std::shared_ptr<Property> pProperty);

      std::shared_ptr<Property> GetProperty_(const String &sPropertyName);

      std::map<String, std::shared_ptr<Property> > items_;
      boost::recursive_mutex checked_mutex_;
      boost::recursive_mutex cache_mutex_;
   };
}
