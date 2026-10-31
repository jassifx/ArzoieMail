// Copyright (c) 2026 ArzoieMail contributors.

#pragma once

#include "SpamTest.h"

namespace HM
{
   // The native Bayesian filter. Adds its score when the trained token counts put the spam
   // probability of the message at or above the configured threshold.
   class SpamTestBayes : public SpamTest
   {
   public:

      virtual SpamTestType GetTestType()
      {
         return SpamTest::PostTransmission;
      }

      virtual String GetName() const;
      virtual bool GetIsEnabled();
      virtual std::set<std::shared_ptr<SpamTestResult> > RunTest(std::shared_ptr<SpamTestData> pTestData);
   };
}
