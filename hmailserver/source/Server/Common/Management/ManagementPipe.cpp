#include "stdafx.h"
#include "ManagementPipe.h"
#include "ManagementAuthentication.h"
#include "../BO/Account.h"
#include "../BO/Domains.h"
#include "../BO/DNSBlackLists.h"
#include "../Persistence/PersistentDNSBlacklist.h"
#include "../AntiSpam/SpamAssassin/SpamAssassinTestConnect.h"
#include "../Util/Unicode.h"
#include "../Util/ServerStatus.h"
#include <sddl.h>
#include <boost/json/src.hpp>

namespace HM
{
   namespace
   {
      namespace json = boost::json;
      const wchar_t PipeName[] = L"\\\\.\\pipe\\ArzoieMail.Management.v1";
      const DWORD MaximumFrame = 65536;
      struct RequestError { int status; };

      std::string Utf8(const String &text) { return Unicode::ToUTF8(text); }
      String Wide(const json::value &value)
      {
         if (!value.is_string() || value.as_string().find('\0') != json::string::npos) throw RequestError{422};
         String result;
         if (!Unicode::MultiByteToWide(AnsiString(value.as_string().data(), static_cast<int>(value.as_string().size())), result))
            throw RequestError{422};
         return result;
      }
      std::string Text(const json::object &object, const char *name, size_t maximum)
      {
         const auto *value = object.if_contains(name);
         if (!value || !value->is_string() || value->as_string().size() > maximum
             || value->as_string().find('\0') != json::string::npos) throw RequestError{422};
         return std::string(value->as_string());
      }
      __int64 Id(const json::object &arguments)
      {
         const auto *value = arguments.if_contains("id");
         if (!value || !value->is_int64() || value->as_int64() <= 0) throw RequestError{422};
         return value->as_int64();
      }
      json::object User(const Account &account)
      {
         return {{"address", Utf8(account.GetAddress())}, {"level", static_cast<int>(account.GetAdminLevel())},
                 {"domainId", account.GetDomainID()}, {"accountId", account.GetID()}};
      }
      json::object DomainView(const Domain &domain)
      {
         return {{"id", domain.GetID()}, {"name", Utf8(domain.GetName())}, {"active", domain.GetIsActive()},
                 {"dkimEnabled", domain.GetDKIMEnabled()}, {"selector", std::string(domain.GetDKIMSelector())}};
      }
      json::object DnsblView(DNSBlackList &item)
      {
         return {{"id", item.GetID()}, {"Active", item.GetIsActive()}, {"DNSHost", Utf8(item.GetDNSHost())},
                 {"ExpectedResult", Utf8(item.GetExpectedResult())}, {"RejectMessage", Utf8(item.GetRejectMessage())},
                 {"Score", item.GetScore()}};
      }

      // One explicit mapping is used for both read/write. The dispatcher never reflects a member name.
      struct BoolSetting
      {
         const char *name;
         bool (AntiSpamConfiguration::*get)();
         void (AntiSpamConfiguration::*set)(bool);
      };
      struct IntSetting
      {
         const char *name;
         int (AntiSpamConfiguration::*get)();
         void (AntiSpamConfiguration::*set)(int);
         int minimum;
         int maximum;
      };
#define BOOL_SETTING(name) {#name, &AntiSpamConfiguration::Get##name, &AntiSpamConfiguration::Set##name}
#define INT_SETTING(name, low, high) {#name, &AntiSpamConfiguration::Get##name, &AntiSpamConfiguration::Set##name, low, high}
      const BoolSetting BoolSettings[] = {
         BOOL_SETTING(SpamAssassinEnabled), BOOL_SETTING(SpamAssassinMergeScore), BOOL_SETTING(UseSPF),
         BOOL_SETTING(UseMXChecks), BOOL_SETTING(CheckHostInHelo), BOOL_SETTING(CheckPTR),
         BOOL_SETTING(DKIMVerificationEnabled), BOOL_SETTING(DMARCEnabled), BOOL_SETTING(DMARCHonorPolicy),
         BOOL_SETTING(AddAuthenticationResultsHeader), BOOL_SETTING(AddHeaderSpam), BOOL_SETTING(AddHeaderReason),
         BOOL_SETTING(PrependSubject)
      };
      const IntSetting IntSettings[] = {
         INT_SETTING(SpamMarkThreshold, 0, 10000), INT_SETTING(SpamDeleteThreshold, 0, 10000),
         INT_SETTING(SpamAssassinPort, 1, 65535), INT_SETTING(SpamAssassinScore, 0, 10000),
         INT_SETTING(UseSPFScore, 0, 10000), INT_SETTING(UseMXChecksScore, 0, 10000),
         INT_SETTING(CheckHostInHeloScore, 0, 10000), INT_SETTING(CheckPTRScore, 0, 10000),
         INT_SETTING(DKIMVerificationFailureScore, 0, 10000), INT_SETTING(DMARCFailureScore, 0, 10000),
         {"MaximumMessageSize", &AntiSpamConfiguration::GetAntiSpamMaxSizeKB, &AntiSpamConfiguration::SetAntiSpamMaxSizeKB, 0, 2147483647}
      };
#undef BOOL_SETTING
#undef INT_SETTING
      json::object SpamView(AntiSpamConfiguration &config)
      {
         json::object result;
         for (const auto &field : BoolSettings) result[field.name] = (config.*field.get)();
         for (const auto &field : IntSettings) result[field.name] = (config.*field.get)();
         result["SpamAssassinHost"] = Utf8(config.GetSpamAssassinHost());
         result["PrependSubjectText"] = Utf8(config.GetPrependSubjectText());
         return result;
      }
      void UpdateSpam(AntiSpamConfiguration &config, const json::object &fields)
      {
         if (fields.empty()) throw RequestError{422};
         // Validate every field before the first persistent change.
         for (const auto &field : fields)
         {
            bool known = false;
            for (const auto &rule : BoolSettings) if (field.key() == rule.name)
            {
               known = true;
               if (!field.value().is_bool()) throw RequestError{422};
            }
            for (const auto &rule : IntSettings) if (field.key() == rule.name)
            {
               known = true;
               if (!field.value().is_int64() || field.value().as_int64() < rule.minimum || field.value().as_int64() > rule.maximum)
                  throw RequestError{422};
            }
            if (field.key() == "SpamAssassinHost" || field.key() == "PrependSubjectText")
            {
               known = true;
               Text(fields, field.key() == "SpamAssassinHost" ? "SpamAssassinHost" : "PrependSubjectText", 255);
               Wide(field.value());
            }
            if (!known) throw RequestError{422};
         }
         for (const auto &field : fields)
         {
            for (const auto &rule : BoolSettings) if (field.key() == rule.name) (config.*rule.set)(field.value().as_bool());
            for (const auto &rule : IntSettings) if (field.key() == rule.name) (config.*rule.set)(static_cast<int>(field.value().as_int64()));
            if (field.key() == "SpamAssassinHost") config.SetSpamAssassinHost(Wide(field.value()));
            if (field.key() == "PrependSubjectText") config.SetPrependSubjectText(Wide(field.value()));
         }
      }

      json::value Dispatch(json::object &request)
      {
         const auto *version = request.if_contains("version");
         if (!version || !version->is_int64() || version->as_int64() != 1) throw RequestError{422};
         Text(request, "username", 320);
         const auto password = Text(request, "password", 1024);
         if (password.empty()) throw RequestError{401};
         auto account = ManagementAuthentication::Authenticate(Wide(request.at("username")), Wide(request.at("password")));
         // Do not keep an additional password copy in the parsed JSON longer than necessary.
         auto &secret = request.at("password").as_string();
         SecureZeroMemory(secret.data(), secret.size());
         if (!account) throw RequestError{401};
         const auto operation = Text(request, "operation", 64);
         const auto *argumentValue = request.if_contains("arguments");
         if (!argumentValue || !argumentValue->is_object()) throw RequestError{422};
         const auto &arguments = argumentValue->as_object();
         if (operation == "authenticate") return User(*account);
         if (operation == "domains" || operation == "domain")
         {
            const bool serverAdmin = account->GetAdminLevel() == Account::ServerAdmin;
            Domains domains;
            if (serverAdmin) domains.Refresh(); else domains.Refresh(account->GetDomainID());
            if (operation == "domain")
            {
               const auto id = Id(arguments);
               if (account->GetAdminLevel() == Account::NormalUser || (!serverAdmin && id != account->GetDomainID())) throw RequestError{403};
               auto domain = domains.GetItemByDBID(id);
               if (!domain) throw RequestError{404};
               return DomainView(*domain);
            }
            json::array items;
            for (const auto &domain : domains.GetSnapshot()) items.push_back(DomainView(*domain));
            return json::object{{"items", std::move(items)}};
         }
         if (account->GetAdminLevel() != Account::ServerAdmin) throw RequestError{403};
         auto &config = Configuration::Instance()->GetAntiSpamConfiguration();
         if (operation == "status")
         {
            Domains domains;
            domains.Refresh();
            return json::object{{"version", Utf8(Application::Instance()->GetVersionNumber())},
                                {"state", static_cast<int>(ServerStatus::Instance()->GetState())}, {"domains", domains.GetCount()}};
         }
         if (operation == "antispam.read") return SpamView(config);
         if (operation == "antispam.update") { UpdateSpam(config, arguments); return SpamView(config); }
         if (operation == "spamassassin.test")
         {
            String message;
            return json::object{{"connected", SpamAssassinTestConnect().TestConnect(config.GetSpamAssassinHost(), config.GetSpamAssassinPort(), message)}};
         }
         auto lists = config.GetDNSBlackLists();
         if (operation == "dnsbl.list")
         {
            json::array items;
            for (const auto &item : lists->GetSnapshot()) items.push_back(DnsblView(*item));
            return json::object{{"items", std::move(items)}};
         }
         if (operation == "dnsbl.delete")
         {
            auto id = Id(arguments);
            if (!lists->GetItemByDBID(id)) throw RequestError{404};
            if (!lists->DeleteItemByDBID(id)) throw RequestError{503};
            return json::object{{"deleted", true}};
         }
         if (operation == "dnsbl.create" || operation == "dnsbl.update")
         {
            const bool create = operation == "dnsbl.create";
            const auto *value = create ? &static_cast<const json::value &>(request.at("arguments")) : arguments.if_contains("fields");
            if (!value || !value->is_object() || value->as_object().empty()) throw RequestError{422};
            const auto &fields = value->as_object();
            if (create && fields.size() != 5) throw RequestError{422};
            auto original = create ? nullptr : lists->GetItemByDBID(Id(arguments));
            if (!create && !original) throw RequestError{404};
            auto item = original ? std::make_shared<DNSBlackList>(*original) : std::make_shared<DNSBlackList>();
            for (const auto &field : fields)
            {
               if (field.key() == "Active")
               {
                  if (!field.value().is_bool()) throw RequestError{422};
                  item->SetIsActive(field.value().as_bool());
               }
               else if (field.key() == "Score")
               {
                  if (!field.value().is_int64() || field.value().as_int64() < 0 || field.value().as_int64() > 10000) throw RequestError{422};
                  item->SetScore(static_cast<int>(field.value().as_int64()));
               }
               else if (field.key() == "DNSHost")
               {
                  auto host = Text(fields, "DNSHost", 253);
                  if (host.empty() || host.find_first_not_of("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-") != std::string::npos) throw RequestError{422};
                  item->SetDNSHost(Wide(field.value()));
               }
               else if (field.key() == "ExpectedResult") { Text(fields, "ExpectedResult", 255); item->SetExpectedResult(Wide(field.value())); }
               else if (field.key() == "RejectMessage") { Text(fields, "RejectMessage", 255); item->SetRejectMessage(Wide(field.value())); }
               else throw RequestError{422};
            }
            if (!PersistentDNSBlackList::SaveObject(item) || item->GetID() <= 0) throw RequestError{503};
            lists->Refresh();
            return DnsblView(*item);
         }
         throw RequestError{404};
      }
   }

   ManagementPipe::~ManagementPipe() { Stop(); }

   bool ManagementPipe::Start()
   {
      wchar_t clientSid[256] = {};
      DWORD count = GetEnvironmentVariableW(L"ARZOIEMAIL_MANAGEMENT_CLIENT_SID", clientSid, 256);
      if (count == 0)
         count = GetPrivateProfileStringW(L"Management", L"ApiClientSID", L"", clientSid, 256,
            IniFileSettings::GetInitializationFile().c_str());
      if (count == 0) return true; // Explicitly opt in; no pipe on existing installations.
      if (count >= 255) return false;
      PSID parsedSid = nullptr;
      if (!ConvertStringSidToSidW(clientSid, &parsedSid)) return false;
      bool unsafe = IsWellKnownSid(parsedSid, WinWorldSid) || IsWellKnownSid(parsedSid, WinAuthenticatedUserSid)
                 || IsWellKnownSid(parsedSid, WinBuiltinUsersSid) || IsWellKnownSid(parsedSid, WinBuiltinAdministratorsSid);
      LocalFree(parsedSid);
      if (unsafe) return false;
      HANDLE token = nullptr;
      if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return false;
      DWORD size = 0;
      GetTokenInformation(token, TokenUser, nullptr, 0, &size);
      std::vector<unsigned char> user(size);
      bool ok = GetTokenInformation(token, TokenUser, user.data(), size, &size) != FALSE;
      CloseHandle(token);
      if (!ok) return false;
      LPWSTR serverSid = nullptr;
      if (!ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER *>(user.data())->User.Sid, &serverSid)) return false;
      // Client read/write rights deliberately exclude FILE_CREATE_PIPE_INSTANCE.
      std::wstring acl = L"D:P(A;;GA;;;SY)(A;;GA;;;" + std::wstring(serverSid) + L")(A;;0x12019b;;;" + clientSid + L")";
      LocalFree(serverSid);
      PSECURITY_DESCRIPTOR descriptor = nullptr;
      if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(acl.c_str(), SDDL_REVISION_1, &descriptor, nullptr)) return false;
      SECURITY_ATTRIBUTES attributes = {sizeof(SECURITY_ATTRIBUTES), descriptor, FALSE};
      pipe_ = CreateNamedPipeW(PipeName, PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | FILE_FLAG_FIRST_PIPE_INSTANCE,
         PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS, 1, MaximumFrame, MaximumFrame, 0, &attributes);
      LocalFree(descriptor);
      if (pipe_ == INVALID_HANDLE_VALUE) return false;
      stopped_ = CreateEventW(nullptr, TRUE, FALSE, nullptr);
      if (!stopped_) { CloseHandle(pipe_); pipe_ = INVALID_HANDLE_VALUE; return false; }
      worker_ = std::thread([this] { Run_(); });
      LOG_APPLICATION("Native management endpoint started with restricted local access.");
      return true;
   }

   void ManagementPipe::Stop()
   {
      if (stopped_) SetEvent(stopped_);
      if (worker_.joinable()) worker_.join();
      if (pipe_ != INVALID_HANDLE_VALUE) { CloseHandle(pipe_); pipe_ = INVALID_HANDLE_VALUE; }
      if (stopped_) { CloseHandle(stopped_); stopped_ = nullptr; }
   }

   bool ManagementPipe::Transfer_(void *buffer, DWORD length, bool write)
   {
      auto bytes = static_cast<unsigned char *>(buffer);
      DWORD offset = 0;
      const ULONGLONG deadline = GetTickCount64() + 5000;
      while (offset < length)
      {
         if (GetTickCount64() >= deadline) return false;
         OVERLAPPED overlapped = {};
         overlapped.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
         if (!overlapped.hEvent) return false;
         DWORD done = 0;
         BOOL ok = write ? WriteFile(pipe_, bytes + offset, length - offset, &done, &overlapped)
                         : ReadFile(pipe_, bytes + offset, length - offset, &done, &overlapped);
         if (!ok && GetLastError() == ERROR_IO_PENDING)
         {
            HANDLE events[] = {stopped_, overlapped.hEvent};
            const ULONGLONG now = GetTickCount64();
            const DWORD remaining = now >= deadline ? 0 : static_cast<DWORD>(deadline - now);
            if (WaitForMultipleObjects(2, events, FALSE, remaining) != WAIT_OBJECT_0 + 1)
            {
               CancelIoEx(pipe_, &overlapped);
               GetOverlappedResult(pipe_, &overlapped, &done, TRUE);
               CloseHandle(overlapped.hEvent);
               return false;
            }
            ok = GetOverlappedResult(pipe_, &overlapped, &done, FALSE);
         }
         CloseHandle(overlapped.hEvent);
         if (!ok || done == 0) return false;
         offset += done;
      }
      return true;
   }

   void ManagementPipe::Run_()
   {
      while (WaitForSingleObject(stopped_, 0) != WAIT_OBJECT_0)
      {
         OVERLAPPED connect = {};
         connect.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
         if (!connect.hEvent) return;
         bool connected = ConnectNamedPipe(pipe_, &connect) != FALSE;
         if (!connected)
         {
            DWORD error = GetLastError();
            if (error == ERROR_PIPE_CONNECTED) connected = true;
            else if (error == ERROR_IO_PENDING)
            {
               HANDLE events[] = {stopped_, connect.hEvent};
               if (WaitForMultipleObjects(2, events, FALSE, INFINITE) == WAIT_OBJECT_0 + 1)
               {
                  DWORD unused = 0;
                  connected = GetOverlappedResult(pipe_, &connect, &unused, FALSE) != FALSE;
               }
               else
               {
                  CancelIoEx(pipe_, &connect);
                  DWORD unused = 0;
                  GetOverlappedResult(pipe_, &connect, &unused, TRUE);
               }
            }
         }
         CloseHandle(connect.hEvent);
         if (!connected) { DisconnectNamedPipe(pipe_); continue; }
         DWORD size = 0;
         if (Transfer_(&size, sizeof(size), false) && size > 1 && size <= MaximumFrame)
         {
            std::vector<char> input(size);
            if (Transfer_(input.data(), size, false))
            {
               json::object response;
               try
               {
                  json::parse_options options;
                  options.max_depth = 16;
                  boost::system::error_code error;
                  auto request = json::parse(json::string_view(input.data(), input.size()), error, {}, options);
                  if (error || !request.is_object()) throw RequestError{422};
                  response = {{"status", 200}, {"data", Dispatch(request.as_object())}};
               }
               catch (const RequestError &error) { response = {{"status", error.status}}; }
               catch (...) { response = {{"status", 503}}; }
               SecureZeroMemory(input.data(), input.size());
               auto output = json::serialize(response);
               if (output.size() > MaximumFrame)
                  output = json::serialize(json::object{{"status", 413}});
               size = static_cast<DWORD>(output.size());
               if (Transfer_(&size, sizeof(size), true) && Transfer_(output.data(), size, true))
               {
                  unsigned char acknowledgement = 0;
                  Transfer_(&acknowledgement, 1, false);
               }
            }
            SecureZeroMemory(input.data(), input.size());
         }
         DisconnectNamedPipe(pipe_);
      }
   }
}
