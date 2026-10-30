// Policy collections use explicit, typed adapters rather than COM/object reflection.
template<class T> struct PolicyField
{
   const char *name;
   const char *kind;
   int minimum;
   int maximum;
   const char *help;
   std::function<json::value(T &)> read;
   std::function<void(T &, const json::value &)> write;
};
template<class T, class Getter, class Setter>
PolicyField<T> PolicyText(const char *name, int maximum, const char *help, Getter get, Setter set)
{
   return {name, "string", 0, maximum, help,
      [get](T &item) { return json::value(Utf8(std::invoke(get, item))); },
      [set, maximum](T &item, const json::value &value)
      {
         if (!value.is_string() || value.as_string().size() > static_cast<size_t>(maximum)) throw RequestError{422};
         for (const unsigned char c : value.as_string()) if (c < 32 || c == 127) throw RequestError{422};
         const auto text = Wide(value);
         if constexpr (std::is_same_v<std::invoke_result_t<Setter, T &, const String &>, bool>)
         { if (!std::invoke(set, item, text)) throw RequestError{422}; }
         else std::invoke(set, item, text);
      }};
}
template<class T, class Getter, class Setter>
PolicyField<T> PolicyBool(const char *name, const char *help, Getter get, Setter set)
{
   return {name, "boolean", 0, 1, help,
      [get](T &item) { return json::value(std::invoke(get, item)); },
      [set](T &item, const json::value &value)
      { if (!value.is_bool()) throw RequestError{422}; std::invoke(set, item, value.as_bool()); }};
}
template<class T, class Getter, class Setter>
PolicyField<T> PolicyInt(const char *name, int low, int high, const char *help, Getter get, Setter set)
{
   return {name, "integer", low, high, help,
      [get](T &item) { return json::value(static_cast<__int64>(std::invoke(get, item))); },
      [set, low, high](T &item, const json::value &value)
      {
         if (!value.is_int64() || value.as_int64() < low || value.as_int64() > high) throw RequestError{422};
         std::invoke(set, item, static_cast<int>(value.as_int64()));
      }};
}
template<class T>
json::object PolicyView(T &item, const std::vector<PolicyField<T>> &fields)
{
   json::object result{{"id", std::to_string(item.GetID())}};
   for (const auto &field : fields) result[field.name] = field.read(item);
   return result;
}
template<class T, class Persistence, class Validate, class Refresh>
json::value ManagePolicyCollection(const std::string &action, json::object &arguments,
   const char *selectSql, const char *idColumn, const std::vector<PolicyField<T>> &fields, Validate validate, Refresh refresh)
{
   if (action != "list" && action != "read" && action != "create" && action != "update" && action != "delete") throw RequestError{404};
   const auto load = [&]()
   {
      std::vector<std::shared_ptr<T>> items;
      const auto rows = Application::Instance()->GetDBManager()->OpenRecordset(SQLCommand(selectSql));
      if (!rows) throw RequestError{503};
      while (!rows->IsEOF())
      {
         const auto rawId = rows->GetInt64Value(idColumn);
         // Legacy adapters narrow some policy keys. Fail visibly before invoking them;
         // never wrap a stored key into another policy object's identity.
         if (rawId <= 0 || rawId > 2147483647) throw RequestError{503};
         auto item = std::make_shared<T>();
         if (!Persistence::ReadObject(item, rows)) throw RequestError{503};
         items.push_back(item); rows->MoveNext();
      }
      return items;
   };
   auto items = load();
   const auto view = [&](T &item) { return PolicyView(item, fields); };
   if (action == "list")
   {
      auto result = Page(items, arguments, view);
      json::array schema;
      for (const auto &field : fields) schema.push_back(json::object{{"name", field.name}, {"type", field.kind},
         {"minimum", field.minimum}, {"maximum", field.maximum}, {"help", field.help}});
      T defaults;
      result["schema"] = std::move(schema); result["defaults"] = view(defaults);
      result["idMaximum"] = 2147483647;
      return result;
   }
   std::shared_ptr<T> original;
   if (action != "create")
   {
      const auto id = Id(arguments);
      if (id > 2147483647) throw RequestError{422}; // These legacy schema keys are SQL INT.
      for (const auto &item : items) if (item->GetID() == id) { original = item; break; }
      if (!original) throw RequestError{404};
      if (action == "read") return view(*original);
   }
   auto &patch = OperationFields(arguments);
   if (original)
   {
      const auto *expected = patch.if_contains("expected");
      if (!expected || !expected->is_object()) throw RequestError{422};
      if (expected->as_object() != view(*original)) throw RequestError{409};
   }
   if (action == "delete")
   {
      if (patch.size() != 1) throw RequestError{422};
      if (!Persistence::DeleteObject(original)) throw RequestError{503};
      refresh();
      for (const auto &item : load()) if (item->GetID() == original->GetID()) throw RequestError{503};
      return json::object{{"deleted", true}};
   }
   auto target = original ? std::make_shared<T>(*original) : std::make_shared<T>();
   bool changed = false;
   for (const auto &entry : patch)
   {
      if (entry.key() == "expected" && original) continue;
      const PolicyField<T> *rule = nullptr;
      for (const auto &field : fields) if (entry.key() == field.name) rule = &field;
      if (!rule) throw RequestError{422};
      rule->write(*target, entry.value()); changed = true;
   }
   if (!changed) throw RequestError{422};
   validate(*target);
   String name = target->GetName(); name.ToLower();
   for (const auto &item : items)
   {
      String existing = item->GetName(); existing.ToLower();
      if (existing == name && item->GetID() != target->GetID()) throw RequestError{409};
   }
   String error;
   if (!Persistence::SaveObject(target, error, PersistenceModeNormal)) throw RequestError{error.IsEmpty() ? 503 : 422};
   if (target->GetID() <= 0 || target->GetID() > 2147483647) throw RequestError{503};
   for (const auto &saved : load()) if (saved->GetID() == target->GetID())
   {
      if (view(*saved) != view(*target)) throw RequestError{503};
      refresh(); return view(*saved);
   }
   throw RequestError{503};
}

#include "SecurityRanges.inc"

json::value ManagePolicies(const std::string &operation, json::object &arguments, const Account &actor)
{
   if (actor.GetAdminLevel() != Account::ServerAdmin) throw RequestError{403};
   const auto separator = operation.find('.');
   if (separator == std::string::npos) throw RequestError{404};
   const auto resource = operation.substr(0, separator), action = operation.substr(separator + 1);
   auto &spam = Configuration::Instance()->GetAntiSpamConfiguration();
   if (resource == "securityranges") return ManageSecurityRanges(action, arguments);
   if (resource == "surbl")
   {
      const std::vector<PolicyField<SURBLServer>> fields{
         PolicyText<SURBLServer>("zone", 253, "DNS suffix of the URI blocklist provider.", &SURBLServer::GetDNSHost, &SURBLServer::SetDNSHost),
         PolicyBool<SURBLServer>("active", "New providers are disabled until explicitly enabled.", &SURBLServer::GetIsActive, &SURBLServer::SetIsActive),
         PolicyInt<SURBLServer>("score", 0, 10000, "Score added after a matching URI lookup.", &SURBLServer::GetScore, &SURBLServer::SetScore),
         PolicyText<SURBLServer>("rejectMessage", 255, "Single-line explanation for a matching URI.", &SURBLServer::GetRejectMessage, &SURBLServer::SetRejectMessage)};
      return ManagePolicyCollection<SURBLServer, PersistentSURBLServer>(action, arguments,
         "select * from hm_surblservers order by surblid", "surblid", fields,
         [](SURBLServer &item)
         {
            if (item.GetDNSHost().IsEmpty() || !StringParser::IsValidDomainName(item.GetDNSHost()) || item.GetDNSHost().Find(_T("*")) >= 0) throw RequestError{422};
         }, [&]() { spam.GetSURBLServers()->Refresh(); });
   }
   if (resource == "blockedattachments")
   {
      const std::vector<PolicyField<BlockedAttachment>> fields{
         PolicyText<BlockedAttachment>("pattern", 255, "Attachment filename pattern, for example *.exe.", &BlockedAttachment::GetWildcard, &BlockedAttachment::SetWildcard),
         PolicyText<BlockedAttachment>("description", 255, "Description of the attachment policy.", &BlockedAttachment::GetDescription, &BlockedAttachment::SetDescription)};
      return ManagePolicyCollection<BlockedAttachment, PersistentBlockedAttachment>(action, arguments,
         "select * from hm_blocked_attachments order by baid", "baid", fields,
         [](BlockedAttachment &item)
         {
            if (item.GetWildcard().IsEmpty() || item.GetWildcard().Find(_T("/")) >= 0 || item.GetWildcard().Find(_T("\\")) >= 0) throw RequestError{422};
         }, []() { Configuration::Instance()->GetBlockedAttachments()->Refresh(); });
   }
   if (resource == "incomingrelays")
   {
      const std::vector<PolicyField<IncomingRelay>> fields{
         PolicyText<IncomingRelay>("name", 100, "Name of the trusted incoming relay.", &IncomingRelay::GetName, &IncomingRelay::SetName),
         PolicyText<IncomingRelay>("lowerAddress", 64, "First IP address trusted to supply upstream sender information.", &IncomingRelay::GetLowerIPString, &IncomingRelay::SetLowerIPString),
         PolicyText<IncomingRelay>("upperAddress", 64, "Last address in the same IPv4 or IPv6 family.", &IncomingRelay::GetUpperIPString, &IncomingRelay::SetUpperIPString)};
      return ManagePolicyCollection<IncomingRelay, PersistentIncomingRelay>(action, arguments,
         "select * from hm_incoming_relays order by relayid", "relayid", fields,
         [](IncomingRelay &item)
         {
            if (item.GetName().IsEmpty() || item.GetLowerIP().GetType() != item.GetUpperIP().GetType()
                || item.GetLowerIP() > item.GetUpperIP()) throw RequestError{422};
         }, []() { if (!Configuration::Instance()->GetSMTPConfiguration()->GetIncomingRelays()->Refresh()) throw RequestError{503}; });
   }
   throw RequestError{404};
}
