// Copyright (c) 2026 ArzoieMail contributors.

#include "stdafx.h"

#include "BayesStore.h"

#include "../../SQL/DALConnection.h"
#include "../../SQL/DALRecordset.h"

#ifdef _DEBUG
#define DEBUG_NEW new(_NORMAL_BLOCK, __FILE__, __LINE__)
#define new DEBUG_NEW
#endif

namespace HM
{
   namespace
   {
      // Hashes are looked up this many at a time, which keeps every statement well inside
      // the parameter limits of all four databases.
      const size_t LookupBatch = 100;

      // The columns are signed 64-bit; the hash is stored as the same bits.
      __int64 Stored(std::uint64_t hash) { return static_cast<__int64>(hash); }
      std::uint64_t Hash(__int64 stored) { return static_cast<std::uint64_t>(stored); }

      String ParameterName(size_t index)
      {
         String name;
         name.Format(_T("@T%d"), static_cast<int>(index));
         return name;
      }

      SQLCommand LookupCommand(const std::vector<std::uint64_t> &hashes, size_t first, size_t count)
      {
         String sql = _T("select tokenhash, spamcount, hamcount from hm_bayes_tokens where tokenhash in (");
         for (size_t index = 0; index < count; ++index)
         {
            if (index > 0) sql += _T(", ");
            sql += ParameterName(index);
         }
         sql += _T(")");
         SQLCommand command(sql);
         for (size_t index = 0; index < count; ++index)
            command.AddParameter(AnsiString(ParameterName(index)), Stored(hashes[first + index]));
         return command;
      }

      std::uint32_t Count(long value)
      {
         return value < 0 ? 0 : static_cast<std::uint32_t>(value);
      }
   }

   bool
   BayesStore::LoadCounts(const std::vector<std::uint64_t> &tokens, std::map<std::uint64_t, Bayes::TokenCounts> &counts,
      std::uint32_t &spamMessages, std::uint32_t &hamMessages)
   {
      spamMessages = 0;
      hamMessages = 0;

      std::vector<std::uint64_t> hashes(tokens);
      hashes.push_back(Bayes::TotalsToken);

      auto manager = Application::Instance()->GetDBManager();
      for (size_t first = 0; first < hashes.size(); first += LookupBatch)
      {
         const size_t count = (std::min)(LookupBatch, hashes.size() - first);
         auto rows = manager->OpenRecordset(LookupCommand(hashes, first, count));
         if (!rows) return false;
         for (; !rows->IsEOF(); rows->MoveNext())
         {
            const auto hash = Hash(rows->GetInt64Value("tokenhash"));
            const auto spam = Count(rows->GetLongValue("spamcount")), ham = Count(rows->GetLongValue("hamcount"));
            if (hash == Bayes::TotalsToken)
            {
               spamMessages = spam;
               hamMessages = ham;
            }
            else
               counts[hash] = Bayes::TokenCounts{spam, ham};
         }
      }
      return true;
   }

   bool
   BayesStore::Train(const std::vector<std::uint64_t> &tokens, bool spam)
   {
      auto manager = Application::Instance()->GetDBManager();
      String error;
      TransactionLease::BeginResult started;
      auto connection = manager->BeginTransaction(error, &started);
      // Without transactions, each statement commits on its own. A failure part way then
      // leaves the message partly counted, which shifts its tokens slightly and nothing more.
      if (!connection && started != TransactionLease::BeginResult::Unsupported)
         return false;

      const auto execute = [&](const SQLCommand &command)
      {
         return connection ? connection->TryExecute(command, error) == DALConnection::DALSuccess : manager->Execute(command);
      };
      const auto open = [&](const SQLCommand &command) -> std::shared_ptr<DALRecordset>
      {
         if (!connection) return manager->OpenRecordset(command);
         auto rows = connection->CreateRecordset();
         if (!rows || rows->TryOpen(connection, command, error) != DALConnection::DALSuccess) return nullptr;
         return rows;
      };

      bool succeeded = true;
      try
      {
         std::vector<std::uint64_t> hashes(tokens);
         hashes.push_back(Bayes::TotalsToken);

         std::set<std::uint64_t> existing;
         for (size_t first = 0; succeeded && first < hashes.size(); first += LookupBatch)
         {
            const size_t count = (std::min)(LookupBatch, hashes.size() - first);
            auto rows = open(LookupCommand(hashes, first, count));
            if (!rows) { succeeded = false; break; }
            for (; !rows->IsEOF(); rows->MoveNext())
               existing.insert(Hash(rows->GetInt64Value("tokenhash")));
         }

         const String increment = spam
            ? _T("update hm_bayes_tokens set spamcount = spamcount + 1 where tokenhash = @HASH")
            : _T("update hm_bayes_tokens set hamcount = hamcount + 1 where tokenhash = @HASH");
         for (size_t index = 0; succeeded && index < hashes.size(); ++index)
         {
            if (existing.count(hashes[index]))
            {
               SQLCommand command(increment);
               command.AddParameter("@HASH", Stored(hashes[index]));
               succeeded = execute(command);
            }
            else
            {
               // Compact binds by ordinal, so the parameters are added in the order they appear.
               SQLCommand command(_T("insert into hm_bayes_tokens (tokenhash, spamcount, hamcount) values (@HASH, @SPAM, @HAM)"));
               command.AddParameter("@HASH", Stored(hashes[index]));
               command.AddParameter("@SPAM", spam ? 1 : 0);
               command.AddParameter("@HAM", spam ? 0 : 1);
               succeeded = execute(command);
            }
         }
      }
      catch (...)
      {
         if (connection) manager->RollbackTransaction(connection, error);
         throw;
      }

      if (!connection) return succeeded;
      if (!succeeded)
      {
         manager->RollbackTransaction(connection, error);
         return false;
      }
      return manager->CommitTransaction(connection, error);
   }

   bool
   BayesStore::Reset()
   {
      auto manager = Application::Instance()->GetDBManager();
      if (!manager->Execute(SQLCommand(_T("delete from hm_bayes_tokens where tokenhash <> 0"))))
         return false;
      return manager->Execute(SQLCommand(_T("update hm_bayes_tokens set spamcount = 0, hamcount = 0 where tokenhash = 0")));
   }

   bool
   BayesStore::GetStatistics(Statistics &statistics)
   {
      statistics = Statistics();
      auto manager = Application::Instance()->GetDBManager();
      auto totals = manager->OpenRecordset(SQLCommand(_T("select spamcount, hamcount from hm_bayes_tokens where tokenhash = 0")));
      if (!totals) return false;
      if (!totals->IsEOF())
      {
         statistics.spamMessages = Count(totals->GetLongValue("spamcount"));
         statistics.hamMessages = Count(totals->GetLongValue("hamcount"));
      }
      auto tokens = manager->OpenRecordset(SQLCommand(_T("select count(*) as tokencount from hm_bayes_tokens where tokenhash <> 0")));
      if (!tokens) return false;
      if (!tokens->IsEOF()) statistics.tokens = tokens->GetLongValue("tokencount");
      return true;
   }
}
