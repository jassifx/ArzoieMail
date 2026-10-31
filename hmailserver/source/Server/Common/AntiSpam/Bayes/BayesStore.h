// Copyright (c) 2026 ArzoieMail contributors.
//
// The token table of the native Bayesian filter, hm_bayes_tokens. Each row holds a token hash
// and how many trained spam and ham messages contained it. The row with hash TotalsToken
// holds the number of trained spam and ham messages.

#pragma once

#include "BayesFilter.h"

namespace HM
{
   class BayesStore
   {
   public:

      struct Statistics
      {
         __int64 spamMessages = 0;
         __int64 hamMessages = 0;
         __int64 tokens = 0;
      };

      // The counts of those of tokens that have been seen, and the trained message totals.
      static bool LoadCounts(const std::vector<std::uint64_t> &tokens, std::map<std::uint64_t, Bayes::TokenCounts> &counts,
         std::uint32_t &spamMessages, std::uint32_t &hamMessages);

      // Adds one message with these tokens to the spam or the ham counts, in one transaction
      // where the database supports it. Callers serialize training and Reset.
      static bool Train(const std::vector<std::uint64_t> &tokens, bool spam);

      static bool Reset();

      static bool GetStatistics(Statistics &statistics);
   };
}
