#include "stdafx.h"
#include "ManagementPipe.h"
#include "ManagementAuthentication.h"
#include "../BO/Account.h"
#include "../BO/Accounts.h"
#include "../BO/Aliases.h"
#include "../BO/DistributionLists.h"
#include "../BO/DistributionListRecipients.h"
#include "../Persistence/PersistentDistributionListRecipient.h"
#include "../Persistence/PersistentDomain.h"
#include "../Persistence/PersistenceMode.h"
#include "../Util/Hashing/PasswordHasher.h"
#include "../Util/Crypt.h"
#include "../BO/Domains.h"
#include "../BO/DNSBlackLists.h"
#include "../Persistence/PersistentDNSBlacklist.h"
#include "../AntiSpam/SpamAssassin/SpamAssassinTestConnect.h"
#include "../Util/Unicode.h"
#include "../Util/ServerStatus.h"
#include "../Util/Time.h"
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
      struct SecretWipe
      {
         json::value *value;
         ~SecretWipe()
         {
            if (value && value->is_string())
            {
               auto &text = value->as_string();
               SecureZeroMemory(text.data(), text.size());
            }
         }
      };

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
                 {"dkimEnabled", domain.GetDKIMEnabled()}, {"selector", std::string(domain.GetDKIMSelector())},
                 {"maxMessageSizeKB", domain.GetMaxMessageSize()}, {"maxSizeMB", domain.GetMaxSizeMB()},
                 {"maxAccountSizeMB", domain.GetMaxAccountSize()}};
      }
      json::object AccountView(const Account &account)
      {
         // Never return password hashes, AD credentials or private configuration.
         return {{"id", account.GetID()}, {"domainId", account.GetDomainID()},
                 {"address", Utf8(account.GetAddress())}, {"active", account.GetActive()},
                 {"level", static_cast<int>(account.GetAdminLevel())}, {"maxSizeMB", account.GetAccountMaxSize()}};
      }
      __int64 Number(const json::object &fields, const char *name, __int64 maximum, __int64 fallback = 0)
      {
         const auto *value = fields.if_contains(name);
         if (!value) return fallback;
         if (!value->is_int64() || value->as_int64() < 0 || value->as_int64() > maximum) throw RequestError{422};
         return value->as_int64();
      }
      template<class T, class View>
      json::object Page(const std::vector<std::shared_ptr<T>> &items, const json::object &arguments, View view)
      {
         const auto offset = Number(arguments, "offset", 2147483647);
         const auto limit = Number(arguments, "limit", 100, 50);
         if (!limit) throw RequestError{422};
         String search;
         if (arguments.if_contains("search")) { Text(arguments, "search", 255); search = Wide(arguments.at("search")); search.ToLower(); }
         json::array result;
         __int64 total = 0;
         for (const auto &item : items)
         {
            String name = item->GetName(); name.ToLower();
            if (!search.IsEmpty() && name.Find(search) < 0) continue;
            if (total >= offset && result.size() < static_cast<size_t>(limit)) result.push_back(view(*item));
            ++total;
         }
         return {{"items", std::move(result)}, {"total", total}, {"offset", offset}, {"limit", limit}};
      }
      // Loaded objects are request-local. All fields are checked before SaveObject, which also
      // enforces shared quota/duplicate/address rules and invalidates the existing caches.
      json::value ManageObjects(const std::string &operation, json::object &arguments, const Account &actor)
      {
         const bool server = actor.GetAdminLevel() == Account::ServerAdmin;
         if (actor.GetAdminLevel() == Account::NormalUser) throw RequestError{403};
         const bool accountOperation = operation.rfind("accounts.", 0) == 0;
         const bool create = operation == "domains.create" || operation == "accounts.create";
         const bool remove = operation == "domains.delete" || operation == "accounts.delete";
         const bool list = operation == "domains.list" || operation == "accounts.list";
         if (operation != "domains.list" && operation != "domains.create" && operation != "domains.update"
             && operation != "domains.delete" && operation != "accounts.list" && operation != "accounts.read"
             && operation != "accounts.create" && operation != "accounts.update" && operation != "accounts.delete") throw RequestError{404};
         if (!accountOperation && (create || remove) && !server) throw RequestError{403};
         if (operation == "domains.list")
         {
            Domains domains;
            if (server) domains.Refresh(); else domains.Refresh(actor.GetDomainID());
            return Page(domains.GetVector(), arguments, DomainView);
         }
         const auto domainId = accountOperation ? Number(arguments, "domainId", 9223372036854775807LL) : (create ? 0 : Id(arguments));
         if (!create || accountOperation)
            if (!server && domainId != actor.GetDomainID()) throw RequestError{403};
         auto domain = std::make_shared<Domain>();
         if ((!create || accountOperation) && !PersistentDomain::ReadObject(domain, domainId)) throw RequestError{404};
         if (accountOperation && list)
         {
            Accounts accounts(domainId); accounts.Refresh();
            return Page(accounts.GetVector(), arguments, AccountView);
         }
         auto target = std::make_shared<Account>();
         if (accountOperation && !create)
         {
            if (!PersistentAccount::ReadObject(target, Id(arguments)) || target->GetDomainID() != domainId) throw RequestError{404};
            // Check the persisted role, before accepting any requested role change.
            if (!server && target->GetAdminLevel() == Account::ServerAdmin) throw RequestError{403};
            if (operation == "accounts.read") return AccountView(*target);
         }
         if (remove)
         {
            if (accountOperation && target->GetID() == actor.GetID()) throw RequestError{409};
            if (!accountOperation && actor.GetDomainID() == domainId) throw RequestError{409};
            if (!(accountOperation ? PersistentAccount::DeleteObject(target) : PersistentDomain::DeleteObject(domain))) throw RequestError{503};
            return json::object{{"deleted", true}};
         }
         auto *value = arguments.if_contains("fields");
         if (!value || !value->is_object() || value->as_object().empty()) throw RequestError{422};
         auto &fields = value->as_object();
         SecretWipe wipe{fields.if_contains("password")};
         for (const auto &field : fields)
         {
            const auto key = field.key();
            if (key == "active") { if (!field.value().is_bool()) throw RequestError{422}; }
            else if (key == "maxSizeMB" || (!accountOperation && (key == "maxMessageSizeKB" || key == "maxAccountSizeMB")))
            {
               if (!server && !accountOperation) throw RequestError{403};
               Number(fields, std::string(key).c_str(), 2147483647);
            }
            else if (accountOperation && key == "level")
            {
               auto level = Number(fields, "level", 2);
               if (!server && (level == 2 || (target->GetID() == actor.GetID() && level != actor.GetAdminLevel()))) throw RequestError{403};
            }
            else if (accountOperation && key == "password")
            {
               if (Text(fields, "password", 1024).empty()) throw RequestError{422};
               Wide(field.value());
            }
            else if (create && key == (accountOperation ? "address" : "name"))
            {
               if (Text(fields, accountOperation ? "address" : "name", accountOperation ? 320 : 253).empty()) throw RequestError{422};
               Wide(field.value());
            }
            else throw RequestError{422};
         }
         if (create)
         {
            if (accountOperation)
            {
               if (!fields.if_contains("address") || !fields.if_contains("password")) throw RequestError{422};
               target->SetDomainID(domainId); target->SetAddress(Wide(fields.at("address")));
            }
            else
            {
               if (!fields.if_contains("name")) throw RequestError{422};
               domain->SetName(Wide(fields.at("name")));
            }
         }
         if (accountOperation)
         {
            if (fields.if_contains("active")) target->SetActive(fields.at("active").as_bool());
            if (fields.if_contains("maxSizeMB")) target->SetAccountMaxSize(static_cast<long>(Number(fields, "maxSizeMB", 2147483647)));
            if (fields.if_contains("level")) target->SetAdminLevel(static_cast<Account::AdminLevel>(Number(fields, "level", 2)));
            if (fields.if_contains("password"))
            {
               if (target->GetIsAD()) throw RequestError{422}; // AD password changes belong to the directory.
               auto hash = PasswordHasher::Hash(Wide(fields.at("password")));
               if (hash.GetLength() == 0) throw RequestError{503};
               target->SetPassword(hash); target->SetPasswordEncryption(Crypt::ETPHC);
            }
            String error;
            if (!PersistentAccount::SaveObject(target, error, true, PersistenceModeNormal)) throw RequestError{error.IsEmpty() ? 503 : 422};
            return AccountView(*target);
         }
         if (fields.if_contains("active")) domain->SetIsActive(fields.at("active").as_bool());
         if (fields.if_contains("maxSizeMB")) domain->SetMaxSizeMB(static_cast<int>(Number(fields, "maxSizeMB", 2147483647)));
         if (fields.if_contains("maxAccountSizeMB")) domain->SetMaxAccountSize(static_cast<int>(Number(fields, "maxAccountSizeMB", 2147483647)));
         if (fields.if_contains("maxMessageSizeKB")) domain->SetMaxMessageSize(static_cast<int>(Number(fields, "maxMessageSizeKB", 2147483647)));
         String error;
         if (!PersistentDomain::SaveObject(domain, error, PersistenceModeNormal)) throw RequestError{error.IsEmpty() ? 503 : 422};
         return DomainView(*domain);
      }
      String MailAddress(const json::object &fields, const char *key, bool optional = false)
      {
         const auto input = Text(fields, key, 255);
         for (const unsigned char character : input)
            if (character < 32 || character == 127) throw RequestError{422};
         String address = Wide(fields.at(key));
         if (optional && address.IsEmpty()) return address;
         if (!StringParser::IsValidEmailAddress(address) || address.FindOneOf(_T("\r\n\t ,;\"")) != -1)
            throw RequestError{422};
         return address;
      }
      json::object MailboxBehaviorView(const Account &account)
      {
         return {{"forwardEnabled", account.GetForwardEnabled()}, {"forwardAddress", Utf8(account.GetForwardAddress())},
                 {"forwardKeepOriginal", account.GetForwardKeepOriginal()}, {"forwardAbortSpamFlagged", account.GetForwardAbortSpamFlagged()},
                 {"autoReplyEnabled", account.GetVacationMessageIsOn()}, {"autoReplySubject", Utf8(account.GetVacationSubject())},
                 {"autoReplyBody", Utf8(account.GetVacationMessage())}, {"autoReplyExpires", account.GetVacationExpires()},
                 {"autoReplyExpiresDate", Utf8(account.GetVacationExpiresDate().Left(10))}, {"autoReplyAbortSpamFlagged", account.GetVacationAbortSpamFlagged()}};
      }
      json::value ManageMailboxBehavior(const std::string &operation, json::object &arguments, const Account &actor)
      {
         if (actor.GetAdminLevel() == Account::NormalUser) throw RequestError{403};
         const bool server = actor.GetAdminLevel() == Account::ServerAdmin;
         const auto domainId = Number(arguments, "domainId", 9223372036854775807LL);
         if (!server && domainId != actor.GetDomainID()) throw RequestError{403};
         auto target = std::make_shared<Account>();
         if (!PersistentAccount::ReadObject(target, Id(arguments)) || target->GetDomainID() != domainId) throw RequestError{404};
         if (!server && target->GetAdminLevel() == Account::ServerAdmin) throw RequestError{403};
         if (operation == "accounts.behavior.read") return MailboxBehaviorView(*target);
         if (operation != "accounts.behavior.update") throw RequestError{404};
         auto *value = arguments.if_contains("fields");
         if (!value || !value->is_object() || value->as_object().empty()) throw RequestError{422};
         const auto &fields = value->as_object();
         // Validate the entire request before invoking setters (disabling vacation clears
         // the mail engine's per-sender reply history as an existing BO side effect).
         for (const auto &field : fields)
         {
            const auto key = field.key();
            if (key == "forwardEnabled" || key == "forwardKeepOriginal" || key == "forwardAbortSpamFlagged"
                || key == "autoReplyEnabled" || key == "autoReplyExpires" || key == "autoReplyAbortSpamFlagged")
            { if (!field.value().is_bool()) throw RequestError{422}; }
            else if (key == "forwardAddress") MailAddress(fields, "forwardAddress", true);
            else if (key == "autoReplySubject" || key == "autoReplyBody")
            {
               const auto text = Text(fields, std::string(key).c_str(), key == "autoReplyBody" ? 16000 : 255);
               Wide(field.value());
               for (const unsigned char character : text)
                  if ((character < 32 && !(key == "autoReplyBody" && (character == '\r' || character == '\n' || character == '\t')))
                      || character == 127) throw RequestError{422};
            }
            else if (key == "autoReplyExpiresDate")
            {
               const auto date = Text(fields, "autoReplyExpiresDate", 10);
               for (size_t index = 0; index < date.size(); ++index)
                  if (index != 4 && index != 7 && (date[index] < '0' || date[index] > '9')) throw RequestError{422};
               if (!date.empty() && (date.size() != 10 || date[4] != '-' || date[7] != '-'
                   || date.substr(0, 4) < "1900" || !Time::IsValidSystemDate(Wide(field.value())))) throw RequestError{422};
            }
            else throw RequestError{422};
         }
         const auto current = MailboxBehaviorView(*target);
         const auto effective = [&](const char *key) -> const json::value & {
            const auto *field = fields.if_contains(key); return field ? *field : current.at(key);
         };
         const auto address = Wide(effective("forwardAddress"));
         if (effective("forwardEnabled").as_bool())
         {
            json::object check{{"address", effective("forwardAddress")}};
            MailAddress(check, "address");
            if (address.CompareNoCase(target->GetAddress()) == 0) throw RequestError{422};
         }
         if (effective("autoReplyExpires").as_bool() && effective("autoReplyExpiresDate").as_string().empty()) throw RequestError{422};
         if (fields.if_contains("forwardAddress")) target->SetForwardAddress(address);
         if (fields.if_contains("forwardEnabled")) target->SetForwardEnabled(fields.at("forwardEnabled").as_bool());
         if (fields.if_contains("forwardKeepOriginal")) target->SetForwardKeepOriginal(fields.at("forwardKeepOriginal").as_bool());
         if (fields.if_contains("forwardAbortSpamFlagged")) target->SetForwardAbortSpamFlagged(fields.at("forwardAbortSpamFlagged").as_bool());
         if (fields.if_contains("autoReplySubject")) target->SetVacationSubject(Wide(fields.at("autoReplySubject")));
         if (fields.if_contains("autoReplyBody")) target->SetVacationMessage(Wide(fields.at("autoReplyBody")));
         if (fields.if_contains("autoReplyExpiresDate"))
         {
            // All supported schemas require a non-null timestamp even while expiry is off.
            const auto date = Wide(fields.at("autoReplyExpiresDate"));
            target->SetVacationExpiresDate(date.IsEmpty() ? Time::GetCurrentDate() : date);
         }
         if (fields.if_contains("autoReplyExpires")) target->SetVacationExpires(fields.at("autoReplyExpires").as_bool());
         if (fields.if_contains("autoReplyAbortSpamFlagged")) target->SetVacationAbortSpamFlagged(fields.at("autoReplyAbortSpamFlagged").as_bool());
         if (fields.if_contains("autoReplyEnabled")) target->SetVacationMessageIsOn(fields.at("autoReplyEnabled").as_bool());
         String error;
         if (!PersistentAccount::SaveObject(target, error, true, PersistenceModeNormal)) throw RequestError{error.IsEmpty() ? 503 : 422};
         return MailboxBehaviorView(*target);
      }
      json::object AliasView(const Alias &alias)
      {
         return {{"id", alias.GetID()}, {"domainId", alias.GetDomainID()}, {"address", Utf8(alias.GetName())},
                 {"target", Utf8(alias.GetValue())}, {"active", alias.GetIsActive()}};
      }
      json::object ListView(const DistributionList &list)
      {
         return {{"id", list.GetID()}, {"domainId", list.GetDomainID()}, {"address", Utf8(list.GetAddress())},
                 {"active", list.GetActive()}, {"mode", static_cast<int>(list.GetListMode())},
                 {"requireAuth", list.GetRequireAuth()}, {"requireAddress", Utf8(list.GetRequireAddress())}};
      }
      json::object MemberView(const DistributionListRecipient &member)
      {
         return {{"id", member.GetID()}, {"listId", member.GetListID()}, {"address", Utf8(member.GetAddress())}};
      }
      json::value ManageAddressing(const std::string &operation, const json::object &arguments, const Account &actor)
      {
         if (actor.GetAdminLevel() == Account::NormalUser) throw RequestError{403};
         const auto action = operation.substr(operation.find('.') + 1);
         if (action != "list" && action != "read" && action != "create" && action != "update" && action != "delete") throw RequestError{404};
         const bool aliasOperation = operation.rfind("aliases.", 0) == 0;
         const bool memberOperation = operation.rfind("members.", 0) == 0;
         const bool create = action == "create";
         const auto domainId = Number(arguments, "domainId", 9223372036854775807LL);
         if (!domainId) throw RequestError{422};
         if (actor.GetAdminLevel() != Account::ServerAdmin && actor.GetDomainID() != domainId) throw RequestError{403};
         auto domain = std::make_shared<Domain>();
         if (!PersistentDomain::ReadObject(domain, domainId)) throw RequestError{404};
         if (action == "list" && !memberOperation)
         {
            if (aliasOperation) { Aliases items(domainId); items.Refresh(); return Page(items.GetVector(), arguments, AliasView); }
            DistributionLists items(domainId); items.Refresh(); return Page(items.GetVector(), arguments, ListView);
         }
         auto alias = std::make_shared<Alias>();
         auto list = std::make_shared<DistributionList>();
         auto member = std::make_shared<DistributionListRecipient>();
         std::shared_ptr<DistributionListRecipients> members;
         if (memberOperation)
         {
            const auto listId = Number(arguments, "listId", 9223372036854775807LL);
            if (!listId) throw RequestError{422};
            if (!PersistentDistributionList::ReadObject(list, listId) || list->GetDomainID() != domainId) throw RequestError{404};
            members = list->GetMembers();
            if (action == "list") return Page(members->GetVector(), arguments, MemberView);
            if (!create)
            {
               member = members->GetItemByDBID(Id(arguments));
               if (!member) throw RequestError{404};
            }
         }
         else if (!create)
         {
            const auto id = Id(arguments);
            if (aliasOperation)
            {
               if (!PersistentAlias::ReadObject(alias, id) || alias->GetDomainID() != domainId) throw RequestError{404};
            }
            else if (!PersistentDistributionList::ReadObject(list, id) || list->GetDomainID() != domainId) throw RequestError{404};
         }
         if (action == "read") return memberOperation ? MemberView(*member) : aliasOperation ? AliasView(*alias) : ListView(*list);
         if (action == "delete")
         {
            bool success = memberOperation ? PersistentDistributionListRecipient::DeleteObject(member)
               : aliasOperation ? PersistentAlias::DeleteObject(alias) : PersistentDistributionList::DeleteObject(list);
            if (!success) throw RequestError{503};
            return json::object{{"deleted", true}};
         }
         const auto *value = arguments.if_contains("fields");
         if (!value || !value->is_object() || value->as_object().empty()) throw RequestError{422};
         const auto &fields = value->as_object();
         for (const auto &field : fields)
         {
            const auto key = field.key();
            if (key == "address" && (create || memberOperation)) MailAddress(fields, "address");
            else if (key == "target" && aliasOperation) MailAddress(fields, "target");
            else if (!memberOperation && (key == "active" || (!aliasOperation && key == "requireAuth")))
            { if (!field.value().is_bool()) throw RequestError{422}; }
            else if (!aliasOperation && !memberOperation && key == "mode") Number(fields, "mode", 3);
            else if (!aliasOperation && !memberOperation && key == "requireAddress") MailAddress(fields, "requireAddress", true);
            else throw RequestError{422};
         }
         if (create && !fields.if_contains("address")) throw RequestError{422};
         String error;
         bool success;
         if (memberOperation)
         {
            if (create) member->SetListID(list->GetID());
            if (fields.if_contains("address")) member->SetAddress(MailAddress(fields, "address"));
            if (member->GetAddress().CompareNoCase(list->GetAddress()) == 0) throw RequestError{422};
            for (const auto &existing : members->GetVector())
               if (existing->GetID() != member->GetID() && existing->GetAddress().CompareNoCase(member->GetAddress()) == 0) throw RequestError{422};
            success = PersistentDistributionListRecipient::SaveObject(member, error, PersistenceModeNormal);
         }
         else if (aliasOperation)
         {
            if (create)
            {
               if (!fields.if_contains("target")) throw RequestError{422};
               alias->SetDomainID(domainId); alias->SetName(MailAddress(fields, "address"));
            }
            if (fields.if_contains("target")) alias->SetValue(MailAddress(fields, "target"));
            if (fields.if_contains("active")) alias->SetIsActive(fields.at("active").as_bool());
            if (alias->GetName().CompareNoCase(alias->GetValue()) == 0) throw RequestError{422};
            success = PersistentAlias::SaveObject(alias, error, PersistenceModeNormal);
         }
         else
         {
            if (create)
            {
               list->SetDomainID(domainId); list->SetAddress(MailAddress(fields, "address"));
               list->SetRequireAuth(true); list->SetListMode(DistributionList::LMMembership);
            }
            if (fields.if_contains("active")) list->SetActive(fields.at("active").as_bool());
            if (fields.if_contains("requireAuth")) list->SetRequireAuth(fields.at("requireAuth").as_bool());
            if (fields.if_contains("mode")) list->SetListMode(static_cast<DistributionList::ListMode>(Number(fields, "mode", 3)));
            if (fields.if_contains("requireAddress")) list->SetRequireAddress(MailAddress(fields, "requireAddress", true));
            if (list->GetListMode() == DistributionList::LMAnnouncement
                && (list->GetRequireAddress().IsEmpty() || !StringParser::IsValidEmailAddress(list->GetRequireAddress()))) throw RequestError{422};
            success = PersistentDistributionList::SaveObject(list, error, PersistenceModeNormal);
         }
         if (!success) throw RequestError{error.IsEmpty() ? 503 : 422};
         return memberOperation ? MemberView(*member) : aliasOperation ? AliasView(*alias) : ListView(*list);
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
         auto *argumentValue = request.if_contains("arguments");
         if (!argumentValue || !argumentValue->is_object()) throw RequestError{422};
         auto &arguments = argumentValue->as_object();
         if (operation == "authenticate") return User(*account);
         if (operation.rfind("accounts.behavior.", 0) == 0)
            return ManageMailboxBehavior(operation, arguments, *account);
         if (operation.rfind("aliases.", 0) == 0 || operation.rfind("lists.", 0) == 0 || operation.rfind("members.", 0) == 0)
            return ManageAddressing(operation, arguments, *account);
         if (operation.rfind("domains.", 0) == 0 || operation.rfind("accounts.", 0) == 0)
            return ManageObjects(operation, arguments, *account);
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
