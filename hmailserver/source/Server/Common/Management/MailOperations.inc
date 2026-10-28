// Private implementation included inside ManagementPipe.cpp's anonymous namespace.
// Transport/authentication stay there; writes use native business/persistence objects.
json::object &OperationFields(json::object &arguments)
{
   auto *value = arguments.if_contains("fields");
   if (!value || !value->is_object() || value->as_object().empty()) throw RequestError{422};
   return value->as_object();
}
void AllowedFields(const json::object &fields, std::initializer_list<const char *> allowed)
{
   for (const auto &field : fields)
   {
      bool found = false;
      for (const auto key : allowed) if (field.key() == key) { found = true; break; }
      if (!found) throw RequestError{422};
   }
}
bool Boolean(const json::object &fields, const char *key, bool fallback)
{
   const auto *value = fields.if_contains(key);
   if (!value) return fallback;
   if (!value->is_bool()) throw RequestError{422};
   return value->as_bool();
}
String CleanText(const json::object &fields, const char *key, size_t maximum, bool multiline = false)
{
   const auto text = Text(fields, key, maximum);
   for (const unsigned char character : text)
      if ((character < 32 && !(multiline && (character == '\r' || character == '\n' || character == '\t'))) || character == 127) throw RequestError{422};
   return Wide(fields.at(key));
}
String OperationDomain(const json::object &fields, const char *key)
{
   auto name = Text(fields, key, 253);
   if (name == "*") return _T("*");
   auto labels = name.rfind("*.", 0) == 0 ? name.substr(2) : name;
   if (labels.empty() || labels.back() == '.') throw RequestError{422};
   for (const unsigned char c : labels)
      if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '.')) throw RequestError{422};
   for (size_t start = 0; start < labels.size();)
   {
      auto end = labels.find('.', start); if (end == std::string::npos) end = labels.size();
      if (end == start || end - start > 63 || labels[start] == '-' || labels[end-1] == '-') throw RequestError{422};
      start = end + 1;
   }
   auto result = Wide(fields.at(key)); result.ToLower(); return result;
}
json::object QueueView(const PersistentMessage::QueueItem &item)
{
   // New queue endpoints use decimal-string IDs, preserving every signed 64-bit ID in JS.
   return {{"id", std::to_string(item.id)}, {"sender", Utf8(item.sender)}, {"created", Utf8(item.created)},
      {"nextTry", Utf8(item.nextTry)}, {"size", item.size}, {"attempts", item.retries}, {"busy", item.locked}, {"held", item.state == 3}};
}
json::value ManageQueue(const std::string &operation, json::object &arguments, const Account &actor)
{
   if (actor.GetAdminLevel() != Account::ServerAdmin) throw RequestError{403};
   if (operation == "queue.wake")
   {
      if (!arguments.empty()) throw RequestError{422};
      DeliveryQueue::StartDelivery(); return json::object{{"requested", true}};
   }
   if (operation == "queue.list")
   {
      const auto offset = Number(arguments, "offset", 2147483647), limit = Number(arguments, "limit", 100, 50);
      if (!limit) throw RequestError{422};
      String search;
      if (arguments.if_contains("search")) { Text(arguments, "search", 255); search = Wide(arguments.at("search")); search.ToLower(); }
      std::vector<PersistentMessage::QueueItem> items;
      if (!PersistentMessage::ReadQueue(items)) throw RequestError{503};
      json::array result; __int64 total = 0;
      for (const auto &item : items)
      {
         String text = item.sender + _T(" ") + String(std::to_string(item.id).c_str()); text.ToLower();
         if (!search.IsEmpty() && text.Find(search) < 0) continue;
         if (total >= offset && result.size() < static_cast<size_t>(limit)) result.push_back(QueueView(item));
         ++total;
      }
      return json::object{{"items", std::move(result)}, {"total", total}, {"offset", offset}, {"limit", limit}};
   }
   if (operation != "queue.read" && operation != "queue.retry" && operation != "queue.delete") throw RequestError{404};
   const auto id = Id(arguments);
   if (operation == "queue.read")
   {
      PersistentMessage::QueueItem item;
      if (!PersistentMessage::ReadQueueItem(id, item)) throw RequestError{503};
      if (!item.id) throw RequestError{404};
      auto view = QueueView(item); auto message = std::make_shared<Message>(false);
      if (!PersistentMessage::ReadObject(message, id) || message->GetAccountID() != 0
          || (message->GetState() != Message::Delivering && static_cast<int>(message->GetState()) != 3)) throw RequestError{404};
      json::array recipients;
      for (const auto &recipient : message->GetRecipients()->GetVector())
      { if (recipients.size() == 100) break; recipients.emplace_back(Utf8(recipient->GetAddress())); }
      view["recipients"] = std::move(recipients); view["recipientCount"] = message->GetRecipients()->GetCount(); return view;
   }
   const auto result = operation == "queue.retry" ? DeliveryQueue::TryRetry(id) : DeliveryQueue::TryRemove(id);
   if (result == DeliveryQueue::MutationResult::Missing) throw RequestError{404};
   if (result == DeliveryQueue::MutationResult::Busy) throw RequestError{409};
   if (result != DeliveryQueue::MutationResult::Success) throw RequestError{503};
   return json::object{{"requested", true}};
}
json::object RouteView(const Route &route)
{
   return {{"id", route.GetID()}, {"domain", Utf8(route.DomainName())}, {"description", Utf8(route.GetDescription())},
      {"host", Utf8(route.TargetSMTPHost())}, {"port", route.TargetSMTPPort()}, {"tries", route.NumberOfTries()},
      {"retryMinutes", route.MinutesBetweenTry()}, {"allAddresses", route.ToAllAddresses()},
      {"recipientLocal", route.GetTreatRecipientAsLocalDomain()}, {"senderLocal", route.GetTreatSenderAsLocalDomain()},
      {"security", static_cast<int>(route.GetConnectionSecurity())}, {"requiresAuth", route.GetRelayerRequiresAuth()},
      {"username", Utf8(route.GetRelayerAuthUsername())}, {"passwordConfigured", !route.GetRelayerAuthPassword().IsEmpty()}};
}
json::object RouteAddressView(const RouteAddress &address)
{ return {{"id", address.GetID()}, {"routeId", address.GetRouteID()}, {"address", Utf8(address.GetAddress())}}; }
json::value ManageRoutes(const std::string &operation, json::object &arguments, const Account &actor)
{
   if (actor.GetAdminLevel() != Account::ServerAdmin) throw RequestError{403};
   Routes routes; routes.Refresh();
   if (operation == "routes.list") return Page(routes.GetVector(), arguments, RouteView);
   const bool addresses = operation.rfind("routeaddresses.", 0) == 0, create = operation == "routes.create";
   const auto action = operation.substr(operation.find('.') + 1);
   if (action != "create" && action != "read" && action != "update" && action != "delete" && !(addresses && action == "list")) throw RequestError{404};
   auto route = create ? std::make_shared<Route>() : routes.GetItemByDBID(addresses ? Number(arguments, "routeId", 9223372036854775807LL) : Id(arguments));
   if (!route) throw RequestError{404};
   const auto reload = []() { Configuration::Instance()->GetSMTPConfiguration()->GetRoutes()->Refresh(); };
   if (addresses)
   {
      auto members = route->GetAddresses();
      if (action == "list") return Page(members->GetVector(), arguments, RouteAddressView);
      if (action == "read" || action == "delete")
      {
         auto item = members->GetItemByDBID(Id(arguments)); if (!item) throw RequestError{404};
         if (action == "read") return RouteAddressView(*item);
         if (!PersistentRouteAddress::DeleteObject(item)) throw RequestError{503}; reload(); return json::object{{"deleted", true}};
      }
      if (action != "create") throw RequestError{404};
      auto &fields = OperationFields(arguments); AllowedFields(fields, {"address"}); const auto address = MailAddress(fields, "address");
      for (const auto &item : members->GetSnapshot()) if (item->GetAddress().CompareNoCase(address) == 0) throw RequestError{422};
      auto item = std::make_shared<RouteAddress>(); item->SetRouteID(route->GetID()); item->SetAddress(address);
      if (!PersistentRouteAddress::SaveObject(item) || !item->GetID()) throw RequestError{503};
      reload(); return RouteAddressView(*item);
   }
   if (action == "read") return RouteView(*route);
   if (action == "delete")
   {
      if (!PersistentRoute::DeleteObject(route)) throw RequestError{503}; reload(); return json::object{{"deleted", true}};
   }
   auto &fields = OperationFields(arguments); SecretWipe wipe{fields.if_contains("password")};
   AllowedFields(fields, {"domain", "description", "host", "port", "tries", "retryMinutes", "allAddresses", "recipientLocal", "senderLocal", "security", "requiresAuth", "username", "password"});
   if (create)
   {
      if (!fields.if_contains("domain") || !fields.if_contains("host")) throw RequestError{422};
      route->DomainName(OperationDomain(fields, "domain")); route->TargetSMTPPort(25); route->NumberOfTries(4);
      route->MinutesBetweenTry(60); route->ToAllAddresses(false); route->SetConnectionSecurity(CSSTARTTLSRequired);
   }
   else if (fields.if_contains("domain")) throw RequestError{422};
   if (fields.if_contains("description")) route->SetDescription(CleanText(fields, "description", 255));
   if (fields.if_contains("host"))
   {
      const auto host = Text(fields, "host", 253); boost::system::error_code error; boost::asio::ip::make_address(host, error);
      if (error)
      { auto checked = OperationDomain(fields, "host"); if (checked.Find(_T("*")) >= 0) throw RequestError{422}; }
      route->TargetSMTPHost(CleanText(fields, "host", 253));
   }
   for (const auto key : {"port", "tries", "retryMinutes"})
      if (fields.if_contains(key) && Number(fields, key, std::string(key) == "port" ? 65535 : std::string(key) == "tries" ? 1000 : 10080) == 0) throw RequestError{422};
   if (fields.if_contains("port")) route->TargetSMTPPort(static_cast<long>(Number(fields, "port", 65535)));
   if (fields.if_contains("tries")) route->NumberOfTries(static_cast<long>(Number(fields, "tries", 1000)));
   if (fields.if_contains("retryMinutes")) route->MinutesBetweenTry(static_cast<long>(Number(fields, "retryMinutes", 10080)));
   route->ToAllAddresses(Boolean(fields, "allAddresses", route->ToAllAddresses()));
   route->SetTreatRecipientAsLocalDomain(Boolean(fields, "recipientLocal", route->GetTreatRecipientAsLocalDomain()));
   route->SetTreatSenderAsLocalDomain(Boolean(fields, "senderLocal", route->GetTreatSenderAsLocalDomain()));
   if (fields.if_contains("security")) route->SetConnectionSecurity(static_cast<ConnectionSecurity>(Number(fields, "security", 3)));
   route->SetRelayerRequiresAuth(Boolean(fields, "requiresAuth", route->GetRelayerRequiresAuth()));
   if (fields.if_contains("username")) route->SetRelayerAuthUsername(CleanText(fields, "username", 255));
   if (fields.if_contains("password")) { Text(fields, "password", 1024); route->SetRelayerAuthPassword(Wide(fields.at("password"))); }
   if (route->GetRelayerRequiresAuth() && (route->GetRelayerAuthUsername().IsEmpty() || route->GetRelayerAuthPassword().IsEmpty()
       || (route->GetConnectionSecurity() != CSSSL && route->GetConnectionSecurity() != CSSTARTTLSRequired))) throw RequestError{422};
   String error;
   if (!PersistentRoute::SaveObject(route, error, PersistenceModeNormal) || route->GetID() <= 0) throw RequestError{error.IsEmpty() ? 503 : 422};
   // Legacy route persistence has weak success reporting. Read back every saved field and
   // the secret internally before publishing a new routing snapshot; never return the secret.
   Routes verified; verified.Refresh(); auto saved = verified.GetItemByDBID(route->GetID());
   if (!saved || json::serialize(RouteView(*saved)) != json::serialize(RouteView(*route)) || saved->GetRelayerAuthPassword() != route->GetRelayerAuthPassword()) throw RequestError{503};
   reload(); return RouteView(*saved);
}
bool HeaderName(const String &name)
{
   const auto text = Utf8(name); if (text.empty() || text.size() > 78) return false;
   for (const unsigned char c : text)
      if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-')) return false;
   return true;
}
json::object CriterionView(const RuleCriteria &criterion)
{
   return {{"field", criterion.GetUsePredefined() ? static_cast<int>(criterion.GetPredefinedField()) : 0},
      {"header", Utf8(criterion.GetHeaderField())}, {"match", static_cast<int>(criterion.GetMatchType())}, {"value", Utf8(criterion.GetMatchValue())}};
}
std::shared_ptr<RuleCriteria> ParseCriterion(const json::object &fields)
{
   AllowedFields(fields, {"field", "header", "match", "value"});
   const auto field = Number(fields, "field", 8), match = Number(fields, "match", 8);
   if (!match || match == RuleCriteria::MatchesRegEx) throw RequestError{422};
   auto criterion = std::make_shared<RuleCriteria>();
   criterion->SetUsePredefined(field != 0); criterion->SetPredefinedField(static_cast<RuleCriteria::PredefinedField>(field));
   criterion->SetMatchType(static_cast<RuleCriteria::MatchType>(match));
   if (!field)
   { const auto header = CleanText(fields, "header", 78); if (!HeaderName(header)) throw RequestError{422}; criterion->SetHeaderField(header); }
   else if (fields.if_contains("header")) CleanText(fields, "header", 78);
   criterion->SetMatchValue(CleanText(fields, "value", 255, field == RuleCriteria::FTBody));
   if (field == RuleCriteria::FTMessageSize || field == RuleCriteria::FTDeliveryAttempts)
   {
      const auto text = Utf8(criterion->GetMatchValue());
      if (text.empty() || text.size() > 10) throw RequestError{422};
      unsigned long long value = 0;
      for (const auto c : text) { if (c < '0' || c > '9') throw RequestError{422}; value = value * 10 + c - '0'; }
      if (value > 2147483647) throw RequestError{422};
   }
   return criterion;
}
json::object ActionView(RuleAction &action)
{
   json::object view{{"type", static_cast<int>(action.GetType())}};
   switch (action.GetType())
   {
   case RuleAction::Forward: view["to"] = Utf8(action.GetTo()); view["abortSpam"] = action.GetAbortSpamFlagged(); break;
   case RuleAction::Reply:
      view["subject"] = Utf8(action.GetSubject()); view["body"] = Utf8(action.GetBody()); view["fromName"] = Utf8(action.GetFromName());
      view["fromAddress"] = Utf8(action.GetFromAddress()); view["abortSpam"] = action.GetAbortSpamFlagged(); break;
   case RuleAction::MoveToIMAPFolder: view["folder"] = Utf8(action.GetIMAPFolder()); break;
   case RuleAction::SetHeaderValue: view["header"] = Utf8(action.GetHeaderName()); view["value"] = Utf8(action.GetValue()); break;
   case RuleAction::SendUsingRoute: view["routeId"] = action.GetRouteID(); break;
   case RuleAction::Delete: case RuleAction::StopRuleProcessing: case RuleAction::CreateCopy: break;
   default: view["unsupported"] = true; break;
   }
   return view;
}
std::shared_ptr<RuleAction> ParseAction(const json::object &fields, const Account &actor, const std::shared_ptr<Account> &mailbox, int order)
{
   auto action = std::make_shared<RuleAction>(); action->SetSortOrder(order);
   const auto type = static_cast<RuleAction::Type>(Number(fields, "type", 10)); action->SetType(type);
   switch (type)
   {
   case RuleAction::Delete: case RuleAction::StopRuleProcessing: case RuleAction::CreateCopy:
      AllowedFields(fields, {"type"}); break;
   case RuleAction::Forward:
      AllowedFields(fields, {"type", "to", "abortSpam"}); action->SetTo(MailAddress(fields, "to"));
      if (mailbox && action->GetTo().CompareNoCase(mailbox->GetAddress()) == 0) throw RequestError{422};
      action->SetAbortSpamFlagged(Boolean(fields, "abortSpam", true)); break;
   case RuleAction::Reply:
      AllowedFields(fields, {"type", "fromAddress", "fromName", "subject", "body", "abortSpam"});
      action->SetFromAddress(MailAddress(fields, "fromAddress"));
      if (mailbox && action->GetFromAddress().CompareNoCase(mailbox->GetAddress()) != 0) throw RequestError{422};
      action->SetFromName(CleanText(fields, "fromName", 255)); action->SetSubject(CleanText(fields, "subject", 255));
      action->SetBody(CleanText(fields, "body", 8000, true)); action->SetAbortSpamFlagged(Boolean(fields, "abortSpam", true)); break;
   case RuleAction::MoveToIMAPFolder:
      AllowedFields(fields, {"type", "folder"}); action->SetIMAPFolder(CleanText(fields, "folder", 255));
      if (action->GetIMAPFolder().IsEmpty()) throw RequestError{422}; break;
   case RuleAction::SetHeaderValue:
   {
      AllowedFields(fields, {"type", "header", "value"}); auto header = CleanText(fields, "header", 78);
      if (!HeaderName(header)) throw RequestError{422}; String lower = header; lower.ToLower();
      if (lower == _T("received") || lower == _T("return-path") || lower == _T("authentication-results")
          || lower == _T("dkim-signature") || lower == _T("auto-submitted") || lower == _T("content-type")
          || lower == _T("content-transfer-encoding") || lower == _T("mime-version")
          || lower.Left(4) == _T("arc-") || lower.Left(14) == _T("x-hmailserver-")) throw RequestError{422};
      action->SetHeaderName(header); action->SetValue(CleanText(fields, "value", 255)); break;
   }
   case RuleAction::SendUsingRoute:
   {
      if (actor.GetAdminLevel() != Account::ServerAdmin) throw RequestError{403};
      AllowedFields(fields, {"type", "routeId"}); const auto routeId = Number(fields, "routeId", 9223372036854775807LL);
      Routes routes; routes.Refresh(); if (!routeId || !routes.GetItemByDBID(routeId)) throw RequestError{422}; action->SetRouteID(routeId); break;
   }
   default: throw RequestError{422}; // Scripts, file/local-binding actions are not web operations.
   }
   return action;
}
json::object RuleView(const Rule &rule)
{
   return {{"id", rule.GetID()}, {"accountId", rule.GetAccountID()}, {"name", Utf8(rule.GetName())},
      {"active", rule.GetActive()}, {"useAnd", rule.GetUseAND()}, {"sortOrder", rule.GetSortOrder()}};
}
json::object RuleDetail(Rule &rule)
{
   auto view = RuleView(rule); json::array criteria, actions;
   for (const auto &item : rule.GetCriterias()->GetSnapshot()) criteria.push_back(CriterionView(*item));
   for (const auto &item : rule.GetActions()->GetSnapshot()) actions.push_back(ActionView(*item));
   view["criteria"] = std::move(criteria); view["actions"] = std::move(actions); return view;
}
json::value ManageRules(const std::string &operation, json::object &arguments, const Account &actor)
{
   if (actor.GetAdminLevel() == Account::NormalUser) throw RequestError{403};
   const auto accountId = Number(arguments, "accountId", 9223372036854775807LL);
   std::shared_ptr<Account> mailbox;
   if (!accountId) { if (actor.GetAdminLevel() != Account::ServerAdmin) throw RequestError{403}; }
   else
   {
      const auto domainId = Number(arguments, "domainId", 9223372036854775807LL);
      if (actor.GetAdminLevel() != Account::ServerAdmin && domainId != actor.GetDomainID()) throw RequestError{403};
      mailbox = std::make_shared<Account>();
      if (!PersistentAccount::ReadObject(mailbox, accountId) || mailbox->GetDomainID() != domainId) throw RequestError{404};
      if (actor.GetAdminLevel() != Account::ServerAdmin && mailbox->GetAdminLevel() == Account::ServerAdmin) throw RequestError{403};
   }
   Rules rules(accountId); rules.Refresh();
   if (operation == "rules.list") return Page(rules.GetVector(), arguments, RuleView);
   const bool create = operation == "rules.create";
   if (!create && operation != "rules.read" && operation != "rules.update" && operation != "rules.delete") throw RequestError{404};
   auto rule = create ? std::make_shared<Rule>() : rules.GetItemByDBID(Id(arguments)); if (!rule) throw RequestError{404};
   if (operation == "rules.read") return RuleDetail(*rule);
   if (operation == "rules.delete")
   { if (!PersistentRule::DeleteCheckedObject(rule)) throw RequestError{503}; return json::object{{"deleted", true}}; }
   auto &fields = OperationFields(arguments); AllowedFields(fields, {"name", "active", "useAnd", "sortOrder", "criteria", "actions"});
   const bool active = Boolean(fields, "active", create ? false : rule->GetActive());
   if (create && active) throw RequestError{422};
   if ((!create && rule->GetActive() && !(fields.size() == 1 && fields.if_contains("active") && !active))
       || (active && fields.size() != 1)) throw RequestError{409};
   if (create)
   {
      if (!fields.if_contains("name")) throw RequestError{422};
      rule->SetAccountID(accountId); rule->SetActive(false);
      int last = -1; for (const auto &item : rules.GetSnapshot()) last = (std::max)(last, item->GetSortOrder());
      if (last == 2147483647) throw RequestError{422}; rule->SetSortOrder(last + 1);
   }
   if (fields.if_contains("name")) { auto name = CleanText(fields, "name", 255); if (name.IsEmpty()) throw RequestError{422}; rule->SetName(name); }
   if (fields.if_contains("sortOrder"))
   {
      const auto order = static_cast<int>(Number(fields, "sortOrder", 2147483647));
      for (const auto &item : rules.GetSnapshot()) if (item->GetID() != rule->GetID() && item->GetSortOrder() == order) throw RequestError{422};
      rule->SetSortOrder(order);
   }
   rule->SetUseAND(Boolean(fields, "useAnd", rule->GetUseAND()));
   const bool replaceCriteria = fields.if_contains("criteria") != nullptr, replaceActions = fields.if_contains("actions") != nullptr;
   if (replaceCriteria)
   {
      const auto &value = fields.at("criteria"); if (!value.is_array() || value.as_array().size() > 20) throw RequestError{422};
      auto criteria = rule->GetCriterias(); criteria->GetVector().clear();
      for (const auto &item : value.as_array()) { if (!item.is_object()) throw RequestError{422}; criteria->AddItem(ParseCriterion(item.as_object())); }
   }
   if (replaceActions)
   {
      const auto &value = fields.at("actions"); if (!value.is_array() || value.as_array().size() > 20) throw RequestError{422};
      auto actions = rule->GetActions(); actions->GetVector().clear();
      for (const auto &item : value.as_array()) { if (!item.is_object()) throw RequestError{422}; actions->AddItem(ParseAction(item.as_object(), actor, mailbox, actions->GetCount())); }
   }
   if (active)
   {
      const auto criteria = rule->GetCriterias()->GetSnapshot(); const auto actions = rule->GetActions()->GetSnapshot();
      if (criteria.empty() || actions.empty() || criteria.size() > 20 || actions.size() > 20) throw RequestError{422};
      for (const auto &item : criteria) ParseCriterion(CriterionView(*item));
      for (const auto &item : actions) ParseAction(ActionView(*item), actor, mailbox, item->GetSortOrder());
   }
   rule->SetActive(active);
   if (!PersistentRule::SaveCheckedObject(rule, replaceCriteria, replaceActions)) throw RequestError{503};
   return RuleDetail(*rule);
}
