// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

#include "stdafx.h"
#include "ClassTester.h"
#include "../Application/BackupManager.h"
#include "../Application/TimeoutCalculator.h"
#include "../BO/MessageData.h"
#include "../BO/Message.h"
#include "../Mime/MimeTester.h"
#include "../Util/Utilities.h"
#include "../Util/MessageUtilities.h"
#include "../Util/Charset.h"
#include "../Util/RegularExpression.h"
#include "../TCPIP/LocalIPAddresses.h"
#include "../TCPIP/IPAddress.h"
#include "../TCPIP/DNSResolverWinApi.h"
#include "Time.h"
#include "Utilities.h"
#include "Parsing/AddresslistParser.h"
#include "../../IMAP/IMAPSimpleCommandParser.h"
#include "../../IMAP/IMAPCommandRangeActionTester.h"
#include "../Persistence/UIDValidityGeneratorTester.h"
#include "BlowFish.h"
#include "../Persistence/PersistentMessage.h"
#include "../../SMTP/SPF/Conformance/SPFConformanceTester.h"
#include "../../SMTP/SPF/SPFEvaluatorTester.h"
#include "../../SMTP/SPF/SPFMacroExpanderTester.h"
#include "../../SMTP/SPF/SPFRecordTester.h"
#include "../../SMTP/SRS/SRSTester.h"
#include "../AntiSpam/DMARC/DMARCTester.h"
#include "PublicSuffixListTester.h"
#include "../../SMTP/BLCheck.h"
#include "../Application/BackupManager.h"
#include "../Util/Encoding/Base64.h"
#include "../Util/Encoding/ModifiedUTF7.h"
#include "../Util/Hashing/HashCreator.h"
#include "../Util/Hashing/PasswordHasher.h"
#include "SecretProtector.h"
#include "../Persistence/SecretMigration.h"
#include "../SQL/SQLCommand.h"
#include "../SQL/SQLStatement.h"
#include "../SQL/ADOConnection.h"
#include "../SQL/TransactionLeaseTests.h"
#include "../AntiSpam/Bayes/BayesFilterTests.h"
#include "../BO/SSLCertificate.h"
#include "../BO/TCPIPPort.h"
#include "../Persistence/PersistentSSLCertificate.h"
#include "../Persistence/PersistentTCPIPPort.h"
#include "../Util/EventTester.h"
#include "../Scripting/ScriptServer.h"
#include <boost/pool/object_pool.hpp>

#ifdef _DEBUG
   #define DEBUG_NEW new(_NORMAL_BLOCK, __FILE__, __LINE__)
   #define new DEBUG_NEW
#endif

namespace HM
{
   namespace
   {
      // Writes each failure where a debugger will show it, then fails the run.
      //
      // A logic_error rather than an Assert, which throws nothing and so
      // terminates: the regression suite runs these tests through the COM API,
      // which catches an exception and reports it, and terminating there takes
      // the server down and fails everything after it instead of saying what
      // went wrong.
      void ReportFailures_(const AnsiString &what, const std::vector<AnsiString> &failures)
      {
         if (failures.empty())
            return;

         for (AnsiString failure : failures)
         {
            String message = _T("hMailServer: FAILED: ");
            message += String(failure);
            message += _T("\n");

            OutputDebugString(message);
         }

         throw std::logic_error(Formatter::FormatAsAnsi("{0} check(s) against {1} failed. See the debug output for which.",
                                                        (int) failures.size(), what));
      }
   }

   ClassTester::ClassTester()
   {

   }

   ClassTester::~ClassTester()
   {

   }

   void
   ClassTester::DoTests()
   {
      EventTester *pEventTester = new EventTester;
      pEventTester->Test();
      delete pEventTester;

      OutputDebugString(_T("hMailServer: Testing mime parser\n"));
	   MimeTester *pMimeTester = new MimeTester;
      pMimeTester->Test();
	   delete pMimeTester;

      OutputDebugString(_T("hMailServer: Testing StringParser\n"));
      StringParserTester *pParser = new StringParserTester();
      pParser->Test();
      delete pParser;
  
      OutputDebugString(_T("hMailServer: Testing FileUtilities\n"));
      FileUtilitiesTester fileUtilitiesTester;
      fileUtilitiesTester.Test();

      OutputDebugString(_T("hMailServer: Testing IPAddress\n"));
      IPAddressTester ipAddressTester;
      ipAddressTester.Test();

      OutputDebugString(_T("hMailServer: Testing DNSResolverWinApi\n"));
      DNSResolverWinApiTester dnsResolverWinApiTester;
      dnsResolverWinApiTester.Test();

      OutputDebugString(_T("hMailServer: Testing MessageUtilities\n"));
      MessageUtilitiesTester messageUtilitiesTester;
      messageUtilitiesTester.Test();

      OutputDebugString(_T("hMailServer: Testing Formatter\n"));
      FormatterTester formatterTester;
      formatterTester.Test();

      OutputDebugString(_T("hMailServer: Testing TimeoutCalculator\n"));
      TimeoutCalculatorTester timeoutCalculatortester;
      timeoutCalculatortester.Test();

      OutputDebugString(_T("hMailServer: Test DateTime\n"));
      DateTimeTests tests;
      tests.Test();

      OutputDebugString(_T("hMailServer: Testing Utilities\n"));
      UtilitiesTester *pTestUtilities = new UtilitiesTester();
      pTestUtilities->Test();
      delete pTestUtilities;

      OutputDebugString(_T("hMailServer: Test BLChecktester\n"));
      BLCheckTester blchecktester;
      blchecktester.Test();

      // The SPF record grammar, then the macro expansion of RFC 7208 section 7,
      // and then the RFC 7208 conformance suite. Every failure is reported before
      // the run is failed, because a suite which stops at the first of 203 cases
      // says much less than one which lists them all.
      OutputDebugString(_T("hMailServer: Testing the SPF record grammar\n"));
      SPFRecordTester spfRecordTester;
      ReportFailures_("the SPF record grammar", spfRecordTester.Run());

      OutputDebugString(_T("hMailServer: Testing SPF macro expansion\n"));
      SPFMacroExpanderTester spfMacroExpanderTester;
      ReportFailures_("SPF macro expansion", spfMacroExpanderTester.Run());

      OutputDebugString(_T("hMailServer: Testing SPF evaluation\n"));
      SPFEvaluatorTester spfEvaluatorTester;
      ReportFailures_("SPF evaluation", spfEvaluatorTester.Run());

      OutputDebugString(_T("hMailServer: Testing SPF conformance\n"));
      SPFConformance::SPFConformanceTester spfConformanceTester;
      ReportFailures_("the RFC 7208 conformance suite", spfConformanceTester.Run());

      OutputDebugString(_T("hMailServer: Testing SRS\n"));
      SRSTester srsTester;
      srsTester.Test();

      OutputDebugString(_T("hMailServer: Testing public suffix list\n"));
      PublicSuffixListTester publicSuffixListTester;
      publicSuffixListTester.Test();

      OutputDebugString(_T("hMailServer: Testing DMARC\n"));
      DMARCTester dmarcTester;
      dmarcTester.Test();

      OutputDebugString(_T("hMailServer: Testing SHA256\n"));
      HashCreatorTester tester;
      tester.Test();

      OutputDebugString(_T("hMailServer: Testing PasswordHasher\n"));
      SecretProtectorTester secretProtectorTester;
      secretProtectorTester.Test();

      SecretMigrationTester secretMigrationTester;
      secretMigrationTester.Test();

      PasswordHasherTester passwordHasherTester;
      passwordHasherTester.Test();

      OutputDebugString(_T("hMailServer: Testing SQLStatement\n"));
      SQLStatementTester sqlStatementTester;
      sqlStatementTester.Test();

      ADOConnectionTester adoConnectionTester;
      adoConnectionTester.Test();

      std::vector<AnsiString> transactionFailures;
      for (const auto &failure : TransactionLeaseTests::Run()) transactionFailures.emplace_back(failure.c_str());
      ReportFailures_("database transaction leases", transactionFailures);

      std::vector<AnsiString> bayesFailures;
      for (const auto &failure : BayesFilterTests::Run()) bayesFailures.emplace_back(failure.c_str());
      ReportFailures_("Bayesian filter", bayesFailures);

      {
         // Read the real provider's BIGINT values without altering identity seeds,
         // certificate registrations or active listeners. A truncated reference can
         // otherwise select an unrelated certificate with the same low 32 bits.
         constexpr __int64 certificateId = 4294967297LL;
         constexpr __int64 listenerId = 4294967298LL;
         String identityQuery =
            "SELECT CAST(4294967297 AS BIGINT) AS sslcertificateid, "
            "'Native identity fixture' AS sslcertificatename, "
            "'unused-certificate.pem' AS sslcertificatefile, "
            "'unused-key.pem' AS sslprivatekeyfile, "
            "CAST(4294967298 AS BIGINT) AS portid, 1 AS portprotocol, "
            "45264 AS portnumber, 0 AS portconnectionsecurity, "
            "CAST(0 AS BIGINT) AS portaddress1, CAST(NULL AS BIGINT) AS portaddress2, "
            "CAST(4294967297 AS BIGINT) AS portsslcertificateid FROM hm_dbversion";
         // MySQL's integer CAST target is SIGNED; the other providers use BIGINT.
         if (IniFileSettings::Instance()->GetDatabaseType() == DatabaseSettings::TypeMYSQLServer)
            identityQuery.Replace(_T("AS BIGINT"), _T("AS SIGNED"));
         const auto row = Application::Instance()->GetDBManager()->OpenRecordset(SQLCommand(identityQuery));
         if (!row || row->IsEOF())
            throw std::logic_error("Could not read native certificate identity fixture.");
         const auto certificate = std::make_shared<SSLCertificate>();
         const auto listener = std::make_shared<TCPIPPort>();
         std::vector<AnsiString> failures;
         if (!PersistentSSLCertificate::ReadObject(certificate, row) || certificate->GetID() != certificateId)
            failures.emplace_back("Certificate persistence narrowed a BIGINT identity.");
         if (!PersistentTCPIPPort::ReadObject(listener, row) || listener->GetID() != listenerId)
            failures.emplace_back("Listener persistence narrowed a BIGINT identity.");
         if (listener->GetSSLCertificateID() != certificateId)
            failures.emplace_back("Listener persistence narrowed a BIGINT certificate reference.");
         const __int64 identities[] = {0, 2147483647LL, 2147483648LL, certificateId, 9223372036854775807LL};
         for (const auto identity : identities)
         {
            listener->SetSSLCertificateID(identity);
            if (listener->GetSSLCertificateID() != identity)
               failures.emplace_back("Listener setter narrowed a certificate reference.");
         }
         for (const auto &failure : failures)
            LOG_APPLICATION("Native transport identity regression: " + String(failure));
         ReportFailures_("native transport identities", failures);
      }

      {
         DatabaseConnectionManager unavailable;
         String error;
         // A failed connection acquisition must not approve an upgrade or pass
         // a null physical client to script parsing. No live database is altered.
         if (unavailable.EnsuresPrerequisites(REQUIRED_DB_VERSION, error) || error.IsEmpty())
         {
            LOG_APPLICATION("Native regression: disconnected database prerequisites incorrectly succeeded.");
            throw std::logic_error("Disconnected database prerequisite checks must fail with an error.");
         }
         error.Empty();
         if (unavailable.ExecuteScript(_T("unused-disconnected-database-script.sql"), error) || error.IsEmpty())
            throw std::logic_error("Disconnected database scripts must fail before parsing.");
      }

      OutputDebugString(_T("hMailServer: Testing RegularExpressionTester\n"));
      RegularExpressionTester *pRegExTest = new RegularExpressionTester();
      pRegExTest->Test();
      delete pRegExTest;

      OutputDebugString(_T("hMailServer: Testing Base64\n"));
      Base64Tester base64Tester;
      base64Tester.Test();

      OutputDebugString(_T("hMailServer: Testing ModifiedUTF7Tester\n"));
      ModifiedUTF7Tester modifiedUTF7Tester;
      modifiedUTF7Tester.Test();

      OutputDebugString(_T("hMailServer: Testing AddresslistParser\n"));
      AddresslistParserTester *pTest2 = new AddresslistParserTester();
      pTest2->Test();
      delete pTest2;

      OutputDebugString(_T("hMailServer: Testing charset\n"));
      CharsetTester *pCharsetTester = new CharsetTester;
      pCharsetTester->Test();
      delete pCharsetTester;

      OutputDebugString(_T("hMailServer: Testing LocalIPAddresses\n"));
      LocalIPAddressesTester *pTest4 = new LocalIPAddressesTester();
      pTest4->Test();
      delete pTest4;

      OutputDebugString(_T("hMailServer: Testing MessageData\n"));
      MessageDataTester *pMsgData = new MessageDataTester();
      pMsgData->Test();
      delete pMsgData;

      OutputDebugString(_T("hMailServer: Testing Time\n"));
      TimeTester *pTimeT = new TimeTester();
      pTimeT->Test();
      delete pTimeT;

      OutputDebugString(_T("hMailServer: Testing BlowFishEncryptorTester\n"));
      BlowFishEncryptorTester *pTest3 = new BlowFishEncryptorTester();
      pTest3->Test();
      delete pTest3;

      OutputDebugString(_T("hMailServer: Testing IMAPSimpleCommandParserTester\n"));
      IMAPSimpleCommandParserTester *pTest = new IMAPSimpleCommandParserTester();
      pTest->Test();
      delete pTest;

      OutputDebugString(_T("hMailServer: Testing IMAP message sets\n"));
      IMAPCommandRangeActionTester rangeActionTester;
      ReportFailures_("IMAP message sets", rangeActionTester.Run());

      OutputDebugString(_T("hMailServer: Testing UIDVALIDITY counters\n"));
      UIDValidityGeneratorTester uidValidityTester;
      ReportFailures_("UIDVALIDITY counters", uidValidityTester.Run());

      OutputDebugString(_T("hMailServer: Testing script string literals\n"));
      ScriptServerTester scriptServerTester;
      ReportFailures_("script string literals", scriptServerTester.Run());
   }

   void 
   ClassTester::LoadSettings_()
   {
      String sAppPath = Utilities::GetBinDirectory();
      if (sAppPath.Right(1) != _T("\\"))
         sAppPath += _T("\\");

      String sConfigFile = sAppPath + "test_config.xml";
      String sTestSpec = FileUtilities::ReadCompleteTextFile(sConfigFile);

      if (sTestSpec.IsEmpty())
         return;

      XDoc oDoc;
      oDoc.Load(sTestSpec);

      XNode *pBackupNode = oDoc.GetChild(_T("Config"));

      if (!pBackupNode)
         throw;

      mime_data_path_ = pBackupNode->GetChildValue(_T("MimeDataPath"));
   }

   void 
   ClassTester::TestBackup_()
   {
      std::shared_ptr<BackupManager> pBackupManager = Application::Instance()->GetBackupManager();
      std::shared_ptr<Backup> pBackup = pBackupManager->LoadBackup("C:\\Temp\\Backup\\HMBackup 2006-12-10 091555.zip");
      pBackup->SetRestoreOptions(1 | 2 | 4 | 8 | 16 | 32);
      pBackupManager->StartRestore(pBackup);

      Sleep(1000000);
   }
}
