// External POP3 fetch accounts, scoped to one mailbox. Server administrators, and domain
// administrators for mailboxes in their own domain that are not server administrators.
json::object FetchAccountView(const FetchAccount &account)
{
   return {{"id", account.GetID()}, {"accountId", account.GetAccountID()}, {"name", Utf8(account.GetName())},
      {"active", account.GetActive()}, {"serverAddress", Utf8(account.GetServerAddress())},
      {"serverType", static_cast<int>(account.GetServerType())}, {"port", account.GetPort()},
      {"security", static_cast<int>(account.GetConnectionSecurity())}, {"username", Utf8(account.GetUsername())},
      {"passwordConfigured", !account.GetPassword().IsEmpty()}, {"minutesBetweenFetch", account.GetMinutesBetweenTry()},
      {"daysToKeep", account.GetDaysToKeep()}, {"processMimeRecipients", account.GetProcessMIMERecipients()},
      {"mimeRecipientHeaders", Utf8(account.GetMIMERecipientHeaders())}, {"processMimeDate", account.GetProcessMIMEDate()},
      {"routeRecipients", account.GetEnableRouteRecipients()}, {"useAntiSpam", account.GetUseAntiSpam()},
      {"useAntiVirus", account.GetUseAntiVirus()}, {"nextTry", Utf8(account.GetNextTry())},
      {"running", PersistentFetchAccount::IsLocked(account.GetID())}};
}

json::value ManageFetchAccounts(const std::string &operation, json::object &arguments, const Account &actor)
{
   if (actor.GetAdminLevel() == Account::NormalUser) throw RequestError{403};
   const auto domainId = Number(arguments, "domainId", 9223372036854775807LL), accountId = Number(arguments, "accountId", 9223372036854775807LL);
   if (!domainId || !accountId) throw RequestError{422};
   if (actor.GetAdminLevel() != Account::ServerAdmin && domainId != actor.GetDomainID()) throw RequestError{403};
   auto mailbox = std::make_shared<Account>();
   if (!PersistentAccount::ReadObject(mailbox, accountId) || mailbox->GetDomainID() != domainId) throw RequestError{404};
   if (actor.GetAdminLevel() != Account::ServerAdmin && mailbox->GetAdminLevel() == Account::ServerAdmin) throw RequestError{403};
   FetchAccounts accounts(accountId);
   accounts.Refresh();
   if (operation == "fetchaccounts.list") return Page(accounts.GetVector(), arguments, FetchAccountView);
   const auto action = operation.substr(operation.find('.') + 1);
   const bool create = action == "create";
   if (!create && action != "read" && action != "update" && action != "delete" && action != "run") throw RequestError{404};
   auto account = create ? std::make_shared<FetchAccount>() : accounts.GetItemByDBID(Id(arguments));
   if (!account) throw RequestError{404};
   if (action == "read") return FetchAccountView(*account);
   if (action == "run")
   {
      // Same as the legacy "download now": make it due and wake the fetch manager.
      if (!account->GetActive()) throw RequestError{409};
      PersistentFetchAccount::SetRetryNow(account->GetID());
      if (auto manager = Application::Instance()->GetExternalFetchManager()) manager->SetCheckNow();
      FetchAccounts refreshed(accountId);
      refreshed.Refresh();
      auto current = refreshed.GetItemByDBID(account->GetID());
      return current ? FetchAccountView(*current) : FetchAccountView(*account);
   }
   if (action == "delete")
   {
      // A download in progress still holds the account; deleting it underneath would orphan its UIDs.
      if (PersistentFetchAccount::IsLocked(account->GetID())) throw RequestError{409};
      if (!PersistentFetchAccount::DeleteObject(account)) throw RequestError{503};
      return json::object{{"deleted", true}};
   }
   auto &fields = OperationFields(arguments);
   SecretWipe wipe{fields.if_contains("password")};
   AllowedFields(fields, {"name", "active", "serverAddress", "port", "security", "username", "password", "minutesBetweenFetch",
      "daysToKeep", "processMimeRecipients", "mimeRecipientHeaders", "processMimeDate", "routeRecipients", "useAntiSpam", "useAntiVirus"});
   if (create)
   {
      if (!fields.if_contains("name") || !fields.if_contains("serverAddress") || !fields.if_contains("username") || !fields.if_contains("password"))
         throw RequestError{422};
      account->SetAccountID(accountId);
      account->SetServerType(FetchAccount::POP3);
      account->SetActive(false);
      account->SetPort(995);
      account->SetConnectionSecurity(CSSSL);
      account->SetMinutesBetweenTry(30);
      account->SetDaysToKeep(0);
      account->SetMIMERecipientHeaders(_T("To,CC,X-RCPT-To,X-Envelope-To"));
      account->SetUseAntiSpam(true);
      account->SetUseAntiVirus(true);
   }
   if (fields.if_contains("name")) { const auto name = CleanText(fields, "name", 255); if (name.IsEmpty()) throw RequestError{422}; account->SetName(name); }
   if (fields.if_contains("serverAddress"))
   {
      const auto host = Text(fields, "serverAddress", 253);
      boost::system::error_code error;
      boost::asio::ip::make_address(host, error);
      if (error && OperationDomain(fields, "serverAddress").Find(_T("*")) >= 0) throw RequestError{422};
      account->SetServerAddress(Wide(fields.at("serverAddress")));
   }
   if (fields.if_contains("port"))
   {
      const auto port = Number(fields, "port", 65535);
      if (!port) throw RequestError{422};
      account->SetPort(static_cast<int>(port));
   }
   if (fields.if_contains("security"))
   {
      // New choices always encrypt the POP3 logon. Existing plaintext or optional-STARTTLS
      // accounts keep their setting until it is changed.
      const auto security = Number(fields, "security", 3);
      if (security != CSSSL && security != CSSTARTTLSRequired) throw RequestError{422};
      account->SetConnectionSecurity(static_cast<ConnectionSecurity>(security));
   }
   if (fields.if_contains("username")) { const auto user = CleanText(fields, "username", 255); if (user.IsEmpty()) throw RequestError{422}; account->SetUsername(user); }
   if (fields.if_contains("password"))
   {
      const auto password = Text(fields, "password", 1024);
      if (password.empty()) throw RequestError{422};
      for (const unsigned char character : password) if (character < 32 || character == 127) throw RequestError{422};
      account->SetPassword(Wide(fields.at("password")));
   }
   if (fields.if_contains("minutesBetweenFetch"))
   {
      const auto minutes = Number(fields, "minutesBetweenFetch", 10080);
      if (!minutes) throw RequestError{422};
      account->SetMinutesBetweenTry(static_cast<int>(minutes));
   }
   if (fields.if_contains("daysToKeep"))
   {
      // -1 deletes after download, 0 keeps messages on the server, N deletes after N days.
      const auto *value = fields.if_contains("daysToKeep");
      if (!value->is_int64() || value->as_int64() < -1 || value->as_int64() > 36500) throw RequestError{422};
      account->SetDaysToKeep(static_cast<int>(value->as_int64()));
   }
   if (fields.if_contains("mimeRecipientHeaders"))
   {
      const auto headers = Text(fields, "mimeRecipientHeaders", 255);
      for (const unsigned char character : headers)
         if (!std::isalnum(character) && character != '-' && character != ',') throw RequestError{422};
      if (headers.find(",,") != std::string::npos || (!headers.empty() && (headers.front() == ',' || headers.back() == ','))) throw RequestError{422};
      account->SetMIMERecipientHeaders(Wide(fields.at("mimeRecipientHeaders")));
   }
   account->SetActive(Boolean(fields, "active", account->GetActive()));
   account->SetProcessMIMERecipients(Boolean(fields, "processMimeRecipients", account->GetProcessMIMERecipients()));
   account->SetProcessMIMEDate(Boolean(fields, "processMimeDate", account->GetProcessMIMEDate()));
   account->SetEnableRouteRecipients(Boolean(fields, "routeRecipients", account->GetEnableRouteRecipients()));
   account->SetUseAntiSpam(Boolean(fields, "useAntiSpam", account->GetUseAntiSpam()));
   account->SetUseAntiVirus(Boolean(fields, "useAntiVirus", account->GetUseAntiVirus()));
   // As in the legacy form: recipients come from MIME headers only when headers are named,
   // and route recipients only apply to MIME recipients.
   if (account->GetProcessMIMERecipients() && account->GetMIMERecipientHeaders().IsEmpty()) throw RequestError{422};
   if (account->GetEnableRouteRecipients() && !account->GetProcessMIMERecipients()) throw RequestError{422};
   String error;
   if (!PersistentFetchAccount::SaveObject(account, error, PersistenceModeNormal) || account->GetID() <= 0) throw RequestError{error.IsEmpty() ? 503 : 422};
   // Read back every field, and the secret internally, before reporting success.
   FetchAccounts verified(accountId);
   verified.Refresh();
   auto saved = verified.GetItemByDBID(account->GetID());
   if (!saved || saved->GetPassword() != account->GetPassword()) throw RequestError{503};
   auto expected = FetchAccountView(*account), actual = FetchAccountView(*saved);
   expected.erase("nextTry"); expected.erase("running"); actual.erase("nextTry"); actual.erase("running");
   if (expected != actual) throw RequestError{503};
   return FetchAccountView(*saved);
}
