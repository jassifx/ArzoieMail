// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

using NUnit.Framework;
using RegressionTests.Infrastructure;
using RegressionTests.Shared;

namespace RegressionTests.API
{
   // Values go into the SQL the server runs: bound as parameters on SQL Server Compact,
   // PostgreSQL and MySQL through MariaDB Connector/C, written into the SQL as escaped literals
   // on MySQL through libmysql. Either way they must come back exactly as they went in, and
   // never become part of the SQL. CI runs these on all three databases.
   [TestFixture]
   public class SqlValues : TestFixtureBase
   {
      // The characters that end, escape or mark something in SQL - quotes, backslashes,
      // comment and statement markers, parameter names and placeholders of every client, LIKE
      // wildcards - and some that are not ASCII. Short enough for the 60 characters of the
      // first name column.
      private const string Awkward = "O'Brien \\ \\' \" ; -- @VALUE @@ID ? $1 % _ ü ✓ 漢";

      [Test]
      [Description("Text values are stored and read back as they were given.")]
      public void TestTextValuesAreStoredAsGiven()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "values@example.test", "test");
         account.PersonFirstName = Awkward;
         account.VacationSubject = Awkward;
         account.VacationMessage = Awkward + "\r\n" + Awkward;
         account.Save();

         // Through a new collection, which reads the accounts from the database.
         var stored = _application.Domains.ItemByName[_domain.Name].Accounts.ItemByAddress["values@example.test"];

         Assert.That(stored.PersonFirstName, Is.EqualTo(Awkward));
         Assert.That(stored.VacationSubject, Is.EqualTo(Awkward));
         Assert.That(stored.VacationMessage, Is.EqualTo(Awkward + "\r\n" + Awkward));
      }

      [Test]
      [Description("An account whose address contains a quote can log on, and a quote in a user name never widens the lookup.")]
      public void TestLogonLooksUpTheAddressAsGiven()
      {
         SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "o'brien@example.test", "secret");

         var pop3 = new Pop3ClientSimulator();
         Assert.That(pop3.ConnectAndLogon("o'brien@example.test", "secret"), Is.True);
         pop3.Disconnect();

         // Were the address written into the SQL unescaped, this would match every account.
         var injected = new Pop3ClientSimulator();
         Assert.That(injected.ConnectAndLogon("x' OR '1'='1", "secret"), Is.False);
         injected.Disconnect();
      }
   }
}
