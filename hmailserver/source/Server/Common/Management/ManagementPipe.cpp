#include "stdafx.h"
#include "ManagementPipe.h"
#include "ManagementAuthentication.h"
#include "../BO/Account.h"
#include "../BO/Accounts.h"
#include "../BO/DomainAliases.h"
#include "../BO/Routes.h"
#include "../BO/RouteAddresses.h"
#include "../BO/Rules.h"
#include "../BO/RuleCriterias.h"
#include "../BO/RuleActions.h"
#include "../BO/MessageRecipients.h"
#include "../BO/Message.h"
#include "../Persistence/PersistentMessage.h"
#include "../BO/MessageRecipient.h"
#include "../../SMTP/DeliveryQueue.h"
#include "../../SMTP/SMTPDeliveryManager.h"
#include "../../SMTP/SMTPConfiguration.h"
#include "../../SMTP/SRS/SRS.h"
#include "../../IMAP/IMAPConfiguration.h"
#include "../Persistence/PersistentMessageMetaData.h"
#include <boost/asio/ip/address.hpp>
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
#include "../Util/EmailAllUsers.h"
#include "../BO/ServerMessage.h"
#include "../BO/ServerMessages.h"
#include "../Persistence/PersistentServerMessage.h"
#include "../Diagnostics/Diagnostic.h"
#include "../TCPIP/DNSResolver.h"
#include "../TCPIP/HostNameAndIpAddress.h"
#include "../BO/FetchAccount.h"
#include "../BO/FetchAccounts.h"
#include "../Persistence/PersistentFetchAccount.h"
#include "../../ExternalFetcher/ExternalFetchManager.h"
#include "../BO/Group.h"
#include "../BO/Groups.h"
#include "../BO/GroupMember.h"
#include "../BO/GroupMembers.h"
#include "../Persistence/PersistentGroup.h"
#include "../Persistence/PersistentGroupMember.h"
#include "../BO/IMAPFolder.h"
#include "../BO/IMAPFolders.h"
#include "../BO/ACLPermission.h"
#include "../BO/ACLPermissions.h"
#include "../Persistence/PersistentIMAPFolder.h"
#include "../Persistence/PersistentACLPermission.h"
#include "../Util/Encoding/ModifiedUTF7.h"
#include "../Application/Backup.h"
#include "../Application/BackupManager.h"
#include "../Util/SecretProtector.h"
#include "../Persistence/SecretMigration.h"
#include "../BO/WhiteListAddress.h"
#include "../BO/WhiteListAddresses.h"
#include "../Persistence/PersistentWhiteListAddress.h"
#include "../BO/GreyListingWhiteAddress.h"
#include "../BO/GreyListingWhiteAddresses.h"
#include "../Persistence/PersistentGreyListingWhiteAddress.h"
#include "../AntiVirus/AntiVirusConfiguration.h"
#include "../AntiVirus/VirusScannerTester.h"
#include "../Threading/Task.h"
#include "../Threading/WorkQueue.h"
#include "../Util/Time.h"
#include "../Application/PropertySet.h"
#include "../BO/SURBLServers.h"
#include "../BO/BlockedAttachments.h"
#include "../BO/IncomingRelays.h"
#include "../BO/SecurityRanges.h"
#include "../Cache/Cache.h"
#include "../SQL/IPAddressSQLHelper.h"
#include "../AntiSpam/Bayes/BayesStore.h"
#include "../AntiSpam/Bayes/BayesTrainer.h"
#include "../BO/Messages.h"
#include "../../IMAP/IMAPFolderContainer.h"
#include "../../IMAP/MessagesContainer.h"
#include <functional>
#include <string_view>
#include <array>
#include <mutex>
#include <regex>
#include <openssl/hmac.h>
#include <openssl/rand.h>
#include <openssl/ssl.h>
#include <openssl/err.h>
#include <openssl/pem.h>
#include <openssl/rsa.h>
#include <openssl/evp.h>
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
         // Authentication can upgrade a legacy hash. Derive the session binding
         // from persisted state afterward, never from the pre-upgrade cache object.
         auto stored = std::make_shared<Account>();
         const Account *current = &account;
         if (account.GetID() > 0)
         {
            if (!PersistentAccount::ReadObject(stored, account.GetID()) || !stored->GetActive()
                || stored->GetDomainID() != account.GetDomainID()) throw RequestError{401};
            current = stored.get();
         }
         static const auto key = [] {
            std::array<unsigned char, 32> bytes{};
            if (RAND_priv_bytes(bytes.data(), static_cast<int>(bytes.size())) != 1) throw RequestError{503};
            return bytes;
         }();
         const auto hash = current->GetID() > 0 ? current->GetPassword() : IniFileSettings::Instance()->GetAdministratorPassword();
         std::string material = json::serialize(json::object{{"account", current->GetID()}, {"domain", current->GetDomainID()},
            {"address", Utf8(current->GetAddress())}, {"directory", current->GetIsAD()},
            {"directoryDomain", Utf8(current->GetADDomain())}, {"directoryUser", Utf8(current->GetADUsername())},
            {"format", current->GetPasswordEncryption()}});
         material += '\n'; material += Utf8(hash);
         unsigned char digest[EVP_MAX_MD_SIZE]{};
         unsigned int length = 0;
         const bool ok = HMAC(EVP_sha256(), key.data(), static_cast<int>(key.size()),
            reinterpret_cast<const unsigned char *>(material.data()), material.size(), digest, &length) != nullptr;
         SecureZeroMemory(material.data(), material.size());
         if (!ok || length != 32) throw RequestError{503};
         const char hex[] = "0123456789ABCDEF";
         std::string stamp(64, '0');
         for (unsigned int index = 0; index < length; ++index)
         {
            stamp[index * 2] = hex[digest[index] >> 4];
            stamp[index * 2 + 1] = hex[digest[index] & 15];
         }
         return {{"address", Utf8(current->GetAddress())}, {"level", static_cast<int>(current->GetAdminLevel())},
                 {"domainId", current->GetDomainID()}, {"accountId", current->GetID()}, {"securityStamp", stamp}};
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
      json::object SignatureView(const Account &account)
      {
         return {{"signatureEnabled", account.GetEnableSignature()}, {"signaturePlainText", Utf8(account.GetSignaturePlainText())},
                 {"signatureHtml", Utf8(account.GetSignatureHTML())}};
      }
      // Self-service must never persist the whole account: a stale object could restore
      // an administrator's password, role, directory binding or disabled state.
      void SaveSelfPatch(std::shared_ptr<Account> &target, SQLStatement &statement,
         const String &expectedPassword, const std::function<bool(const Account &)> &verify)
      {
         statement.SetWhereClause("accountid = @SELF_ID and accountdomainid = @SELF_DOMAIN and accountactive = 1 "
            "and accountadminlevel = @SELF_ROLE and accountisad = @SELF_AD "
            "and accountpassword = @SELF_PASSWORD and accountaddress = @SELF_ADDRESS");
         auto command = statement.GetCommand();
         // Compact binds by ordinal: SET parameters precede these WHERE parameters.
         command.AddParameter("@SELF_ID", target->GetID());
         command.AddParameter("@SELF_DOMAIN", target->GetDomainID());
         command.AddParameter("@SELF_ROLE", static_cast<int>(target->GetAdminLevel()));
         command.AddParameter("@SELF_AD", target->GetIsAD() ? 1 : 0);
         command.AddParameter("@SELF_PASSWORD", expectedPassword);
         command.AddParameter("@SELF_ADDRESS", target->GetAddress());
         if (!Application::Instance()->GetDBManager()->Execute(command)) throw RequestError{503};
         Cache<Account>::Instance()->RemoveObject(target->GetID());
         auto stored = std::make_shared<Account>();
         if (!PersistentAccount::ReadObject(stored, target->GetID())) throw RequestError{503};
         if (!stored->GetActive() || stored->GetDomainID() != target->GetDomainID()
             || stored->GetAdminLevel() != target->GetAdminLevel() || stored->GetIsAD() != target->GetIsAD()
             || stored->GetAddress() != target->GetAddress() || !verify(*stored)) throw RequestError{409};
         target = stored;
      }
      json::value ManageSignature(const std::string &operation, const json::object &arguments, std::shared_ptr<Account> target,
         bool selfService = false)
      {
         if (operation == "accounts.signature.read") return SignatureView(*target);
         if (operation != "accounts.signature.update") throw RequestError{404};
         const auto *value = arguments.if_contains("fields");
         if (!value || !value->is_object() || value->as_object().empty()) throw RequestError{422};
         const auto &fields = value->as_object();
         for (const auto &field : fields)
         {
            if (field.key() == "signatureEnabled")
            { if (!field.value().is_bool()) throw RequestError{422}; }
            else if (field.key() == "signaturePlainText" || field.key() == "signatureHtml")
            {
               const auto text = Text(fields, std::string(field.key()).c_str(), 16000);
               Wide(field.value());
               for (const unsigned char character : text)
                  if ((character < 32 && character != '\r' && character != '\n' && character != '\t') || character == 127)
                     throw RequestError{422};
            }
            else throw RequestError{422};
         }
         if (fields.if_contains("signatureEnabled")) target->SetEnableSignature(fields.at("signatureEnabled").as_bool());
         if (fields.if_contains("signaturePlainText")) target->SetSignaturePlainText(Wide(fields.at("signaturePlainText")));
         if (fields.if_contains("signatureHtml")) target->SetSignatureHTML(Wide(fields.at("signatureHtml")));
         if (selfService)
         {
            SQLStatement statement(SQLStatement::STUpdate, "hm_accounts");
            if (fields.if_contains("signatureEnabled")) statement.AddColumn("accountenablesignature", target->GetEnableSignature());
            if (fields.if_contains("signaturePlainText")) statement.AddColumn("accountsignatureplaintext", target->GetSignaturePlainText());
            if (fields.if_contains("signatureHtml")) statement.AddColumn("accountsignaturehtml", target->GetSignatureHTML());
            SaveSelfPatch(target, statement, target->GetPassword(), [&](const Account &stored) {
               const auto view = SignatureView(stored);
               for (const auto &field : fields) if (view.at(field.key()) != field.value()) return false;
               return stored.GetPassword() == target->GetPassword();
            });
         }
         else
         {
            String error;
            if (!PersistentAccount::SaveObject(target, error, true, PersistenceModeNormal)) throw RequestError{error.IsEmpty() ? 503 : 422};
         }
         return SignatureView(*target);
      }
      json::value ManageMailboxSettings(const std::string &operation, json::object &arguments, const Account &actor, bool selfService = false)
      {
         if (actor.GetAdminLevel() == Account::NormalUser && !selfService) throw RequestError{403};
         if (selfService && (actor.GetID() <= 0 || Id(arguments) != actor.GetID()
             || Number(arguments, "domainId", 9223372036854775807LL) != actor.GetDomainID())) throw RequestError{403};
         const bool server = actor.GetAdminLevel() == Account::ServerAdmin;
         const auto domainId = Number(arguments, "domainId", 9223372036854775807LL);
         if (!server && domainId != actor.GetDomainID()) throw RequestError{403};
         auto target = std::make_shared<Account>();
         if (!PersistentAccount::ReadObject(target, Id(arguments)) || target->GetDomainID() != domainId) throw RequestError{404};
         if (!server && target->GetAdminLevel() == Account::ServerAdmin) throw RequestError{403};
         if (operation == "accounts.profile.read" || operation == "accounts.profile.update")
         {
            const auto view = [&]() { return json::object{{"firstName", Utf8(target->GetPersonFirstName())},
               {"lastName", Utf8(target->GetPersonLastName())}, {"directoryBacked", target->GetIsAD()},
               {"lastLogon", Utf8(target->GetLastLogonTime())}}; };
            if (operation == "accounts.profile.read") return view();
            const auto *value = arguments.if_contains("fields");
            if (!value || !value->is_object() || value->as_object().empty()) throw RequestError{422};
            const auto &fields = value->as_object();
            for (const auto &field : fields)
            {
               if (field.key() != "firstName" && field.key() != "lastName") throw RequestError{422};
               const auto text = Text(fields, std::string(field.key()).c_str(), 255);
               Wide(field.value());
               for (const unsigned char character : text) if (character < 32 || character == 127) throw RequestError{422};
            }
            if (fields.if_contains("firstName")) target->SetPersonFirstName(Wide(fields.at("firstName")));
            if (fields.if_contains("lastName")) target->SetPersonLastName(Wide(fields.at("lastName")));
            if (selfService)
            {
               SQLStatement statement(SQLStatement::STUpdate, "hm_accounts");
               if (fields.if_contains("firstName")) statement.AddColumn("accountpersonfirstname", target->GetPersonFirstName());
               if (fields.if_contains("lastName")) statement.AddColumn("accountpersonlastname", target->GetPersonLastName());
               SaveSelfPatch(target, statement, target->GetPassword(), [&](const Account &stored) {
                  return (!fields.if_contains("firstName") || stored.GetPersonFirstName() == target->GetPersonFirstName())
                     && (!fields.if_contains("lastName") || stored.GetPersonLastName() == target->GetPersonLastName())
                     && stored.GetPassword() == target->GetPassword();
               });
            }
            else
            {
               String error;
               if (!PersistentAccount::SaveObject(target, error, true, PersistenceModeNormal)) throw RequestError{error.IsEmpty() ? 503 : 422};
            }
            return view();
         }
         if (operation.rfind("accounts.signature.", 0) == 0) return ManageSignature(operation, arguments, target, selfService);
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
         // For self-service defer the BO's reply-history side effect until the
         // guarded database write and readback succeed.
         if (fields.if_contains("autoReplyEnabled") && !selfService) target->SetVacationMessageIsOn(fields.at("autoReplyEnabled").as_bool());
         if (selfService)
         {
            SQLStatement statement(SQLStatement::STUpdate, "hm_accounts");
            if (fields.if_contains("forwardEnabled")) statement.AddColumn("accountforwardenabled", target->GetForwardEnabled());
            if (fields.if_contains("forwardAddress")) statement.AddColumn("accountforwardaddress", target->GetForwardAddress());
            if (fields.if_contains("forwardKeepOriginal")) statement.AddColumn("accountforwardkeeporiginal", target->GetForwardKeepOriginal());
            if (fields.if_contains("forwardAbortSpamFlagged")) statement.AddColumn("accountforwardabortspamflagged", target->GetForwardAbortSpamFlagged());
            if (fields.if_contains("autoReplyEnabled")) statement.AddColumn("accountvacationmessageon", fields.at("autoReplyEnabled").as_bool());
            if (fields.if_contains("autoReplySubject")) statement.AddColumn("accountvacationsubject", target->GetVacationSubject());
            if (fields.if_contains("autoReplyBody")) statement.AddColumn("accountvacationmessage", target->GetVacationMessage());
            if (fields.if_contains("autoReplyExpires")) statement.AddColumn("accountvacationexpires", target->GetVacationExpires());
            if (fields.if_contains("autoReplyExpiresDate")) statement.AddColumn("accountvacationexpiredate", target->GetVacationExpiresDate());
            if (fields.if_contains("autoReplyAbortSpamFlagged")) statement.AddColumn("accountvacationabortspamflagged", target->GetVacationAbortSpamFlagged());
            auto intended = MailboxBehaviorView(*target);
            if (fields.if_contains("autoReplyEnabled")) intended["autoReplyEnabled"] = fields.at("autoReplyEnabled");
            const auto previous = target;
            SaveSelfPatch(target, statement, target->GetPassword(), [&](const Account &stored) {
               const auto view = MailboxBehaviorView(stored);
               for (const auto &field : fields) if (view.at(field.key()) != intended.at(field.key())) return false;
               return stored.GetPassword() == target->GetPassword();
            });
            if (fields.if_contains("autoReplyEnabled")) previous->SetVacationMessageIsOn(fields.at("autoReplyEnabled").as_bool());
         }
         else
         {
            String error;
            if (!PersistentAccount::SaveObject(target, error, true, PersistenceModeNormal)) throw RequestError{error.IsEmpty() ? 503 : 422};
         }
         return MailboxBehaviorView(*target);
      }
      json::object DomainSettingsView(const Domain &domain)
      {
         return {{"catchAllAddress", Utf8(domain.GetPostmaster())}, {"plusAddressingEnabled", domain.GetUsePlusAddressing()},
            {"plusAddressingCharacter", Utf8(domain.GetPlusAddressingChar())}, {"greylistingEnabled", domain.GetASUseGreyListing()},
            {"maxAccounts", domain.GetMaxNoOfAccountsEnabled() ? domain.GetMaxNoOfAccounts() : 0},
            {"maxAliases", domain.GetMaxNoOfAliasesEnabled() ? domain.GetMaxNoOfAliases() : 0},
            {"maxLists", domain.GetMaxNoOfDistributionLists()}, {"signatureEnabled", domain.GetEnableSignature()},
            {"signaturePlainText", Utf8(domain.GetSignaturePlainText())}, {"signatureHtml", Utf8(domain.GetSignatureHTML())},
            {"signatureMethod", static_cast<int>(domain.GetSignatureMethod())},
            {"signaturesOnReplies", domain.GetAddSignaturesToReplies()}, {"signaturesOnLocalMail", domain.GetAddSignaturesToLocalMail()}};
      }
      json::value ManageDomainSettings(const std::string &operation, const json::object &arguments, const Account &actor)
      {
         if (actor.GetAdminLevel() == Account::NormalUser) throw RequestError{403};
         const bool server = actor.GetAdminLevel() == Account::ServerAdmin;
         const auto id = Id(arguments);
         if (!server && id != actor.GetDomainID()) throw RequestError{403};
         auto domain = std::make_shared<Domain>();
         if (!PersistentDomain::ReadObject(domain, id)) throw RequestError{404};
         if (operation == "domains.settings.read") return DomainSettingsView(*domain);
         if (operation != "domains.settings.update") throw RequestError{404};
         const auto *value = arguments.if_contains("fields");
         if (!value || !value->is_object() || value->as_object().empty()) throw RequestError{422};
         const auto &fields = value->as_object();
         for (const auto &field : fields)
         {
            const auto key = field.key();
            if (key == "plusAddressingEnabled" || key == "greylistingEnabled" || key == "signatureEnabled"
                || key == "signaturesOnReplies" || key == "signaturesOnLocalMail")
            { if (!field.value().is_bool()) throw RequestError{422}; }
            else if (key == "maxAccounts" || key == "maxAliases" || key == "maxLists")
            {
               if (!server) throw RequestError{403};
               Number(fields, std::string(key).c_str(), 2147483647);
            }
            else if (key == "signatureMethod")
            { if (Number(fields, "signatureMethod", 3) < 1) throw RequestError{422}; }
            else if (key == "catchAllAddress")
            {
               const auto address = MailAddress(fields, "catchAllAddress", true);
               if (!address.IsEmpty())
               {
                  auto recipient = std::make_shared<Account>();
                  if (!PersistentAccount::ReadObject(recipient, address) || recipient->GetDomainID() != id || !recipient->GetActive()) throw RequestError{422};
                  if (!server && recipient->GetAdminLevel() == Account::ServerAdmin) throw RequestError{403};
               }
            }
            else if (key == "plusAddressingCharacter")
            {
               const auto character = Text(fields, "plusAddressingCharacter", 1);
               // One unambiguous ASCII separator; empty is permitted only when disabled.
               if (!character.empty() && std::string("+-.=_").find(character[0]) == std::string::npos) throw RequestError{422};
            }
            else if (key == "signaturePlainText" || key == "signatureHtml")
            {
               const auto text = Text(fields, std::string(key).c_str(), 16000);
               Wide(field.value());
               for (const unsigned char character : text)
                  if ((character < 32 && character != '\r' && character != '\n' && character != '\t') || character == 127) throw RequestError{422};
            }
            else throw RequestError{422};
         }
         const auto current = DomainSettingsView(*domain);
         const auto effective = [&](const char *key) -> const json::value & {
            const auto *field = fields.if_contains(key); return field ? *field : current.at(key);
         };
         if (effective("plusAddressingEnabled").as_bool() && effective("plusAddressingCharacter").as_string().empty()) throw RequestError{422};
         if (fields.if_contains("catchAllAddress")) domain->SetPostmaster(Wide(fields.at("catchAllAddress")));
         if (fields.if_contains("plusAddressingEnabled")) domain->SetUsePlusAddressing(fields.at("plusAddressingEnabled").as_bool());
         if (fields.if_contains("plusAddressingCharacter")) domain->SetPlusAddressingChar(Wide(fields.at("plusAddressingCharacter")));
         if (fields.if_contains("greylistingEnabled")) domain->SetASUseGreyListing(fields.at("greylistingEnabled").as_bool());
         if (fields.if_contains("signatureEnabled")) domain->SetEnableSignature(fields.at("signatureEnabled").as_bool());
         if (fields.if_contains("signaturePlainText")) domain->SetSignaturePlainText(Wide(fields.at("signaturePlainText")));
         if (fields.if_contains("signatureHtml")) domain->SetSignatureHTML(Wide(fields.at("signatureHtml")));
         if (fields.if_contains("signatureMethod")) domain->SetSignatureMethod(static_cast<Domain::DomainSignatureMethod>(Number(fields, "signatureMethod", 3)));
         if (fields.if_contains("signaturesOnReplies")) domain->SetAddSignaturesToReplies(fields.at("signaturesOnReplies").as_bool());
         if (fields.if_contains("signaturesOnLocalMail")) domain->SetAddSignaturesToLocalMail(fields.at("signaturesOnLocalMail").as_bool());
         if (fields.if_contains("maxAccounts")) { const auto count = static_cast<int>(Number(fields, "maxAccounts", 2147483647)); domain->SetMaxNoOfAccounts(count); domain->SetMaxNoOfAccountsEnabled(count != 0); }
         if (fields.if_contains("maxAliases")) { const auto count = static_cast<int>(Number(fields, "maxAliases", 2147483647)); domain->SetMaxNoOfAliases(count); domain->SetMaxNoOfAliasesEnabled(count != 0); }
         if (fields.if_contains("maxLists")) { const auto count = static_cast<int>(Number(fields, "maxLists", 2147483647)); domain->SetMaxNoOfDistributionLists(count); domain->SetMaxNoOfDistributionListsEnabled(count != 0); }
         String error;
         if (!PersistentDomain::SaveObject(domain, error, PersistenceModeNormal)) throw RequestError{error.IsEmpty() ? 503 : 422};
         return DomainSettingsView(*domain);
      }
      json::object DomainAliasView(const DomainAlias &alias)
      { return {{"id", alias.GetID()}, {"domainId", alias.GetDomainID()}, {"name", Utf8(alias.GetName())}}; }
      json::value ManageDomainAliases(const std::string &operation, const json::object &arguments, const Account &actor)
      {
         if (actor.GetAdminLevel() == Account::NormalUser) throw RequestError{403};
         const auto domainId = Number(arguments, "domainId", 9223372036854775807LL);
         if (!domainId) throw RequestError{422};
         const bool server = actor.GetAdminLevel() == Account::ServerAdmin;
         if (!server && domainId != actor.GetDomainID()) throw RequestError{403};
         if (operation != "domainaliases.list" && operation != "domainaliases.read" && operation != "domainaliases.create" && operation != "domainaliases.delete") throw RequestError{404};
         if (!server && (operation == "domainaliases.create" || operation == "domainaliases.delete")) throw RequestError{403};
         auto domain = std::make_shared<Domain>();
         if (!PersistentDomain::ReadObject(domain, domainId)) throw RequestError{404};
         DomainAliases aliases(domainId); aliases.Refresh();
         if (operation == "domainaliases.list") return Page(aliases.GetVector(), arguments, DomainAliasView);
         if (operation == "domainaliases.read" || operation == "domainaliases.delete")
         {
            auto alias = aliases.GetItemByDBID(Id(arguments));
            if (!alias) throw RequestError{404};
            if (operation == "domainaliases.read") return DomainAliasView(*alias);
            if (!PersistentDomainAlias::DeleteObject(alias)) throw RequestError{503};
            return json::object{{"deleted", true}};
         }
         const auto *value = arguments.if_contains("fields");
         if (!value || !value->is_object() || value->as_object().size() != 1 || !value->as_object().if_contains("name")) throw RequestError{422};
         const auto name = Text(value->as_object(), "name", 253);
         for (const unsigned char character : name)
            if (!((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z')
                  || (character >= '0' && character <= '9') || character == '-' || character == '.')) throw RequestError{422};
         for (size_t start = 0; start < name.size();)
         {
            auto end = name.find('.', start); if (end == std::string::npos) end = name.size();
            if (end == start || end - start > 63 || name[start] == '-' || name[end - 1] == '-') throw RequestError{422};
            start = end + 1;
         }
         if (!name.empty() && name.back() == '.') throw RequestError{422};
         String wide = Wide(value->as_object().at("name")); wide.ToLower();
         if (wide.IsEmpty() || !StringParser::IsValidDomainName(wide)) throw RequestError{422};
         auto alias = std::make_shared<DomainAlias>(); alias->SetDomainID(domainId); alias->SetAlias(wide);
         String error;
         const bool saved = PersistentDomainAlias::SaveObject(alias, error, PersistenceModeNormal);
         // Legacy persistence can report true on an insert failure; a fresh object must get an ID.
         if (!saved || alias->GetID() <= 0) throw RequestError{error.IsEmpty() ? 503 : 422};
         return DomainAliasView(*alias);
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
      #include "MailOperations.inc"
      #include "ServerSettings.inc"
      #include "TransportInventory.inc"
      #include "PolicyCollections.inc"
      #include "MailboxSelfService.inc"
      #include "ServerOperations.inc"
      #include "FetchAccounts.inc"
      #include "GroupsFolders.inc"
      #include "Maintenance.inc"
      #include "AntiVirus.inc"
      #include "Dkim.inc"
      #include "Bayes.inc"
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
         BOOL_SETTING(PrependSubject), BOOL_SETTING(BayesEnabled), BOOL_SETTING(BayesLearnFromJunk)
      };
      const IntSetting IntSettings[] = {
         INT_SETTING(SpamMarkThreshold, 0, 10000), INT_SETTING(SpamDeleteThreshold, 0, 10000),
         INT_SETTING(SpamAssassinPort, 1, 65535), INT_SETTING(SpamAssassinScore, 0, 10000),
         INT_SETTING(UseSPFScore, 0, 10000), INT_SETTING(UseMXChecksScore, 0, 10000),
         INT_SETTING(CheckHostInHeloScore, 0, 10000), INT_SETTING(CheckPTRScore, 0, 10000),
         INT_SETTING(DKIMVerificationFailureScore, 0, 10000), INT_SETTING(DMARCFailureScore, 0, 10000),
         INT_SETTING(BayesScore, 0, 10000), INT_SETTING(BayesThreshold, 50, 100), INT_SETTING(BayesMinimumMessages, 1, 100000),
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
         if (operation.rfind("self.", 0) == 0) return ManageSelf(operation, arguments, *account);
         if (operation.rfind("settings.", 0) == 0) return ManageServerSettings(operation, arguments, *account);
         if (operation.rfind("transport.", 0) == 0) return ReadTransportInventory(operation, arguments, *account);
         if (operation.rfind("surbl.", 0) == 0 || operation.rfind("blockedattachments.", 0) == 0
             || operation.rfind("incomingrelays.", 0) == 0 || operation.rfind("securityranges.", 0) == 0
             || operation.rfind("spamallowlist.", 0) == 0 || operation.rfind("greylistexceptions.", 0) == 0)
            return ManagePolicies(operation, arguments, *account);
         if (operation.rfind("queue.", 0) == 0) return ManageQueue(operation, arguments, *account);
         if (operation.rfind("routes.", 0) == 0 || operation.rfind("routeaddresses.", 0) == 0) return ManageRoutes(operation, arguments, *account);
         if (operation.rfind("rules.", 0) == 0) return ManageRules(operation, arguments, *account);
         if (operation.rfind("domains.settings.", 0) == 0) return ManageDomainSettings(operation, arguments, *account);
         if (operation.rfind("domainaliases.", 0) == 0) return ManageDomainAliases(operation, arguments, *account);
         if (operation.rfind("accounts.behavior.", 0) == 0 || operation.rfind("accounts.signature.", 0) == 0 || operation.rfind("accounts.profile.", 0) == 0)
            return ManageMailboxSettings(operation, arguments, *account);
         if (operation.rfind("fetchaccounts.", 0) == 0) return ManageFetchAccounts(operation, arguments, *account);
         if (operation.rfind("dkim.", 0) == 0) return ManageDkim(operation, arguments, *account);
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
         if (operation == "status" || operation.rfind("server.", 0) == 0 || operation.rfind("servermessages.", 0) == 0
             || operation.rfind("sendout.", 0) == 0 || operation.rfind("diagnostics.", 0) == 0 || operation.rfind("logs.", 0) == 0)
            return ManageServerOperations(operation, arguments);
         if (operation.rfind("groups.", 0) == 0 || operation.rfind("groupmembers.", 0) == 0) return ManageGroups(operation, arguments);
         if (operation.rfind("publicfolders.", 0) == 0) return ManagePublicFolders(operation, arguments);
         if (operation.rfind("folderpermissions.", 0) == 0) return ManageFolderPermissions(operation, arguments);
         if (operation.rfind("backup.", 0) == 0 || operation.rfind("secretkey.", 0) == 0) return ManageMaintenance(operation, arguments);
         if (operation.rfind("antivirus.", 0) == 0) return ManageAntiVirus(operation, arguments);
         if (operation.rfind("bayes.", 0) == 0) return ManageBayes(operation, arguments);
         if (operation == "antispam.read") return SpamView(config);
         if (operation == "antispam.update") { UpdateSpam(config, arguments); return SpamView(config); }
         if (operation == "spamassassin.test")
         {
            // The test scans the GTUBE sample, which every SpamAssassin installation must flag.
            // Report whether it did, and its X-Spam headers, so a reachable but misconfigured
            // engine is not mistaken for a working one.
            String message;
            const bool connected = SpamAssassinTestConnect().TestConnect(config.GetSpamAssassinHost(), config.GetSpamAssassinPort(), message);
            std::string headers;
            bool detected = false;
            if (connected)
            {
               std::vector<String> lines = StringParser::SplitString(message, _T("\r\n"));
               for (const auto &line : lines)
               {
                  if (line.IsEmpty()) break;
                  if (line.Left(7).CompareNoCase(_T("X-Spam-")) != 0) continue;
                  if (line.Left(16).CompareNoCase(_T("X-Spam-Status: Y")) == 0 || line.Left(14).CompareNoCase(_T("X-Spam-Flag: Y")) == 0) detected = true;
                  if (headers.size() < 2000) headers += Utf8(line.Left(400)) + "\n";
               }
            }
            return json::object{{"connected", connected}, {"gtubeDetected", detected}, {"headers", headers}};
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
