// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

#pragma once

#include "DALConnection.h"
#include "TransactionLease.h"

namespace HM
{
   class SQLCommand;

   class DatabaseConnectionManager
   {
   public:
      DatabaseConnectionManager(void);
      ~DatabaseConnectionManager(void);

      bool CreateConnections(String &sErrorMessage);
      
      void Disconnect();

      bool Execute(const SQLStatement &statement, __int64 *iInsertID, int iIgnoreErrors, String &sErrorMessage);
      bool Execute(const SQLCommand &command, __int64 *iInsertID, int iIgnoreErrors, String &sErrorMessage);

      bool Execute(const SQLStatement &statement, __int64 *iInsertID = 0, int iIgnoreErrors = 0)
      {
         String sErrorMessage;
         return Execute(statement, iInsertID, iIgnoreErrors, sErrorMessage);
      }

      bool Execute(const SQLCommand &command, __int64 *iInsertID = 0, int iIgnoreErrors = 0)
      {
         String sErrorMessage;
         return Execute(command, iInsertID, iIgnoreErrors, sErrorMessage);
      }
      
      std::shared_ptr<DALRecordset> OpenRecordset(const SQLStatement &statement);
      std::shared_ptr<DALRecordset> OpenRecordset(const SQLCommand &command);

      int GetCurrentDatabaseVersion();

      bool GetIsConnected();

      std::shared_ptr<DALConnection> BeginTransaction(String &sErrorMessage, TransactionLease::BeginResult *result = nullptr);
      bool CommitTransaction(std::shared_ptr<DALConnection> pConnection, String &sErrorMessage);
      bool RollbackTransaction(std::shared_ptr<DALConnection> pConnection, String &sErrorMessage);
      bool ExecuteScript(const String &sFile, String &sErrorMessage);

      bool EnsuresPrerequisites(long DBVersion, String &sErrorMessage);
   private:

      DALConnection::ConnectionResult Connect_(String &sErrorMessage);

      std::shared_ptr<DALConnection> GetConnection_();
      void ReleaseConnection_(std::shared_ptr<DALConnection> pConn);
      void RetireConnection_(std::shared_ptr<DALConnection> connection) noexcept;
 
      boost::recursive_mutex mutex_;
      
      std::set<std::shared_ptr<DALConnection> > busy_connections_;
      std::set<std::shared_ptr<DALConnection> > available_connections_;
      

   };
}
