// Copyright (c) 2026 ArzoieMail contributors.
//
// Cases for the pure part of the Bayesian filter. Run from ClassTester in the server project and
// from the portable runner under the sanitizers.

#pragma once

#include "BayesFilter.h"

#include <algorithm>
#include <cmath>
#include <string>
#include <vector>

namespace HM
{
   class BayesFilterTests
   {
   public:
      static std::vector<std::string> Run()
      {
         using namespace Bayes;
         std::vector<std::string> failures;
         const auto check = [&](bool condition, const char *message) { if (!condition) failures.emplace_back(message); };
         const auto has = [](const std::vector<std::uint64_t> &tokens, const wchar_t *token)
         {
            return std::find(tokens.begin(), tokens.end(), HashToken(token)) != tokens.end();
         };

         check(HashToken(L"winner") == HashToken(L"winner"), "A token always hashes the same.");
         check(HashToken(L"winner") != HashToken(L"winners"), "Different tokens hash differently.");
         check(HashToken(L"") != TotalsToken && HashToken(L"winner") != TotalsToken, "No token hashes to the totals row.");

         {
            const auto tokens = Tokenize(L"Claim your PRIZE", L"<p>Hello <b>WINNER</b>, it is a big win</p>", L"Example.COM");
            check(has(tokens, L"subject:claim") && has(tokens, L"subject:prize"), "Subject words are tokens of their own, in lower case.");
            check(!has(tokens, L"claim"), "A subject word is not also a body word.");
            check(has(tokens, L"winner") && has(tokens, L"hello"), "Body words are lower-cased and kept across markup.");
            check(!has(tokens, L"b") && !has(tokens, L"p"), "Tag names are not words.");
            check(!has(tokens, L"it") && !has(tokens, L"is"), "Words shorter than three letters are dropped.");
            check(has(tokens, L"from:example.com"), "The sender domain is a lower-cased token.");
            std::vector<std::uint64_t> sorted(tokens);
            std::sort(sorted.begin(), sorted.end());
            check(std::adjacent_find(sorted.begin(), sorted.end()) == sorted.end(), "Each token appears once.");
         }
         {
            const auto tokens = Tokenize(L"", L"Visit https://Prize-Claim.example/now or http://other.example", L"");
            check(has(tokens, L"url:prize-claim.example") && has(tokens, L"url:other.example"), "Link host names are tokens.");
            check(!has(tokens, L"from:"), "No sender domain gives no sender token.");
         }
         {
            const auto tokens = Tokenize(L"", L"QWxhZGRpbjpvcGVuIHNlc2FtZQZZZZZZZZZZZZZZZZZZZZZZZZZ", L"");
            check(tokens.size() == 1 && has(tokens, L"long:q5"), "A long run counts only by its first letter and size.");
         }
         {
            std::wstring body;
            for (int index = 0; index < 500; ++index) body += L"word" + std::to_wstring(index) + L" ";
            check(Tokenize(L"", body, L"", 100).size() == 100, "No more than the maximum number of tokens is returned.");
            check(Tokenize(L"", L"", L"").empty(), "An empty message has no tokens.");
         }

         {
            const std::vector<std::uint64_t> spam{HashToken(L"lottery"), HashToken(L"prize"), HashToken(L"jackpot")};
            const std::vector<std::uint64_t> ham{HashToken(L"agenda"), HashToken(L"budget"), HashToken(L"meeting")};
            std::map<std::uint64_t, TokenCounts> counts;
            for (const auto token : spam) counts[token] = TokenCounts{20, 0};
            for (const auto token : ham) counts[token] = TokenCounts{0, 20};
            counts[HashToken(L"hello")] = TokenCounts{20, 20};

            check(SpamProbability(spam, counts, 0, 20) == 0.5 && SpamProbability(spam, counts, 20, 0) == 0.5, "Without both spam and ham there is no evidence.");
            check(SpamProbability({HashToken(L"unseen")}, counts, 20, 20) == 0.5, "Unseen tokens give no evidence.");
            check(SpamProbability({HashToken(L"hello")}, counts, 20, 20) == 0.5, "A token seen equally in both gives no evidence.");
            check(SpamProbability(spam, counts, 20, 20) > 0.9, "Spam tokens give a high spam probability.");
            check(SpamProbability(ham, counts, 20, 20) < 0.1, "Ham tokens give a low spam probability.");
            std::vector<std::uint64_t> mixed(spam);
            mixed.insert(mixed.end(), ham.begin(), ham.end());
            const double balanced = SpamProbability(mixed, counts, 20, 20);
            check(balanced > 0.3 && balanced < 0.7, "Equal spam and ham evidence is uncertain.");

            // Counts above the message totals, as after a reset raced with training, stay in range.
            counts[HashToken(L"lottery")] = TokenCounts{4000000000u, 0};
            const double bounded = SpamProbability(spam, counts, 1, 1);
            check(!std::isnan(bounded) && bounded >= 0.0 && bounded <= 1.0, "The probability is always between 0 and 1.");
         }

         check(ClassifyCopy(false, true, false) == CopyVerdict::Spam, "A copy into Junk is spam.");
         check(ClassifyCopy(true, true, false) == CopyVerdict::None, "A copy from Junk to Junk says nothing.");
         check(ClassifyCopy(true, false, false) == CopyVerdict::Ham, "A copy out of Junk is ham.");
         check(ClassifyCopy(true, false, true) == CopyVerdict::None, "A copy from Junk to Trash says nothing.");
         check(ClassifyCopy(false, false, false) == CopyVerdict::None && ClassifyCopy(false, false, true) == CopyVerdict::None,
            "Copies between other folders say nothing.");
         return failures;
      }
   };
}
