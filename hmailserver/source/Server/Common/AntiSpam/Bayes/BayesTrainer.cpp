// Copyright (c) 2026 ArzoieMail contributors.

#include "stdafx.h"

#include "BayesTrainer.h"
#include "BayesStore.h"

#include "../../BO/Message.h"
#include "../../BO/MessageData.h"
#include "../../Threading/Task.h"
#include "../../Threading/WorkQueue.h"
#include "../../Util/Parsing/StringParser.h"

#ifdef _DEBUG
#define DEBUG_NEW new(_NORMAL_BLOCK, __FILE__, __LINE__)
#define new DEBUG_NEW
#endif

namespace HM
{
   namespace
   {
      // The most of a message body that is tokenized. Words beyond it rarely change the verdict,
      // and the limit bounds the work a large message can cause.
      const int MaximumText = 200 * 1024;
   }

   class BayesTrainingTask : public Task
   {
   public:
      BayesTrainingTask() : Task("BayesTrainingTask") {}
   protected:
      void DoWork() override { BayesTrainer::Instance().Drain_(); }
   };

   BayesTrainer &
   BayesTrainer::Instance()
   {
      static BayesTrainer trainer;
      return trainer;
   }

   std::vector<std::uint64_t>
   BayesTrainer::Tokens(const MessageData &data, const String &senderAddress)
   {
      const String body = (data.GetBody() + _T(" ") + data.GetHTMLBody()).Left(MaximumText);
      const String domain = StringParser::ExtractDomain(senderAddress);
      return Bayes::Tokenize(std::wstring(data.GetSubject().c_str()), std::wstring(body.c_str()), std::wstring(domain.c_str()));
   }

   bool
   BayesTrainer::Queue(const String &fileName, const String &senderAddress, bool spam)
   {
      std::lock_guard<std::mutex> guard(queue_mutex_);
      if (queue_.size() >= MaximumPending)
      {
         ++dropped_;
         return false;
      }
      queue_.push_back(Item{fileName, senderAddress, spam});
      if (running_) return true;

      auto workQueue = Application::Instance()->GetMaintenanceWorkQueue();
      if (!workQueue)
      {
         queue_.pop_back();
         ++dropped_;
         return false;
      }
      running_ = true;
      workQueue->AddTask(std::make_shared<BayesTrainingTask>());
      return true;
   }

   void
   BayesTrainer::Drain_()
   {
      for (;;)
      {
         Item item;
         {
            std::lock_guard<std::mutex> guard(queue_mutex_);
            if (queue_.empty())
            {
               running_ = false;
               return;
            }
            item = queue_.front();
            queue_.pop_front();
         }
         TrainOne_(item);
      }
   }

   void
   BayesTrainer::TrainOne_(const Item &item)
   {
      bool succeeded = false;
      try
      {
         // The message may have been expunged since it was queued.
         if (FileUtilities::Exists(item.fileName))
         {
            MessageData data;
            if (data.LoadFromMessage(item.fileName, std::make_shared<Message>(false)) && !data.IsHeaderOnly())
            {
               const auto tokens = Tokens(data, item.senderAddress);
               std::lock_guard<std::mutex> guard(store_mutex_);
               succeeded = BayesStore::Train(tokens, item.spam);
            }
         }
      }
      catch (...)
      {
         ErrorManager::Instance()->ReportError(ErrorManager::Medium, 5800, "BayesTrainer::TrainOne_", "Training the Bayesian filter on a message failed unexpectedly.");
      }

      std::lock_guard<std::mutex> guard(queue_mutex_);
      if (succeeded) ++trained_; else ++failed_;
   }

   BayesTrainer::Status
   BayesTrainer::GetStatus()
   {
      std::lock_guard<std::mutex> guard(queue_mutex_);
      Status status;
      status.pending = queue_.size();
      status.running = running_;
      status.trained = trained_;
      status.failed = failed_;
      status.dropped = dropped_;
      return status;
   }

   bool
   BayesTrainer::Reset()
   {
      {
         std::lock_guard<std::mutex> guard(queue_mutex_);
         queue_.clear();
      }
      // Waits for a message being trained, so none is counted into the cleared table.
      std::lock_guard<std::mutex> guard(store_mutex_);
      return BayesStore::Reset();
   }
}
