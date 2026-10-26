using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using RegressionTests.Shared;

namespace RegressionTests.API
{
   [TestFixture]
   public class SqlServer : TestFixtureBase
   {
      [Test]
      public void TestNativeSqlServerConnectionPolicy()
      {
         // Exercises provider selection, required TLS, invalid settings and quoted credentials
         // in the native connection builder, even when this fixture uses a different database.
         _application.Utilities.RunTestSuite("I know what I am doing.");
      }

      [Test]
      public void TestSqlServerSessionsRequireEncryption()
      {
         if (_application.Database.DatabaseType != hMailServer.eDBtype.hDBTypeMSSQL)
            Assert.Ignore("Requires the installed SQL Server integration environment.");
         var server = Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_SQLSERVER");
         Assert.That(server, Is.Not.Null.And.Not.Empty,
            "The SQL Server CI/VM provisioner must supply the verified TLS endpoint.");
         dynamic connection = Activator.CreateInstance(Type.GetTypeFromProgID("ADODB.Connection", true));
         object recordset = null;
         try
         {
            // Test setup uses a trusted VM-only certificate. Do not bypass its validation.
            connection.Open("Provider=MSOLEDBSQL19;Data Source=\"" + server.Replace("\"", "\"\"") +
               "\";Integrated Security=SSPI;Use Encryption for Data=Mandatory;Trust Server Certificate=False;");
            object affected;
            dynamic rows = connection.Execute(
               "SELECT COUNT(*) AS total, SUM(CASE WHEN c.encrypt_option='TRUE' THEN 1 ELSE 0 END) AS encrypted " +
               "FROM sys.dm_exec_sessions s JOIN sys.dm_exec_connections c ON s.session_id=c.session_id " +
               "WHERE s.program_name=N'ArzoieMail'", out affected, 1);
            recordset = rows;
            int total = Convert.ToInt32(rows.Fields.Item("total").Value);
            Assert.That(total, Is.GreaterThan(0), "No native ArzoieMail SQL sessions were found.");
            Assert.That(Convert.ToInt32(rows.Fields.Item("encrypted").Value), Is.EqualTo(total),
               "Every native SQL Server connection must be encrypted.");
         }
         finally
         {
            if (recordset != null) Marshal.FinalReleaseComObject(recordset);
            connection.Close();
            Marshal.FinalReleaseComObject(connection);
         }
      }
   }
}
