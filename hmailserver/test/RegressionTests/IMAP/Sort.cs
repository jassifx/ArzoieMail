// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using NUnit.Framework;
using RegressionTests.Shared;

namespace RegressionTests.IMAP
{
   [TestFixture]
   public class Sort : TestFixtureBase
   {
      [SetUp]
      public new void SetUp()
      {
         _application.Settings.IMAPSortEnabled = true;

         base.SetUp();
      }

      [Test]
      [Description("Issue 340, Incorrect date sorting order")]
      public void TestDateSortOrder()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "imapsort@example.test", "test");
         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.LogonWithLiteral("imapsort@example.test", "test");
         Assert.That(simulator.SelectFolder("Inbox"), Is.True);

         var response =
            simulator.SendSingleCommandWithLiteral("A04 APPEND INBOX \"22-Feb-2008 22:00:00 +0200\" {37}",
               "Date: Wed, 15 Dec 2010 13:00:00 +0000");
         Assert.That(response.Contains("* 1 EXISTS"), Is.True, response);

         response = simulator.SendSingleCommandWithLiteral("A04 APPEND INBOX \"22-Feb-2008 21:00:00 +0200\" {37}",
            "Date: Wed, 15 Dec 2010 14:00:00 +0000");
         Assert.That(response.Contains("* 2 EXISTS"), Is.True, response);

         response = simulator.SendSingleCommandWithLiteral("A04 APPEND INBOX \"22-Feb-2008 20:00:00 +0200\" {37}",
            "Date: Wed, 15 Dec 2010 12:00:00 +0000");
         Assert.That(response.Contains("* 3 EXISTS"), Is.True, response);

         response = simulator.SendSingleCommandWithLiteral("A04 APPEND INBOX \"23-Feb-2008 01:30:23 +0200\" {37}",
            "Date: Wed, 15 Dec 2010 11:00:00 +0000");
         Assert.That(response.Contains("* 4 EXISTS"), Is.True, response);

         var sortDateResponse = simulator.SendSingleCommand("A10 SORT (DATE) US-ASCII ALL");

         Assert.That(sortDateResponse.Contains(" 4 3 1 2"), Is.True);
         simulator.Disconnect();
      }

      [Test]
      [Description("Issue 340, Incorrect date sorting order")]
      public void TestDateSortOrderNonexistantDate()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "imapsort@example.test", "test");
         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.LogonWithLiteral("imapsort@example.test", "test");
         Assert.That(simulator.SelectFolder("Inbox"), Is.True);

         var response = simulator.SendSingleCommandWithLiteral(
            "A04 APPEND INBOX \"22-Feb-2008 22:00:00 +0200\" {4}", "ABCD");
         Assert.That(response.Contains("* 1 EXISTS"), Is.True, response);

         response = simulator.SendSingleCommandWithLiteral("A04 APPEND INBOX \"22-Feb-2008 21:00:00 +0200\" {4}",
            "ABCD");
         Assert.That(response.Contains("* 2 EXISTS"), Is.True, response);

         response = simulator.SendSingleCommandWithLiteral("A04 APPEND INBOX \"22-Feb-2008 20:00:00 +0200\" {4}",
            "ABCD");
         Assert.That(response.Contains("* 3 EXISTS"), Is.True, response);

         response = simulator.SendSingleCommandWithLiteral("A04 APPEND INBOX \"23-Feb-2008 01:30:23 +0200\" {4}",
            "ABCD");
         Assert.That(response.Contains("* 4 EXISTS"), Is.True, response);

         /*
          * RFC 5256 "2.2. Sent Date" chapter. If the sent date cannot be determined (a Date: header is missing or cannot be parsed), 
          * the INTERNALDATE for that message is used as the sent date.
          */

         var sortDateResponse = simulator.SendSingleCommand("A10 SORT (DATE) US-ASCII ALL");
         var sortArivalDateResponse = simulator.SendSingleCommand("A10 SORT (ARRIVAL) US-ASCII ALL");

         Assert.That(sortArivalDateResponse.Contains(" 3 2 1 4"), Is.True);
         Assert.That(sortArivalDateResponse, Is.EqualTo(sortDateResponse));
         simulator.Disconnect();
      }

      [Test]
      [Description("Issue 168 - IMAP: Search for message with specific UID fails. ")]
      public void TestSearchSpecficUID()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "search@example.test", "test");

         // Send a message to this account.
         var smtpClientSimulator = new SmtpClientSimulator();
         for (var i = 0; i < 5; i++)
            smtpClientSimulator.Send("search@example.test", "search@example.test", "Test1",
               "This is a test of IMAP Search");

         ImapClientSimulator.AssertMessageCount("search@example.test", "test", "INBOX", 5);

         var messages = account.IMAPFolders.get_ItemByName("Inbox").Messages;

         var second = messages[1].UID;
         var third = messages[2].UID;
         var fourth = messages[3].UID;


         var simulator = new ImapClientSimulator();
         simulator.Connect();
         simulator.Logon("search@example.test", "test");
         Assert.That(simulator.SelectFolder("INBOX"), Is.True);

         var result =
            simulator.SendSingleCommand(string.Format("a01 SORT (REVERSE DATE) UTF-8 ALL UID {0},{1}", second, third));
         AssertSortResultContains(result, 2, 3);

         result = simulator.SendSingleCommand(string.Format("a01 SORT (DATE) UTF-8 ALL UID {0},{1}", third, second));
         AssertSortResultContains(result, 2, 3);

         result = simulator.SendSingleCommand(string.Format("a01 SORT (DATE) UTF-8 ALL UID {0}:{1}", second, fourth));
         AssertSortResultContains(result, 2, 3, 4);

         result = simulator.SendSingleCommand(string.Format("a01 SORT (DATE) UTF-8 ALL UID {0}:*", second));
         AssertSortResultContains(result, 2, 3, 4, 5);
      }

      private void AssertSortResultContains(string sortResponse, params int[] expected)
      {
         var response = ParseSortResult(sortResponse);

         Assert.That(response.Count, Is.EqualTo(expected.Length), sortResponse);

         foreach (var expectedItem in expected)
            Assert.That(response.Contains(expectedItem), Is.True, sortResponse);
      }

      private List<int> ParseSortResult(string resultText)
      {
         // Parses a string such as * SORT 2 3 4 5
         var messageListPart = resultText.Substring("* SORT ".Length);
         var end = messageListPart.IndexOf("\r\n", StringComparison.CurrentCultureIgnoreCase);
         messageListPart = messageListPart.Substring(0, end);

         var messages = messageListPart.Split(' ');

         var result = new List<int>();

         foreach (var message in messages)
            result.Add(int.Parse(message));

         return result;
      }


      [Test]
      public void TestSortDeletedOrAnswered()
      {
         var domain = _application.Domains[0];
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "search@example.test", "test");

         // Send a message to this account.
         var smtpClientSimulator = new SmtpClientSimulator();
         smtpClientSimulator.Send("search@example.test", "search@example.test", "aa", "This is a test of IMAP Search");
         ImapClientSimulator.AssertMessageCount("search@example.test", "test", "INBOX", 1);
         smtpClientSimulator.Send("search@example.test", "search@example.test", "bb", "This is a test of IMAP Search");
         ImapClientSimulator.AssertMessageCount("search@example.test", "test", "INBOX", 2);

         var simulator = new ImapClientSimulator();
         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("search@example.test", "test");
         Assert.That(simulator.SelectFolder("INBOX"), Is.True);

         Assert.That(simulator.Sort("(DATE) UTF-8 ALL OR ANSWERED DELETED"), Is.EqualTo(""));
      }

      [Test]
      public void TestSortReverseArrival()
      {
         var domain = _application.Domains[0];
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "search@example.test", "test");

         // Send a message to this account.
         var smtpClientSimulator = new SmtpClientSimulator();
         smtpClientSimulator.Send("search@example.test", "search@example.test", "Test1",
            "This is a test of IMAP Search");
         ImapClientSimulator.AssertMessageCount("search@example.test", "test", "INBOX", 1);

         // The two messages needs to be sent a second apart, so we actually need to pause a bit here.

         Thread.Sleep(1000);
         smtpClientSimulator.Send("search@example.test", "search@example.test", "Test2",
            "This is a test of IMAP Search");
         ImapClientSimulator.AssertMessageCount("search@example.test", "test", "INBOX", 2);

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("search@example.test", "test");
         Assert.That(simulator.SelectFolder("INBOX"), Is.True);

         Assert.That(simulator.Sort("(ARRIVAL) UTF-8 ALL"), Is.EqualTo("1 2"));
         Assert.That(simulator.Sort("(REVERSE ARRIVAL) UTF-8 ALL"), Is.EqualTo("2 1"));
      }

      [Test]
      public void TestSortReverseSize()
      {
         var domain = _application.Domains[0];
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "search@example.test", "test");

         var longBodyString = new StringBuilder();
         longBodyString.Append('A', 10000);

         // Send a message to this account.
         var smtpClientSimulator = new SmtpClientSimulator();
         smtpClientSimulator.Send("search@example.test", "search@example.test", "Test1", longBodyString.ToString());
         ImapClientSimulator.AssertMessageCount("search@example.test", "test", "INBOX", 1);

         smtpClientSimulator.Send("search@example.test", "search@example.test", "Test2", "Test body");
         ImapClientSimulator.AssertMessageCount("search@example.test", "test", "INBOX", 2);

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("search@example.test", "test");
         Assert.That(simulator.SelectFolder("INBOX"), Is.True);

         Assert.That(simulator.Sort("(SIZE) UTF-8 ALL"), Is.EqualTo("2 1"));
         Assert.That(simulator.Sort("(REVERSE SIZE) UTF-8 ALL"), Is.EqualTo("1 2"));
      }

      [Test]
      public void TestSortSubject()
      {
         var domain = _application.Domains[0];
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "search@example.test", "test");

         // Send a message to this account.
         var smtpClientSimulator = new SmtpClientSimulator();
         smtpClientSimulator.Send("search@example.test", "search@example.test", "Test1",
            "This is a test of IMAP Search");
         ImapClientSimulator.AssertMessageCount("search@example.test", "test", "INBOX", 1);
         smtpClientSimulator.Send("search@example.test", "search@example.test", "Test2",
            "This is a test of IMAP Search");
         ImapClientSimulator.AssertMessageCount("search@example.test", "test", "INBOX", 2);

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("search@example.test", "test");
         Assert.That(simulator.SelectFolder("INBOX"), Is.True);

         Assert.That(simulator.Sort("(SUBJECT) UTF-8 ALL"), Is.EqualTo("1 2"));
      }

      [Test]
      public void TestSortSubjectReverse()
      {
         var domain = _application.Domains[0];
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "search@example.test", "test");

         // Send a message to this account.
         var smtpClientSimulator = new SmtpClientSimulator();
         smtpClientSimulator.Send("search@example.test", "search@example.test", "Test1",
            "This is a test of IMAP Search");
         ImapClientSimulator.AssertMessageCount("search@example.test", "test", "INBOX", 1);
         smtpClientSimulator.Send("search@example.test", "search@example.test", "Test2",
            "This is a test of IMAP Search");
         ImapClientSimulator.AssertMessageCount("search@example.test", "test", "INBOX", 2);

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("search@example.test", "test");
         Assert.That(simulator.SelectFolder("INBOX"), Is.True);

         Assert.That(simulator.Sort("(REVERSE SUBJECT) UTF-8 ALL"), Is.EqualTo("2 1"));
      }

      [Test]
      public void TestSortSubjectSearch()
      {
         var domain = _application.Domains[0];
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "search@example.test", "test");

         // Send a message to this account.
         var smtpClientSimulator = new SmtpClientSimulator();
         smtpClientSimulator.Send("search@example.test", "search@example.test", "aa", "This is a test of IMAP Search");
         ImapClientSimulator.AssertMessageCount("search@example.test", "test", "INBOX", 1);
         smtpClientSimulator.Send("search@example.test", "search@example.test", "bb", "This is a test of IMAP Search");
         ImapClientSimulator.AssertMessageCount("search@example.test", "test", "INBOX", 2);

         var simulator = new ImapClientSimulator();
         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("search@example.test", "test");
         Assert.That(simulator.SelectFolder("INBOX"), Is.True);

         Assert.That(simulator.Sort("(DATE) UTF-8 ALL UNANSWERED OR HEADER SUBJECT aa HEADER SUBJECT bb"), Is.EqualTo("1 2"));
         Assert.That(simulator.Sort("(DATE) UTF-8 ALL UNANSWERED OR (HEADER SUBJECT aa) (HEADER SUBJECT bb)"),
            Is.EqualTo("1 2"));
         Assert.That(simulator.Sort("(DATE) UTF-8 ALL UNANSWERED (OR HEADER SUBJECT aa HEADER SUBJECT bb)"),
            Is.EqualTo("1 2"));

         Assert.That(simulator.Sort("(DATE) UTF-8 ALL UNANSWERED OR HEADER SUBJECT aa HEADER SUBJECT cc"), Is.EqualTo("1"));
         Assert.That(simulator.Sort("(DATE) UTF-8 ALL UNANSWERED OR (HEADER SUBJECT aa) (HEADER SUBJECT cc)"),
            Is.EqualTo("1"));
         Assert.That(simulator.Sort("(DATE) UTF-8 ALL UNANSWERED (OR HEADER SUBJECT aa HEADER SUBJECT cc)"), Is.EqualTo("1"));

         Assert.That(simulator.Sort("(DATE) UTF-8 ALL UNANSWERED OR HEADER SUBJECT bb HEADER SUBJECT cc"), Is.EqualTo("2"));
         Assert.That(simulator.Sort("(DATE) UTF-8 ALL UNANSWERED OR (HEADER SUBJECT bb) (HEADER SUBJECT cc)"),
            Is.EqualTo("2"));
         Assert.That(simulator.Sort("(DATE) UTF-8 ALL UNANSWERED (OR HEADER SUBJECT bb HEADER SUBJECT cc)"), Is.EqualTo("2"));
      }


      [Test]
      public void TestSubjectSearch()
      {
         var domain = _application.Domains[0];
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "search@example.test", "test");

         // Send a message to this account.
         var smtpClientSimulator = new SmtpClientSimulator();
         smtpClientSimulator.Send("search@example.test", "search@example.test", "Test1",
            "This is a test of IMAP Search");
         ImapClientSimulator.AssertMessageCount("search@example.test", "test", "INBOX", 1);
         smtpClientSimulator.Send("search@example.test", "search@example.test", "Test2",
            "This is a test of IMAP Search");
         ImapClientSimulator.AssertMessageCount("search@example.test", "test", "INBOX", 2);

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("search@example.test", "test");
         Assert.That(simulator.SelectFolder("INBOX"), Is.True);

         Assert.That(simulator.Sort("(REVERSE SUBJECT) UTF-8 ALL HEADER SUBJECT \"Test1\""), Is.EqualTo("1"));
         Assert.That(simulator.Sort("(REVERSE SUBJECT) UTF-8 ALL HEADER SUBJECT \"Test2\""), Is.EqualTo("2"));
         Assert.That(simulator.Sort("(REVERSE SUBJECT) UTF-8 ALL (HEADER SUBJECT \"Test1\")"), Is.EqualTo("1"));
         Assert.That(simulator.Sort("(REVERSE SUBJECT) UTF-8 ALL (HEADER SUBJECT \"Test2\")"), Is.EqualTo("2"));
      }


      [Test]
      public void TestSubjectSearchMultipleMatches()
      {
         var domain = _application.Domains[0];
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "search@example.test", "test");

         // Send a message to this account.
         var smtpClientSimulator = new SmtpClientSimulator();
         smtpClientSimulator.Send("search@example.test", "search@example.test", "Test1",
            "This is a test of IMAP Search");
         ImapClientSimulator.AssertMessageCount("search@example.test", "test", "INBOX", 1);
         smtpClientSimulator.Send("search@example.test", "search@example.test", "TestA",
            "This is a test of IMAP Search");
         ImapClientSimulator.AssertMessageCount("search@example.test", "test", "INBOX", 2);
         smtpClientSimulator.Send("search@example.test", "search@example.test", "Test1",
            "This is a test of IMAP Search");
         ImapClientSimulator.AssertMessageCount("search@example.test", "test", "INBOX", 3);

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("search@example.test", "test");
         Assert.That(simulator.SelectFolder("INBOX"), Is.True);

         Assert.That(simulator.Sort("(SUBJECT) UTF-8 ALL HEADER SUBJECT \"Test1\""), Is.EqualTo("1 3"));
         Assert.That(simulator.Sort("(SUBJECT) UTF-8 ALL HEADER SUBJECT \"TestA\""), Is.EqualTo("2"));
         Assert.That(simulator.Sort("(REVERSE SUBJECT) UTF-8 ALL HEADER SUBJECT \"Test1\""), Is.EqualTo("3 1"));
         Assert.That(simulator.Sort("(REVERSE SUBJECT) UTF-8 ALL HEADER SUBJECT \"TestA\""), Is.EqualTo("2"));
         Assert.That(simulator.Sort("(REVERSE SUBJECT) UTF-8 ALL (HEADER SUBJECT) \"Test1\""), Is.EqualTo("3 1"));
         Assert.That(simulator.Sort("(REVERSE SUBJECT) UTF-8 ALL (HEADER SUBJECT) \"TestA\""), Is.EqualTo("2"));
      }

      [Test]
      public void TestSubjectSearchValueWithParanthesis()
      {
         var domain = _application.Domains[0];
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "search@example.test", "test");

         // Send a message to this account.
         var smtpClientSimulator = new SmtpClientSimulator();
         smtpClientSimulator.Send("search@example.test", "search@example.test", "Te(st1",
            "This is a test of IMAP Search");
         ImapClientSimulator.AssertMessageCount("search@example.test", "test", "INBOX", 1);
         smtpClientSimulator.Send("search@example.test", "search@example.test", "Te)st2",
            "This is a test of IMAP Search");
         ImapClientSimulator.AssertMessageCount("search@example.test", "test", "INBOX", 2);

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("search@example.test", "test");
         Assert.That(simulator.SelectFolder("INBOX"), Is.True);

         Assert.That(simulator.Sort("(SUBJECT) UTF-8 ALL HEADER SUBJECT \"Te(st1\""), Is.EqualTo("1"));
         Assert.That(simulator.Sort("(SUBJECT) UTF-8 ALL HEADER SUBJECT \"Te)st2\""), Is.EqualTo("2"));
      }
   }
}