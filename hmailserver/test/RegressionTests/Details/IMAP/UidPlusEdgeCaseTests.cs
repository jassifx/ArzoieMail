// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using hMailServer;
using NUnit.Framework;
using RegressionTests.Infrastructure;
using RegressionTests.Shared;
using static RegressionTests.Shared.UidPlusHelpers;

namespace RegressionTests.Details.IMAP
{
   /// <summary>
   ///    Edge cases for UIDPLUS, RFC 4315. Explicit, since they go beyond what the regression
   ///    suite needs. The fixed gaps are covered by IMAP.UidPlusCompliance.
   /// </summary>
   [TestFixture]
   [Explicit("Detail test - run manually.")]
   [Category("Details")]
   public class UidPlusEdgeCaseTests : TestFixtureBase
   {
      private Account _account;

      [SetUp]
      public void CreateAccount()
      {
         _account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "uidplus@example.test", Password);
      }

      private TrackedImapSession Connect(string name = "session")
      {
         return new TrackedImapSession(_account.Address, Password, name);
      }

      private static List<string> AppendMessages(TrackedImapSession session, string folder, int count, string flags = null)
      {
         var ids = new List<string>();

         for (var i = 0; i < count; i++)
         {
            var id = Guid.NewGuid() + "@example.test";
            var response = session.Append(folder, CreateMessage(id), flags);
            AssertAppendUid(response);
            ids.Add(id);
         }

         return ids;
      }

      #region APPENDUID

      [Test]
      [Description("APPEND to the selected folder reports EXISTS, and the APPENDUID is usable at once.")]
      public void AppendToSelectedFolderReportsExistsAndUidIsUsable()
      {
         var session = Connect();
         AppendMessages(session, "INBOX", 2);
         session.Select("INBOX");

         var id = Guid.NewGuid() + "@example.test";
         var append = session.Append("INBOX", CreateMessage(id));
         var uid = AssertAppendUid(append);

         Assert.That(append, Does.Contain("* 3 EXISTS"));
         Assert.That(session.KnownCount, Is.EqualTo(3));

         Assert.That(FetchMessageIds(session, uid.ToString())[uid], Is.EqualTo(id));

         session.AssertNoViolations();
         session.Disconnect();
      }

      [TestCase("INBOX")]
      [TestCase("inbox")]
      [TestCase("Parent.Child")]
      [TestCase("Space Folder")]
      [Description("APPENDUID, SELECT, EXAMINE and STATUS must all report the same UIDVALIDITY.")]
      public void UidValidityIsConsistentAcrossCommands(string folder)
      {
         var session = Connect();

         if (!folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
            Assert.That(TrackedImapSession.IsOk(session.Command("CREATE \"" + folder + "\"")), Is.True);

         var appendValidity = long.Parse(ParseAppendUid(session.Append(folder, CreateMessage("a@example.test"))).Groups[1].Value);
         var statusValidity = GetStatusValue(session.Command("STATUS \"" + folder + "\" (UIDVALIDITY)"), "UIDVALIDITY");
         var selectValidity = GetResponseCodeValue(session.Select(folder), "UIDVALIDITY");
         var examineValidity = GetResponseCodeValue(session.Select(folder, true), "UIDVALIDITY");

         Assert.That(statusValidity, Is.EqualTo(appendValidity));
         Assert.That(selectValidity, Is.EqualTo(appendValidity));
         Assert.That(examineValidity, Is.EqualTo(appendValidity));
         Assert.That(appendValidity, Is.GreaterThan(0));

         session.Disconnect();
      }

      [Test]
      [Description("UIDs are strictly ascending. Expunging the highest message must not free its UID.")]
      public void AppendAfterExpungingHighestDoesNotReuseUid()
      {
         var session = Connect();
         AppendMessages(session, "INBOX", 2);
         session.Select("INBOX");

         var uid = AssertAppendUid(session.Append("INBOX", CreateMessage("x@example.test"), "\\Deleted"));
         Assert.That(TrackedImapSession.IsOk(session.Command("UID EXPUNGE " + uid)), Is.True);

         var next = AssertAppendUid(session.Append("INBOX", CreateMessage("y@example.test")));
         Assert.That(next, Is.GreaterThan(uid));

         session.AssertNoViolations();
         session.Disconnect();
      }

      [Test]
      [Description("A folder deleted and re-created, even within the same second, gets a higher UIDVALIDITY " +
                   "(RFC 3501 2.3.1.1). Otherwise a client caching APPENDUID results maps the new UID 1 to the old message.")]
      public void RecreatedFolderGetsNewUidValidityOrHigherUids()
      {
         var session = Connect();

         // Retry so the delete and re-create land in the same second at least once.
         for (var attempt = 0; attempt < 10; attempt++)
         {
            Assert.That(TrackedImapSession.IsOk(session.Command("CREATE \"Recreated\"")), Is.True);
            var first = ParseAppendUid(session.Append("Recreated", CreateMessage("old@example.test")));
            Assert.That(TrackedImapSession.IsOk(session.Command("DELETE \"Recreated\"")), Is.True);

            Assert.That(TrackedImapSession.IsOk(session.Command("CREATE \"Recreated\"")), Is.True);
            var second = ParseAppendUid(session.Append("Recreated", CreateMessage("new@example.test")));
            Assert.That(TrackedImapSession.IsOk(session.Command("DELETE \"Recreated\"")), Is.True);

            var firstValidity = long.Parse(first.Groups[1].Value);
            var secondValidity = long.Parse(second.Groups[1].Value);
            var firstUid = long.Parse(first.Groups[2].Value);
            var secondUid = long.Parse(second.Groups[2].Value);

            Assert.That(secondValidity > firstValidity || secondUid > firstUid,
               Is.True,
               string.Format("Attempt {0}: UIDVALIDITY {1} reused and UID {2} re-issued.", attempt, firstValidity, secondUid));
         }

         session.Disconnect();
      }

      [Test]
      public void AppendToMissingFolderHasTryCreateAndNoAppendUid()
      {
         var session = Connect();
         var append = session.Append("DoesNotExist", CreateMessage("a@example.test"));

         Assert.That(TrackedImapSession.IsNo(append), Is.True, append);
         Assert.That(append, Does.Contain("[TRYCREATE]"));
         Assert.That(append, Does.Not.Contain("APPENDUID"));

         session.Disconnect();
      }

      [Test]
      public void AppendOverQuotaHasNoAppendUid()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "quota@example.test", Password, 1);
         var session = new TrackedImapSession(account.Address, Password);

         var body = string.Concat(Enumerable.Repeat(new string('x', 98) + "\r\n", 12000));
         var append = session.Append("INBOX", CreateMessage("big@example.test", body));

         Assert.That(TrackedImapSession.IsNo(append), Is.True, append);
         Assert.That(append, Does.Not.Contain("APPENDUID"));

         session.Disconnect();
      }

      [Test]
      [Description("A draft replaced from a single session: APPEND with \\Deleted, then UID EXPUNGE of the old one.")]
      public void AppendWithDeletedFlagCanBeUidExpungedFromAnotherSession()
      {
         var observer = Connect("observer");
         AppendMessages(observer, "INBOX", 1);
         observer.Select("INBOX");

         var appender = Connect("appender");
         var uid = AssertAppendUid(appender.Append("INBOX", CreateMessage("draft@example.test"), "\\Deleted"));
         appender.Disconnect();

         // The observer learns about the message and then expunges it by UID, without a NOOP first.
         var expunge = observer.Command("UID EXPUNGE " + uid);
         Assert.That(TrackedImapSession.IsOk(expunge), Is.True, expunge);

         observer.Command("NOOP");
         Assert.That(UidSearchAll(observer), Is.EqualTo(new[] { 1L }).AsCollection);

         observer.AssertNoViolations();
         observer.Disconnect();
      }

      #endregion

      #region COPYUID

      [Test]
      [Description("COPYUID source and destination correspond element by element, in the order the set gives.")]
      public void CopyUidCorrespondsForUnorderedSet()
      {
         var session = Connect();
         Assert.That(TrackedImapSession.IsOk(session.Command("CREATE \"Target\"")), Is.True);
         var ids = AppendMessages(session, "INBOX", 5);
         session.Select("INBOX");

         var copy = session.Command("UID COPY 5,1,3 \"Target\"");
         var copyUid = ParseCopyUid(copy);
         Assert.That(copyUid, Is.Not.Null, copy);
         Assert.That(copyUid.Source.Count, Is.EqualTo(3), copy);
         Assert.That(copyUid.Destination.Count, Is.EqualTo(3), copy);

         session.Select("Target");
         var destinationIds = FetchMessageIds(session, "1:*");

         for (var i = 0; i < copyUid.Source.Count; i++)
            Assert.That(destinationIds[copyUid.Destination[i]], Is.EqualTo(ids[(int) copyUid.Source[i] - 1]), copy);

         session.Disconnect();
      }

      [TestCase("3:1")]
      [TestCase("*:1")]
      [TestCase("1:*")]
      public void CopyUidWithRangeInEitherOrder(string set)
      {
         var session = Connect();
         Assert.That(TrackedImapSession.IsOk(session.Command("CREATE \"Target\"")), Is.True);
         AppendMessages(session, "INBOX", 3);
         session.Select("INBOX");

         var copy = session.Command("UID COPY " + set + " \"Target\"");
         Assert.That(copy, Does.Match(@"OK \[COPYUID \d+ 1:3 1:3\]"));

         session.Disconnect();
      }

      [Test]
      [Description("UID COPY skips a message another session expunged (RFC 3501 6.4.8). COPYUID lists only the copies.")]
      public void UidCopySkipsMessageExpungedElsewhere()
      {
         var session = Connect("copier");
         Assert.That(TrackedImapSession.IsOk(session.Command("CREATE \"Target\"")), Is.True);
         AppendMessages(session, "INBOX", 3);
         session.Select("INBOX");

         var other = Connect("other");
         other.Select("INBOX");
         other.Command("UID STORE 2 +FLAGS (\\Deleted)");
         Assert.That(TrackedImapSession.IsOk(other.Command("UID EXPUNGE 2")), Is.True);
         other.Disconnect();

         var copy = session.Command("UID COPY 1:3 \"Target\"");
         Assert.That(TrackedImapSession.IsOk(copy), Is.True, copy);

         var copyUid = ParseCopyUid(copy);
         Assert.That(copyUid, Is.Not.Null, copy);
         Assert.That(copyUid.Source, Is.EqualTo(new[] { 1L, 3L }).AsCollection, copy);
         Assert.That(copyUid.Destination.Count, Is.EqualTo(2), copy);

         session.AssertNoViolations();
         session.Disconnect();
      }

      [Test]
      [Description("COPY by sequence number of a message expunged elsewhere fails, and copies nothing.")]
      public void CopyBySequenceOfMessageExpungedElsewhereCopiesNothing()
      {
         var session = Connect("copier");
         Assert.That(TrackedImapSession.IsOk(session.Command("CREATE \"Target\"")), Is.True);
         AppendMessages(session, "INBOX", 3);
         session.Select("INBOX");

         var other = Connect("other");
         other.Select("INBOX");
         other.Command("UID STORE 2 +FLAGS (\\Deleted)");
         Assert.That(TrackedImapSession.IsOk(other.Command("UID EXPUNGE 2")), Is.True);
         other.Disconnect();

         var copy = session.Command("COPY 1:3 \"Target\"");
         Assert.That(TrackedImapSession.IsNo(copy), Is.True, copy);
         Assert.That(copy, Does.Not.Contain("COPYUID"));

         Assert.That(GetStatusValue(session.Command("STATUS \"Target\" (MESSAGES)"), "MESSAGES"), Is.EqualTo(0));

         session.AssertNoViolations();
         session.Disconnect();
      }

      [Test]
      [Description("Missing Insert permission should give NO, not BAD. BAD means a syntax error.")]
      public void CopyWithoutInsertPermissionIsNo()
      {
         var folder = _settings.PublicFolders.Add("ReadOnlyShare");
         folder.Save();
         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = _account.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.set_Permission(eACLPermission.ePermissionRead, true);
         permission.Save();

         var session = Connect();
         AppendMessages(session, "INBOX", 1);
         session.Select("INBOX");

         var copy = session.Command("UID COPY 1 \"#Public.ReadOnlyShare\"");
         Assert.That(TrackedImapSession.IsNo(copy), Is.True, copy);
         Assert.That(copy, Does.Not.Contain("COPYUID"));

         session.Disconnect();
      }

      [Test]
      [Description("A folder with many gaps between the UIDs gives a long, non-contiguous COPYUID source set.")]
      public void CopyUidWithManyGaps()
      {
         const int count = 200;

         var session = Connect();
         Assert.That(TrackedImapSession.IsOk(session.Command("CREATE \"Target\"")), Is.True);
         var ids = AppendMessages(session, "INBOX", count);
         session.Select("INBOX");

         // Expunge every even UID.
         var even = string.Join(",", Enumerable.Range(1, count / 2).Select(i => (i * 2).ToString()));
         session.Command("UID STORE " + even + " +FLAGS (\\Deleted)");
         Assert.That(TrackedImapSession.IsOk(session.Command("UID EXPUNGE 1:*")), Is.True);
         Assert.That(session.KnownCount, Is.EqualTo(count / 2));

         var copy = session.Command("UID COPY 1:* \"Target\"");
         var copyUid = ParseCopyUid(copy);
         Assert.That(copyUid, Is.Not.Null, copy);

         Assert.That(copyUid.Source, Is.EqualTo(Enumerable.Range(0, count / 2).Select(i => (long)(i * 2 + 1))).AsCollection);
         Assert.That(copy, Does.Contain(" 1:" + count / 2 + "]"));

         session.Select("Target");
         var destinationIds = FetchMessageIds(session, "1:*");
         for (var i = 0; i < copyUid.Source.Count; i++)
            Assert.That(destinationIds[copyUid.Destination[i]], Is.EqualTo(ids[(int) copyUid.Source[i] - 1]));

         session.AssertNoViolations();
         session.Disconnect();
      }

      [Test]
      [Description("Destination UIDs continue above the highest UID ever used, even after the folder was emptied.")]
      public void CopyUidDestinationContinuesAfterEmptiedFolder()
      {
         var session = Connect();
         Assert.That(TrackedImapSession.IsOk(session.Command("CREATE \"Target\"")), Is.True);
         AppendMessages(session, "Target", 3, "\\Deleted");
         session.Select("Target");
         Assert.That(TrackedImapSession.IsOk(session.Command("UID EXPUNGE 1:*")), Is.True);
         Assert.That(session.KnownCount, Is.EqualTo(0));

         AppendMessages(session, "INBOX", 1);
         session.Select("INBOX");
         var copy = session.Command("UID COPY 1 \"Target\"");
         Assert.That(copy, Does.Match(@"OK \[COPYUID \d+ 1 4\]"));

         session.Disconnect();
      }

      [Test]
      [Description("COPY from an EXAMINEd folder is allowed and reports COPYUID.")]
      public void CopyFromExaminedFolderReturnsCopyUid()
      {
         var session = Connect();
         Assert.That(TrackedImapSession.IsOk(session.Command("CREATE \"Target\"")), Is.True);
         AppendMessages(session, "INBOX", 2);
         session.Select("INBOX", true);

         var copy = session.Command("UID COPY 1:2 \"Target\"");
         Assert.That(copy, Does.Match(@"OK \[COPYUID \d+ 1:2 1:2\]"));

         session.Disconnect();
      }

      [Test]
      [Description("A destination folder name given as a literal.")]
      public void CopyToFolderNamedByLiteralReturnsCopyUid()
      {
         var session = Connect();
         Assert.That(TrackedImapSession.IsOk(session.Command("CREATE \"Target\"")), Is.True);
         AppendMessages(session, "INBOX", 1);
         session.Select("INBOX");

         var copy = session.Simulator.SendSingleCommandWithLiteral("L01 UID COPY 1 {6}", "Target");
         Assert.That(copy, Does.Match(@"L01 OK \[COPYUID \d+ 1 1\]"));

         session.Disconnect();
      }

      [Test]
      [Description("A failed COPY removes its copies again. A session watching the destination must see a " +
                   "consistent stream and end up with nothing.")]
      public void FailedCopyIsConsistentForObserverOfDestination()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "quota@example.test", Password, 1);

         var copier = new TrackedImapSession(account.Address, Password, "copier");
         Assert.That(TrackedImapSession.IsOk(copier.Command("CREATE \"Target\"")), Is.True);

         var body = string.Concat(Enumerable.Repeat(new string('x', 98) + "\r\n", 3072));
         for (var i = 0; i < 2; i++)
            AssertAppendUid(copier.Append("INBOX", CreateMessage(Guid.NewGuid() + "@example.test", body)));

         var observer = new TrackedImapSession(account.Address, Password, "observer");
         observer.Select("Target");

         copier.Select("INBOX");
         var copy = copier.Command("COPY 1:2 \"Target\"");
         Assert.That(TrackedImapSession.IsNo(copy), Is.True, copy);

         observer.Command("NOOP");
         observer.Command("NOOP");
         Assert.That(observer.KnownCount, Is.EqualTo(0));
         Assert.That(UidSearchAll(observer), Is.Empty);

         observer.AssertNoViolations();
         copier.AssertNoViolations();
         observer.Disconnect();
         copier.Disconnect();
      }

      #endregion

      #region UID EXPUNGE

      [Test]
      [Description("Several messages expunged by one UID EXPUNGE. Each EXPUNGE accounts for the ones before it.")]
      public void UidExpungeOfSeveralMessagesAdjustsSequenceNumbers()
      {
         var session = Connect();
         AppendMessages(session, "INBOX", 5);
         session.Select("INBOX");

         session.Command("UID STORE 2,3,5 +FLAGS (\\Deleted)");
         var expunge = session.Command("UID EXPUNGE 1:*");

         Assert.That(GetExpungedSequences(expunge), Is.EqualTo(new[] { 2, 2, 3 }).AsCollection, expunge);
         Assert.That(UidSearchAll(session), Is.EqualTo(new[] { 1L, 4L }).AsCollection);

         session.AssertNoViolations();
         session.Disconnect();
      }

      [Test]
      [Description("Messages appended elsewhere with \\Deleted and not yet reported must get EXISTS before EXPUNGE.")]
      public void UidExpungeOfUnreportedMessageSendsExistsFirst()
      {
         var session = Connect("expunger");
         AppendMessages(session, "INBOX", 2);
         session.Select("INBOX");

         var other = Connect("other");
         var uid = AssertAppendUid(other.Append("INBOX", CreateMessage("new@example.test"), "\\Deleted"));
         other.Disconnect();

         var expunge = session.Command("UID EXPUNGE " + uid);
         Assert.That(TrackedImapSession.IsOk(expunge), Is.True, expunge);

         session.AssertNoViolations();
         Assert.That(UidSearchAll(session), Is.EqualTo(new[] { 1L, 2L }).AsCollection);
         session.Disconnect();
      }

      [Test]
      [Description("A set naming messages already expunged elsewhere. The session is told about those too.")]
      public void UidExpungeWithMessagesExpungedElsewhere()
      {
         var session = Connect("expunger");
         AppendMessages(session, "INBOX", 4);
         session.Select("INBOX");
         session.Command("UID STORE 1:4 +FLAGS (\\Deleted)");

         var other = Connect("other");
         other.Select("INBOX");
         Assert.That(TrackedImapSession.IsOk(other.Command("UID EXPUNGE 2")), Is.True);
         other.Disconnect();

         var expunge = session.Command("UID EXPUNGE 1:3");
         Assert.That(TrackedImapSession.IsOk(expunge), Is.True, expunge);

         session.Command("NOOP");
         Assert.That(session.KnownCount, Is.EqualTo(1));
         Assert.That(UidSearchAll(session), Is.EqualTo(new[] { 4L }).AsCollection);

         session.AssertNoViolations();
         session.Disconnect();
      }

      [Test]
      public void UidExpungeIsCaseInsensitive()
      {
         var session = Connect();
         AppendMessages(session, "INBOX", 1, "\\Deleted");
         session.Select("INBOX");

         var expunge = session.Command("uid expunge 1");
         Assert.That(GetExpungedSequences(expunge), Is.EqualTo(new[] { 1 }).AsCollection, expunge);

         session.Disconnect();
      }

      [Test]
      public void UidExpungeWithoutSelectedFolderIsRefused()
      {
         var session = Connect();
         var expunge = session.Command("UID EXPUNGE 1");
         Assert.That(TrackedImapSession.IsNo(expunge) || TrackedImapSession.IsBad(expunge), Is.True, expunge);

         AppendMessages(session, "INBOX", 1, "\\Deleted");
         session.Select("INBOX");
         Assert.That(TrackedImapSession.IsOk(session.Command("CLOSE")), Is.True);

         expunge = session.Command("UID EXPUNGE 1");
         Assert.That(TrackedImapSession.IsOk(expunge), Is.False, expunge);

         session.Disconnect();
      }

      [TestCase("1, 2")]
      [TestCase("1 2")]
      [TestCase("1:2:3")]
      [TestCase("4294967296")]
      [TestCase("-1")]
      [TestCase("1,")]
      [TestCase("*:")]
      [Description("Malformed sets are rejected, and nothing is expunged.")]
      public void UidExpungeWithMalformedSetExpungesNothing(string set)
      {
         var session = Connect();
         AppendMessages(session, "INBOX", 2, "\\Deleted");
         session.Select("INBOX");

         var expunge = session.Command("UID EXPUNGE " + set);
         Assert.That(TrackedImapSession.IsBad(expunge), Is.True, expunge);
         Assert.That(GetExpungedSequences(expunge), Is.Empty, expunge);
         Assert.That(UidSearchAll(session), Is.EqualTo(new[] { 1L, 2L }).AsCollection);

         session.Disconnect();
      }

      [TestCase("1,1", new[] {1})]
      [TestCase("2:1", new[] {1, 1})]
      [TestCase("*:1", new[] {1, 1})]
      [TestCase("4294967295", new int[0])]
      [TestCase("3:4294967295", new int[0])]
      public void UidExpungeWithUnusualValidSets(string set, int[] expected)
      {
         var session = Connect();
         AppendMessages(session, "INBOX", 2, "\\Deleted");
         session.Select("INBOX");

         var expunge = session.Command("UID EXPUNGE " + set);
         Assert.That(TrackedImapSession.IsOk(expunge), Is.True, expunge);
         Assert.That(GetExpungedSequences(expunge), Is.EqualTo(expected).AsCollection, expunge);

         session.AssertNoViolations();
         session.Disconnect();
      }

      [Test]
      [Description("Missing Expunge permission should give NO, not BAD.")]
      public void UidExpungeWithoutPermissionIsNo()
      {
         var folder = _settings.PublicFolders.Add("Share");
         folder.Save();
         var permission = folder.Permissions.Add();
         permission.PermissionAccountID = _account.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.set_Permission(eACLPermission.ePermissionLookup, true);
         permission.set_Permission(eACLPermission.ePermissionRead, true);
         permission.set_Permission(eACLPermission.ePermissionInsert, true);
         permission.set_Permission(eACLPermission.ePermissionWriteDeleted, true);
         permission.Save();

         var session = Connect();
         AppendMessages(session, "#Public.Share", 1, "\\Deleted");
         session.Select("#Public.Share");

         var expunge = session.Command("UID EXPUNGE 1");
         Assert.That(TrackedImapSession.IsNo(expunge), Is.True, expunge);

         session.Disconnect();
      }

      [Test]
      [Description("An IDLE session sees UID EXPUNGE from another session straight away, with valid numbers.")]
      public void UidExpungeIsReportedToIdleSession()
      {
         var setup = Connect("setup");
         AppendMessages(setup, "INBOX", 3);
         setup.Disconnect();

         var idler = new ImapClientSimulator();
         Assert.That(idler.ConnectAndLogon(_account.Address, Password), Is.True);
         Assert.That(idler.SelectFolder("INBOX"), Is.True);
         Assert.That(idler.StartIdle(), Is.True);

         var expunger = Connect("expunger");
         expunger.Select("INBOX");
         expunger.Command("UID STORE 1,3 +FLAGS (\\Deleted)");
         Assert.That(TrackedImapSession.IsOk(expunger.Command("UID EXPUNGE 3")), Is.True);

         Assert.That(idler.AssertPendingDataExists(), Is.True, "IDLE session was not notified.");

         string output;
         Assert.That(idler.EndIdle(false, out output), Is.True, output);
         Assert.That(GetExpungedSequences(output), Is.EqualTo(new[] { 3 }).AsCollection, output);
         Assert.That(output, Does.Contain("* 2 EXISTS"));

         expunger.Disconnect();
         idler.Disconnect();
      }

      [Test]
      [Description("A long UID set with many parts on a large folder.")]
      public void UidExpungeWithLongSet()
      {
         const int count = 500;

         var session = Connect();
         AppendMessages(session, "INBOX", count, "\\Deleted");
         session.Select("INBOX");

         // Every third UID, with some UIDs that don't exist, written out one by one.
         var uids = Enumerable.Range(1, count + 100).Where(i => i % 3 == 0).ToList();
         var expunge = session.Command("UID EXPUNGE " + string.Join(",", uids));
         Assert.That(TrackedImapSession.IsOk(expunge), Is.True, expunge);

         var expected = uids.Count(uid => uid <= count);
         Assert.That(GetExpungedSequences(expunge).Length, Is.EqualTo(expected));
         Assert.That(session.KnownCount, Is.EqualTo(count - expected));

         session.AssertNoViolations();
         session.Disconnect();
      }

      [Test]
      [Description("UID EXPUNGE must not touch \\Deleted messages outside the set, even ones other sessions flagged.")]
      public void UidExpungeLeavesOtherDeletedMessages()
      {
         var session = Connect("expunger");
         AppendMessages(session, "INBOX", 4);
         session.Select("INBOX");

         var other = Connect("other");
         other.Select("INBOX");
         other.Command("UID STORE 1:4 +FLAGS (\\Deleted)");

         var expunge = session.Command("UID EXPUNGE 2");
         Assert.That(GetExpungedSequences(expunge), Is.EqualTo(new[] { 2 }).AsCollection, expunge);

         other.Command("NOOP");
         Assert.That(UidSearchAll(other), Is.EqualTo(new[] { 1L, 3L, 4L }).AsCollection);

         session.AssertNoViolations();
         other.AssertNoViolations();
         session.Disconnect();
         other.Disconnect();
      }

      #endregion
   }
}
