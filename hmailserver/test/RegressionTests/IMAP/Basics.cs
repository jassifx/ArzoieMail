// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

using System;
using System.Text;
using hMailServer;
using NUnit.Framework;
using RegressionTests.Infrastructure;
using RegressionTests.Shared;

namespace RegressionTests.IMAP
{
   [TestFixture]
   public class Basics : TestFixtureBase
   {
      [Test]
      public void TestAppendBadLiteral()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "check@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.LogonWithLiteral("check@example.test", "test");
         simulator.SendSingleCommandWithLiteral("A01 APPEND INBOX {TEST}", "ABCD");
         Assert.That(simulator.GetMessageCount("INBOX"), Is.EqualTo(0));
         simulator.Disconnect();
      }

      [Test]
      public void TestAppendDeletedMessage()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "check@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("check@example.test", "test");
         simulator.SendSingleCommandWithLiteral("A01 APPEND INBOX (\\Deleted) {4}", "ABCD");
         Assert.That(simulator.GetMessageCount("INBOX"), Is.EqualTo(1));

         Assert.That(simulator.Search("DELETED"), Is.EqualTo("1"));

         simulator.Disconnect();
      }

      [Test]
      [Description("Test that one can use APPEND and specify the folder name separately using {}-notation")]
      public void TestAppendFolderNameInOctet()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "check@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("check@example.test", "test");
         simulator.SelectFolder("INBOX");
         simulator.CreateFolder("MONK");
         simulator.SendRaw("A01 APPEND {4}\r\n");
         var result = simulator.Receive();
         Assert.That(result.StartsWith("+ Ready for additional command text."), Is.True);

         simulator.SendRaw("MONK (\\Seen) \"20-Jan-2009 12:59:50 +0100\" {5}\r\n");
         result = simulator.Receive();
         Assert.That(result.StartsWith("+ Ready for literal data"), Is.True);

         simulator.SendRaw("WOOOT\r\n");
         result = simulator.Receive();

         Assert.That(result, Does.Match(@"^A01 OK \[APPENDUID \d+ \d+\] APPEND completed\r\n$"));
      }

      [Test]
      [Description(
         "Test that one can use APPEND and specify the folder name separately using {}-notation. Do not include flag list (it's optional)"
      )]
      public void TestAppendFolderNameInOctetNoFlagList()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "check@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("check@example.test", "test");
         simulator.SelectFolder("INBOX");
         simulator.CreateFolder("MONK");
         simulator.SendRaw("A01 APPEND {4}\r\n");
         var result = simulator.Receive();
         Assert.That(result.StartsWith("+ Ready for additional command text."), Is.True);

         simulator.SendRaw("MONK  \"12-Jan-2009 12:12:12 +0100\" {5}\r\n");
         result = simulator.Receive();
         Assert.That(result.StartsWith("+ Ready for literal data"), Is.True);

         simulator.SendRaw("WOOOT\r\n");
         result = simulator.Receive();

         Assert.That(result, Does.Match(@"^A01 OK \[APPENDUID \d+ \d+\] APPEND completed\r\n$"));

         var date = Convert.ToDateTime(account.IMAPFolders.get_ItemByName("MONK").Messages[0].InternalDate);

         Assert.That(date.Year, Is.EqualTo(2009));
         Assert.That(date.Day, Is.EqualTo(12));
         Assert.That(date.Month, Is.EqualTo(1));
      }

      [Test]
      [Description(
         "Test that one can use APPEND and specify the folder name separately using {}-notation. Do not include flag list or timestamp (it's optional)"
      )]
      public void TestAppendFolderNameInOctetNoFlagListOrDate()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "check@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("check@example.test", "test");
         simulator.SelectFolder("INBOX");
         simulator.CreateFolder("MONK");
         simulator.SendRaw("A01 APPEND {4}\r\n");
         var result = simulator.Receive();
         Assert.That(result.StartsWith("+ Ready for additional command text."), Is.True);

         simulator.SendRaw("MONK {5}\r\n");
         result = simulator.Receive();
         Assert.That(result.StartsWith("+ Ready for literal data"), Is.True);

         simulator.SendRaw("WOOOT\r\n");
         result = simulator.Receive();

         Assert.That(result, Does.Match(@"^A01 OK \[APPENDUID \d+ \d+\] APPEND completed\r\n$"));
      }

      [Test]
      [Description(
         "Test that one can use APPEND and specify the folder name separately using {}-notation. Do not include timestamp but set deleted flag."
      )]
      public void TestAppendFolderNameInOctetSetDeletedFlag()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "check@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("check@example.test", "test");
         simulator.SelectFolder("INBOX");
         simulator.CreateFolder("MONK");
         simulator.SendRaw("A01 APPEND {4}\r\n");
         var result = simulator.Receive();
         Assert.That(result.StartsWith("+ Ready for additional command text."), Is.True);

         simulator.SendRaw("MONK (\\Seen \\Deleted) {5}\r\n");
         result = simulator.Receive();
         Assert.That(result.StartsWith("+ Ready for literal data"), Is.True);

         simulator.SendRaw("WOOOT\r\n");
         result = simulator.Receive();

         Assert.That(result, Does.Match(@"^A01 OK \[APPENDUID \d+ \d+\] APPEND completed\r\n$"));
      }

      [Test]
      [Description("Issue 247: IMAP: Untagged EXISTS not sent after APPEND completion.")]
      public void TestAppendResponseContainsExists()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "check@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.LogonWithLiteral("check@example.test", "test");
         Assert.That(simulator.SelectFolder("Inbox"), Is.True);
         var response1 = simulator.SendSingleCommandWithLiteral("A01 APPEND INBOX {4}", "ABCD");
         Assert.That(response1.Contains("* 1 EXISTS"), Is.True, response1);
         Assert.That(response1.Contains("* 1 RECENT"), Is.True, response1);
         Assert.That(simulator.GetMessageCount("INBOX"), Is.EqualTo(1));
         simulator.Disconnect();
      }

      [Test]
      public void TestAuthenticate()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "literal@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         var result = simulator.SendSingleCommand("A01 AUTHENTICATE");
         Assert.That(result.Contains("NO IMAP AUTHENTICATE is not enabled."), Is.True);
         simulator.Disconnect();
      }

      [Test]
      public void TestBeforeLogon()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "delete@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();

         Assert.That(simulator.ExamineFolder("NonexistantFolder").Contains("NO Authenticate first"), Is.True);
         Assert.That(simulator.SelectFolder("NonexistantFolder"), Is.False);
         Assert.That(simulator.Copy(1, "SomeFolder"), Is.False);
         Assert.That(simulator.CheckFolder("SomeFolder"), Is.False);
         Assert.That(simulator.Fetch("123 a").Contains("NO Authenticate first"), Is.True);
         Assert.That(simulator.List().Contains("NO Authenticate first"), Is.True);
         Assert.That(simulator.LSUB().Contains("NO Authenticate first"), Is.True);
         Assert.That(simulator.GetMyRights("APA").Contains("NO Authenticate first"), Is.True);
         Assert.That(simulator.RenameFolder("A", "B"), Is.False);
         Assert.That(simulator.Status("SomeFolder", "MESSAGES").Contains("A01 OK"), Is.False);
      }

      [Test]
      [Description("Issue 218, IMAP: Problem with file name containing non-latin chars. In this test they are encoded"
      )]
      public void TestBodyStructureWithNonLatinCharacterInAttachmentHeader()
      {
         var messageText =
            "From: \"Test\" <test@example.test>" + "\r\n" +
            "To: \"Test\" <test@example.test>" + "\r\n" +
            "Subject: test" + "\r\n" +
            "MIME-Version: 1.0" + "\r\n" +
            "Content-Type: multipart/mixed;" + "\r\n" +
            "   boundary=\"----=_NextPart_000_000C_01C9EEB2.08D2EC80\"" + "\r\n" +
            "X-Priority: 3" + "\r\n" +
            "" + "\r\n" +
            "This is a multi-part message in MIME format." + "\r\n" +
            "" + "\r\n" +
            "------=_NextPart_000_000C_01C9EEB2.08D2EC80" + "\r\n" +
            "Content-Type: text/plain;" + "\r\n" +
            "  format=flowed;" + "\r\n" +
            "	charset=\"iso-8859-1\";" + "\r\n" +
            "	reply-type=original" + "\r\n" +
            "Content-Transfer-Encoding: 7bit" + "\r\n" +
            "" + "\r\n" +
            "" + "\r\n" +
            "------=_NextPart_000_000C_01C9EEB2.08D2EC80" + "\r\n" +
            "Content-Type: application/octet-stream;" + "\r\n" +
            "	name=\"=?iso-8859-1?B?beT2LnppcA==?=\"" + "\r\n" +
            "Content-Transfer-Encoding: base64" + "\r\n" +
            "Content-Disposition: attachment;" + "\r\n" +
            "	filename=\"=?iso-8859-1?B?beT2LnppcA==?=\"" + "\r\n" +
            "" + "\r\n" +
            "iVBORw0KGgoAAAANSUhEUgAAAqgAAAH4CAIAAAAJvIhhAAAAAXNSR0IArs4c6QAAAARnQU1BAACx" + "\r\n" +
            "uIDgj5MrSIAAAQIEzgkI/nP2KhMgQIAAgbiA4I+TK0iAAAECBM4JCP5z9ioTIECAAIG4wP8ChvJS" + "\r\n" +
            "wXUaKVoAAAAASUVORK5CYII=" + "\r\n" +
            "" + "\r\n" +
            "------=_NextPart_000_000C_01C9EEB2.08D2EC80--" + "\r\n";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         SmtpClientSimulator.StaticSendRaw(account.Address, account.Address, messageText);

         CustomAsserts.AssertFolderMessageCount(account.IMAPFolders[0], 1);

         var simulator = new ImapClientSimulator();
         simulator.ConnectAndLogon(account.Address, "test");
         simulator.SelectFolder("INBOX");
         var result = simulator.Fetch("1 BODYSTRUCTURE");
         simulator.Disconnect();

         Assert.That(result.Contains("(\"NAME\" \"=?iso-8859-1?B?beT2LnppcA==?=\")"), Is.True);
         Assert.That(result.Contains("(\"FILENAME\" \"=?iso-8859-1?B?beT2LnppcA==?=\")"), Is.True);

         var fileName = account.IMAPFolders.get_ItemByName("INBOX").Messages[0].Attachments[0].Filename;
         Assert.That(fileName, Is.EqualTo("mäö.zip"));
      }

      [Test]
      [Description("Issue 218, IMAP: Problem with file name containing non-latin chars (RFC 2184 compliance)")]
      public void TestBodyStructureWithNonLatinCharacterMultiLine()
      {
         var messageText =
            "To: test@example.test\r\n" +
            "Content-Type: multipart/mixed;\r\n" +
            " boundary=\"------------000008080307000003010005\"\r\n" +
            "\r\n" +
            "This is a multi-part message in MIME format.\r\n" +
            "--------------000008080307000003010005\r\n" +
            "Content-Type: text/plain; charset=ISO-8859-1; format=flowed\r\n" +
            "Content-Transfer-Encoding: 7bit\r\n" +
            "\r\n" +
            "Test\r\n" +
            "\r\n" +
            "--------------000008080307000003010005\r\n" +
            "Content-Type: image/png;\r\n" +
            "Content-Transfer-Encoding: base64\r\n" +
            "Content-Disposition: inline;\r\n" +
            " filename*0*=ISO-8859-1''%F6%50%C4%C9%CD%C1%D6%60%F6%F6%E4%27%20%31%20%F6;\r\n" +
            " filename*1*=\"%50%C4%C9%CD%C1%D6%60%F6%F6%E4%27%20%32%20%F6%50%C4%C9%CD%C1\";\r\n" +
            " filename*2*=%D6%60%F6%F6%E4%27%20%33%2E%70%6E%67;\r\n" +
            "\r\n" +
            "iVBORw0KGgoAAAANSUhEUgAAAqgAAAH4CAIAAAAJvIhhAAAAAXNSR0IArs4c6QAAAARnQU1B\r\n" +
            "AACxjwv8YQUAAAAgY0hSTQAAeiYAAICEAAD6AAAAgOgAAHUwAADqYAAAOpgAABdwnLpRPAAA\r\n" +
            "--------------000008080307000003010005--\r\n";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         SmtpClientSimulator.StaticSendRaw(account.Address, account.Address, messageText);

         CustomAsserts.AssertFolderMessageCount(account.IMAPFolders[0], 1);

         var simulator = new ImapClientSimulator();
         simulator.ConnectAndLogon(account.Address, "test");
         simulator.SelectFolder("INBOX");
         var result = simulator.Fetch("1 BODYSTRUCTURE");
         simulator.Disconnect();

         Assert.That(
            result.Contains(
               "\"FILENAME\" \"=?ISO-8859-1?Q?=F6=50=C4=C9=CD=C1=D6=60=F6=F6=E4=27=20=31=20=F6=50=C4=C9=CD=C1=D6=60=F6=F6=E4=27=20=32=20=F6=50=C4=C9=CD=C1=D6=60=F6=F6=E4=27=20=33=2E=70=6E=67?=\""),
            Is.True);
         Assert.That(
            result.Contains(
               "\"NAME\" \"=?ISO-8859-1?Q?=F6=50=C4=C9=CD=C1=D6=60=F6=F6=E4=27=20=31=20=F6=50=C4=C9=CD=C1=D6=60=F6=F6=E4=27=20=32=20=F6=50=C4=C9=CD=C1=D6=60=F6=F6=E4=27=20=33=2E=70=6E=67?=\""),
            Is.True);
      }

      [Test]
      [Description(
         "Issue 218, IMAP: Problem with file name containing non-latin chars (RFC 2184 compliance). In this test, there's only one line of 2184-encoded data."
      )]
      public void TestBodyStructureWithNonLatinCharacterSingleLine()
      {
         var messageText =
            "Return-Path: martin@hmailserver.com\r\n" +
            "Delivered-To: martin@hmailserver.com\r\n" +
            "Received: from www.hmailserver.com ([127.0.0.1])\r\n" +
            "	by mail.hmailserver.com\r\n" +
            "	; Tue, 16 Jun 2009 21:39:18 +0200\r\n" +
            "MIME-Version: 1.0\r\n" +
            "Date: Tue, 16 Jun 2009 21:39:18 +0200\r\n" +
            "From: <martin@hmailserver.com>\r\n" +
            "To: <martin@hmailserver.com>\r\n" +
            "Subject: sdafsda\r\n" +
            "Message-ID: <96aee740f2abe8450648c1752a9a987b@localhost>\r\n" +
            "X-Sender: martin@hmailserver.com\r\n" +
            "User-Agent: RoundCube Webmail/0.2.2\r\n" +
            "Content-Type: multipart/mixed;\r\n" +
            "	boundary=\"=_b63968892a76b1a5be17f4d37b085f54\"\r\n" +
            "\r\n" +
            "--=_b63968892a76b1a5be17f4d37b085f54\r\n" +
            "Content-Transfer-Encoding: 8bit\r\n" +
            "Content-Type: text/plain; charset=\"UTF-8\"\r\n" +
            "\r\n" +
            "--=_b63968892a76b1a5be17f4d37b085f54\r\n" +
            "Content-Transfer-Encoding: base64\r\n" +
            "Content-Type: application/x-zip; charset=\"UTF-8\";\r\n" +
            " name*=\"UTF-8''m%C3%A4%C3%B6.zip\"; \r\n" +
            "Content-Disposition: attachment;\r\n" +
            " filename*=\"UTF-8''m%C3%A4%C3%B6.zip\"; \r\n" +
            "\r\n" +
            "iVBORw0KGgoAAAANSUhEUgAAAqgAAAH4CAIAAAAJvIhhAAAAAXNSR0IArs4c6QAAAARnQU1BAACx\r\n" +
            "jwv8YQUAAAAgY0hSTQAAeiYAAICEAAD6AAAAgOgAAHUwAADqYAAAOpgAABdwnLpRPAAAIr1JREFU\r\n" +
            "eF7t29GSYzluBND1/3/0ejb6wY6emK2WrpQEkaefdQvAAcn02OH/+fe///0v/wgQIECAAIESgb+C\r\n" +
            "3z8CBAgQIECgROBfJXMakwABAgQIEPjP/5qfAgECBAgQINAjIPh7dm1SAgQIECDgv/idAQIECBAg\r\n" +
            "--=_b63968892a76b1a5be17f4d37b085f54--\r\n" +
            "";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         SmtpClientSimulator.StaticSendRaw(account.Address, account.Address, messageText);

         CustomAsserts.AssertFolderMessageCount(account.IMAPFolders[0], 1);


         var simulator = new ImapClientSimulator();
         simulator.ConnectAndLogon(account.Address, "test");
         simulator.SelectFolder("INBOX");
         var result = simulator.Fetch("1 BODYSTRUCTURE");
         simulator.Disconnect();

         Assert.That(result.Contains("''"), Is.False, result);
         Assert.That(result.Contains("\"FILENAME\" \"=?UTF-8?Q?m=C3=A4=C3=B6.zip?=\""), Is.True, result);
         Assert.That(result.Contains("\"NAME\" \"=?UTF-8?Q?m=C3=A4=C3=B6.zip?=\""), Is.True, result);
      }

      [Test]
      [Description("Issue 218, IMAP: Problem with file name containing non-latin chars. In this test they are encoded"
      )]
      public void TestBodyStructureWithNonLatinCharacterSingleLineEncoded()
      {
         var messageText =
            "Message-ID: <1d11306c5648497247447e1073c3b0e2.squirrel@www.*******.**>\r\n" +
            "Date: Fri, 29 May 2009 11:53:03 +0200\r\n" +
            "Subject: attachment's name test\r\n" +
            "From: martin@hmailserver.com\r\n" +
            "To: martin@hmailserver.com\r\n" +
            "User-Agent: SquirrelMail/1.4.19\r\n" +
            "MIME-Version: 1.0\r\n" +
            "Content-Type: multipart/mixed;boundary=\"----=_20090529115303_60479\"\r\n" +
            "X-Priority: 3 (Normal)\r\n" +
            "Importance: Normal\r\n" +
            "\r\n" +
            "------=_20090529115303_60479\r\n" +
            "Content-Type: text/plain; charset=\"iso-8859-2\"\r\n" +
            "Content-Transfer-Encoding: 8bit\r\n" +
            "\r\n" +
            "test.±æê³ñó¶¼¿.txt\r\n" +
            "------=_20090529115303_60479\r\n" +
            "Content-Type: text/plain; name=\r\n" +
            "    =?iso-8859-2?Q?test.=B1=E6=EA=B3=F1=F3=B6=BC=BF.txt?=\r\n" +
            "Content-Transfer-Encoding: 8bit\r\n" +
            "Content-Disposition: attachment; filename=\"\r\n" +
            "    =?iso-8859-2?Q?test.=B1=E6=EA=B3=F1=F3=B6=BC=BF.txt?=\"\r\n" +
            "\r\n" +
            "1234\r\n" +
            "------=_20090529115303_60479--\r\n";


         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         SmtpClientSimulator.StaticSendRaw(account.Address, account.Address, messageText);

         CustomAsserts.AssertFolderMessageCount(account.IMAPFolders[0], 1);

         var simulator = new ImapClientSimulator();
         simulator.ConnectAndLogon(account.Address, "test");
         simulator.SelectFolder("INBOX");
         var result = simulator.Fetch("1 BODYSTRUCTURE");
         simulator.Disconnect();

         Assert.That(result.Contains("(\"NAME\" \"=?iso-8859-2?Q?test.=B1=E6=EA=B3=F1=F3=B6=BC=BF.txt?=\")"), Is.True);
         Assert.That(result.Contains("(\"FILENAME\" \"=?iso-8859-2?Q?test.=B1=E6=EA=B3=F1=F3=B6=BC=BF.txt?=\")"), Is.True);
      }

      [Test]
      [Description(
         "Issue 218, IMAP: Problem with file name containing non-latin chars (RFC 2184 compliance). In this test, there are spaces in the 2184-encoded data."
      )]
      public void TestBodyStructureWithNonLatinCharacterSingleLineWithSpace()
      {
         var messageText =
            "Return-Path: martin@hmailserver.com\r\n" +
            "Delivered-To: martin@hmailserver.com\r\n" +
            "Received: from www.hmailserver.com ([127.0.0.1])\r\n" +
            "	by mail.hmailserver.com\r\n" +
            "	; Tue, 16 Jun 2009 21:39:18 +0200\r\n" +
            "MIME-Version: 1.0\r\n" +
            "Date: Tue, 16 Jun 2009 21:39:18 +0200\r\n" +
            "From: <martin@hmailserver.com>\r\n" +
            "To: <martin@hmailserver.com>\r\n" +
            "Subject: sdafsda\r\n" +
            "Message-ID: <96aee740f2abe8450648c1752a9a987b@localhost>\r\n" +
            "X-Sender: martin@hmailserver.com\r\n" +
            "User-Agent: RoundCube Webmail/0.2.2\r\n" +
            "Content-Type: multipart/mixed;\r\n" +
            "	boundary=\"=_b63968892a76b1a5be17f4d37b085f54\"\r\n" +
            "\r\n" +
            "--=_b63968892a76b1a5be17f4d37b085f54\r\n" +
            "Content-Transfer-Encoding: 8bit\r\n" +
            "Content-Type: text/plain; charset=\"UTF-8\"\r\n" +
            "\r\n" +
            "--=_b63968892a76b1a5be17f4d37b085f54\r\n" +
            "Content-Transfer-Encoding: base64\r\n" +
            "Content-Type: application/x-zip; charset=\"UTF-8\";\r\n" +
            " name*=\"UTF-8''m%C3%A4%C3%B6 m%C3%A4%C3%B6.zip\"; \r\n" +
            "Content-Disposition: attachment;\r\n" +
            " filename*=\"UTF-8''m%C3%A4%C3%B6 m%C3%A4%C3%B6.zip\"; \r\n" +
            "\r\n" +
            "iVBORw0KGgoAAAANSUhEUgAAAqgAAAH4CAIAAAAJvIhhAAAAAXNSR0IArs4c6QAAAARnQU1BAACx\r\n" +
            "jwv8YQUAAAAgY0hSTQAAeiYAAICEAAD6AAAAgOgAAHUwAADqYAAAOpgAABdwnLpRPAAAIr1JREFU\r\n" +
            "eF7t29GSYzluBND1/3/0ejb6wY6emK2WrpQEkaefdQvAAcn02OH/+fe///0v/wgQIECAAIESgb+C\r\n" +
            "3z8CBAgQIECgROBfJXMakwABAgQIEPjP/5qfAgECBAgQINAjIPh7dm1SAgQIECDgv/idAQIECBAg\r\n" +
            "--=_b63968892a76b1a5be17f4d37b085f54--\r\n" +
            "";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         SmtpClientSimulator.StaticSendRaw(account.Address, account.Address, messageText);

         CustomAsserts.AssertFolderMessageCount(account.IMAPFolders[0], 1);


         var simulator = new ImapClientSimulator();
         simulator.ConnectAndLogon(account.Address, "test");
         simulator.SelectFolder("INBOX");
         var result = simulator.Fetch("1 BODYSTRUCTURE");
         simulator.Disconnect();

         Assert.That(result.Contains("''"), Is.False, result);
         Assert.That(result.Contains("\"FILENAME\" \"=?UTF-8?Q?m=C3=A4=C3=B6 m=C3=A4=C3=B6.zip?=\""), Is.True, result);
         Assert.That(result.Contains("\"NAME\" \"=?UTF-8?Q?m=C3=A4=C3=B6 m=C3=A4=C3=B6.zip?=\""), Is.True, result);
      }

      [Test]
      [Description("Issue 218, IMAP: Problem with file name containing non-latin chars (RFC 2184 compliance)")]
      public void TestBodyStructureWithNonLatinCharacterTest2()
      {
         var messageText =
            "To: test@example.test\r\n" +
            "Content-Type: multipart/mixed;\r\n" +
            " boundary=\"------------000008080307000003010005\"\r\n" +
            "\r\n" +
            "This is a multi-part message in MIME format.\r\n" +
            "--------------000008080307000003010005\r\n" +
            "Content-Type: text/plain; charset=ISO-8859-1; format=flowed\r\n" +
            "Content-Transfer-Encoding: 7bit\r\n" +
            "\r\n" +
            "Test\r\n" +
            "\r\n" +
            "--------------000008080307000003010005\r\n" +
            "Content-Type: image/png;\r\n" +
            " name=\"=?ISO-8859-1?Q?=E9=2Epng?=\"\r\n" +
            "Content-Transfer-Encoding: base64\r\n" +
            "Content-Disposition: inline;\r\n" +
            " filename*=ISO-8859-1''%E9%2E%70%6E%67\r\n" +
            "\r\n" +
            "iVBORw0KGgoAAAANSUhEUgAAAqgAAAH4CAIAAAAJvIhhAAAAAXNSR0IArs4c6QAAAARnQU1B\r\n" +
            "AACxjwv8YQUAAAAgY0hSTQAAeiYAAICEAAD6AAAAgOgAAHUwAADqYAAAOpgAABdwnLpRPAAA\r\n" +
            "--------------000008080307000003010005--\r\n";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         SmtpClientSimulator.StaticSendRaw(account.Address, account.Address, messageText);

         CustomAsserts.AssertFolderMessageCount(account.IMAPFolders[0], 1);

         var simulator = new ImapClientSimulator();
         simulator.ConnectAndLogon(account.Address, "test");
         simulator.SelectFolder("INBOX");
         var result = simulator.Fetch("1 BODYSTRUCTURE");
         simulator.Disconnect();

         Assert.That(result.Contains("\"FILENAME\" \"=?ISO-8859-1?Q?=E9=2E=70=6E=67?=\""), Is.True);
         Assert.That(result.Contains("\"NAME\" \"=?ISO-8859-1?Q?=E9=2E=70=6E=67?=\""), Is.True);
      }

      [Test]
      [Description("Issue 218, IMAP: Problem with file name containing non-latin chars (RFC 2184 compliance)")]
      public void TestBodyStructureWithNonLatinCharacterTest3()
      {
         var messageText =
            "To: test@example.test\r\n" +
            "Content-Type: multipart/mixed;\r\n" +
            " boundary=\"------------000008080307000003010005\"\r\n" +
            "\r\n" +
            "This is a multi-part message in MIME format.\r\n" +
            "--------------000008080307000003010005\r\n" +
            "Content-Type: text/plain; charset=ISO-8859-1; format=flowed\r\n" +
            "Content-Transfer-Encoding: 7bit\r\n" +
            "\r\n" +
            "Test\r\n" +
            "\r\n" +
            "--------------000008080307000003010005\r\n" +
            "Content-Type: image/png;\r\n" +
            " name=\"=?ISO-8859-1?Q?=F6=50=C4=C9=CD=C1=D6=60=F6=F6=E4=27=2E=70=6E=67?=\"\r\n" +
            "Content-Transfer-Encoding: base64\r\n" +
            "Content-Disposition: inline;\r\n" +
            " filename*=ISO-8859-1''%F6%50%C4%C9%CD%C1%D6%60%F6%F6%E4%27%2E%70%6E%67\r\n" +
            "\r\n" +
            "iVBORw0KGgoAAAANSUhEUgAAAqgAAAH4CAIAAAAJvIhhAAAAAXNSR0IArs4c6QAAAARnQU1B\r\n" +
            "AACxjwv8YQUAAAAgY0hSTQAAeiYAAICEAAD6AAAAgOgAAHUwAADqYAAAOpgAABdwnLpRPAAA\r\n" +
            "--------------000008080307000003010005--\r\n";

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         SmtpClientSimulator.StaticSendRaw(account.Address, account.Address, messageText);

         CustomAsserts.AssertFolderMessageCount(account.IMAPFolders[0], 1);

         var simulator = new ImapClientSimulator();
         simulator.ConnectAndLogon(account.Address, "test");
         simulator.SelectFolder("INBOX");
         var result = simulator.Fetch("1 BODYSTRUCTURE");
         simulator.Disconnect();

         Assert.That(
            result.Contains("\"FILENAME\" \"=?ISO-8859-1?Q?=F6=50=C4=C9=CD=C1=D6=60=F6=F6=E4=27=2E=70=6E=67?=\""),
            Is.True);
         Assert.That(
            result.Contains("\"NAME\" \"=?ISO-8859-1?Q?=F6=50=C4=C9=CD=C1=D6=60=F6=F6=E4=27=2E=70=6E=67?=\""),
            Is.True);
      }

      [Test]
      public void TestCapability()
      {
         var settings = _settings;

         settings.IMAPIdleEnabled = true;
         settings.IMAPQuotaEnabled = true;
         settings.IMAPSortEnabled = true;

         var simulator = new ImapClientSimulator();
         simulator.Connect();

         var sCapabilities = simulator.GetCapabilities();

         Assert.That(sCapabilities.Contains(" UIDPLUS"), Is.True, sCapabilities);

         if (sCapabilities.IndexOf(" IDLE") == -1 ||
             sCapabilities.IndexOf(" QUOTA") == -1 ||
             sCapabilities.IndexOf(" SORT") == -1)
            throw new Exception("ERROR - Wrong IMAP CAPABILITY.");

         settings.IMAPIdleEnabled = false;
         settings.IMAPQuotaEnabled = true;
         settings.IMAPSortEnabled = true;
         sCapabilities = simulator.GetCapabilities();

         if (sCapabilities.IndexOf(" IDLE") != -1 ||
             sCapabilities.IndexOf(" QUOTA") == -1 ||
             sCapabilities.IndexOf(" SORT") == -1)
            throw new Exception("ERROR - Wrong IMAP CAPABILITY.");

         settings.IMAPIdleEnabled = false;
         settings.IMAPQuotaEnabled = false;
         settings.IMAPSortEnabled = true;
         sCapabilities = simulator.GetCapabilities();

         if (sCapabilities.IndexOf(" IDLE") != -1 ||
             sCapabilities.IndexOf(" QUOTA") != -1 ||
             sCapabilities.IndexOf(" SORT") == -1)
            throw new Exception("ERROR - Wrong IMAP CAPABILITY.");

         settings.IMAPIdleEnabled = false;
         settings.IMAPQuotaEnabled = false;
         settings.IMAPSortEnabled = false;
         sCapabilities = simulator.GetCapabilities();

         if (sCapabilities.IndexOf(" IDLE") != -1 ||
             sCapabilities.IndexOf(" QUOTA") != -1 ||
             sCapabilities.IndexOf(" SORT") != -1)
            throw new Exception("ERROR - Wrong IMAP CAPABILITY.");

         settings.IMAPIdleEnabled = true;
         settings.IMAPQuotaEnabled = false;
         settings.IMAPSortEnabled = false;
         sCapabilities = simulator.GetCapabilities();

         if (sCapabilities.IndexOf(" IDLE") == -1 ||
             sCapabilities.IndexOf(" QUOTA") != -1 ||
             sCapabilities.IndexOf(" SORT") != -1)
            throw new Exception("ERROR - Wrong IMAP CAPABILITY.");

         settings.IMAPIdleEnabled = true;
         settings.IMAPQuotaEnabled = true;
         settings.IMAPSortEnabled = false;
         sCapabilities = simulator.GetCapabilities();

         if (sCapabilities.IndexOf(" IDLE") == -1 ||
             sCapabilities.IndexOf(" QUOTA") == -1 ||
             sCapabilities.IndexOf(" SORT") != -1)
            throw new Exception("ERROR - Wrong IMAP CAPABILITY.");

         settings.IMAPACLEnabled = true;

         sCapabilities = simulator.GetCapabilities();
         Assert.That(sCapabilities.Contains(" ACL"), Is.True);

         settings.IMAPACLEnabled = false;

         sCapabilities = simulator.GetCapabilities();
         Assert.That(sCapabilities.Contains(" ACL"), Is.False);
      }

      [Test]
      public void TestCheck()
      {
         SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "check@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("check@example.test", "test");
         Assert.That(simulator.CreateFolder("TestFolder"), Is.True);
         Assert.That(simulator.CheckFolder("TestFolder"), Is.True);
         simulator.Disconnect();
      }

      [Test]
      public void TestClose()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "close@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("close@example.test", "test");
         Assert.That(simulator.Close(), Is.False);
         Assert.That(simulator.CreateFolder("TestFolder.Sub1"), Is.True);
         Assert.That(simulator.SelectFolder("TestFolder.Sub1"), Is.True);
         Assert.That(simulator.Close(), Is.True);
         simulator.Disconnect();
      }

      [Test]
      [Description(
         "Make sure that the connection object is released after folder is selected, idle mode is switched on and connection is closed."
      )]
      public void TestConnectionObjectRelease()
      {
         LogHandler.DeleteCurrentDefaultLog();

         _settings.IMAPIdleEnabled = true;

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "idleaccount@example.test", "test");

         var simulator = new ImapClientSimulator();

         string data;

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon(account.Address, "test");
         Assert.That(simulator.SelectFolder("INBOX"), Is.True);
         Assert.That(simulator.StartIdle(), Is.True);
         Assert.That(simulator.EndIdle(true, out data), Is.True);
         Assert.That(simulator.Logout(), Is.True);

         var logData = LogHandler.ReadCurrentDefaultLog();

         Assert.That(LogHandler.DefaultLogContains("Ending session"), Is.True);
      }

      [Test]
      public void TestDelete()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "delete@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("delete@example.test", "test");
         Assert.That(simulator.DeleteFolder("DoesNotExist"), Is.False);
         Assert.That(simulator.CreateFolder("DoesExist"), Is.True);
         Assert.That(simulator.SelectFolder("DoesExist"), Is.True);
         simulator.Close();
         Assert.That(simulator.DeleteFolder("DoesExist"), Is.True);
         Assert.That(simulator.SelectFolder("DoesExist"), Is.False);
      }

      [Test]
      [Description("Test that deleting an IMAP folder does not stop notifications from working. (5.0 Build 315 Bug)")]
      public void TestDeleteIMAPFolderNotifications()
      {
         _settings.IMAPIdleEnabled = true;

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "idleaccount@example.test", "test");

         var imapClientSimulator = new ImapClientSimulator();
         var simulator2 = new ImapClientSimulator();
         imapClientSimulator.ConnectAndLogon(account.Address, "test");
         simulator2.ConnectAndLogon(account.Address, "test");

         imapClientSimulator.SelectFolder("Inbox");
         simulator2.CreateFolder("Mailbox");
         simulator2.DeleteFolder("Mailbox");

         SmtpClientSimulator.StaticSend("test@example.test", account.Address, "Test", "test");

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         var noopResponse = imapClientSimulator.NOOP() + imapClientSimulator.NOOP();

         // confirm that the client is notified about this message even though another
         // folder has been dropped by another client.
         Assert.That(noopResponse.Contains(@"* 1 EXISTS"), Is.True, noopResponse);
      }


      [Test]
      public void TestExamineNonexistantFolder()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "delete@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("delete@example.test", "test");
         var result = simulator.ExamineFolder("NonexistantFolder");

         Assert.That(result.Contains("BAD Folder could not be found."), Is.True);
      }

      [Test]
      [Description("Issue 294, IMAP: Incomplete SELECT response")]
      public void TestExamineResponse()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "testselect@example.test", "test");

         SmtpClientSimulator.StaticSend(account.Address, account.Address, "Test", "Test");
         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         var simulator = new ImapClientSimulator();
         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("testselect@example.test", "test");
         var result = simulator.ExamineFolder("Inbox");
         simulator.Disconnect();

         Assert.That(result.Contains("* FLAGS"), Is.True, result);
         Assert.That(result.Contains("* 1 EXISTS"), Is.True, result);
         Assert.That(result.Contains("* 1 RECENT"), Is.True, result);
         Assert.That(result.Contains("* OK [UNSEEN 1]"), Is.True, result);
         Assert.That(result.Contains("* OK [PERMANENTFLAGS"), Is.True, result);
         Assert.That(result.Contains("* OK [UIDNEXT 2]"), Is.True, result);
         Assert.That(result.Contains("* OK [UIDVALIDITY"), Is.True, result);
         Assert.That(result.Contains("OK [READ-ONLY]"), Is.True, result);
      }

      [Test]
      [Description("Assert that the EXPUNGE command works")]
      public void TestExpunge()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "ExpungeAccount@example.test",
            "test");

         for (var i = 0; i < 3; i++)
            SmtpClientSimulator.StaticSend("test@example.test", account.Address, "Test", "test");

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 3);

         var simulator = new ImapClientSimulator();
         Assert.That(simulator.ConnectAndLogon(account.Address, "test"), Is.True);
         Assert.That(simulator.SelectFolder("Inbox"), Is.True);

         Assert.That(simulator.SetFlagOnMessage(1, true, @"\Deleted"), Is.True);
         Assert.That(simulator.SetFlagOnMessage(3, true, @"\Deleted"), Is.True);

         string result;
         Assert.That(simulator.Expunge(out result), Is.True);

         // Messages 1 and 2 should be deleted. 2, because when the first message
         // is deleted, the index of the message which was originally 3, is now 2.
         Assert.That(result.Contains("* 1 EXPUNGE\r\n* 2 EXPUNGE"), Is.True);
      }

      [Test]
      [Description("Assert that when one client deletes a message, others are notified - even if IDLE isn't used.")]
      public void TestExpungeNotification()
      {
         _settings.IMAPIdleEnabled = true;

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "idleaccount@example.test", "test");

         for (var i = 0; i < 5; i++)
            SmtpClientSimulator.StaticSend("test@example.test", account.Address, "Test", "test");

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 5);

         var imapClientSimulator = new ImapClientSimulator();
         var simulator2 = new ImapClientSimulator();
         imapClientSimulator.ConnectAndLogon(account.Address, "test");
         simulator2.ConnectAndLogon(account.Address, "test");

         imapClientSimulator.SelectFolder("Inbox");
         simulator2.SelectFolder("Inbox");

         for (var i = 1; i <= 5; i++) Assert.That(imapClientSimulator.SetFlagOnMessage(i, true, @"\Deleted"), Is.True);

         var noopResponse = simulator2.NOOP() + simulator2.NOOP();

         Assert.That(noopResponse.Contains(@"* 1 FETCH (FLAGS (\Deleted)") &&
                       noopResponse.Contains(@"* 1 FETCH (FLAGS (\Deleted)") &&
                       noopResponse.Contains(@"* 1 FETCH (FLAGS (\Deleted)") &&
                       noopResponse.Contains(@"* 1 FETCH (FLAGS (\Deleted)") &&
                       noopResponse.Contains(@"* 1 FETCH (FLAGS (\Deleted)"),
                       Is.True,
                       noopResponse);

         var result = imapClientSimulator.Expunge();

         var expungeResult = simulator2.NOOP() + simulator2.NOOP();

         Assert.That(
            expungeResult.Contains("* 1 EXPUNGE\r\n* 1 EXPUNGE\r\n* 1 EXPUNGE\r\n* 1 EXPUNGE\r\n* 1 EXPUNGE"),
            Is.True,
            expungeResult);
      }

      [Test]
      public void TestFolderExpungeNotification()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "shared@example.test", "test");

         SmtpClientSimulator.StaticSend(account.Address, account.Address, "TestSubject", "TestBody");
         ImapClientSimulator.AssertMessageCount(account.Address, "test", "Inbox", 1);

         var simulator1 = new ImapClientSimulator();
         var simulator2 = new ImapClientSimulator();

         simulator1.ConnectAndLogon(account.Address, "test");
         simulator2.ConnectAndLogon(account.Address, "test");

         simulator1.SelectFolder("Inbox");
         simulator2.SelectFolder("Inbox");

         var result = simulator2.NOOP();
         Assert.That(result.Contains("Deleted"), Is.False);
         Assert.That(result.Contains("Seen"), Is.False);

         simulator1.SetDeletedFlag(1);
         simulator1.Expunge();

         // the result may (should) come after the first NOOP response stream so do noop twice.
         result = simulator2.NOOP() + simulator2.NOOP();
         Assert.That(result.Contains("* 1 EXPUNGE"), Is.True);

         simulator1.Disconnect();
         simulator2.Disconnect();
      }

      [Test]
      public void TestFolderNamesWithUnicodeChars()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.LogonWithLiteral("folder@example.test", "test");
         simulator.Send("A50 CREATE &AMQAxADEAMQAxADEAMQAxADEAMQAxADEAMQ-\r\n");
         simulator.Disconnect();

         var s = account.IMAPFolders[1].Name;
         Assert.That(s, Is.EqualTo("ÄÄÄÄÄÄÄÄÄÄÄÄÄ"));
      }

      [Test]
      public void TestFolderUpdateNotification()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "shared@example.test", "test");

         SmtpClientSimulator.StaticSend(account.Address, account.Address, "TestSubject", "TestBody");

         ImapClientSimulator.AssertMessageCount(account.Address, "test", "Inbox", 1);


         var simulator1 = new ImapClientSimulator();
         var simulator2 = new ImapClientSimulator();

         simulator1.ConnectAndLogon(account.Address, "test");
         simulator2.ConnectAndLogon(account.Address, "test");

         simulator1.SelectFolder("Inbox");
         simulator2.SelectFolder("Inbox");

         var result = simulator2.NOOP() + simulator2.NOOP();
         Assert.That(result.Contains("Deleted"), Is.False);
         Assert.That(result.Contains("Seen"), Is.False);

         simulator1.SetDeletedFlag(1);
         simulator1.SetSeenFlag(1);

         result = simulator2.NOOP() + simulator2.NOOP();
         Assert.That(result.Contains("Deleted"), Is.True);
         Assert.That(result.Contains("Seen"), Is.True);

         simulator1.Disconnect();
         simulator2.Disconnect();
      }

      [Test]
      public void TestGetQuota()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "imapaccount@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("imapaccount@example.test", "test");
         var result = simulator.GetQuota("Inbox");
         Assert.That(result.Contains("A09 OK"), Is.True);
         simulator.Disconnect();
      }

      [Test]
      public void TestIdle()
      {
         _settings.IMAPIdleEnabled = true;

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "idleaccount@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon(account.Address, "test");
         Assert.That(simulator.SelectFolder("INBOX"), Is.True);

         simulator.StartIdle();

         if (simulator.GetPendingDataExists())
            throw new Exception("Unexpected data exists");

         // Send a message to this account.
         var smtpClientSimulator = new SmtpClientSimulator();
         smtpClientSimulator.Send(account.Address, account.Address, "IDLE Test", "This is a test of IDLE");

         string data;
         Assert.That(simulator.EndIdle(false, out data), Is.True);
      }

      [Test]
      public void TestLSUB()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "list@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.LogonWithLiteral("list@example.test", "test");
         var result = simulator.List().Substring(0, 1);
         Assert.That(result, Is.EqualTo("*"));
      }

      [Test]
      public void TestList()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "list@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("list@example.test", "test");
         var result = simulator.Send("A01 LIST \"\" \"*\"\r\n").Substring(0, 1);
         Assert.That(result, Is.EqualTo("*"));
      }

      [Test]
      public void TestLiteralSupport()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "literal@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.LogonWithLiteral("literal@example.test", "test");
         simulator.Disconnect();
      }

      [Test]
      public void TestLiterals()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("folder@example.test", "test");
         var result = simulator.Send("A01 CREATE {4}");
         result = simulator.Send("HEJS");

         simulator.Disconnect();
      }


      [Test]
      public void TestLogin()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "imapaccount@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("imapaccount@example.test", "test");
         simulator.Disconnect();
      }

      [Test]
      public void TestNamespace()
      {
         var imapFolderName = _settings.IMAPPublicFolderName;

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "delete@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("delete@example.test", "test");
         var result = simulator.Send("A01 NAMESPACE");

         var correctNamespaceSetting = "* NAMESPACE ((\"\" \".\")) NIL ((\"" + imapFolderName + "\" \".\"))";

         if (!result.Contains(correctNamespaceSetting)) Assert.Fail("Namespace failed");
      }


      /// <summary>
      ///    Test that when you delete a message using POP3, IMAP notifications are sent.
      /// </summary>
      [Test]
      public void TestNotificationOnPOP3Deletion()
      {
         _settings.IMAPIdleEnabled = true;

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "idleaccount@example.test", "test");
         SmtpClientSimulator.StaticSend(account.Address, account.Address, "Message 1", "Body 1");
         SmtpClientSimulator.StaticSend(account.Address, account.Address, "Message 1", "Body 1");
         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 2);

         var imapSimulator = new ImapClientSimulator();
         var sWelcomeMessage = imapSimulator.Connect();
         Assert.That(imapSimulator.Logon("idleaccount@example.test", "test"), Is.True);
         Assert.That(imapSimulator.SelectFolder("INBOX"), Is.True);
         Assert.That(imapSimulator.StartIdle(), Is.True);

         var sim = new Pop3ClientSimulator();
         Assert.That(sim.ConnectAndLogon(account.Address, "test"), Is.True);
         Assert.That(sim.DELE(1), Is.True);
         sim.QUIT();

         // After a delete, the following should be sent tot he IMAP client:
         //  - EXPUNGE
         //  - EXISTS
         //  - RECENT
         Assert.That(imapSimulator.AssertPendingDataExists(), Is.True, "No pending data exist");

         var deadline = DateTime.Now.AddSeconds(10);
         var message = new StringBuilder();

         while (DateTime.Now < deadline)
         {
            if (imapSimulator.GetPendingDataExists())
               message.Append(imapSimulator.Receive());

            var str = message.ToString();

            if (str.Contains("* 1 EXPUNGE") &&
                str.Contains("EXISTS") &&
                str.Contains("RECENT"))
               return;
         }

         var receivedText = message.ToString();
         Assert.That(receivedText.Contains("* 1 EXPUNGE"), Is.True, receivedText);
         Assert.That(receivedText.Contains("EXISTS"), Is.True, receivedText);
         Assert.That(receivedText.Contains("RECENT"), Is.True, receivedText);
      }


      [Test]
      public void TestOutOfBounds()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "outofbounds@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("outofbounds@example.test", "test");

         var s = simulator.Send("A01 RENAME TEST");
         if (s.StartsWith("A01 BAD") == false)
            throw new Exception("ERROR - Out of bounds test failed");
      }

      [Test]
      public void TestPublicFolderUpdateNotification()
      {
         var folders = _application.Settings.PublicFolders;
         var folder = folders.Add("Share");
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionType = eACLPermissionType.ePermissionTypeAnyone;
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.set_Permission(eACLPermission.ePermissionRead, true);
         permission.set_Permission(eACLPermission.ePermissionWriteOthers, true);
         permission.set_Permission(eACLPermission.ePermissionWriteSeen, true);
         permission.set_Permission(eACLPermission.ePermissionWriteDeleted, true);
         permission.set_Permission(eACLPermission.ePermissionInsert, true);
         permission.Save();

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "shared@example.test", "test");

         SmtpClientSimulator.StaticSend(account.Address, account.Address, "TestSubject", "TestBody");
         ImapClientSimulator.AssertMessageCount(account.Address, "test", "Inbox", 1);

         var simulator1 = new ImapClientSimulator();
         var simulator2 = new ImapClientSimulator();

         simulator1.ConnectAndLogon(account.Address, "test");
         simulator2.ConnectAndLogon(account.Address, "test");

         simulator1.SelectFolder("Inbox");
         simulator2.SelectFolder("Inbox");

         Assert.That(simulator1.Copy(1, "#Public.Share"), Is.True);

         simulator1.SelectFolder("#Public.Share");
         simulator2.SelectFolder("#Public.Share");

         var result = simulator2.NOOP() + simulator2.NOOP();
         Assert.That(result.Contains("Deleted"), Is.False);
         Assert.That(result.Contains("Seen"), Is.False);

         simulator1.SetDeletedFlag(1);
         simulator1.SetSeenFlag(1);

         result = simulator2.NOOP() + simulator2.NOOP();
         Assert.That(result.Contains("Deleted"), Is.True);
         Assert.That(result.Contains("Seen"), Is.True);

         simulator1.Disconnect();
         simulator2.Disconnect();
      }

      [Test]
      [Description("Issue 244, Recent flag not removed properly.")]
      public void TestRecentRemovedOnMailboxChange()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         SmtpClientSimulator.StaticSend(account.Address, account.Address, "Test", "TestMessage");
         ImapClientSimulator.AssertMessageCount(account.Address, "test", "Inbox", 1);

         var sim = new ImapClientSimulator();
         Assert.That(sim.ConnectAndLogon(account.Address, "test"), Is.True);
         Assert.That(sim.SelectFolder("Inbox"), Is.True);
         Assert.That(sim.CreateFolder("Dummy"), Is.True);
         Assert.That(sim.Copy(1, "Dummy"), Is.True);
         var result = sim.SendSingleCommand("a01 select Dummy");
         Assert.That(result.Contains("* 1 EXISTS\r\n* 1 RECENT"), Is.True, result);
         Assert.That(sim.SelectFolder("Inbox"), Is.True);

         // Make sure that when we switched back to the Inbox, the Recent flag was removed.
         result = sim.SendSingleCommand("a01 select Dummy");
         Assert.That(result.Contains("* 1 EXISTS\r\n* 1 RECENT"), Is.False, result);
         Assert.That(sim.Logout(), Is.True);
      }

      [Test]
      [Description("Issue 244, Recent flag not removed properly")]
      public void TestRecentRemovedOnMailboxClose()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         SmtpClientSimulator.StaticSend(account.Address, account.Address, "Test", "TestMessage");
         ImapClientSimulator.AssertMessageCount(account.Address, "test", "Inbox", 1);

         var sim = new ImapClientSimulator();
         Assert.That(sim.ConnectAndLogon(account.Address, "test"), Is.True);
         Assert.That(sim.SelectFolder("Inbox"), Is.True);
         Assert.That(sim.CreateFolder("Dummy"), Is.True);
         Assert.That(sim.Copy(1, "Dummy"), Is.True);
         var result = sim.SendSingleCommand("a01 select Dummy");
         Assert.That(result.Contains("* 1 EXISTS\r\n* 1 RECENT"), Is.True, result);
         Assert.That(sim.Logout(), Is.True);

         sim = new ImapClientSimulator();
         Assert.That(sim.ConnectAndLogon(account.Address, "test"), Is.True);
         result = sim.SendSingleCommand("a01 select Dummy");
         Assert.That(result.Contains("* 1 EXISTS\r\n* 1 RECENT"), Is.False, result);
         Assert.That(sim.Logout(), Is.True);
      }

      [Test]
      public void TestRename()
      {
         var application = SingletonProvider<TestSetup>.Instance.GetApp();
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("folder@example.test", "test");

         Assert.That(simulator.CreateFolder("Root1"), Is.True);
         Assert.That(simulator.CreateFolder("Root1.Sub1"), Is.True);
         Assert.That(simulator.CreateFolder("Root1.Sub2"), Is.True);
         Assert.That(simulator.CreateFolder("Root1.Sub3"), Is.True);
         Assert.That(simulator.SelectFolder("Root1"), Is.True);
         Assert.That(simulator.SelectFolder("Root1.Sub1"), Is.True);
         Assert.That(simulator.SelectFolder("Root1.Sub2"), Is.True);
         Assert.That(simulator.SelectFolder("Root1.Sub3"), Is.True);
         Assert.That(simulator.RenameFolder("Root1", "Root2"), Is.True);
         Assert.That(simulator.SelectFolder("Root2"), Is.True);
         Assert.That(simulator.SelectFolder("Root2.Sub1"), Is.True);
         Assert.That(simulator.SelectFolder("Root2.Sub2"), Is.True);
         Assert.That(simulator.SelectFolder("Root2.Sub3"), Is.True);
         simulator.Disconnect();
      }

      [Test]
      public void TestRenameAndList()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("folder@example.test", "test");
         Assert.That(simulator.CreateFolder("Root1"), Is.True);
         Assert.That(simulator.CreateFolder("Root2"), Is.True);
         Assert.That(simulator.RenameFolder("Root2", "Root1.Root2"), Is.True);

         var result = simulator.List();

         Assert.That(result.Contains("Root1.Root2"), Is.True);

         simulator.Disconnect();
      }


      [Test]
      public void TestRenameAndList2()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("folder@example.test", "test");
         simulator.CreateFolder("Root1");
         simulator.CreateFolder("Root2");
         simulator.CreateFolder("Root3");

         simulator.RenameFolder("Root2", "Root1.Root2");
         simulator.RenameFolder("Root3", "Root1.Root2.Root3");

         var result = simulator.List();

         Assert.That(result.Contains("Root1\"\r\n"), Is.True);
         Assert.That(result.Contains("Root1.Root2\"\r\n"), Is.True);
         Assert.That(result.Contains("Root1.Root2.Root3\"\r\n"), Is.True);

         simulator.Disconnect();
      }

      [Test]
      public void TestRenameAndList3()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("folder@example.test", "test");
         simulator.CreateFolder("Root1");
         simulator.CreateFolder("Root2");
         simulator.CreateFolder("Root3");

         simulator.RenameFolder("Root2", "Root1.Root2");
         simulator.RenameFolder("Root3", "Root1.Root2.Root3");
         simulator.RenameFolder("Root1.Root2.Root3", "Test");

         var result = simulator.List();

         Assert.That(result.Contains("Root1\"\r\n"), Is.True);
         Assert.That(result.Contains("Root1.Root2\"\r\n"), Is.True);
         Assert.That(result.Contains("Test\"\r\n"), Is.True);

         simulator.Disconnect();
      }

      [Test]
      public void TestRenameAndList4()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("folder@example.test", "test");
         simulator.CreateFolder("Root1");
         simulator.CreateFolder("Root2.Root3");

         simulator.RenameFolder("Root2.Root3", "Root2.Root4");

         var result = simulator.List();

         Assert.That(result.Contains("Root1\"\r\n"), Is.True);
         Assert.That(result.Contains("Root2.Root4\"\r\n"), Is.True);

         simulator.Disconnect();
      }

      [Test]
      public void TestRenameAndList5()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("folder@example.test", "test");
         simulator.CreateFolder("Root1");
         simulator.CreateFolder("Root2.Root3");

         simulator.RenameFolder("Root2.Root3", "Root2.Root4");
         simulator.RenameFolder("Root1", "Root2.Root4.Root1");

         var result = simulator.List();

         Assert.That(result.Contains(" Root1\r\n"), Is.False);
         Assert.That(result.Contains("Root2.Root4.Root1\"\r\n"), Is.True);

         Assert.That(simulator.SelectFolder("Root1"), Is.False);
         Assert.That(simulator.SelectFolder("Root2.Root4.Root1"), Is.True);

         simulator.Disconnect();
      }

      [Test]
      [Description("Test that renaming an IMAP folder does not stop notifications from working.")]
      public void TestRenameIMAPFolderNotifications()
      {
         _settings.IMAPIdleEnabled = true;

         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "idleaccount@example.test", "test");

         var imapClientSimulator = new ImapClientSimulator();
         var simulator2 = new ImapClientSimulator();
         imapClientSimulator.ConnectAndLogon(account.Address, "test");
         simulator2.ConnectAndLogon(account.Address, "test");

         imapClientSimulator.SelectFolder("Inbox");
         simulator2.CreateFolder("Mailbox");
         simulator2.RenameFolder("Mailbox", "Mailbox2");

         SmtpClientSimulator.StaticSend("test@example.test", account.Address, "Test", "test");

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         var noopResponse = imapClientSimulator.NOOP() + imapClientSimulator.NOOP();

         // confirm that the client is notified about this message even though another
         // folder has been dropped by another client.
         Assert.That(noopResponse.Contains(@"* 1 EXISTS"), Is.True, noopResponse);
      }

      [Test]
      [Description("Issue 294, IMAP: Incomplete SELECT response")]
      public void TestSelectResponse()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "testselect@example.test", "test");

         SmtpClientSimulator.StaticSend(account.Address, account.Address, "Test", "Test");
         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         var simulator = new ImapClientSimulator();
         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("testselect@example.test", "test");
         var result = string.Empty;
         simulator.SelectFolder("Inbox", out result);
         simulator.Disconnect();

         Assert.That(result.Contains("* FLAGS"), Is.True, result);
         Assert.That(result.Contains("* 1 EXISTS"), Is.True, result);
         Assert.That(result.Contains("* 1 RECENT"), Is.True, result);
         Assert.That(result.Contains("* OK [UNSEEN 1]"), Is.True, result);
         Assert.That(result.Contains("* OK [PERMANENTFLAGS"), Is.True, result);
         Assert.That(result.Contains("* OK [UIDNEXT 2]"), Is.True, result);
         Assert.That(result.Contains("* OK [UIDVALIDITY"), Is.True, result);
         Assert.That(result.Contains("OK [READ-WRITE]"), Is.True, result);
      }

      [Test]
      public void TestStatus()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "imapaccount@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("imapaccount@example.test", "test");
         Assert.That(simulator.Status("Inbox", "MESSAGES").Contains("A08 OK"), Is.True);
         simulator.Disconnect();
      }

      [Test]
      public void TestSubscribe()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "delete@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("delete@example.test", "test");
         Assert.That(simulator.CreateFolder("TestFolder1"), Is.True);
         Assert.That(simulator.CreateFolder("TestFolder2"), Is.True);
         Assert.That(simulator.CreateFolder("TestFolder3"), Is.True);

         if (simulator.Subscribe("Vaffe"))
            Assert.Fail("Subscribe on non-existent folder succeeded");

         if (!simulator.Subscribe("TestFolder1"))
            Assert.Fail("Subscribe on existent folder failed");
         if (!simulator.Subscribe("TestFolder2"))
            Assert.Fail("Subscribe on existent folder failed");
         if (!simulator.Subscribe("TestFolder3"))
            Assert.Fail("Subscribe on existent folder failed");
      }


      [Test]
      [Description("Test that the SELECT response gives the correct unseen count.")]
      public void TestUnseenResponseInSelect()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@example.test", "test");

         SmtpClientSimulator.StaticSend(account.Address, account.Address, "Test", "TestMessage");

         ImapClientSimulator.AssertMessageCount(account.Address, "test", "Inbox", 1);

         var sim = new ImapClientSimulator();
         Assert.That(sim.ConnectAndLogon(account.Address, "test"), Is.True);
         Assert.That(sim.SelectFolder("Inbox"), Is.True);
         Assert.That(sim.CreateFolder("Dummy"), Is.True);
         Assert.That(sim.Copy(1, "Dummy"), Is.True);

         var result = sim.SendSingleCommand("a01 select Dummy");
         Assert.That(result.Contains("* 1 EXISTS\r\n* 1 RECENT"), Is.True, result);

         var searchResponse = sim.SendSingleCommand("srch1 SEARCH ALL UNSEEN");

         // We should have at least one message here.
         Assert.That(searchResponse.Contains("* SEARCH 1\r\n"), Is.True, searchResponse);

         // Now fetch the body.
         var bodyText = sim.Fetch("1 BODY[TEXT]");

         // Now the message is no longer unseen. Confirm this.
         searchResponse = sim.SendSingleCommand("srch1 SEARCH ALL UNSEEN");
         Assert.That(searchResponse.Contains("* SEARCH\r\n"), Is.True, searchResponse);

         // Close the messages to mark them as no longer recent.
         Assert.That(sim.Close(), Is.True);

         result = sim.SendSingleCommand("a01 select Dummy");
         Assert.That(result.Contains("* 1 EXISTS\r\n* 0 RECENT"), Is.True, result);
      }

      [Test]
      public void TestUnsubscribe()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "delete@example.test", "test");

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();
         simulator.Logon("delete@example.test", "test");
         Assert.That(simulator.CreateFolder("TestFolder1"), Is.True);
         Assert.That(simulator.CreateFolder("TestFolder2"), Is.True);

         if (!simulator.Subscribe("TestFolder1"))
            Assert.Fail("Subscribe on existent folder failed");
         if (!simulator.Subscribe("TestFolder2"))
            Assert.Fail("Subscribe on existent folder failed");

         if (!simulator.Unsubscribe("TestFolder1"))
            Assert.Fail("Unsubscribe on existent folder failed");
         if (!simulator.Unsubscribe("TestFolder2"))
            Assert.Fail("Unsubscribe on existent folder failed");
      }

      [Test]
      public void TestWelcomeMessage()
      {
         _settings.WelcomeIMAP = "HOWDYHO IMAP";

         var simulator = new ImapClientSimulator();

         var sWelcomeMessage = simulator.Connect();

         simulator.Disconnect();

         if (sWelcomeMessage != "* OK HOWDYHO IMAP\r\n")
            throw new Exception("ERROR - Wrong welcome message.");
      }
   }
}