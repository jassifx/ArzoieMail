// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using hMailServer;
using NUnit.Framework;
using RegressionTests.Infrastructure;
using RegressionTests.Shared;

namespace RegressionTests.IMAP
{
   /// <summary>
   ///    RFC 4315, the IMAP UIDPLUS extension: APPENDUID, COPYUID and UID EXPUNGE.
   /// </summary>
   [TestFixture]
   public class UidPlus : TestFixtureBase
   {
      private const string Password = "test";

      #region APPENDUID

      [Test]
      public void AppendReturnsAppendUid()
      {
         var account = AddAccount();
         var simulator = ConnectAndLogon(account);

         var select = simulator.SendSingleCommand("A01 SELECT INBOX");
         var uidValidity = GetResponseCodeValue(select, "UIDVALIDITY");
         var uidNext = GetResponseCodeValue(select, "UIDNEXT");

         var first = simulator.Append("INBOX", CreateMessage("first@example.test"));
         var firstUid = AssertAppendUid(first, uidValidity);
         Assert.That(firstUid, Is.EqualTo(uidNext), first);

         var second = simulator.Append("INBOX", CreateMessage("second@example.test"));
         Assert.That(AssertAppendUid(second, uidValidity), Is.EqualTo(firstUid + 1), second);

         // The reported UID must be the UID of the message that was appended.
         Assert.That(UidSearch(simulator, "HEADER Message-ID first@example.test"), Is.EqualTo(firstUid.ToString()));

         simulator.Disconnect();
      }

      [Test]
      public void AppendToPublicFolderReturnsItsAppendUid()
      {
         var account = AddAccount();
         CreatePublicFolder("Share", account);

         var simulator = ConnectAndLogon(account);

         var select = simulator.SendSingleCommand("A01 SELECT \"#Public.Share\"");
         Assert.That(select.Contains("A01 OK"), Is.True, select);
         var uidValidity = GetResponseCodeValue(select, "UIDVALIDITY");
         var uidNext = GetResponseCodeValue(select, "UIDNEXT");

         var append = simulator.Append("#Public.Share", CreateMessage("public@example.test"));
         Assert.That(AssertAppendUid(append, uidValidity), Is.EqualTo(uidNext), append);

         simulator.Disconnect();
      }

      [Test]
      [Description("A client that can't SELECT the folder must not be told its UIDVALIDITY or UIDs (RFC 4315 6).")]
      public void AppendWithoutReadPermissionHasNoAppendUid()
      {
         var account = AddAccount();
         CreatePublicFolder("Dropbox", account, false);

         var simulator = ConnectAndLogon(account);

         var append = simulator.Append("#Public.Dropbox", CreateMessage("dropbox@example.test"));
         Assert.That(append.Contains("A40 OK"), Is.True, append);
         Assert.That(append.Contains("APPENDUID"), Is.False, append);

         simulator.Disconnect();

         CustomAsserts.AssertFolderMessageCount(_settings.PublicFolders.get_ItemByName("Dropbox"), 1);
      }

      [Test]
      public void AppendToUnselectedFolderReturnsThatFoldersValidity()
      {
         var account = AddAccount();
         var simulator = ConnectAndLogon(account);
         Assert.That(simulator.CreateFolder("Other"), Is.True);

         var status = simulator.Status("Other", "UIDVALIDITY UIDNEXT");
         var uidValidity = GetStatusValue(status, "UIDVALIDITY");
         var uidNext = GetStatusValue(status, "UIDNEXT");

         Assert.That(simulator.SelectFolder("INBOX"), Is.True);

         var append = simulator.Append("Other", CreateMessage("other@example.test"));
         Assert.That(AssertAppendUid(append, uidValidity), Is.EqualTo(uidNext), append);

         simulator.Disconnect();
      }

      [Test]
      [Description("Several clients appending into one folder at the same time must each be told a " +
                   "distinct UID, and it must be the UID the message really got.")]
      public void ConcurrentAppendsReportDistinctUids()
      {
         const int threadCount = 4;
         const int appendsPerThread = 25;

         var account = AddAccount();

         var reported = new List<long>();
         var failures = new List<string>();
         var threads = new List<Thread>();

         for (var t = 0; t < threadCount; t++)
         {
            var threadIndex = t;

            threads.Add(new Thread(() =>
            {
               try
               {
                  var simulator = ConnectAndLogon(account);

                  for (var i = 0; i < appendsPerThread; i++)
                  {
                     var response = simulator.Append("INBOX", CreateMessage("t" + threadIndex + "-" + i + "@example.test"));
                     var match = Regex.Match(response, @"\[APPENDUID \d+ (\d+)\]");

                     lock (reported)
                     {
                        if (match.Success)
                           reported.Add(long.Parse(match.Groups[1].Value));
                        else
                           failures.Add(response);
                     }
                  }

                  simulator.Disconnect();
               }
               catch (Exception ex)
               {
                  lock (reported)
                     failures.Add(ex.ToString());
               }
            }));
         }

         foreach (var thread in threads)
            thread.Start();

         foreach (var thread in threads)
            thread.Join();

         Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
         Assert.That(reported.Distinct().Count(), Is.EqualTo(threadCount * appendsPerThread), "Duplicate APPENDUID reported.");

         var reader = ConnectAndLogon(account);
         Assert.That(reader.SelectFolder("INBOX"), Is.True);
         var actual = UidSearch(reader, "ALL").Split(' ').Select(long.Parse).OrderBy(uid => uid);
         reader.Disconnect();

         Assert.That(actual, Is.EqualTo(reported.OrderBy(uid => uid)).AsCollection);
      }

      #endregion

      #region COPYUID

      [Test]
      public void CopyReturnsCopyUid()
      {
         var account = AddAccount();
         var simulator = ConnectAndLogon(account);
         Assert.That(simulator.CreateFolder("Target"), Is.True);

         AppendMessages(simulator, "INBOX", 3);
         AppendMessages(simulator, "Target", 3);

         var uidValidity = GetStatusValue(simulator.Status("Target", "UIDVALIDITY"), "UIDVALIDITY");

         Assert.That(simulator.SelectFolder("INBOX"), Is.True);

         var copy = simulator.SendSingleCommand("A01 COPY 1:3 \"Target\"");
         Assert.That(copy.Contains("A01 OK [COPYUID " + uidValidity + " 1:3 4:6]"), Is.True, copy);

         var uidCopy = simulator.UidCopy("1,3", "Target");
         Assert.That(uidCopy.Contains("A41 OK [COPYUID " + uidValidity + " 1,3 7:8]"), Is.True, uidCopy);

         simulator.Disconnect();
      }

      [Test]
      [Description("The source set is listed in the order the messages were copied, and the " +
                   "destination set corresponds to it.")]
      public void CopyUidSetsCorrespond()
      {
         var account = AddAccount();
         var simulator = ConnectAndLogon(account);
         Assert.That(simulator.CreateFolder("Target"), Is.True);

         AppendMessages(simulator, "INBOX", 5);

         var uidValidity = GetStatusValue(simulator.Status("Target", "UIDVALIDITY"), "UIDVALIDITY");

         Assert.That(simulator.SelectFolder("INBOX"), Is.True);

         var uidCopy = simulator.UidCopy("1:2,4:5", "Target");
         Assert.That(uidCopy.Contains("A41 OK [COPYUID " + uidValidity + " 1:2,4:5 1:4]"), Is.True, uidCopy);

         simulator.Disconnect();
      }

      [Test]
      public void CopyOfNoMessagesHasNoCopyUid()
      {
         var account = AddAccount();
         var simulator = ConnectAndLogon(account);
         Assert.That(simulator.CreateFolder("Target"), Is.True);

         AppendMessages(simulator, "INBOX", 1);
         Assert.That(simulator.SelectFolder("INBOX"), Is.True);

         var uidCopy = simulator.UidCopy("100", "Target");
         Assert.That(uidCopy.Contains("A41 OK"), Is.True, uidCopy);
         Assert.That(uidCopy.Contains("COPYUID"), Is.False, uidCopy);

         simulator.Disconnect();
      }

      [Test]
      public void CopyIntoSameFolderReturnsCopyUid()
      {
         var account = AddAccount();
         var simulator = ConnectAndLogon(account);

         AppendMessages(simulator, "INBOX", 2);

         var select = simulator.SendSingleCommand("A01 SELECT INBOX");
         var uidValidity = GetResponseCodeValue(select, "UIDVALIDITY");

         var copy = simulator.SendSingleCommand("A02 COPY 1:2 INBOX");
         Assert.That(copy.Contains("A02 OK [COPYUID " + uidValidity + " 1:2 3:4]"), Is.True, copy);

         simulator.Disconnect();
      }

      [Test]
      public void CopyToPublicFolderReturnsItsValidity()
      {
         var account = AddAccount();
         CreatePublicFolder("Share", account);

         var simulator = ConnectAndLogon(account);
         AppendMessages(simulator, "INBOX", 1);

         var uidValidity = GetResponseCodeValue(simulator.SendSingleCommand("A01 SELECT \"#Public.Share\""), "UIDVALIDITY");

         Assert.That(simulator.SelectFolder("INBOX"), Is.True);

         var copy = simulator.SendSingleCommand("A02 COPY 1 \"#Public.Share\"");
         Assert.That(copy.Contains("A02 OK [COPYUID " + uidValidity + " 1 1]"), Is.True, copy);

         simulator.Disconnect();
      }

      [Test]
      [Description("A client that can't SELECT the folder must not be told its UIDVALIDITY or UIDs (RFC 4315 6).")]
      public void CopyWithoutReadPermissionHasNoCopyUid()
      {
         var account = AddAccount();
         CreatePublicFolder("Dropbox", account, false);

         var simulator = ConnectAndLogon(account);
         AppendMessages(simulator, "INBOX", 2);
         Assert.That(simulator.SelectFolder("INBOX"), Is.True);

         var copy = simulator.SendSingleCommand("A01 COPY 1 \"#Public.Dropbox\"");
         Assert.That(copy.Contains("A01 OK"), Is.True, copy);
         Assert.That(copy.Contains("COPYUID"), Is.False, copy);

         var uidCopy = simulator.UidCopy("2", "#Public.Dropbox");
         Assert.That(uidCopy.Contains("A41 OK"), Is.True, uidCopy);
         Assert.That(uidCopy.Contains("COPYUID"), Is.False, uidCopy);

         simulator.Disconnect();

         CustomAsserts.AssertFolderMessageCount(_settings.PublicFolders.get_ItemByName("Dropbox"), 2);
      }

      [Test]
      [Description("COPY is atomic (RFC 3501 6.4.7). If one message can't be copied, the copies " +
                   "already made are removed again.")]
      public void CopyFailingPartWayLeavesDestinationUnchanged()
      {
         // 1 MB quota. Two 300 KB messages and one copy fit, a second copy does not.
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "uidplus@example.test", Password, 1);
         var simulator = ConnectAndLogon(account);
         Assert.That(simulator.CreateFolder("Target"), Is.True);

         var body = string.Concat(Enumerable.Repeat(new string('x', 98) + "\r\n", 3072));

         for (var i = 0; i < 2; i++)
            AssertAppendUid(simulator.Append("INBOX", CreateMessage(Guid.NewGuid() + "@example.test", body)));

         Assert.That(simulator.SelectFolder("INBOX"), Is.True);

         var copy = simulator.SendSingleCommand("A01 COPY 1:2 \"Target\"");
         Assert.That(copy.Contains("A01 NO"), Is.True, copy);
         Assert.That(copy.Contains("COPYUID"), Is.False, copy);

         Assert.That(GetStatusValue(simulator.Status("Target", "MESSAGES"), "MESSAGES"), Is.EqualTo(0));

         // The removed copy no longer counts against the quota.
         copy = simulator.SendSingleCommand("A02 COPY 1 \"Target\"");
         Assert.That(copy.Contains("A02 OK"), Is.True, copy);

         simulator.Disconnect();
      }

      [TestCase("UID COPY 1:2")]
      [TestCase("COPY 1:2")]
      [Description("A message whose file is missing from disk is still in the folder. COPY fails and " +
                   "logs the missing file, rather than treating the message as expunged.")]
      public void CopyOfMessageWithMissingFileFails(string command)
      {
         var account = AddAccount();
         var simulator = ConnectAndLogon(account);
         Assert.That(simulator.CreateFolder("Target"), Is.True);
         AppendMessages(simulator, "INBOX", 2);

         var inbox = account.IMAPFolders.get_ItemByName("INBOX");
         File.Delete(inbox.Messages[0].Filename);

         Assert.That(simulator.SelectFolder("INBOX"), Is.True);

         var copy = simulator.SendSingleCommand("A01 " + command + " \"Target\"");
         Assert.That(copy.Contains("A01 NO"), Is.True, copy);
         Assert.That(copy.Contains("EXPUNGE"), Is.False, copy);
         Assert.That(copy.Contains("COPYUID"), Is.False, copy);

         Assert.That(GetStatusValue(simulator.Status("Target", "MESSAGES"), "MESSAGES"), Is.EqualTo(0));

         // The session still sees the message.
         Assert.That(UidSearch(simulator, "ALL"), Is.EqualTo("1 2"));

         simulator.Disconnect();

         CustomAsserts.AssertReportedError("Message copy failed because message file");
      }

      #endregion

      #region UID EXPUNGE

      [Test]
      [Description("UID EXPUNGE removes only the \\Deleted messages in the given set.")]
      public void UidExpungeRemovesOnlyDeletedMessagesInSet()
      {
         var account = AddAccount();
         var simulator = ConnectAndLogon(account);

         AppendMessages(simulator, "INBOX", 4);
         Assert.That(simulator.SelectFolder("INBOX"), Is.True);

         simulator.SendSingleCommand("A01 UID STORE 1,3 +FLAGS (\\Deleted)");

         var expunge = simulator.UidExpunge("3");
         Assert.That(expunge.Contains("A42 OK"), Is.True, expunge);
         Assert.That(GetExpungedSequences(expunge), Is.EqualTo(new[] {"3"}), expunge);

         // Message 1 is still there and still flagged.
         var flags = simulator.SendSingleCommand("A02 UID FETCH 1 (FLAGS)");
         Assert.That(flags.Contains("\\Deleted"), Is.True, flags);

         // An unflagged message in the set is left alone.
         expunge = simulator.UidExpunge("2");
         Assert.That(expunge.Contains("A42 OK"), Is.True, expunge);
         Assert.That(GetExpungedSequences(expunge), Is.Empty, expunge);

         expunge = simulator.UidExpunge("1:*");
         Assert.That(expunge.Contains("A42 OK"), Is.True, expunge);
         Assert.That(GetExpungedSequences(expunge), Is.EqualTo(new[] {"1"}), expunge);

         Assert.That(UidSearch(simulator, "ALL"), Is.EqualTo("2 4"));

         simulator.Disconnect();
      }

      [Test]
      public void UidExpungeIsReportedToOtherSessions()
      {
         var account = AddAccount();

         var setup = ConnectAndLogon(account);
         AppendMessages(setup, "INBOX", 3);
         setup.Disconnect();

         var observer = ConnectAndLogon(account);
         Assert.That(observer.SelectFolder("INBOX"), Is.True);

         var expunger = ConnectAndLogon(account);
         Assert.That(expunger.SelectFolder("INBOX"), Is.True);
         expunger.SendSingleCommand("A01 UID STORE 1,2 +FLAGS (\\Deleted)");
         var expunge = expunger.UidExpunge("2");
         Assert.That(GetExpungedSequences(expunge), Is.EqualTo(new[] {"2"}), expunge);
         expunger.Disconnect();

         var noop = observer.NOOP() + observer.NOOP();
         Assert.That(GetExpungedSequences(noop), Is.EqualTo(new[] {"2"}), noop);
         Assert.That(UidSearch(observer, "ALL"), Is.EqualTo("1 3"));

         observer.Disconnect();
      }

      [Test]
      public void UidExpungeOnExaminedFolderIsRefused()
      {
         var account = AddAccount();
         var simulator = ConnectAndLogon(account);

         AppendMessages(simulator, "INBOX", 1);
         Assert.That(simulator.SelectFolder("INBOX"), Is.True);
         simulator.SendSingleCommand("A01 UID STORE 1 +FLAGS (\\Deleted)");

         simulator.ExamineFolder("INBOX");

         var expunge = simulator.UidExpunge("1");
         Assert.That(expunge.Contains("A42 NO"), Is.True, expunge);

         Assert.That(simulator.SelectFolder("INBOX"), Is.True);
         Assert.That(UidSearch(simulator, "ALL"), Is.EqualTo("1"));

         simulator.Disconnect();
      }

      [Test]
      public void UidExpungeRequiresExpungePermission()
      {
         var account = AddAccount();
         var permission = CreatePublicFolder("Share", account);

         var simulator = ConnectAndLogon(account);
         AppendMessages(simulator, "#Public.Share", 1);
         Assert.That(simulator.SelectFolder("#Public.Share"), Is.True);
         simulator.SendSingleCommand("A01 UID STORE 1 +FLAGS (\\Deleted)");

         var expunge = simulator.UidExpunge("1");
         Assert.That(expunge.Contains("A42 OK"), Is.False, expunge);

         permission.set_Permission(eACLPermission.ePermissionExpunge, true);
         permission.Save();

         expunge = simulator.UidExpunge("1");
         Assert.That(expunge.Contains("A42 OK"), Is.True, expunge);
         Assert.That(GetExpungedSequences(expunge), Is.EqualTo(new[] {"1"}), expunge);

         simulator.Disconnect();
      }

      [Test]
      public void UidExpungeWithoutSetIsRejected()
      {
         var account = AddAccount();
         var simulator = ConnectAndLogon(account);
         Assert.That(simulator.SelectFolder("INBOX"), Is.True);

         var result = simulator.SendSingleCommand("A01 UID EXPUNGE");
         Assert.That(result.Contains("A01 BAD"), Is.True, result);

         simulator.Disconnect();
      }

      // The message set rules are tested in IMAPCommandRangeActionTester. This checks that UID EXPUNGE uses them.
      [Test]
      public void UidExpungeWithMalformedSetIsRejected()
      {
         var account = AddAccount();
         var simulator = ConnectAndLogon(account);

         AppendMessages(simulator, "INBOX", 2);
         Assert.That(simulator.SelectFolder("INBOX"), Is.True);
         simulator.SendSingleCommand("A01 UID STORE 1:2 +FLAGS (\\Deleted)");

         var expunge = simulator.UidExpunge("1,,2");
         Assert.That(expunge.Contains("A42 BAD"), Is.True, expunge);
         Assert.That(GetExpungedSequences(expunge), Is.Empty, expunge);

         Assert.That(UidSearch(simulator, "ALL"), Is.EqualTo("1 2"));

         simulator.Disconnect();
      }

      [Test]
      [Description("* is the highest UID in the folder (RFC 3501 9).")]
      public void UidExpungeOfStarExpungesLastMessage()
      {
         var account = AddAccount();
         var simulator = ConnectAndLogon(account);

         AppendMessages(simulator, "INBOX", 3);
         Assert.That(simulator.SelectFolder("INBOX"), Is.True);
         simulator.SendSingleCommand("A01 UID STORE 1:3 +FLAGS (\\Deleted)");

         var expunge = simulator.UidExpunge("*");
         Assert.That(expunge.Contains("A42 OK"), Is.True, expunge);
         Assert.That(GetExpungedSequences(expunge), Is.EqualTo(new[] {"3"}), expunge);

         simulator.Disconnect();
      }

      [Test]
      [Description("A client saving a draft replaces the old one with APPEND, UID STORE and UID EXPUNGE. " +
                   "A message another client flagged \\Deleted must survive this.")]
      public void ReplacingDraftDoesNotExpungeOtherDeletedMessages()
      {
         var account = AddAccount();

         var setup = ConnectAndLogon(account);
         Assert.That(setup.CreateFolder("MyDrafts"), Is.True);
         setup.Append("MyDrafts", CreateMessage("unrelated@example.test"));
         setup.Disconnect();

         var other = ConnectAndLogon(account);
         Assert.That(other.SelectFolder("MyDrafts"), Is.True);
         other.SendSingleCommand("A01 STORE 1 +FLAGS (\\Deleted)");

         var client = ConnectAndLogon(account);
         Assert.That(client.SelectFolder("MyDrafts"), Is.True);

         var oldUid = AssertAppendUid(client.Append("MyDrafts", CreateMessage("draft-1@example.test")));
         var newUid = AssertAppendUid(client.Append("MyDrafts", CreateMessage("draft-2@example.test")));

         client.SendSingleCommand("A02 UID STORE " + oldUid + " +FLAGS (\\Deleted)");
         var expunge = client.UidExpunge(oldUid.ToString());
         Assert.That(expunge.Contains("A42 OK"), Is.True, expunge);
         Assert.That(GetExpungedSequences(expunge), Is.EqualTo(new[] {"2"}), expunge);

         Assert.That(UidSearch(client, "ALL"), Is.EqualTo("1 " + newUid));

         other.NOOP();
         Assert.That(UidSearch(other, "ALL"), Is.EqualTo("1 " + newUid));

         client.Disconnect();
         other.Disconnect();
      }

      #endregion

      #region Helpers

      private Account AddAccount()
      {
         return SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "uidplus@example.test", Password);
      }

      private static ImapClientSimulator ConnectAndLogon(Account account)
      {
         var simulator = new ImapClientSimulator();
         Assert.That(simulator.ConnectAndLogon(account.Address, Password), Is.True);
         return simulator;
      }

      private IMAPFolderPermission CreatePublicFolder(string name, Account account, bool read = true)
      {
         var folder = _settings.PublicFolders.Add(name);
         folder.Save();

         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = account.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.set_Permission(eACLPermission.ePermissionRead, read);
         permission.set_Permission(eACLPermission.ePermissionInsert, true);
         permission.set_Permission(eACLPermission.ePermissionWriteDeleted, true);
         permission.Save();

         return permission;
      }

      private static string CreateMessage(string messageId, string body = "Body\r\n")
      {
         return "Message-ID: <" + messageId + ">\r\n" +
                "Subject: " + messageId + "\r\n" +
                "\r\n" +
                body;
      }

      private static void AppendMessages(ImapClientSimulator simulator, string folderName, int count)
      {
         for (var i = 0; i < count; i++)
         {
            var response = simulator.Append(folderName, CreateMessage(Guid.NewGuid() + "@example.test"));
            Assert.That(response.Contains("A40 OK"), Is.True, response);
         }
      }

      private static long AssertAppendUid(string response, long expectedUidValidity = -1)
      {
         var match = Regex.Match(response, @"A40 OK \[APPENDUID (\d+) (\d+)\]");
         Assert.That(match.Success, Is.True, "No APPENDUID in response. " + response);

         if (expectedUidValidity >= 0)
            Assert.That(long.Parse(match.Groups[1].Value), Is.EqualTo(expectedUidValidity), response);

         return long.Parse(match.Groups[2].Value);
      }

      private static long GetResponseCodeValue(string response, string name)
      {
         var match = Regex.Match(response, @"\[" + name + @" (\d+)\]");
         Assert.That(match.Success, Is.True, name + " not found. " + response);
         return long.Parse(match.Groups[1].Value);
      }

      private static long GetStatusValue(string response, string name)
      {
         var match = Regex.Match(response, name + @" (\d+)");
         Assert.That(match.Success, Is.True, name + " not found. " + response);
         return long.Parse(match.Groups[1].Value);
      }

      private static string[] GetExpungedSequences(string response)
      {
         return Regex.Matches(response, @"^\* (\d+) EXPUNGE", RegexOptions.Multiline)
            .Cast<Match>()
            .Select(match => match.Groups[1].Value)
            .ToArray();
      }

      private static string UidSearch(ImapClientSimulator simulator, string criteria)
      {
         var response = simulator.SendSingleCommand("A43 UID SEARCH " + criteria);
         Assert.That(response.Contains("A43 OK"), Is.True, response);

         var match = Regex.Match(response, @"^\* SEARCH ?(.*)\r$", RegexOptions.Multiline);
         Assert.That(match.Success, Is.True, response);

         return match.Groups[1].Value.Trim();
      }

      #endregion
   }
}
