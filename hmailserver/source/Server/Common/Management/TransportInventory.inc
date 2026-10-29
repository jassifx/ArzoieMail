// Read-only configuration inventory for guided transport setup. File paths and
// private-key material are deliberately absent from both SELECTs and responses.
struct TransportInventoryRow
{
   String name;
   json::object fields;
   String GetName() const { return name; }
};

json::value ReadTransportInventory(const std::string &operation, const json::object &arguments, const Account &actor)
{
   if (actor.GetAdminLevel() != Account::ServerAdmin) throw RequestError{403};
   const bool listeners = operation == "transport.listeners.list";
   if (!listeners && operation != "transport.certificates.list") throw RequestError{404};
   for (const auto &argument : arguments)
      if (argument.key() != "offset" && argument.key() != "limit" && argument.key() != "search") throw RequestError{422};
   auto paging = arguments;
   paging["limit"] = Number(arguments, "limit", 25, 25);
   // Validate filters even if the configured collection is empty.
   Number(paging, "offset", 2147483647);
   if (arguments.if_contains("search")) { Text(arguments, "search", 255); Wide(arguments.at("search")); }
   const auto rows = Application::Instance()->GetDBManager()->OpenRecordset(SQLCommand(listeners
      ? "select portid, portprotocol, portnumber, portaddress1, portaddress2, portconnectionsecurity, portsslcertificateid from hm_tcpipports order by portid"
      : "select sslcertificateid, sslcertificatename from hm_sslcertificates order by sslcertificateid"));
   if (!rows) throw RequestError{503};
   std::vector<std::shared_ptr<TransportInventoryRow>> items;
   while (!rows->IsEOF())
   {
      auto item = std::make_shared<TransportInventoryRow>();
      const auto id = rows->GetInt64Value(listeners ? "portid" : "sslcertificateid");
      if (id <= 0) throw RequestError{503};
      item->fields["id"] = std::to_string(id);
      if (listeners)
      {
         IPAddressSQLHelper helper;
         const String address = helper.Construct(rows, "portaddress1", "portaddress2").ToString();
         const auto protocol = rows->GetLongValue("portprotocol"), security = rows->GetLongValue("portconnectionsecurity");
         const auto port = rows->GetLongValue("portnumber");
         const auto certificate = rows->GetInt64Value("portsslcertificateid");
         if (certificate < 0) throw RequestError{503};
         const char *protocolName = protocol == STSMTP ? "SMTP" : protocol == STIMAP ? "IMAP" : protocol == STPOP3 ? "POP3" : "Unknown";
         const char *securityName = security == CSNone ? "Plaintext" : security == CSSSL ? "Implicit TLS"
            : security == CSSTARTTLSOptional ? "STARTTLS optional" : security == CSSTARTTLSRequired ? "STARTTLS required" : "Unknown";
         item->name = String(protocolName) + _T(" ") + address + _T(":") + StringParser::IntToString(port);
         item->fields["protocol"] = protocolName;
         item->fields["protocolCode"] = protocol;
         item->fields["address"] = Utf8(address);
         item->fields["port"] = port;
         item->fields["security"] = securityName;
         item->fields["securityCode"] = security;
         item->fields["certificateId"] = certificate ? json::value(std::to_string(certificate)) : json::value(nullptr);
      }
      else
      {
         item->name = rows->GetStringValue("sslcertificatename");
         item->fields["name"] = Utf8(item->name);
         item->name += _T(" ") + String(std::to_string(id).c_str());
      }
      items.push_back(item);
      rows->MoveNext();
   }
   auto result = Page(items, paging, [](const auto &item) { return item.fields; });
   result["source"] = "configured";
   result["maximumLimit"] = 25;
   return result;
}
