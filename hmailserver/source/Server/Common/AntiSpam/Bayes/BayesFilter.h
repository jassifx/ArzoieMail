// Copyright (c) 2026 ArzoieMail contributors.
//
// The pure part of the native Bayesian spam filter: turning a message into tokens and
// combining per-token evidence into a spam probability. It has no Windows, database or
// message-store dependency, so the portable build compiles and tests it as well.
//
// Tokens are stored as 64-bit hashes, never as text, so the token table holds no message
// content. The probability follows Gary Robinson's method with Fisher's chi-square
// combining, as SpamBayes and SpamAssassin's Bayes plugin do.

#pragma once

#include <cstdint>
#include <map>
#include <string>
#include <vector>

namespace HM
{
   namespace Bayes
   {
      // The hash under which the store keeps the trained message totals. Token hashes are
      // never this value.
      const std::uint64_t TotalsToken = 0;

      // FNV-1a over the UTF-16/UTF-32 code units of the token; never TotalsToken.
      std::uint64_t HashToken(const std::wstring &token);

      // The distinct tokens of a message, as hashes, at most maxTokens of them. Words come
      // from the subject (prefixed so they count separately) and the body with HTML tags
      // removed. The sender domain and the host names of links become tokens of their own.
      std::vector<std::uint64_t> Tokenize(const std::wstring &subject, const std::wstring &body,
         const std::wstring &senderDomain, size_t maxTokens = 2000);

      struct TokenCounts
      {
         std::uint32_t spam = 0;
         std::uint32_t ham = 0;
      };

      // The probability, 0 to 1, that a message with these tokens is spam, given how often
      // each token was seen in spamMessages trained spam and hamMessages trained ham.
      // 0.5 means there is no evidence either way.
      double SpamProbability(const std::vector<std::uint64_t> &tokens, const std::map<std::uint64_t, TokenCounts> &counts,
         std::uint32_t spamMessages, std::uint32_t hamMessages);
   }
}
