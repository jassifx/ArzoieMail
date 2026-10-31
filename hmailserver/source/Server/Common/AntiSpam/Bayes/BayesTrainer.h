// Copyright (c) 2026 ArzoieMail contributors.
//
// Trains the native Bayesian filter in the background. Messages are queued by file name, from
// IMAP copies into and out of Junk folders and from administrator requests, and one task at a
// time drains the queue on the maintenance work queue. Training and Reset never overlap.

#pragma once

#include "BayesFilter.h"

#include <deque>
#include <mutex>

namespace HM
{
   class MessageData;

   class BayesTrainer
   {
   public:

      static BayesTrainer &Instance();

      struct Status
      {
         size_t pending = 0;
         bool running = false;
         __int64 trained = 0;
         __int64 failed = 0;
         __int64 dropped = 0;
      };

      // Queues the message file for training as spam or ham. Returns false, and counts the
      // message as dropped, when the queue is already full.
      bool Queue(const String &fileName, const String &senderAddress, bool spam);

      Status GetStatus();

      // Clears the queue and the token table.
      bool Reset();

      // The tokens of a message, as both scoring and training see them.
      static std::vector<std::uint64_t> Tokens(const MessageData &data, const String &senderAddress);

      // The most messages that wait for training at once.
      static const size_t MaximumPending = 10000;

   private:

      struct Item
      {
         String fileName;
         String senderAddress;
         bool spam;
      };

      friend class BayesTrainingTask;
      void Drain_();
      void TrainOne_(const Item &item);

      std::mutex queue_mutex_;
      std::deque<Item> queue_;
      bool running_ = false;
      __int64 trained_ = 0;
      __int64 failed_ = 0;
      __int64 dropped_ = 0;

      // Held while a message is trained or the table is reset.
      std::mutex store_mutex_;
   };
}
