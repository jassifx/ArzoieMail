// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

#pragma once

namespace HM
{
   class Account;

   class EmailAllUsers
   {
   public:
      EmailAllUsers(void);
      ~EmailAllUsers(void);

      bool Start(const String &sRecipientWildcard, const String &sFromAddress, const String &sFromName, const String &sSubject, const String &sBody);

      // The active accounts in active domains whose address matches the wildcard.
      static std::vector<std::shared_ptr<Account> > GetRecipients(const String &sRecipientWildcard);

      // Queues one message to the given accounts. False if there are none or it could not be saved.
      static bool Send(const std::vector<std::shared_ptr<Account> > &recipients, const String &sFromAddress, const String &sFromName, const String &sSubject, const String &sBody);

   };
}