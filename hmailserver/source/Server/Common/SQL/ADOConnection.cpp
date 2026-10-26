// Copyright (c) 2005 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

#include "stdafx.h"

#include "ADOConnection.h"
#include "ADORecordset.h"
#include "SQLCommand.h"
#include "DatabaseSettings.h"
#include "Macros/MSSQLMacroExpander.h"
#include "ADOInt64Helper.h"
#include <stdexcept>

using namespace std;

#ifdef _DEBUG
#define DEBUG_NEW new(_NORMAL_BLOCK, __FILE__, __LINE__)
#define new DEBUG_NEW
#endif

namespace HM
{
   ADOConnection::ADOConnection(std::shared_ptr<DatabaseSettings> pSettings) :
      DALConnection(pSettings)
   {
      HRESULT hr =cADOConnection.CreateInstance(__uuidof(Connection));

      if (FAILED(hr))
         throw std::runtime_error("Cannot initialize the Windows ADO database component.");
      cADOConnection->PutCursorLocation(adUseClient);

      bConnected = false;
   }


   ADOConnection::~ADOConnection()
   {

      try
      {
         cADOConnection = NULL;
      }
      catch(...)
      {

      }


   }


   bool
   ADOConnection::Disconnect()
   {

      try
      {

         if (!bConnected)
            return false; // --- already disconnected.

         if (cADOConnection->State == 1)
            cADOConnection->Close();
      }
      catch ( ... )
      {
         assert(0);
      }

      bConnected = false;

      return true;
   }


   DALConnection::ConnectionResult
   ADOConnection::Connect(String &sErrorMessage)
   {
      if (bConnected)
         return Connected;

      String connectionString;
      try
      {
         connectionString = BuildConnectionString_(database_settings_);
      }
      catch (const std::invalid_argument &error)
      {
         sErrorMessage = String(error.what());
         return FatalError;
      }

      CLSID providerId;
      if (FAILED(CLSIDFromProgIDEx(L"MSOLEDBSQL19", &providerId)))
      {
         sErrorMessage = "SQL Server requires the x64 Microsoft OLE DB Driver 19 for SQL Server. Install it before running DBSetup or starting the service. Legacy providers are not used.";
         return FatalError;
      }

      try
      {
         // Credentials are already quoted in the connection string. Passing them again to
         // Open would introduce a second source of authentication properties.
         cADOConnection->Open(_bstr_t(connectionString), _bstr_t(), _bstr_t(), adConnectUnspecified);
         cADOConnection->PutCursorLocation(adUseClient);
      }
      catch (_com_error &error)
      {
         sErrorMessage = "SQL Server connection failed (OLE DB 19). Check the server/instance, authentication, encryption and trusted certificate configuration. ";
         sErrorMessage += String((const wchar_t *)_bstr_t(error.Description()));
         // Never log the connection string, which contains the database credential.
         return TemporaryFailure;
      }

      bConnected = true;

      // Change the format of dates. We always insert dates using the format yyyy-mm-dd.
      // We need to inform MSSQL about this.
      String sSetDateFormat = "SET DATEFORMAT ymd";
      String sErrorMsg;

      SQLCommand command1(sSetDateFormat);
      TryExecute(command1, sErrorMsg, 0, 0);

      // Set transaction isolation level to read uncommitted. This may give phantom
      // reads but that isn't a problem since we've implemented our own locking
      // mechanism.
      String sIsolationLevel = "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED";

      sErrorMsg = "";

      SQLCommand command2(sIsolationLevel);
      TryExecute(command2, sErrorMsg, 0, 0);

      return Connected;
   }

   _ConnectionPtr
   ADOConnection::GetConnection() const
   {
      return cADOConnection;
   }

   DALConnection::ExecutionResult
   ADOConnection::TryExecute(const SQLCommand &command, String &sErrorMessage, __int64 *iInsertID, int iIgnoreErrors)
   {
      if (!bConnected)
         return DALConnectionProblem; // --- already disconnected.

      String queryString = command.GetQueryString();

      try
      {
         // Execute the SQL statement.
         _CommandPtr adoCommand;
         adoCommand.CreateInstance(__uuidof(Command));
         InitializeCommandParameters(adoCommand, command, queryString);

         adoCommand->CommandType = adCmdText;
         adoCommand->CommandText = _bstr_t(queryString);
         adoCommand->ActiveConnection = cADOConnection;
         adoCommand->Execute(NULL, NULL, adCmdText);

         // Check what unique ID we got back (if we're interested). The query string is a
         // _bstr_t, so it is freed even when Open throws.
         if (iInsertID != nullptr)
         {
            _RecordsetPtr pIdentityRS;
            pIdentityRS.CreateInstance(__uuidof(Recordset));
            pIdentityRS->PutCursorLocation(adUseClient);
            pIdentityRS->PutRefActiveConnection(cADOConnection);

            _bstr_t identitySQL(_T("SELECT @@IDENTITY AS IDENT"));
            pIdentityRS->Open(identitySQL, vtMissing, adOpenKeyset, adLockOptimistic, adCmdText);
            pIdentityRS->PutRefActiveConnection(NULL);

            *iInsertID = GetIdentityFromRS_(pIdentityRS);
         }
      }
      catch ( _com_error &err )
      {
         ExecutionResult dbErr = GetErrorType(_com_error::WCodeToHRESULT(err.WCode()));
         if (iIgnoreErrors > 0 && iIgnoreErrors & dbErr)
            return DALConnection::DALSuccess;

         if (queryString.Find(_T("[IGNORE-ERRORS]")) >= 0)
            return DALConnection::DALSuccess;

         _bstr_t bstrSource( err.Source() );
         _bstr_t bstrDescription( err.Description() );

         LPCSTR lpcSource = bstrSource;
         String sErrSource = lpcSource;

         LPCSTR lpcDesc = bstrDescription;
         String sErrDesc = lpcDesc;

         sErrorMessage = "Source: ADOConnection::Execute(), Code: HM10044, Description: Error while executing SQL statement: \n";
         sErrorMessage += queryString;
         sErrorMessage += "\n";
         sErrorMessage += sErrSource;
         sErrorMessage += "\n";
         sErrorMessage += sErrDesc;

         return dbErr;
      }
      catch ( ... )
      {

         sErrorMessage = "Source: ADOConnection::Execute(), Code: HM10045, Description: Unknown error when executing SQL statement: \n";
         sErrorMessage.append(queryString);

         return DALUnknown;
      }

      return DALSuccess;
   }

   bool
   ADOConnection::IsConnected() const
   {
      return bConnected;
   };

   __int64
   ADOConnection::GetIdentityFromRS_(_RecordsetPtr pRS) const
   {
      try
      {
         if (pRS->GetRecordCount() == 0)
            return 0;

         _variant_t    vaField;
         FieldsPtr      pFields;
         FieldPtr       pField;
         HRESULT			hr;

         hr = pRS->get_Fields( &pFields );
         vaField = "IDENT";
         pFields->get_Item( vaField, &pField );

         vaField.Clear();
         if (pField)
         {
            pField->get_Value(&vaField);

            switch (vaField.vt)
            {
            case VT_NULL:
               return 0;
            case VT_I4:
               return vaField.intVal;
            default:
               if (vaField.decVal.sign == DECIMAL_NEG)
                  return -vaField.cyVal.int64;
               else
                  return vaField.cyVal.int64;
            }
         }

         return 0;
      }
      catch (...)
      {
         ErrorManager::Instance()->ReportError(ErrorManager::High, 5030, "ADOConnection::GetIdentityFromRS_", "Error while determening @@IDENTITY");
      }

      return 0;
   }

   void
   ADOConnection::OnConnected()
   {
      // Nothing to do here. Only used my MySQL.
   }

   DALConnection::ExecutionResult
   ADOConnection::GetErrorType(int iErrorCode)
   {
      switch (iErrorCode)
      {
      case 0:
         return DALSuccess;
      case 0x80040E2FL: // DB_E_INTEGRITYVIOLATION:
         return DALErrorInSQL;
      case 0x80040200L:
         return DALConnectionProblem;
      default:
         return DALUnknown;
      }
   }

   bool
   ADOConnection::BeginTransaction(String &sErrorMessage)
   {
      return TryExecute(SQLCommand("BEGIN TRANSACTION"), sErrorMessage, 0, 0) == DALSuccess;
   }

   bool
   ADOConnection::CommitTransaction(String &sErrorMessage)
   {
      return TryExecute(SQLCommand("COMMIT TRANSACTION"), sErrorMessage, 0, 0) == DALSuccess;
   }

   bool
   ADOConnection::RollbackTransaction(String &sErrorMessage)
   {
      return TryExecute(SQLCommand("ROLLBACK TRANSACTION"), sErrorMessage, 0, 0) == DALSuccess;
   }

   void
   ADOConnection::SetTimeout(int seconds)
   {
      cADOConnection->PutCommandTimeout(seconds);
   }

   bool
   ADOConnection::CheckServerVersion(String &errorMessage)
   {
      std::shared_ptr<ADOConnection> connection = shared_from_this();

      ADORecordset recordset;
      if (recordset.TryOpen(connection, SQLCommand("SELECT SERVERPROPERTY('productversion') as ProductVersion"), errorMessage) != DALConnection::DALSuccess)
         return false;

      String version = recordset.GetStringValue("ProductVersion");

      std::vector<String> versionVector = StringParser::SplitString(version, ".");
      if (versionVector.empty())
      {
         errorMessage = "SQL Server did not return a valid product version.";
         return false;
      }
      int majorVersion = _ttoi(versionVector[0]);

      if (majorVersion < RequiredVersion)
      {
         errorMessage = "ArzoieMail requires Microsoft SQL Server 2019 or newer (including Express).";
         return false;
      }

      return true;
   }

   std::shared_ptr<DALRecordset>
   ADOConnection::CreateRecordset()
   {
      std::shared_ptr<ADORecordset> recordset = std::shared_ptr<ADORecordset>(new ADORecordset());
      return recordset;
   }

   std::shared_ptr<IMacroExpander>
   ADOConnection::CreateMacroExpander()
   {
      std::shared_ptr<MSSQLMacroExpander> expander = std::shared_ptr<MSSQLMacroExpander>(new MSSQLMacroExpander());
      return expander;
   }

   void
   ADOConnection::InitializeCommandParameters(_CommandPtr &adoCommand, const SQLCommand &sqlCommand, String &queryString) const
   {
      for(const SQLParameter &parameter : sqlCommand.GetParameters())
      {
         String parameterName = parameter.GetName();

         if (parameter.GetType() == SQLParameter::ParamTypeInt32)
         {
            VARIANT integerType;
            integerType.vt = VT_I4;
            integerType.intVal = parameter.GetInt32Value();

            adoCommand->Parameters->Append(adoCommand->CreateParameter(_bstr_t(),adInteger,adParamInput, 4, integerType));
         }
         else if (parameter.GetType() == SQLParameter::ParamTypeInt64)
         {
                  ADO64Helper::AddInt64Parameter(adoCommand, parameterName, parameter.GetInt64Value());
         }
         else if (parameter.GetType() == SQLParameter::ParamTypeUnsignedInt32)
         {
            VARIANT integerType;
            integerType.vt = VT_UI4;
            integerType.intVal = parameter.GetUnsignedInt32Value();

            adoCommand->Parameters->Append(adoCommand->CreateParameter(_bstr_t(),adInteger,adParamInput, 4, integerType));
         }
         else if (parameter.GetType() == SQLParameter::ParamTypeString)
         {
            const String &value = parameter.GetStringValue();

            VARIANT stringType;
            stringType.vt = VT_BSTR;
            stringType.bstrVal  = _bstr_t(value);

            int length = 0;

            if (value.GetLength() < 4000)
               length = 4000;
            else
               length = value.GetLength();

            if (!value.IsEmpty())
               adoCommand->Parameters->Append(adoCommand->CreateParameter(_bstr_t(), adVarWChar, adParamInput, length, stringType));
            else
               adoCommand->Parameters->Append(adoCommand->CreateParameter(_bstr_t(), adVarWChar, adParamInput, 1, stringType));
         }

         queryString.Replace(parameterName, _T("?"));
      }
   }

   namespace
   {
      String QuoteConnectionValue(const String &value)
      {
         if (value.find(L'\0') != String::npos)
            throw std::invalid_argument("SQL Server connection settings cannot contain a null character.");
         String escaped = value;
         escaped.Replace(_T("\""), _T("\"\""));
         return _T("\"") + escaped + _T("\"");
      }
   }

   String
   ADOConnection::BuildConnectionString_(std::shared_ptr<DatabaseSettings> settings)
   {
      String provider = settings->GetProvider();
      if (!provider.IsEmpty() && provider.CompareNoCase(_T("MSOLEDBSQL19")) != 0)
         throw std::invalid_argument("SQL Server Provider must be blank or MSOLEDBSQL19. Remove the legacy Provider override and install Microsoft OLE DB Driver 19.");

      String encryption = settings->GetMSSQLEncryption();
      if (encryption.CompareNoCase(_T("Mandatory")) == 0)
         encryption = "Mandatory";
      else if (encryption.CompareNoCase(_T("Strict")) == 0)
         encryption = "Strict";
      else
         throw std::invalid_argument("MSSQLEncryption must be Mandatory or Strict. Unencrypted SQL Server connections are not supported.");

      String trust = settings->GetMSSQLTrustCertificate();
      if (trust != _T("0") && trust != _T("1"))
         throw std::invalid_argument("MSSQLTrustServerCertificate must be 0 or 1.");
      if (encryption == _T("Strict") && trust == _T("1"))
         throw std::invalid_argument("Strict SQL Server encryption requires certificate validation; MSSQLTrustServerCertificate must be 0.");

      String server = settings->GetServer();
      if (server.IsEmpty())
         throw std::invalid_argument("Specify the SQL Server host or named instance.");
      if (server.Left(3).CompareNoCase(_T("np:")) == 0 || server.Left(4).CompareNoCase(_T("lpc:")) == 0)
         throw std::invalid_argument("SQL Server connections require TCP with TLS; named pipes and shared memory are not supported.");
      if (server.Left(4).CompareNoCase(_T("tcp:")) != 0)
         server = _T("tcp:") + server;
      int port = settings->GetPort();
      if (port < 0 || port > 65535)
         throw std::invalid_argument("SQL Server port must be 0 (instance/default) or between 1 and 65535.");
      if (port > 0)
      {
         if (server.Find(_T(",")) >= 0)
            throw std::invalid_argument("Specify the SQL Server port either in Server or in Port, not both.");
         server += _T(",") + StringParser::IntToString(port);
      }

      String connection = _T("Provider=MSOLEDBSQL19;Application Name=ArzoieMail;DataTypeCompatibility=80;");
      connection += _T("Use Encryption for Data=") + encryption + _T(";Trust Server Certificate=") + (trust == _T("1") ? _T("True;") : _T("False;"));
      connection += _T("Connect Timeout=15;Persist Security Info=False;");
      connection += _T("Data Source=") + QuoteConnectionValue(server) + _T(";");
      connection += _T("Initial Catalog=") + QuoteConnectionValue(settings->GetDatabaseName()) + _T(";");
      if (settings->GetUsername().IsEmpty())
         connection += _T("Integrated Security=SSPI;");
      else
      {
         connection += _T("User ID=") + QuoteConnectionValue(settings->GetUsername()) + _T(";");
         connection += _T("Password=") + QuoteConnectionValue(settings->GetPassword()) + _T(";");
      }
      if (!settings->GetDatabaseServerFailoverPartner().IsEmpty())
         connection += _T("Failover Partner=") + QuoteConnectionValue(settings->GetDatabaseServerFailoverPartner()) + _T(";");
      if (!settings->GetMSSQLCertificateHost().IsEmpty())
         connection += _T("Host Name In Certificate=") + QuoteConnectionValue(settings->GetMSSQLCertificateHost()) + _T(";");
      return connection;
   }

   void
   ADOConnectionTester::Test()
   {
      auto settings = [](const String &provider, const String &encryption = _T("Mandatory"),
                         const String &trust = _T("0"), int port = 0)
      {
         return std::make_shared<DatabaseSettings>(provider, _T("localhost"), _T("mail"), _T(""), _T(""),
            _T(""), _T(""), DatabaseSettings::TypeMSSQLServer, port, encryption, trust);
      };
      auto require = [](bool condition)
      {
         if (!condition)
            throw std::logic_error("SQL Server connection security regression failed.");
      };
      String connection = ADOConnection::BuildConnectionString_(settings(""));
      require(connection.Find(_T("Provider=MSOLEDBSQL19;")) == 0);
      require(connection.Find(_T("Use Encryption for Data=Mandatory;Trust Server Certificate=False;")) >= 0);
      require(connection.Find(_T("DataTypeCompatibility=80;")) >= 0);
      require(connection.Find(_T("Integrated Security=SSPI;")) >= 0);
      require(connection.Find(_T("Data Source=\"tcp:localhost\";")) >= 0);
      require(ADOConnection::BuildConnectionString_(settings("msoledbsql19", "strict")).Find(_T("Use Encryption for Data=Strict;")) >= 0);
      require(ADOConnection::BuildConnectionString_(settings("", "Mandatory", "1")).Find(_T("Trust Server Certificate=True;")) >= 0);
      require(ADOConnection::BuildConnectionString_(settings("", "Mandatory", "0", 14330)).Find(_T("Data Source=\"tcp:localhost,14330\";")) >= 0);
      for (auto invalid : {settings("SQLOLEDB"), settings("SQLNCLI"), settings("MSOLEDBSQL"),
         settings("", "Optional"), settings("", "Mandatory", "true"), settings("", "Strict", "1"),
         settings("", "Mandatory", "0", -1), settings("", "Mandatory", "0", 65536)})
      {
         bool refused = false;
         try { ADOConnection::BuildConnectionString_(invalid); }
         catch (const std::invalid_argument &) { refused = true; }
         require(refused);
      }
      auto special = std::make_shared<DatabaseSettings>(_T(""), _T("localhost"), _T("mail;db"),
         _T("user;id"), _T("pass\";Use Encryption for Data=Optional;"), _T(""), _T("mirror;node"),
         DatabaseSettings::TypeMSSQLServer, 0, _T("Mandatory"), _T("0"), _T("sql.example.test"));
      connection = ADOConnection::BuildConnectionString_(special);
      require(connection.Find(_T("Initial Catalog=\"mail;db\";")) >= 0);
      require(connection.Find(_T("Password=\"pass\"\";Use Encryption for Data=Optional;\";")) >= 0);
      require(connection.Find(_T("Failover Partner=\"mirror;node\";")) >= 0);
      require(connection.Find(_T("Host Name In Certificate=\"sql.example.test\";")) >= 0);
      for (const String &endpoint : {String(_T("np:localhost")), String(_T("lpc:localhost")), String(_T(""))})
      {
         auto invalid = std::make_shared<DatabaseSettings>(_T(""), endpoint, _T("mail"), _T(""), _T(""),
            _T(""), _T(""), DatabaseSettings::TypeMSSQLServer, 0);
         bool refused = false;
         try { ADOConnection::BuildConnectionString_(invalid); }
         catch (const std::invalid_argument &) { refused = true; }
         require(refused);
      }
   }
}
