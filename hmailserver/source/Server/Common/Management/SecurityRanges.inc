json::value ManageSecurityRanges(const std::string &action, json::object &arguments)
{
   bool relayRiskAcknowledged = false;
   const std::vector<PolicyField<SecurityRange>> fields{
      PolicyText<SecurityRange>("name", 100, "Administrative name of this IP policy.", &SecurityRange::GetName, &SecurityRange::SetName),
      PolicyText<SecurityRange>("lowerAddress", 64, "First IPv4 or IPv6 address.", &SecurityRange::GetLowerIPString, &SecurityRange::SetLowerIPString),
      PolicyText<SecurityRange>("upperAddress", 64, "Last address, in the same family.", &SecurityRange::GetUpperIPString, &SecurityRange::SetUpperIPString),
      PolicyInt<SecurityRange>("priority", 0, 1000000, "Higher numbers take precedence. Avoid overlapping ranges with equal priority.", &SecurityRange::GetPriority, &SecurityRange::SetPriority),
      PolicyBool<SecurityRange>("allowSmtp", "Allow new SMTP connections from these addresses.", &SecurityRange::GetAllowSMTP, &SecurityRange::SetAllowSMTP),
      PolicyBool<SecurityRange>("allowImap", "Allow new IMAP connections.", &SecurityRange::GetAllowIMAP, &SecurityRange::SetAllowIMAP),
      PolicyBool<SecurityRange>("allowPop3", "Allow new POP3 connections.", &SecurityRange::GetAllowPOP3, &SecurityRange::SetAllowPOP3),
      PolicyBool<SecurityRange>("localToLocal", "Permit local sender to local recipient delivery.", &SecurityRange::GetAllowRelayL2L, &SecurityRange::SetAllowRelayL2L),
      PolicyBool<SecurityRange>("localToExternal", "Permit local sender to external recipient delivery, subject to authentication.", &SecurityRange::GetAllowRelayL2R, &SecurityRange::SetAllowRelayL2R),
      PolicyBool<SecurityRange>("externalToLocal", "Permit inbound external sender to local recipient delivery.", &SecurityRange::GetAllowRelayR2L, &SecurityRange::SetAllowRelayR2L),
      PolicyBool<SecurityRange>("externalToExternal", "Permit external relay. Require authentication unless these addresses are explicitly trusted relays.", &SecurityRange::GetAllowRelayR2R, &SecurityRange::SetAllowRelayR2R),
      PolicyBool<SecurityRange>("authLocalToLocal", "Require SMTP authentication for local-to-local delivery.", &SecurityRange::GetRequireSMTPAuthLocalToLocal, &SecurityRange::SetRequireSMTPAuthLocalToLocal),
      PolicyBool<SecurityRange>("authLocalToExternal", "Require SMTP authentication for local-to-external delivery.", &SecurityRange::GetRequireSMTPAuthLocalToExternal, &SecurityRange::SetRequireSMTPAuthLocalToExternal),
      PolicyBool<SecurityRange>("authExternalToLocal", "Require SMTP authentication for external-to-local delivery.", &SecurityRange::GetRequireSMTPAuthExternalToLocal, &SecurityRange::SetRequireSMTPAuthExternalToLocal),
      PolicyBool<SecurityRange>("authExternalToExternal", "Require SMTP authentication for external relay.", &SecurityRange::GetRequireSMTPAuthExternalToExternal, &SecurityRange::SetRequireSMTPAuthExternalToExternal),
      PolicyBool<SecurityRange>("requireTlsForAuth", "Require encrypted transport for SMTP authentication.", &SecurityRange::GetRequireTLSForAuth, &SecurityRange::SetRequireTLSForAuth),
      PolicyBool<SecurityRange>("spamProtection", "Apply spam checks to mail from this range.", &SecurityRange::GetSpamProtection, &SecurityRange::SetSpamProtection),
      PolicyBool<SecurityRange>("virusProtection", "Apply existing antivirus policy to this range.", &SecurityRange::GetVirusProtection, &SecurityRange::SetVirusProtection),
      PolicyBool<SecurityRange>("expires", "Expire this policy using the native server-local clock and cleanup scheduler.", &SecurityRange::GetExpires, &SecurityRange::SetExpires),
      {"expiresAt", "string", 0, 19, "Server-local expiry: YYYY-MM-DD HH:MM:SS. Existing automatic bans may be inspected or removed.",
         [](SecurityRange &item) {
            const auto date = item.GetExpiresTime();
            return json::value(date.GetStatus() == DateTime::valid ? Utf8(Time::GetTimeStampFromDateTime(date)) : "2001-01-01 00:00:00");
         },
         [](SecurityRange &item, const json::value &value) {
            if (!value.is_string() || value.as_string().size() != 19) throw RequestError{422};
            const auto &text = value.as_string();
            for (size_t index = 0; index < text.size(); ++index)
            {
               const char separator = index == 4 || index == 7 ? '-' : index == 10 ? ' ' : index == 13 || index == 16 ? ':' : 0;
               if (separator ? text[index] != separator : text[index] < '0' || text[index] > '9') throw RequestError{422};
            }
            if (std::string(text.data(), 4) < "1900") throw RequestError{422};
            const auto date = Time::GetDateFromSystemDate(Wide(value));
            if (date.GetStatus() != DateTime::valid || Utf8(Time::GetTimeStampFromDateTime(date)) != std::string(text)) throw RequestError{422};
            item.SetExpiresTime(date);
         }},
      {"acknowledgeUnauthenticatedRelay", "boolean", 0, 1,
         "One-request confirmation: permitting unauthenticated external delivery can create an open relay. Use only exact trusted source ranges. This acknowledgement is never stored.",
         [](SecurityRange &) { return json::value(false); },
         [&](SecurityRange &, const json::value &value) {
            if (!value.is_bool()) throw RequestError{422};
            relayRiskAcknowledged = value.as_bool();
         }}
   };
   if (action == "match")
   {
      if (arguments.size() != 1) throw RequestError{422};
      IPAddress address;
      if (!address.TryParse(Wide(json::value(Text(arguments, "address", 64))))) throw RequestError{422};
      const auto match = PersistentSecurityRange::ReadMatchingIP(address);
      return json::object{{"matched", match ? json::value(PolicyView(*match, fields)) : json::value(nullptr)},
         {"note", "Higher priority wins. Expiry follows the native cleanup scheduler; existing connections are not terminated."}};
   }
   return ManagePolicyCollection<SecurityRange, PersistentSecurityRange>(action, arguments,
      "select * from hm_securityranges order by rangeid", "rangeid", fields,
      [&](SecurityRange &item) {
         if (item.GetName().IsEmpty() || item.GetLowerIP().GetType() != item.GetUpperIP().GetType()
             || item.GetLowerIP() > item.GetUpperIP()) throw RequestError{422};
         // Preserve intentional trusted-relay configurations without silently
         // converting a routine web edit into unauthenticated external relay.
         if (!relayRiskAcknowledged && ((item.GetAllowRelayL2R() && !item.GetRequireSMTPAuthLocalToExternal())
             || (item.GetAllowRelayR2R() && !item.GetRequireSMTPAuthExternalToExternal()))) throw RequestError{422};
         if (item.GetExpires() && (item.GetExpiresTime().GetStatus() != DateTime::valid
             || Time::GetTimeStampFromDateTime(item.GetExpiresTime()) <= Time::GetCurrentDateTime())) throw RequestError{422};
      }, []() { /* New protocol connections read the native security-range persistence layer. */ });
}
