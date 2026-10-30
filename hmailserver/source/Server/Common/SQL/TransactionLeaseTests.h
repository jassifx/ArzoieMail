#pragma once
#include "TransactionLease.h"
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

namespace HM
{
   class TransactionLeaseTests
   {
      struct Connection
      {
         bool connected = true, beginResult = true, finishResult = true, reconnectResult = true;
         bool throwBegin = false, throwFinish = false, uncertain = false;
         bool supportsTransactions = true;
         int beginCalls = 0, reconnectCalls = 0;
         bool IsConnected() const { return connected; }
         bool GetSupportsTransactions() const { return supportsTransactions; }
         bool Reconnect(std::string &error)
         {
            ++reconnectCalls;
            connected = reconnectResult;
            if (!connected) error = "reconnect failed";
            return connected;
         }
         bool BeginTransaction(std::string &error)
         {
            ++beginCalls;
            if (throwBegin) { uncertain = true; throw std::runtime_error("begin threw"); }
            if (!connected || !beginResult) { uncertain = true; error = "begin failed"; return false; }
            return true;
         }
         bool End(std::string &error)
         {
            if (throwFinish) { uncertain = true; throw std::runtime_error("finish threw"); }
            if (!finishResult) { uncertain = true; error = "finish failed"; return false; }
            return true;
         }
      };
      struct Pool
      {
         std::shared_ptr<Connection> available = std::make_shared<Connection>();
         int outstanding = 0, released = 0, retired = 0, unsafeRecycles = 0;
         std::shared_ptr<Connection> Acquire()
         {
            if (!available) return {};
            auto value = available; available.reset(); ++outstanding; return value;
         }
         void Release(const std::shared_ptr<Connection> &value)
         {
            --outstanding; ++released;
            if (value->uncertain) ++unsafeRecycles;
            available = value;
         }
         void Retire(const std::shared_ptr<Connection> &)
         {
            --outstanding; ++retired;
            available = std::make_shared<Connection>(); available->connected = false;
         }
      };
   public:
      static std::vector<std::string> Run()
      {
         std::vector<std::string> failures;
         const auto check = [&](bool condition, const char *message) { if (!condition) failures.emplace_back(message); };
         const auto begin = [](Pool &pool, const auto &connection, std::string &error) {
            return TransactionLease::Begin(connection, error, [&](const auto &value) { pool.Release(value); },
               [&](const auto &value) { pool.Retire(value); }) == TransactionLease::BeginResult::Started;
         };
         const auto finish = [](Pool &pool, const auto &connection, std::string &error) {
            return TransactionLease::Finish(connection, error, [](Connection &value, std::string &result) { return value.End(result); },
               [&](const auto &value) { pool.Release(value); }, [&](const auto &value) { pool.Retire(value); });
         };
         {
            Pool pool; std::string error; auto connection = pool.Acquire(); connection->beginResult = false;
            check(!begin(pool, connection, error), "Failed begin must report failure.");
            check(error == "begin failed", "Begin failure must retain its original diagnostic.");
            check(pool.outstanding == 0 && pool.retired == 1, "Failed begin must not leak the leased connection.");
            check(pool.available && pool.available != connection, "An uncertain begin must retire the physical connection.");
            auto recovered = pool.Acquire();
            check(begin(pool, recovered, error), "A fresh connection must allow recovery after a failed begin.");
            if (recovered) finish(pool, recovered, error);
         }
         for (const char *operation : {"commit", "rollback"})
         {
            Pool pool; std::string error; auto connection = pool.Acquire();
            check(begin(pool, connection, error), "Healthy begin must succeed.");
            connection->finishResult = false;
            check(!finish(pool, connection, error), operation);
            check(pool.outstanding == 0 && pool.retired == 1 && pool.released == 0,
               "Failed terminal operations must retire rather than recycle their lease.");
            check(pool.unsafeRecycles == 0, "An unconfirmed transaction must never return to the reusable pool.");
         }
         for (bool initial : {true, false})
         {
            Pool pool; std::string error; auto connection = pool.Acquire();
            connection->throwBegin = initial; connection->throwFinish = !initial;
            try
            {
               if (initial) begin(pool, connection, error); else finish(pool, connection, error);
               check(false, "Injected transaction exception must propagate.");
            }
            catch (const std::runtime_error &failure)
            {
               check(std::string(failure.what()) == (initial ? "begin threw" : "finish threw"), "Failure cleanup must preserve the original exception.");
            }
            check(pool.outstanding == 0 && pool.retired == 1, "Thrown transaction operations must release ownership exactly once.");
         }
         {
            Pool pool; std::string error; auto connection = pool.Acquire();
            check(begin(pool, connection, error), "Healthy transaction begins.");
            check(pool.outstanding == 1 && pool.released == 0 && pool.retired == 0, "A successful begin retains its exclusive lease.");
            check(finish(pool, connection, error), "Healthy transaction finishes.");
            check(pool.outstanding == 0 && pool.released == 1 && pool.retired == 0, "A confirmed terminal operation returns the healthy connection exactly once.");
         }
         {
            Pool pool; std::string error; auto connection = pool.Acquire(); connection->connected = false;
            check(begin(pool, connection, error) && connection->reconnectCalls == 1, "A replacement connection reconnects before starting a transaction.");
         }
         {
            Pool pool; std::string error; auto connection = pool.Acquire(); connection->connected = false;
            check(TransactionLease::EnsureConnected(connection, error, [](Connection &value, std::string &result) { return value.Reconnect(result); },
               [&](const auto &value) { pool.Retire(value); }) && connection->IsConnected(),
               "Ordinary SQL borrowers must also receive an opened replacement client.");
         }
         {
            Pool pool; std::string error; auto connection = pool.Acquire(); connection->connected = false; connection->reconnectResult = false;
            check(!begin(pool, connection, error), "Reconnect failure must fail the transaction.");
            check(connection->beginCalls == 0 && pool.retired == 1 && pool.outstanding == 0, "Failed reconnect must not begin or leak a lease.");
         }
         {
            Pool pool; std::string error; std::shared_ptr<Connection> missing;
            check(!begin(pool, missing, error) && !finish(pool, missing, error), "An unavailable connection must fail safely.");
            check(pool.released == 0 && pool.retired == 0, "No connection means no pool ownership to release.");
         }
         {
            Pool pool; std::string error; auto connection = pool.Acquire(); connection->supportsTransactions = false;
            const bool started = begin(pool, connection, error);
            check(!started && connection->beginCalls == 0, "A non-transactional engine must not pretend to start a checked transaction.");
            check(pool.outstanding == 0 && pool.released == 1 && pool.retired == 0,
               "Unsupported transactions must return the still-healthy client without retiring it.");
            if (started) finish(pool, connection, error);
         }
         return failures;
      }
   };
}
