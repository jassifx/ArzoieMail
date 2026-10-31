// The native Bayesian filter. Server administrators only; the dispatcher checks that first.
// Its settings are in the antispam section. The model is global: one token table trained from
// every mailbox. Training is queued for the background trainer, so these calls return before
// the messages are counted; bayes.status reports the queue.
json::object BayesStatus()
{
   BayesStore::Statistics statistics;
   if (!BayesStore::GetStatistics(statistics)) throw RequestError{503};
   const auto trainer = BayesTrainer::Instance().GetStatus();
   auto &config = Configuration::Instance()->GetAntiSpamConfiguration();
   const __int64 minimum = (std::max)(config.GetBayesMinimumMessages(), 1);
   return {{"enabled", config.GetBayesEnabled()}, {"spamMessages", statistics.spamMessages}, {"hamMessages", statistics.hamMessages},
      {"tokens", statistics.tokens}, {"minimumMessages", minimum},
      {"ready", statistics.spamMessages >= minimum && statistics.hamMessages >= minimum},
      {"pending", static_cast<__int64>(trainer.pending)}, {"training", trainer.running}, {"trained", trainer.trained},
      {"failed", trainer.failed}, {"dropped", trainer.dropped}};
}

json::value ManageBayes(const std::string &operation, json::object &arguments)
{
   if (operation == "bayes.status")
   {
      if (!arguments.empty()) throw RequestError{422};
      return BayesStatus();
   }
   if (operation == "bayes.reset")
   {
      // Forgetting every trained message cannot be undone, so the caller confirms it.
      AllowedFields(arguments, {"confirm"});
      const auto *confirm = arguments.if_contains("confirm");
      if (!confirm || !confirm->is_bool() || !confirm->as_bool()) throw RequestError{422};
      if (!BayesTrainer::Instance().Reset()) throw RequestError{503};
      LOG_APPLICATION("Management reset the Bayesian filter.");
      return BayesStatus();
   }
   if (operation != "bayes.train") throw RequestError{404};

   // Trains on every message of one folder of one mailbox, for example a user's Junk folder as
   // spam and their Inbox as ham, to give a new filter its first counts.
   AllowedFields(arguments, {"accountId", "folder", "as"});
   const auto accountId = Number(arguments, "accountId", 9223372036854775807LL);
   const auto path = CleanText(arguments, "folder", 255);
   const auto as = Text(arguments, "as", 8);
   if (!accountId || path.IsEmpty() || (as != "spam" && as != "ham")) throw RequestError{422};
   auto account = std::make_shared<Account>();
   if (!PersistentAccount::ReadObject(account, accountId)) throw RequestError{404};
   auto folders = IMAPFolderContainer::Instance()->GetFoldersForAccount(accountId);
   auto folder = folders ? folders->GetFolderByFullPath(ModifiedUTF7::Encode(path)) : nullptr;
   if (!folder) throw RequestError{404};

   __int64 queued = 0, dropped = 0;
   for (const auto &message : MessagesContainer::Instance()->GetMessages(accountId, folder->GetID())->GetCopy())
   {
      if (message->GetFlagDeleted()) continue;
      if (BayesTrainer::Instance().Queue(PersistentMessage::GetFileName(account, message), message->GetFromAddress(), as == "spam"))
         ++queued;
      else
         ++dropped;
   }
   String note;
   note.Format(_T("Management queued %d messages for Bayesian training as %s."), static_cast<int>(queued), as == "spam" ? _T("spam") : _T("ham"));
   LOG_APPLICATION(note);
   return json::object{{"queued", queued}, {"dropped", dropped}};
}
