// Copyright (c) 2026 ArzoieMail contributors.

#include "stdafx.h"

#include "BayesFilter.h"

#include <algorithm>
#include <cmath>
#include <cwctype>
#include <set>

namespace HM
{
   namespace Bayes
   {
      namespace
      {
         // Robinson's strength of the prior and its assumed probability for a new token.
         const double PriorStrength = 1.0;
         const double PriorProbability = 0.5;
         // Tokens closer to 0.5 than this carry too little evidence to count.
         const double MinimumDeviation = 0.1;
         // At most this many of the most decisive tokens are combined.
         const size_t MaximumDecisive = 150;

         const size_t MinimumWord = 3;
         const size_t MaximumWord = 24;

         bool IsWordCharacter(wchar_t character)
         {
            return std::iswalnum(static_cast<wint_t>(character)) || character == L'$' || character == L'€' || character == L'£';
         }

         // The chi-square survival function for an even number of degrees of freedom.
         double ChiSquareSurvival(double chi, size_t degrees)
         {
            const double half = chi / 2.0;
            double term = std::exp(-half), sum = term;
            for (size_t index = 1; index < degrees / 2; ++index)
            {
               term *= half / static_cast<double>(index);
               sum += term;
            }
            return (std::min)(sum, 1.0);
         }

         void AddWords(const std::wstring &text, const std::wstring &prefix, std::vector<std::wstring> &tokens)
         {
            std::wstring word;
            const auto flush = [&]()
            {
               if (word.size() >= MinimumWord && word.size() <= MaximumWord) tokens.push_back(prefix + word);
               // Long runs (encoded data, tracking strings) count only by their first letter and size.
               else if (word.size() > MaximumWord) tokens.push_back(prefix + L"long:" + word.substr(0, 1) + std::to_wstring(word.size() / 10));
               word.clear();
            };
            for (const wchar_t character : text)
            {
               if (IsWordCharacter(character)) word += static_cast<wchar_t>(std::towlower(static_cast<wint_t>(character)));
               else flush();
            }
            flush();
         }

         // The host of every http(s) link in the text, as "url:host".
         void AddLinkHosts(const std::wstring &text, std::vector<std::wstring> &tokens)
         {
            std::wstring lower(text);
            std::transform(lower.begin(), lower.end(), lower.begin(), [](wchar_t c) { return static_cast<wchar_t>(std::towlower(static_cast<wint_t>(c))); });
            for (size_t position = lower.find(L"http"); position != std::wstring::npos; position = lower.find(L"http", position + 4))
            {
               size_t start = position + 4;
               if (start < lower.size() && lower[start] == L's') ++start;
               if (lower.compare(start, 3, L"://") != 0) continue;
               start += 3;
               size_t end = start;
               while (end < lower.size() && (std::iswalnum(static_cast<wint_t>(lower[end])) || lower[end] == L'.' || lower[end] == L'-')) ++end;
               if (end > start && end - start <= 253) tokens.push_back(L"url:" + lower.substr(start, end - start));
            }
         }

         // Markup is replaced by spaces, so neighbouring words do not merge.
         std::wstring WithoutTags(const std::wstring &text)
         {
            std::wstring result;
            result.reserve(text.size());
            bool inTag = false;
            for (const wchar_t character : text)
            {
               if (character == L'<') { inTag = true; result += L' '; }
               else if (character == L'>' && inTag) inTag = false;
               else if (!inTag) result += character;
            }
            return result;
         }
      }

      std::uint64_t
      HashToken(const std::wstring &token)
      {
         std::uint64_t hash = 14695981039346656037ULL;
         for (const wchar_t character : token)
         {
            // Hash the low 32 bits of each code unit, so the result is the same where
            // wchar_t is 16 bits (Windows) and where it is 32 bits.
            std::uint32_t unit = static_cast<std::uint32_t>(character);
            for (int shift = 0; shift < 32; shift += 8)
            {
               hash ^= (unit >> shift) & 0xff;
               hash *= 1099511628211ULL;
            }
         }
         return hash == TotalsToken ? 1 : hash;
      }

      std::vector<std::uint64_t>
      Tokenize(const std::wstring &subject, const std::wstring &body, const std::wstring &senderDomain, size_t maxTokens)
      {
         std::vector<std::wstring> words;
         AddWords(subject, L"subject:", words);
         AddLinkHosts(body, words);
         AddWords(WithoutTags(body), L"", words);
         if (!senderDomain.empty())
         {
            std::wstring domain(senderDomain);
            std::transform(domain.begin(), domain.end(), domain.begin(), [](wchar_t c) { return static_cast<wchar_t>(std::towlower(static_cast<wint_t>(c))); });
            words.push_back(L"from:" + domain);
         }

         std::vector<std::uint64_t> hashes;
         std::set<std::uint64_t> seen;
         for (const auto &word : words)
         {
            if (hashes.size() >= maxTokens) break;
            const auto hash = HashToken(word);
            if (seen.insert(hash).second) hashes.push_back(hash);
         }
         return hashes;
      }

      double
      SpamProbability(const std::vector<std::uint64_t> &tokens, const std::map<std::uint64_t, TokenCounts> &counts,
         std::uint32_t spamMessages, std::uint32_t hamMessages)
      {
         if (spamMessages == 0 || hamMessages == 0) return 0.5;

         std::vector<double> probabilities;
         for (const auto token : tokens)
         {
            const auto found = counts.find(token);
            if (found == counts.end()) continue;
            const double spam = (std::min)(static_cast<double>(found->second.spam), static_cast<double>(spamMessages));
            const double ham = (std::min)(static_cast<double>(found->second.ham), static_cast<double>(hamMessages));
            if (spam + ham == 0) continue;
            const double spamRatio = spam / spamMessages, hamRatio = ham / hamMessages;
            const double probability = spamRatio / (spamRatio + hamRatio);
            const double seen = spam + ham;
            const double adjusted = (PriorStrength * PriorProbability + seen * probability) / (PriorStrength + seen);
            if (std::fabs(adjusted - 0.5) >= MinimumDeviation) probabilities.push_back(adjusted);
         }
         if (probabilities.empty()) return 0.5;

         std::sort(probabilities.begin(), probabilities.end(), [](double left, double right) { return std::fabs(left - 0.5) > std::fabs(right - 0.5); });
         if (probabilities.size() > MaximumDecisive) probabilities.resize(MaximumDecisive);

         double spamLog = 0, hamLog = 0;
         for (const double probability : probabilities)
         {
            const double bounded = (std::min)((std::max)(probability, 1e-6), 1.0 - 1e-6);
            spamLog += std::log(1.0 - bounded);
            hamLog += std::log(bounded);
         }
         const size_t degrees = 2 * probabilities.size();
         const double spamness = 1.0 - ChiSquareSurvival(-2.0 * spamLog, degrees);
         const double hamness = 1.0 - ChiSquareSurvival(-2.0 * hamLog, degrees);
         return (spamness - hamness + 1.0) / 2.0;
      }
   }
}
