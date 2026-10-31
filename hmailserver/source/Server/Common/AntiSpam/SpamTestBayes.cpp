// Copyright (c) 2026 ArzoieMail contributors.

#include "stdafx.h"

#include "SpamTestBayes.h"

#include "SpamTestData.h"
#include "SpamTestResult.h"
#include "AntiSpamConfiguration.h"

#include "Bayes/BayesFilter.h"
#include "Bayes/BayesStore.h"
#include "Bayes/BayesTrainer.h"

#include "../BO/MessageData.h"

#include <cmath>

#ifdef _DEBUG
#define DEBUG_NEW new(_NORMAL_BLOCK, __FILE__, __LINE__)
#define new DEBUG_NEW
#endif

namespace HM
{
   String
   SpamTestBayes::GetName() const
   {
      return "SpamTestBayes";
   }

   bool
   SpamTestBayes::GetIsEnabled()
   {
      return Configuration::Instance()->GetAntiSpamConfiguration().GetBayesEnabled();
   }

   std::set<std::shared_ptr<SpamTestResult> >
   SpamTestBayes::RunTest(std::shared_ptr<SpamTestData> pTestData)
   {
      std::set<std::shared_ptr<SpamTestResult> > results;

      AntiSpamConfiguration &config = Configuration::Instance()->GetAntiSpamConfiguration();
      std::shared_ptr<MessageData> data = pTestData->GetMessageData();
      if (!data || data->IsHeaderOnly())
         return results;

      const auto tokens = BayesTrainer::Tokens(*data, pTestData->GetEnvelopeFrom());
      std::map<std::uint64_t, Bayes::TokenCounts> counts;
      std::uint32_t spamMessages = 0, hamMessages = 0;
      if (!BayesStore::LoadCounts(tokens, counts, spamMessages, hamMessages))
         return results;

      // An untrained filter would guess; it scores nothing until it has enough of both.
      const auto minimum = static_cast<std::uint32_t>((std::max)(config.GetBayesMinimumMessages(), 1));
      if (spamMessages < minimum || hamMessages < minimum)
         return results;

      const double probability = Bayes::SpamProbability(tokens, counts, spamMessages, hamMessages);
      const int percent = static_cast<int>(std::floor(probability * 100.0));
      const String message = Formatter::Format("Bayesian filter: spam probability {0}%", percent);
      LOG_DEBUG(message);
      if (percent < config.GetBayesThreshold())
         return results;

      results.insert(std::make_shared<SpamTestResult>(GetName(), SpamTestResult::Fail, config.GetBayesScore(), message));
      return results;
   }
}
