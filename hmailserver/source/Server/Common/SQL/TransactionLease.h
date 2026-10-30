#pragma once

// Shared by the real connection manager and portable failure-injection tests.
namespace HM::TransactionLease
{
   enum class BeginResult { Started, Unavailable, Unsupported };
   template<class Connection, class Error, class Open, class Retire>
   bool EnsureConnected(const Connection &connection, Error &error, Open open, Retire retire)
   {
      if (!connection) { error = "No database connection is available."; return false; }
      bool result = false;
      try { result = connection->IsConnected() || open(*connection, error); }
      catch (...) { retire(connection); throw; }
      if (!result) retire(connection);
      return result;
   }

   template<class Connection, class Error, class Release, class Retire>
   BeginResult Begin(const Connection &connection, Error &error, Release release, Retire retire)
   {
      if (!EnsureConnected(connection, error, [](auto &value, auto &result) { return value.Reconnect(result); }, retire)) return BeginResult::Unavailable;
      if (!connection->GetSupportsTransactions())
      {
         error = "The database does not support checked transactions.";
         release(connection);
         return BeginResult::Unsupported;
      }
      bool result = false;
      try
      {
         // A retired connection is replaced by an unopened client. Reconnect only
         // before BEGIN; never retry after a transaction might have started.
         result = connection->BeginTransaction(error);
      }
      catch (...)
      {
         retire(connection); // The retirement callback must not throw.
         throw;
      }
      if (!result) retire(connection);
      return result ? BeginResult::Started : BeginResult::Unavailable;
   }

   template<class Connection, class Error, class Operation, class Release, class Retire>
   bool Finish(const Connection &connection, Error &error, Operation operation, Release release, Retire retire)
   {
      if (!connection) { error = "No transaction connection is available."; return false; }
      bool result = false;
      try { result = operation(*connection, error); }
      catch (...)
      {
         retire(connection);
         throw;
      }
      // A false commit can mean a lost acknowledgement. Do not retry it or hand
      // a possibly active transaction to another borrower.
      if (result) release(connection);
      else retire(connection);
      return result;
   }
}
