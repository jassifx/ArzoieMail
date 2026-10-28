// Copyright (c) 2005 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com
// Created: 2005-08-02
// Purpose: To offer queue manipiulation to the COM API.

#include "stdafx.h"

#include "DeliveryQueue.h"
#include "SMTPDeliveryManager.h"

#include "../Common/Threading/WorkQueueManager.h"
#include "../Common/BO/Messages.h"
#include "../Common/BO/Message.h"
#include "../Common/Persistence/PersistentMessage.h"

#ifdef _DEBUG
#define DEBUG_NEW new(_NORMAL_BLOCK, __FILE__, __LINE__)
#define new DEBUG_NEW
#endif

namespace HM
{

   bool DeliveryQueue::is_clearing_ = false;
   boost::recursive_mutex &DeliveryQueue::MutationMutex()
   { static boost::recursive_mutex mutex; return mutex; }
   bool DeliveryQueue::IsClearing()
   { boost::lock_guard<boost::recursive_mutex> guard(MutationMutex()); return is_clearing_; }
   DeliveryQueue::MutationResult DeliveryQueue::TryRemove(__int64 id)
   {
      boost::lock_guard<boost::recursive_mutex> guard(MutationMutex());
      if (is_clearing_) return MutationResult::Busy;
      PersistentMessage::QueueItem item;
      if (!PersistentMessage::ReadQueueItem(id, item)) return MutationResult::Failed;
      if (!item.id) return MutationResult::Missing;
      if (item.locked) return MutationResult::Busy;
      auto message = std::make_shared<Message>(false);
      if (!PersistentMessage::ReadObject(message, id)) return MutationResult::Missing;
      if (message->GetAccountID() != 0 || (message->GetState() != Message::Delivering && static_cast<int>(message->GetState()) != 3)) return MutationResult::Missing;
      if (!PersistentMessage::DeleteObject(message)) return MutationResult::Failed;
      if (auto manager = Application::Instance()->GetSMTPDeliveryManager()) manager->UncachePendingMessages();
      return MutationResult::Success;
   }
   DeliveryQueue::MutationResult DeliveryQueue::TryRetry(__int64 id)
   {
      boost::lock_guard<boost::recursive_mutex> guard(MutationMutex());
      if (is_clearing_) return MutationResult::Busy;
      PersistentMessage::QueueItem item;
      if (!PersistentMessage::ReadQueueItem(id, item)) return MutationResult::Failed;
      if (!item.id) return MutationResult::Missing;
      if (item.locked) return MutationResult::Busy;
      if (!PersistentMessage::SetNextTryTime(id, false, -1)) return MutationResult::Failed;
      if (auto manager = Application::Instance()->GetSMTPDeliveryManager()) manager->UncachePendingMessages();
      StartDelivery(); return MutationResult::Success;
   }

   DeliveryQueueClearer::DeliveryQueueClearer(void) :
       Task("DeliveryQueueClearer")
   {
   }

   DeliveryQueueClearer::~DeliveryQueueClearer(void)
   {
   }

   void
   DeliveryQueueClearer::DoWork()
      //---------------------------------------------------------------------------()
      // DESCRIPTION:
      // This function does the actual clearing of the queue.
      //---------------------------------------------------------------------------()
   {
      LOG_DEBUG("Clearing delivery queue");

      // First stop the delivery queue.
      const String& sQueueName = Application::Instance()->GetSMTPDeliveryManager()->GetQueueName();
         std::shared_ptr<WorkQueue> pWQ = WorkQueueManager::Instance()->GetQueue(sQueueName);
      if (!pWQ)
      {
         ErrorManager::Instance()->ReportError(ErrorManager::Medium, 4210, "DeliveryQueueClearer::DoWork", "Could not fetch SMTP delivery queue.");
         DeliveryQueue::OnDeliveryQueueCleared();

         return;
      }
      
      pWQ->Stop();

      // Load the delivery queue from the database
      Messages oMessages(-1,-1);
      oMessages.Refresh(false);

      // Iterate over messages to deliver.
         std::vector<std::shared_ptr<Message> > vecMessages = oMessages.GetVector();
         auto iterMessage = vecMessages.begin();
      while (iterMessage != vecMessages.end())
      {
         // Delete the message from the database
         PersistentMessage::DeleteObject(*iterMessage);

         // Next message in queue
         iterMessage++;
      }

      // Tell the delivery queue to clear it's pending messages list.
      Application::Instance()->GetSMTPDeliveryManager()->UncachePendingMessages();

      // Make sure there doesn't exist any delivery tasks.
      pWQ->Start();

      DeliveryQueue::OnDeliveryQueueCleared();

      LOG_DEBUG("Delivery queue cleared.");

   }

   DeliveryQueue::DeliveryQueue(void)
   {
   }

   DeliveryQueue::~DeliveryQueue(void)
   {
   }

   void
   DeliveryQueue::Clear()
   {
      boost::lock_guard<boost::recursive_mutex> guard(MutationMutex());
      if (is_clearing_)
         return;

      is_clearing_ = true;

      // Use the random work queue to run the task.
      std::shared_ptr<WorkQueue> pQueue = Application::Instance()->GetMaintenanceWorkQueue();

      if (!pQueue)
      {
         ErrorManager::Instance()->ReportError(ErrorManager::Medium, 5117, "DeliveryQueue::Clear()", "WorkQueue was not started. Queue could not be cleared.");

         is_clearing_ = false;
         return;
      }

      // Launch a thread that can clear the delivery queue.
      std::shared_ptr<DeliveryQueueClearer> pClearer = std::shared_ptr<DeliveryQueueClearer>(new DeliveryQueueClearer);

      pQueue->AddTask(pClearer);
   }

   void 
   DeliveryQueue::ResetDeliveryTime(__int64 iMessageID)
   {
      TryRetry(iMessageID);
   }


   void 
   DeliveryQueue::Remove(__int64 iMessageID)
   {
      TryRemove(iMessageID);
   }


   void 
   DeliveryQueue::OnDeliveryQueueCleared()
   {
      boost::lock_guard<boost::recursive_mutex> guard(MutationMutex());
      is_clearing_ = false;
      StartDelivery();
   }

   void 
   DeliveryQueue::StartDelivery()
   {
      std::shared_ptr<SMTPDeliveryManager> pDeliverer = Application::Instance()->GetSMTPDeliveryManager();
      if (!pDeliverer)
         return;

      // Tell the deliverer to look for messages to deliver.
         pDeliverer->SetDeliverMessage();
   }
}
