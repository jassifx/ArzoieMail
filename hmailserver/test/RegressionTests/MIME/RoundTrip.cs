// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

using System.IO;
using System.Text.RegularExpressions;
using hMailServer;
using NUnit.Framework;
using RegressionTests.Infrastructure;
using RegressionTests.Shared;

namespace RegressionTests.MIME
{
   [TestFixture]
   public class RoundTrip : TestFixtureBase
   {
      private static void AddSetHeaderRule(Account account, string headerName, string headerValue)
      {
         var rule = account.Rules.Add();
         rule.Name = "Round-trip test rule";
         rule.Active = true;

         var criteria = rule.Criterias.Add();
         criteria.UsePredefined = true;
         criteria.PredefinedField = eRulePredefinedField.eFTMessageSize;
         criteria.MatchType = eRuleMatchType.eMTGreaterThan;
         criteria.MatchValue = "0";
         criteria.Save();

         var action = rule.Actions.Add();
         action.Type = eRuleActionType.eRASetHeaderValue;
         action.HeaderName = headerName;
         action.Value = headerValue;
         action.Save();

         rule.Save();
      }

      private static string ExtractHeaderValue(string message, string headerName)
      {
         // Normalize line endings, then match the header value including any
         // continuation (folded) lines.
         var normalized = message.Replace("\r\n", "\n");
         var match = Regex.Match(normalized,
            $@"(?m)^{Regex.Escape(headerName)}:[ \t]*((?:[^\n]|\n[ \t])+)",
            RegexOptions.IgnoreCase);
         return match.Success ? match.Groups[1].Value.TrimEnd() : null;
      }

      private static string ExtractBody(string message)
      {
         // Body starts after the blank line separating headers from body.
         var normalized = message.Replace("\r\n", "\n");
         var idx = normalized.IndexOf("\n\n");
         return idx >= 0 ? normalized.Substring(idx + 2) : string.Empty;
      }

      [Test]
      [Description("Adding a new header to a message must not modify existing header values.")]
      public void TestAddingHeaderPreservesExistingHeaders()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "roundtrip@example.test", "test");
         AddSetHeaderRule(account, "X-Test", "test-value");

         SmtpClientSimulator.StaticSendRaw(account.Address, account.Address, TestResources.MessageWithValidDkim);

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         var storedFilename = account.IMAPFolders.get_ItemByName("Inbox").Messages[0].Filename;
         var storedContent = File.ReadAllText(storedFilename);
         var originalContent = ToSmtpWireMessage(TestResources.MessageWithValidDkim);

         Assert.That(storedContent, Does.Contain("X-Test: test-value"),
            "The rule should have added the X-Test header");

         var originalDkim = ExtractHeaderValue(originalContent, "DKIM-Signature");
         var storedDkim = ExtractHeaderValue(storedContent, "DKIM-Signature");
         Assert.That(storedDkim,
            Is.EqualTo(originalDkim),
            "DKIM-Signature header value was modified during re-serialization");

         var originalFrom = ExtractHeaderValue(originalContent, "From");
         var storedFrom = ExtractHeaderValue(storedContent, "From");
         Assert.That(storedFrom,
            Is.EqualTo(originalFrom),
            "From header value was modified during re-serialization");
      }

      [Test]
      [Description("Adding a new header to a plain-text message must not modify the body content.")]
      public void TestAddingHeaderPreservesSimpleBody()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "roundtrip3@example.test", "test");
         AddSetHeaderRule(account, "X-Test", "test-value");

         SmtpClientSimulator.StaticSendRaw(account.Address, account.Address, TestResources.EmailWith_TextPlainBody_TextPlainContentType);

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         var storedFilename = account.IMAPFolders.get_ItemByName("Inbox").Messages[0].Filename;
         var storedContent = File.ReadAllText(storedFilename);
         var originalContent = TestResources.EmailWith_TextPlainBody_TextPlainContentType;

         Assert.That(storedContent, Does.Contain("X-Test: test-value"),
            "The rule should have added the X-Test header");

         var originalBody = ExtractBody(originalContent);
         var storedBody = ExtractBody(storedContent);
         Assert.That(storedBody,
            Is.EqualTo(originalBody),
            "Plain-text body content was modified during re-serialization");
      }

      [Test]
      [Description("Modifying an existing header via a rule must store the new value, not the original raw bytes.")]
      public void TestModifyingExistingHeaderAppliesNewValue()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "roundtrip4@example.test", "test");
         AddSetHeaderRule(account, "Subject", "modified-subject");

         SmtpClientSimulator.StaticSendRaw(account.Address, account.Address, TestResources.EmailWith_TextPlainBody_TextPlainContentType);

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         var storedFilename = account.IMAPFolders.get_ItemByName("Inbox").Messages[0].Filename;
         var storedContent = File.ReadAllText(storedFilename);
         var originalContent = TestResources.EmailWith_TextPlainBody_TextPlainContentType;

         var storedSubject = ExtractHeaderValue(storedContent, "Subject");
         Assert.That(storedSubject,
            Is.EqualTo("modified-subject"),
            "Subject header should contain the rule's new value, not the original raw bytes");

         var originalFrom = ExtractHeaderValue(originalContent, "From");
         var storedFrom = ExtractHeaderValue(storedContent, "From");
         Assert.That(storedFrom,
            Is.EqualTo(originalFrom),
            "Unmodified From header must not be re-encoded during re-serialization");

         var originalBody = ExtractBody(originalContent);
         var storedBody = ExtractBody(storedContent);
         Assert.That(storedBody,
            Is.EqualTo(originalBody),
            "Body must not be modified when only a header is changed");
      }

      [Test]
      [Description("Adding a new header to a message with no Content-Type must not modify the body content.")]
      public void TestAddingHeaderPreservesBodyWithNoContentType()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "roundtrip5@example.test", "test");
         AddSetHeaderRule(account, "X-Test", "test-value");

         SmtpClientSimulator.StaticSendRaw(account.Address, account.Address, TestResources.EmailWith_TextPlainBody_NoContentType);

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         var storedFilename = account.IMAPFolders.get_ItemByName("Inbox").Messages[0].Filename;
         var storedContent = File.ReadAllText(storedFilename);
         var originalContent = TestResources.EmailWith_TextPlainBody_NoContentType;

         Assert.That(storedContent, Does.Contain("X-Test: test-value"),
            "The rule should have added the X-Test header");

         var originalBody = ExtractBody(originalContent);
         var storedBody = ExtractBody(storedContent);
         Assert.That(storedBody,
            Is.EqualTo(originalBody),
            "Body content was modified during re-serialization of a message with no Content-Type");
      }

      [Test]
      [Description("Adding a new header to an HTML message must not modify the body content.")]
      public void TestAddingHeaderPreservesHtmlBody()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "roundtrip6@example.test", "test");
         AddSetHeaderRule(account, "X-Test", "test-value");

         SmtpClientSimulator.StaticSendRaw(account.Address, account.Address, TestResources.EmailWith_TextHtmlBody_TextHtmlContentType);

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         var storedFilename = account.IMAPFolders.get_ItemByName("Inbox").Messages[0].Filename;
         var storedContent = File.ReadAllText(storedFilename);
         var originalContent = TestResources.EmailWith_TextHtmlBody_TextHtmlContentType;

         Assert.That(storedContent, Does.Contain("X-Test: test-value"),
            "The rule should have added the X-Test header");

         var originalBody = ExtractBody(originalContent);
         var storedBody = ExtractBody(storedContent);
         Assert.That(storedBody,
            Is.EqualTo(originalBody),
            "HTML body content was modified during re-serialization");
      }

      [Test]
      [Description("Adding a new header to a multipart message must not modify the body content.")]
      public virtual void TestAddingHeaderPreservesMultipartBody()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "roundtrip2@example.test", "test");
         AddSetHeaderRule(account, "X-Test", "test-value");

         SmtpClientSimulator.StaticSendRaw(account.Address, account.Address, TestResources.MessageWithValidDkim);

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         var storedFilename = account.IMAPFolders.get_ItemByName("Inbox").Messages[0].Filename;
         var storedContent = File.ReadAllText(storedFilename);
         var originalContent = ToSmtpWireMessage(TestResources.MessageWithValidDkim);

         var originalContentType = ExtractHeaderValue(originalContent, "Content-Type");
         var storedContentType = ExtractHeaderValue(storedContent, "Content-Type");
         Assert.That(storedContentType,
            Is.EqualTo(originalContentType),
            "Content-Type header (including multipart boundary) was modified during re-serialization");

         var originalBody = ExtractBody(originalContent);
         var storedBody = ExtractBody(storedContent);
         Assert.That(storedBody,
            Is.EqualTo(originalBody),
            "Multipart body content was modified during re-serialization");
         Assert.That(storedContent, Does.EndWith("\r\n"),
            "Multipart message should retain the trailing CRLF after the closing boundary");
      }

      [Test]
      [Description("A multipart message fixture with no final CRLF must still be fetchable after round-tripping through rules.")]
      public void TestMultipartWithoutFinalFixtureCrlfCanBeParsedAfterRoundTrip()
      {
         var account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "roundtrip7@example.test", "test");
         AddSetHeaderRule(account, "X-Test", "test-value");

         SmtpClientSimulator.StaticSendRaw(account.Address, account.Address, TestResources.EmailWith_TextPlainBody_TextHtmlBody_TextHtmlAttachment);

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         var storedFilename = account.IMAPFolders.get_ItemByName("Inbox").Messages[0].Filename;
         var storedContent = File.ReadAllText(storedFilename);
         Assert.That(storedContent, Does.Contain("X-Test: test-value"),
            "The rule should have added the X-Test header before the message is parsed again");

         var imapSim = new ImapClientSimulator(account.Address, "test", "INBOX");
         var result = imapSim.Fetch("1 BODYSTRUCTURE");
         imapSim.Logout();

         Assert.That(result, Does.Contain("A17 OK FETCH completed"),
            "The round-tripped multipart message should still be parseable over IMAP");

         CustomAsserts.AssertNoReportedError();
      }

      private static string ToSmtpWireMessage(string message)
      {
         return message.EndsWith("\r\n") ? message : message + "\r\n";
      }
   }
}
