#pragma once
#include <atomic>
#include <thread>

namespace HM
{
   class ManagementPipe
   {
   public:
      ManagementPipe() = default;
      ~ManagementPipe();
      bool Start();
      void Stop();

   private:
      void Run_();
      bool Transfer_(void *buffer, DWORD length, bool write);
      HANDLE pipe_ = INVALID_HANDLE_VALUE;
      HANDLE stopped_ = nullptr;
      std::thread worker_;
   };
}
