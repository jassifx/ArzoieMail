// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

#include "stdafx.h"
#include "./DatabaseConnectionManager.h"

#include "DALConnection.h"
#include "DALConnectionFactory.h"
#include "DatabaseSettings.h"

#include "ADORecordset.h"
#include "MySQLRecordset.h"
#include "PGRecordset.h"
#include "SQLCERecordset.h"
#include "MySQLInterface.h"

#include "SQLCommand.h"
#include "TransactionLease.h"

#include "Prerequisites/PrerequisiteList.h"
#include "SQLScriptRunner.h"

#ifdef _DEBUG
#define DEBUG_NEW new(_NORMAL_BLOCK, __FILE__, __LINE__)
#define new DEBUG_NEW
#endif


#ifdef _DEBUG
#define DEBUG_NEW new(_NORMAL_BLOCK, __FILE__, __LINE__)
#define new DEBUG_NEW
#endif

namespace HM
{
   DatabaseConnectionManager::DatabaseConnectionManager(void)
   {
   }

   DatabaseConnectionManager::~DatabaseConnectionManager(void)
   {
   }

   bool
   DatabaseConnectionManager::CreateConnections(String &sErrorMessage)
   {
      int iNoOfConnectionAttempts = IniFileSettings::Instance()->GetNumberOfDatabaseConnectionAttempts();
      int iDelayBetweenConnectionAttempt = IniFileSettings::Instance()->GetDBConnectionAttemptsDelay() * 1000;


      bool bConnectionOK = false;
      for (int iTry = 1; iTry <= iNoOfConnectionAttempts ; iTry++)
      {     
         DALConnection::ConnectionResult iResult = Connect_(sErrorMessage);

         switch (iResult)
         {
         case DALConnection::Connected:
            // Reset the error message. If we failed to connect the first time,
            // but succeeded the second, it's not really an error to care about.
            sErrorMessage = "";
            return true;
         case DALConnection::FatalError:
            // Skip out and report error.
            return false;
         case DALConnection::TemporaryFailure:
            // We failed to connect to the database server.
            // Pause a few seconds and then try again.
            Sleep(iDelayBetweenConnectionAttempt);
            break;
         }




      }  

      return false;
   }

   DALConnection::ConnectionResult
   DatabaseConnectionManager::Connect_(String &sErrorMessage)
   {
      int iNoOfConnections = IniFileSettings::Instance()->GetNumberOfDatabaseConnections();

      IniFileSettings *pIniFileSettings = IniFileSettings::Instance();

      String sProvider = pIniFileSettings->GetDatabaseProvider();
      String sServer = pIniFileSettings->GetDatabaseServer();
      String sUsername = pIniFileSettings->GetUsername();
      String sPassword = pIniFileSettings->GetPassword();
      String sDatabase = pIniFileSettings->GetDatabaseName();
      String sDatabaseDirectory = pIniFileSettings->GetDatabaseDirectory();
      String sDatabaseServerFailoverPartner = pIniFileSettings->GetDatabaseServerFailoverPartner();
      long lDBPort = pIniFileSettings->GetDatabasePort();

      HM::DatabaseSettings::SQLDBType dbType = IniFileSettings::Instance()->GetDatabaseType();

      std::shared_ptr<DatabaseSettings> pSettings = std::shared_ptr<DatabaseSettings> 
         (new DatabaseSettings(sProvider, sServer, sDatabase, sUsername, sPassword, sDatabaseDirectory, sDatabaseServerFailoverPartner, dbType, lDBPort,
            pIniFileSettings->GetMSSQLEncryption(), pIniFileSettings->GetMSSQLTrustCertificate(), pIniFileSettings->GetMSSQLCertificateHost()));

      for (int i = 0; i < iNoOfConnections; i++)
      {
         std::shared_ptr<DALConnection> pConnection = DALConnectionFactory::CreateConnection(pSettings);
         DALConnection::ConnectionResult result = pConnection->Connect(sErrorMessage);

         if (result != DALConnection::Connected)
            return result;

         available_connections_.insert(pConnection);
      }

      // Fetch first connection
      auto iter = available_connections_.begin();
      if (!(*iter)->CheckServerVersion(sErrorMessage))
         return DALConnection::FatalError;

      (*iter)->OnConnected();

      return DALConnection::Connected;
   }

   void
   DatabaseConnectionManager::Disconnect()
   {
      boost::lock_guard<boost::recursive_mutex> guard(mutex_);

      //auto iterConnection = available_connections_.begin();
      for(std::shared_ptr<DALConnection> pConnection : available_connections_)
      {
         pConnection->Disconnect();
      }
      available_connections_.clear();

      for(std::shared_ptr<DALConnection> pConnection : busy_connections_)
      {
         pConnection->Disconnect();
      }

      busy_connections_.clear();
   }
   
   bool 
   DatabaseConnectionManager::Execute(const SQLStatement &statement, __int64 *iInsertID, int iIgnoreErrors, String &sErrorMessage)
   {
      return Execute(statement.GetCommand(), iInsertID, iIgnoreErrors, sErrorMessage);
   }

   bool 
   DatabaseConnectionManager::Execute(const SQLCommand &command, __int64 *iInsertID, int iIgnoreErrors, String &sErrorMessage)
   {
      std::shared_ptr<DALConnection> pDALConn = GetConnection_();

      if (!pDALConn)
      {
         sErrorMessage = "No database connection is available.";
         return false;
      }

      bool bResult = pDALConn->Execute(command, sErrorMessage, iInsertID, iIgnoreErrors);

      ReleaseConnection_(pDALConn);

      return bResult;
   }

   std::shared_ptr<DALRecordset> 
   DatabaseConnectionManager::OpenRecordset(const SQLStatement &statement)
   {
      return OpenRecordset(statement.GetCommand());
   }

   std::shared_ptr<DALRecordset> 
   DatabaseConnectionManager::OpenRecordset(const SQLCommand &command)
   {
      std::shared_ptr<DALRecordset> pRecordset;

      std::shared_ptr<DALConnection> pDALConn = GetConnection_();

      if (!pDALConn)
      {
         return pRecordset;
      }

      pRecordset = pDALConn->CreateRecordset();

      if (!pRecordset->Open(pDALConn, command))
         pRecordset.reset();

      ReleaseConnection_(pDALConn);

      return pRecordset;


   }

   void
   DatabaseConnectionManager::ReleaseConnection_(std::shared_ptr<DALConnection> pConnection)
   {
      boost::lock_guard<boost::recursive_mutex> guard(mutex_);

      auto iterConnection = busy_connections_.find(pConnection);
      if (iterConnection == busy_connections_.end())
      {
         assert(0);
         return;
      }

      busy_connections_.erase(iterConnection);

      // Locate an available connection
      available_connections_.insert(pConnection);
   }

   void
   DatabaseConnectionManager::RetireConnection_(std::shared_ptr<DALConnection> connection) noexcept
   {
      if (!connection) return;
      std::shared_ptr<DALConnection> replacement;
      try { replacement = DALConnectionFactory::CreateConnection(connection->GetSettings()); }
      catch (...) { /* Capacity can shrink on allocation failure; never reuse uncertain state. */ }
      bool owned = false;
      try
      {
         boost::lock_guard<boost::recursive_mutex> guard(mutex_);
         owned = busy_connections_.erase(connection) == 1;
         if (owned && replacement) available_connections_.insert(replacement);
      }
      catch (...) { /* Preserve the original transaction failure during cleanup. */ }
      if (owned)
      {
         // Disconnect outside the pool mutex. Even a failed close cannot put this
         // physical client back in the reusable pool; its replacement opens later.
         try { connection->Disconnect(); } catch (...) { }
         try { LOG_APPLICATION("Retired a database connection after an unconfirmed transaction."); } catch (...) { }
      }
   }

   int 
   DatabaseConnectionManager::GetCurrentDatabaseVersion()
   {
      SQLCommand command("select * from hm_dbversion");
      std::shared_ptr<DALRecordset> pRS = OpenRecordset(command);
      if (!pRS)
         return 0;

      int iRetVal = pRS->GetLongValue("value");

      return iRetVal;
   }

   std::shared_ptr<DALConnection>
   DatabaseConnectionManager::GetConnection_()
   {
      std::shared_ptr<DALConnection> selected;
      // Loop until we find a free connection
      while (1)
      {
         // We want to sleep outside of the lock-scope. Hence the inner scope here.
         {
            boost::lock_guard<boost::recursive_mutex> guard(mutex_);

            // Locate an available connection
            auto iterConnection = available_connections_.begin();

            if (iterConnection != available_connections_.end())
            {
               // Remove the connection from free and add to busy
               selected = (*iterConnection);

               // Remove it from the list of available connections
               available_connections_.erase(iterConnection);

               busy_connections_.insert(selected);
            }
            else
            {
               if (busy_connections_.size() == 0 &&
                  available_connections_.size() == 0)
               {
                  // There's no available connections at all. Nothing to wait for.
                  std::shared_ptr<DALConnection> pEmpty;
                  return pEmpty;
               }
            }
         }

         if (selected) break;
         Sleep(10);
      }
      // Replacement clients start unopened. Open before handing one to any SQL
      // path: MySQL's query/recordset APIs require a non-null native client handle.
      // No pool mutex is held during network I/O, and legacy OnConnected upgrade
      // hooks are not rerun when replacing a client.
      String error;
      if (!TransactionLease::EnsureConnected(selected, error,
         [](DALConnection &connection, String &result) { return connection.Connect(result) == DALConnection::Connected; },
         [this](const auto &connection) noexcept { RetireConnection_(connection); })) return {};
      return selected;
   }

   std::shared_ptr<DALConnection> 
   DatabaseConnectionManager::BeginTransaction(String &sErrorMessage, TransactionLease::BeginResult *result)
   {
      if (result) *result = TransactionLease::BeginResult::Unavailable;
      std::shared_ptr<DALConnection> pDALConnection = GetConnection_();
      const auto started = TransactionLease::Begin(pDALConnection, sErrorMessage,
         [this](const auto &connection) { ReleaseConnection_(connection); },
         [this](const auto &connection) noexcept { RetireConnection_(connection); });
      if (result) *result = started;
      if (started != TransactionLease::BeginResult::Started)
      {
         // Could not start database transaction.
         std::shared_ptr<DALConnection> pEmpty;
         return pEmpty;
      }

      return pDALConnection;
   }

   bool
   DatabaseConnectionManager::CommitTransaction(std::shared_ptr<DALConnection> pConnection, String &sErrorMessage)
   {
      {
         boost::lock_guard<boost::recursive_mutex> guard(mutex_);
         if (!pConnection || busy_connections_.find(pConnection) == busy_connections_.end())
         { sErrorMessage = "The transaction connection is no longer leased."; return false; }
      }
      return TransactionLease::Finish(pConnection, sErrorMessage,
         [](DALConnection &connection, String &error) { return connection.CommitTransaction(error); },
         [this](const auto &connection) { ReleaseConnection_(connection); },
         [this](const auto &connection) noexcept { RetireConnection_(connection); });
   }

   bool
   DatabaseConnectionManager::RollbackTransaction(std::shared_ptr<DALConnection> pConnection, String &sErrorMessage)
   {
      {
         boost::lock_guard<boost::recursive_mutex> guard(mutex_);
         if (!pConnection || busy_connections_.find(pConnection) == busy_connections_.end())
         { sErrorMessage = "The transaction connection is no longer leased."; return false; }
      }
      return TransactionLease::Finish(pConnection, sErrorMessage,
         [](DALConnection &connection, String &error) { return connection.RollbackTransaction(error); },
         [this](const auto &connection) { ReleaseConnection_(connection); },
         [this](const auto &connection) noexcept { RetireConnection_(connection); });
   }

   bool
   DatabaseConnectionManager::GetIsConnected()
   {
      boost::lock_guard<boost::recursive_mutex> guard(mutex_);
      size_t iNoOfConnections = busy_connections_.size() + available_connections_.size();

      if (iNoOfConnections == 0)
         return false;

      return true;
   }

   bool
   DatabaseConnectionManager::ExecuteScript(const String &sFile, String &sErrorMessage)
   {
      std::shared_ptr<DALConnection> pConnection = GetConnection_();
      if (!pConnection)
      {
         sErrorMessage = "No database connection is available.";
         return false;
      }

      SQLScriptRunner scriptRunner;
      bool result = scriptRunner.ExecuteScript(pConnection, sFile, sErrorMessage);

      ReleaseConnection_(pConnection);

      return result;
   }

   bool
   DatabaseConnectionManager::EnsuresPrerequisites(long DBVersion, String &sErrorMessage)
   {
      PrerequisiteList prerequisites;

      std::shared_ptr<DALConnection> pConnection = GetConnection_();

      if (!pConnection)
      {
         sErrorMessage = "No database connection is available.";
         return false;
      }
      bool result = prerequisites.Ensure(pConnection, DBVersion, sErrorMessage);

      ReleaseConnection_(pConnection);

      return result;
   }
}
