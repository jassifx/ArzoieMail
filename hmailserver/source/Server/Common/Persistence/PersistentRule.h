// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

#pragma once

namespace HM
{
   class Rule;
   enum PersistenceMode;

   class PersistentRule
   {
   public:
      PersistentRule(void);
      ~PersistentRule(void);

      static bool ReadObject(std::shared_ptr<Rule> pRule, const SQLCommand& sSQL);
      static bool ReadObject(std::shared_ptr<Rule> pRule, std::shared_ptr<DALRecordset> pRS);

      static bool SaveObject(std::shared_ptr<Rule> pRule, String &errorMessage, PersistenceMode mode);
      static bool SaveObject(std::shared_ptr<Rule> pRule);
      // Management edits are fail-closed: persist inactive first, check every child write,
      // and only activate after the complete definition succeeds. No rollback is implied.
      static bool SaveCheckedObject(std::shared_ptr<Rule> rule, bool replaceCriteria, bool replaceActions);
      static bool DeleteCheckedObject(std::shared_ptr<Rule> rule);
      static bool DeleteObject(std::shared_ptr<Rule> pRule);

      static void DeleteByAccountID(__int64 ID);

   private:

      static void NotifyReload_(std::shared_ptr<Rule> pRule);
   };
}
