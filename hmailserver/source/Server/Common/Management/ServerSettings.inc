// Explicit management contract. Names and database columns never come from the caller.
struct ServerSetting
{
   const char *section;
   const char *name;
   const wchar_t *property;
   const char *kind;
   int minimum;
   int maximum;
   const char *help;
   bool restart;
   long mask = 0;
   // "value:label|value:label"; when present, only these values are accepted.
   const char *choices = nullptr;
   // Applied alone through its native setter, which has side effects beyond the
   // stored value (rewriting rule folders, starting or stopping the indexer).
   bool direct = false;
};
#define ADMIN_BOOL(section, name, property, help, restart) {section, name, property, "boolean", 0, 1, help, restart}
#define ADMIN_INT(section, name, property, low, high, help, restart) {section, name, property, "integer", low, high, help, restart}
#define ADMIN_TEXT(section, name, property, maximum, help, restart) {section, name, property, "string", 0, maximum, help, restart}
#define ADMIN_LOG_FLAG(name, mask, help) {"logging", name, PROPERTY_LOGGING, "boolean", 0, 1, help, false, mask}
#define ADMIN_TLS_FLAG(name, property, mask, help) {"tls", name, property, "boolean", 0, 1, help, true, mask}
#define ADMIN_BACKUP_FLAG(name, mask, help) {"backup", name, PROPERTY_BACKUPOPTIONS, "boolean", 0, 1, help, false, mask}
#define ADMIN_CHOICE(section, name, property, low, high, choices, help) {section, name, property, "integer", low, high, help, false, 0, choices}
#define ADMIN_DIRECT(section, name, property, kind, maximum, choices, help) {section, name, property, kind, 0, maximum, help, false, 0, choices, true}
const ServerSetting ServerSettings[] = {
   ADMIN_BOOL("protocols", "smtpEnabled", PROPERTY_PROTOCOLSMTP, "Enable SMTP listeners after service restart.", true),
   ADMIN_BOOL("protocols", "imapEnabled", PROPERTY_PROTOCOLIMAP, "Enable IMAP listeners after service restart.", true),
   ADMIN_BOOL("protocols", "pop3Enabled", PROPERTY_PROTOCOLPOP3, "Enable POP3 listeners after service restart.", true),
   ADMIN_INT("smtp", "maxConnections", PROPERTY_MAXSMTPCONNECTIONS, 0, 1000000, "Simultaneous SMTP connections; zero means unlimited.", false),
   ADMIN_INT("smtp", "deliveryRetries", PROPERTY_SMTPNOOFTRIES, 0, 10000, "Retries before a delivery failure is returned.", false),
   ADMIN_INT("smtp", "retryMinutes", PROPERTY_SMTPMINUTESBETWEEN, 1, 525600, "Minutes between delivery attempts.", false),
   ADMIN_INT("smtp", "maxMessageSizeKB", PROPERTY_MAXMESSAGESIZE, 0, 2147483647, "SMTP message size in KB; zero disables this limit.", false),
   ADMIN_INT("smtp", "recipientBatchSize", PROPERTY_MAXSMTPRECIPIENTSINBATCH, 1, 100000, "Maximum recipients per outgoing delivery batch.", false),
   ADMIN_INT("smtp", "ruleLoopLimit", PROPERTY_RULELOOPLIMIT, 1, 1000, "Maximum rule processing loops per message.", false),
   ADMIN_INT("smtp", "maxMxHosts", PROPERTY_MAX_NUMBER_OF_MXHOSTS, 1, 1000, "Maximum MX hosts attempted per destination.", false),
   ADMIN_BOOL("smtp", "allowNullSender", PROPERTY_ALLOWMAILFROMNULL, "Allow empty envelope senders used by delivery reports.", false),
   ADMIN_BOOL("smtp", "allowPlaintextAuthentication", PROPERTY_AUTHALLOWPLAINTEXT, "Allow plaintext SASL mechanisms; TLS policy remains independently enforced.", false),
   ADMIN_BOOL("smtp", "allowIncorrectLineEndings", PROPERTY_ALLOWINCORRECTLINEENDINGS, "Permit nonconforming SMTP line endings; leave disabled unless required.", false),
   ADMIN_BOOL("smtp", "addDeliveredToHeader", PROPERTY_ADDDELIVEREDTOHEADER, "Add the local delivery address to delivered mail.", false),
   ADMIN_TEXT("smtp", "welcomeMessage", PROPERTY_WELCOMESMTP, 255, "Single-line SMTP greeting; no CR/LF.", false),
   ADMIN_TEXT("smtp", "deliveryBindAddress", PROPERTY_SMTPDELIVERYBINDTOIP, 64, "Local outgoing IP address; empty selects automatic binding.", false),
   ADMIN_CHOICE("smtp", "deliverySecurity", PROPERTY_SMTPCONNECTIONSECURITY, 0, 3, "0:None|2:STARTTLS when offered|3:STARTTLS required",
      "Transport security for direct delivery to recipient MX hosts. Requiring STARTTLS defers mail to servers that do not offer it."),
   ADMIN_TEXT("smtp", "smartHost", PROPERTY_SMTPRELAYER, 253, "Optional upstream relay for all outgoing mail; empty delivers directly to recipient MX hosts. Routes still take precedence.", false),
   ADMIN_INT("smtp", "smartHostPort", PROPERTY_SMTPRELAYER_PORT, 1, 65535, "Smart host TCP port.", false),
   ADMIN_CHOICE("smtp", "smartHostSecurity", PROPERTY_SMTPRELAYERCONNECTIONSECURITY, 0, 3, "0:None|1:SSL/TLS|2:STARTTLS when offered|3:STARTTLS required",
      "Transport security towards the smart host. Without TLS, credentials and mail cross the network unencrypted."),
   ADMIN_BOOL("smtp", "smartHostAuthentication", PROPERTY_SMTPRELAYER_USEAUTH, "Authenticate to the smart host with the user name and the separately stored write-only credential.", false),
   ADMIN_TEXT("smtp", "smartHostUsername", PROPERTY_SMTPRELAYER_USERNAME, 255, "Smart host user name.", false),
   ADMIN_BOOL("smtp", "srsEnabled", PROPERTY_SRS_ENABLED, "Rewrite the envelope sender when forwarding to other servers, so forwarded mail passes the original domain's SPF policy.", false),
   ADMIN_INT("smtp", "srsMaxAgeDays", PROPERTY_SRS_MAXAGEDAYS, SRS::MinMaxAgeDays, SRS::MaxMaxAgeDays, "Days a rewritten address stays valid for returned bounces.", false),
   ADMIN_INT("imap", "maxConnections", PROPERTY_MAXIMAPCONNECTIONS, 0, 1000000, "Simultaneous IMAP connections; zero means unlimited.", false),
   ADMIN_BOOL("imap", "quotaEnabled", PROPERTY_ENABLEIMAPQUOTA, "Advertise the implemented QUOTA extension.", false),
   ADMIN_BOOL("imap", "idleEnabled", PROPERTY_ENABLEIMAPIDLE, "Advertise the implemented IDLE extension.", false),
   ADMIN_BOOL("imap", "sortEnabled", PROPERTY_ENABLEIMAPSORT, "Advertise the implemented SORT extension.", false),
   ADMIN_BOOL("imap", "aclEnabled", PROPERTY_ENABLEIMAPACL, "Enable shared-folder ACL support.", false),
   ADMIN_BOOL("imap", "saslPlainEnabled", PROPERTY_ENABLEIMAPSASLPLAIN, "Allow SASL PLAIN, subject to transport and security-range policy.", false),
   ADMIN_BOOL("imap", "saslInitialResponseEnabled", PROPERTY_ENABLEIMAPSASLINTIALRESPONSE, "Accept SASL initial responses.", false),
   ADMIN_TEXT("imap", "welcomeMessage", PROPERTY_WELCOMEIMAP, 255, "Single-line IMAP greeting; no CR/LF.", false),
   ADMIN_TEXT("imap", "publicFolderName", PROPERTY_IMAPPUBLICFOLDERNAME, 255, "Public-folder namespace name; clients may need to resubscribe.", false),
   ADMIN_TEXT("imap", "masterUser", PROPERTY_IMAPMASTERUSER, 320, "Optional account that may sign in to any mailbox with a SASL PLAIN authorization identity. This grants full access to every mailbox; leave empty unless required.", false),
   ADMIN_DIRECT("imap", "hierarchyDelimiter", PROPERTY_IMAP_HIERARCHY_DELIMITER, "string", 1, ".:Period (.)|/:Slash (/)|\\:Backslash (\\)",
      "Folder hierarchy separator. Saved alone; refused while a folder or rule action name contains the new character. Clients may need to resynchronize."),
   ADMIN_INT("pop3", "maxConnections", PROPERTY_MAXPOP3CONNECTIONS, 0, 1000000, "Simultaneous POP3 connections; zero means unlimited.", false),
   ADMIN_TEXT("pop3", "welcomeMessage", PROPERTY_WELCOMEPOP3, 255, "Single-line POP3 greeting; no CR/LF.", false),
   ADMIN_BOOL("autoban", "enabled", PROPERTY_AUTOBAN_ENABLED, "Create temporary bans after repeated authentication failures.", false),
   ADMIN_INT("autoban", "maxInvalidAttempts", PROPERTY_MAX_INVALID_LOGON_ATTEMPTS, 1, 100000, "Failed logons required to create a ban.", false),
   ADMIN_INT("autoban", "attemptWindowMinutes", PROPERTY_LOGON_ATTEMPTS_WITHIN, 1, 525600, "Window for counting failed logons, in minutes.", false),
   ADMIN_INT("autoban", "banMinutes", PROPERTY_AUTOBAN_MINUTES, 1, 525600, "Duration of a new automatic ban, in minutes.", false),
   ADMIN_BOOL("autoban", "disconnectInvalidClients", PROPERTY_SMTPDISCONNECTINVALIDCLIENTS, "Disconnect clients after excessive invalid commands.", false),
   ADMIN_INT("autoban", "maxIncorrectCommands", PROPERTY_SMTPMAXINCORRECTCOMMANDS, 1, 100000, "Maximum invalid SMTP commands before disconnection.", false),
   ADMIN_BOOL("greylisting", "enabled", PROPERTY_USEGREYLISTING, "Enable global greylisting, subject to domain/range policy.", false),
   ADMIN_INT("greylisting", "initialDelayMinutes", PROPERTY_GL_INITIALDELAY, 1, 525600, "Minimum wait before retry acceptance, in minutes.", false),
   ADMIN_INT("greylisting", "unusedRetentionHours", PROPERTY_GL_INITIALDELETE, 1, 87600, "Retention of unused triplets, in hours.", false),
   ADMIN_INT("greylisting", "usedRetentionHours", PROPERTY_GL_FINALDELETE, 1, 87600, "Retention of accepted triplets, in hours.", false),
   ADMIN_BOOL("greylisting", "bypassOnSpfSuccess", PROPERTY_BYPASS_GREYLISTING_ON_SPFSUCCESS, "Skip greylisting on successful SPF evaluation.", false),
   ADMIN_BOOL("greylisting", "bypassMailFromMx", PROPERTY_BYPASS_GREYLISTING_ON_MAILFROMMX, "Skip greylisting for sender-domain MX hosts.", false),
   ADMIN_INT("performance", "deliveryThreads", PROPERTY_MAXDELIVERYTHREADS, 1, 1024, "Maximum simultaneous delivery workers.", false),
   ADMIN_INT("performance", "asynchronousTasks", PROPERTY_MAX_NUMBER_OF_ASYNC_TASKS, 1, 4096, "Maximum simultaneous asynchronous tasks.", false),
   ADMIN_INT("performance", "tcpThreads", PROPERTY_TCPIPTHREADS, 1, 256, "Networking worker threads; requires restart.", true),
   ADMIN_BOOL("performance", "cacheEnabled", PROPERTY_USECACHE, "Enable business-object caching.", false),
   ADMIN_INT("performance", "domainCacheSeconds", PROPERTY_DOMAINCACHETTL, 0, 86400, "Domain cache lifetime in seconds.", false),
   ADMIN_INT("performance", "accountCacheSeconds", PROPERTY_ACCOUNTCACHETTL, 0, 86400, "Account cache lifetime in seconds.", false),
   ADMIN_INT("performance", "aliasCacheSeconds", PROPERTY_ALIASCACHETTL, 0, 86400, "Alias cache lifetime in seconds.", false),
   ADMIN_INT("performance", "listCacheSeconds", PROPERTY_DISTRIBUTIONLISTCACHETTL, 0, 86400, "Distribution-list cache lifetime in seconds.", false),
   ADMIN_DIRECT("performance", "messageIndexing", PROPERTY_MESSAGE_INDEXING, "boolean", 1, nullptr,
      "Index message metadata to speed up IMAP SORT and searches. Saved alone; starts or stops the indexer immediately."),
   ADMIN_BOOL("general", "ipv6Preferred", PROPERTY_IPV6_PREFERRED, "Prefer IPv6 when resolving outgoing destinations.", false),
   ADMIN_BOOL("general", "createSpecialUseFolders", PROPERTY_CREATE_DEFAULT_SPECIAL_USE_FOLDERS, "Create default special-use folders for new mailboxes.", false),
   ADMIN_TEXT("general", "defaultDomain", PROPERTY_DEFAULTDOMAIN, 253, "Optional domain appended to unqualified login names.", false),
   ADMIN_TEXT("general", "hostName", PROPERTY_HOSTNAME, 253, "Server hostname used in mail protocol identification.", false),
   ADMIN_TEXT("general", "mirrorAddress", PROPERTY_MIRROREMAILADDRESS, 320, "Optional copy destination; consider privacy and forwarding loops.", false),
   ADMIN_BOOL("antivirus", "clamAvEnabled", PROPERTY_CLAMAV_ENABLED, "Use the existing ClamAV provider; this does not install its engine.", false),
   ADMIN_TEXT("antivirus", "clamAvHost", PROPERTY_CLAMAV_HOST, 253, "ClamAV service hostname or IP address.", false),
   ADMIN_INT("antivirus", "clamAvPort", PROPERTY_CLAMAV_PORT, 1, 65535, "ClamAV service TCP port.", false),
   ADMIN_INT("antivirus", "maxScanSizeKB", PROPERTY_AV_MAXMSGSIZE, 0, 2147483647, "Existing scanner maximum message size, in KB; legacy skip policy still applies.", false),
   ADMIN_BOOL("antivirus", "attachmentBlockingEnabled", PROPERTY_ENABLEATTACHMENTBLOCKING, "Enable configured blocked attachment rules.", false),
   ADMIN_INT("antivirus", "action", PROPERTY_AV_ACTION, 0, 1, "0 deletes infected mail; 1 strips infected attachments under existing engine policy.", false),
   ADMIN_BOOL("antivirus", "notifySender", PROPERTY_AV_NOTIFY_SENDER, "Notify senders after detection; forged senders may cause backscatter.", false),
   ADMIN_BOOL("antivirus", "notifyReceiver", PROPERTY_AV_NOTIFY_RECEIVER, "Notify recipients after detection.", false),
   ADMIN_INT("passwordhash", "algorithm", PROPERTY_PASSWORDHASHALGORITHM, 0, 2, "0 selects the default Argon2id; 1 is Argon2id; 2 is PBKDF2-SHA256. Existing hashes retain their recorded format.", false),
   ADMIN_INT("passwordhash", "memoryKiB", PROPERTY_PASSWORDHASHMEMORYCOST, 0, PasswordHasher::MaxArgon2idMemoryCostKb, "Argon2id memory in KiB: 0 uses 19456; explicit values must be at least 4096. Benchmark authentication capacity before increasing.", false),
   ADMIN_INT("passwordhash", "iterations", PROPERTY_PASSWORDHASHITERATIONS, 0, PasswordHasher::MaxPBKDF2Iterations, "0 uses algorithm defaults (Argon2id 2, PBKDF2 600000). Explicit Argon2id: 1-20; PBKDF2: 10000-10000000. Change algorithm and iterations together.", false),
   ADMIN_BOOL("passwordhash", "autoUpgrade", PROPERTY_PASSWORDHASHAUTOUPGRADE, "Rehash on successful authentication when stored parameters differ. A lower configured cost can also cause rehashing; retain defaults unless deliberately tuning.", false),
   ADMIN_LOG_FLAG("enabled", Logger::LSEnabled, "Enable native logging. Keep log storage private and monitor disk capacity."),
   ADMIN_LOG_FLAG("application", Logger::LSApplication, "Record mail-processing application activity."),
   ADMIN_LOG_FLAG("smtp", Logger::LSSMTP, "Record SMTP conversations; logs contain mail metadata."),
   ADMIN_LOG_FLAG("pop3", Logger::LSPOP3, "Record POP3 conversations."),
   ADMIN_LOG_FLAG("imap", Logger::LSIMAP, "Record IMAP conversations."),
   ADMIN_LOG_FLAG("tcpIp", Logger::LSTCPIP, "Record transport connection diagnostics."),
   ADMIN_LOG_FLAG("debug", Logger::LSDebug, "Detailed diagnostics increase log volume and may expose operational metadata."),
   ADMIN_LOG_FLAG("keepFilesOpen", Logger::LSKeepFilesOpen, "Keep native log files open between writes."),
   ADMIN_BOOL("logging", "awStats", PROPERTY_AWSTATSENABLED, "Enable the existing AWStats-compatible mail activity log.", false),
   ADMIN_BOOL("tls", "verifyRemoteCertificate", PROPERTY_VERIFYREMOTESSLCERTIFICATE, "Verify remote mail-server certificates. Disabling verification allows server impersonation; retain enabled for secure transport.", false),
   ADMIN_TEXT("tls", "cipherList", PROPERTY_SSLCIPHERLIST, 4096, "OpenSSL cipher expression for TLS 1.2 and earlier; empty uses library defaults. TLS 1.3 suites are independent. Restart required.", true),
   ADMIN_TLS_FLAG("tls10Enabled", PROPERTY_SSLVERSIONS, TlsVersion10, "Obsolete TLS 1.0 compatibility; leave disabled. Requires restart."),
   ADMIN_TLS_FLAG("tls11Enabled", PROPERTY_SSLVERSIONS, TlsVersion11, "Obsolete TLS 1.1 compatibility; leave disabled. Requires restart."),
   ADMIN_TLS_FLAG("tls12Enabled", PROPERTY_SSLVERSIONS, TlsVersion12, "Enable TLS 1.2 for mail transport after restart."),
   ADMIN_TLS_FLAG("tls13Enabled", PROPERTY_SSLVERSIONS, TlsVersion13, "Enable TLS 1.3 for mail transport after restart."),
   ADMIN_TLS_FLAG("preferServerCiphers", PROPERTY_TLSOPTIONS, TlsOptionPreferServerCiphers, "Prefer the server cipher order after restart."),
   ADMIN_BACKUP_FLAG("includeSettings", Backup::BOSettings, "Include server settings in backups."),
   ADMIN_BACKUP_FLAG("includeDomains", Backup::BODomains, "Include domains, mailboxes, aliases, lists and their settings."),
   ADMIN_BACKUP_FLAG("includeMessages", Backup::BOMessages, "Include stored messages; requires domains. Backups grow with the message store."),
   ADMIN_BACKUP_FLAG("compress", Backup::BOCompression, "Compress the message files inside the archive."),
   ADMIN_TEXT("backup", "destination", PROPERTY_BACKUPDESTINATION, 240, "Existing local folder the service writes backup archives to. Keep it private: archives hold every mailbox and setting.", false),
   ADMIN_TLS_FLAG("prioritizeChaCha", PROPERTY_TLSOPTIONS, TlsOptionPrioritizeChaCha, "Prioritize ChaCha when the client prefers it; requires server cipher preference and TLS 1.2 or 1.3. Restart required.")
};
#undef ADMIN_BOOL
#undef ADMIN_INT
#undef ADMIN_TEXT
#undef ADMIN_LOG_FLAG
#undef ADMIN_TLS_FLAG
#undef ADMIN_CHOICE
#undef ADMIN_BACKUP_FLAG
#undef ADMIN_DIRECT

json::array SettingChoices(const ServerSetting &field)
{
   json::array options;
   for (std::string_view rest = field.choices ? field.choices : ""; !rest.empty();)
   {
      const auto end = rest.find('|');
      const auto item = rest.substr(0, end), value = item.substr(0, item.find(':')), label = item.substr(item.find(':') + 1);
      options.push_back(json::object{{"value", std::string_view(field.kind) == "integer" ? json::value(std::stoll(std::string(value))) : json::value(value)},
         {"label", label}});
      rest = end == std::string_view::npos ? std::string_view() : rest.substr(end + 1);
   }
   return options;
}

json::value ReadServerSetting(const ServerSetting &field, PropertySet &properties, const std::map<String, long> &masks)
{
   if (field.mask) return (masks.at(field.property) & field.mask) != 0;
   if (std::string_view(field.kind) == "string") return json::value(Utf8(properties.GetString(field.property)));
   if (std::string_view(field.kind) == "boolean") return properties.GetBool(field.property);
   return properties.GetLong(field.property);
}

json::value ManageServerSettings(const std::string &operation, json::object &arguments, const Account &actor)
{
   if (actor.GetAdminLevel() != Account::ServerAdmin) throw RequestError{403};
   if (operation == "settings.smarthost.credential")
   {
      // Write-only: the stored credential is never returned. An empty value clears it.
      SecretWipe wipe{arguments.if_contains("password")};
      if (arguments.size() != 1) throw RequestError{422};
      const auto password = Text(arguments, "password", 1024);
      for (const unsigned char character : password) if (character < 32 || character == 127) throw RequestError{422};
      auto smtp = Configuration::Instance()->GetSMTPConfiguration();
      String value = Wide(arguments.at("password"));
      smtp->SetSMTPRelayerPassword(value);
      SecureZeroMemory(value.data(), value.size() * sizeof(wchar_t));
      return json::object{{"configured", !smtp->GetSMTPRelayerPassword().IsEmpty()}};
   }
   if (operation == "settings.administrator.password")
   {
      // The built-in Administrator is not a mailbox; its hash lives in hMailServer.ini.
      // Any server administrator may change it, but only by proving the current one.
      SecretWipe current{arguments.if_contains("currentPassword")}, replacement{arguments.if_contains("newPassword")};
      if (arguments.size() != 2) throw RequestError{422};
      Text(arguments, "currentPassword", 1024);
      const auto password = Text(arguments, "newPassword", 1024);
      if (password.empty()) throw RequestError{422};
      for (const unsigned char character : password) if (character < 32 || character == 127) throw RequestError{422};
      String proof = Wide(arguments.at("currentPassword")), value = Wide(arguments.at("newPassword"));
      const bool verified = ManagementAuthentication::Authenticate(_T("Administrator"), proof) != nullptr;
      SecureZeroMemory(proof.data(), proof.size() * sizeof(wchar_t));
      if (!verified) { SecureZeroMemory(value.data(), value.size() * sizeof(wchar_t)); throw RequestError{403}; }
      const bool saved = IniFileSettings::Instance()->SetAdministratorPassword(value);
      SecureZeroMemory(value.data(), value.size() * sizeof(wchar_t));
      if (!saved) throw RequestError{503};
      LOG_APPLICATION("The built-in Administrator password was changed through the management API.");
      return json::object{{"changed", true}};
   }
   if (operation == "settings.backup.passphrase")
   {
      // Write-only, like the smart-host credential. Empty clears it, so new backups are unencrypted.
      SecretWipe wipe{arguments.if_contains("passphrase")};
      if (arguments.size() != 1) throw RequestError{422};
      const auto passphrase = Text(arguments, "passphrase", 1024);
      if (!passphrase.empty() && passphrase.size() < 12) throw RequestError{422};
      for (const unsigned char character : passphrase) if (character < 32 || character == 127) throw RequestError{422};
      String value = Wide(arguments.at("passphrase"));
      Configuration::Instance()->SetBackupPassphrase(value);
      SecureZeroMemory(value.data(), value.size() * sizeof(wchar_t));
      return json::object{{"configured", !Configuration::Instance()->GetBackupPassphrase().IsEmpty()}};
   }
   if (operation == "settings.messageindex.clear")
   {
      if (!arguments.empty()) throw RequestError{422};
      PersistentMessageMetaData metadata;
      metadata.Clear();
      return json::object{{"cleared", true}};
   }
   if (operation != "settings.read" && operation != "settings.update") throw RequestError{404};
   const auto section = Text(arguments, "section", 32);
   auto properties = Configuration::Instance()->GetSettings();
   json::array schema;
   json::object values;
   std::map<String, long> masks;
   for (const auto &field : ServerSettings) if (section == field.section && field.mask && !masks.count(field.property))
      masks[field.property] = properties->GetLong(field.property);
   for (const auto &field : ServerSettings) if (section == field.section)
   {
      values[field.name] = ReadServerSetting(field, *properties, masks);
      json::object rule{{"name", field.name}, {"type", field.kind}, {"minimum", field.minimum},
         {"maximum", field.maximum}, {"help", field.help}, {"restart", field.restart}};
      if (field.choices) rule["options"] = SettingChoices(field);
      if (field.direct) rule["direct"] = true;
      schema.push_back(std::move(rule));
   }
   if (values.empty()) throw RequestError{404};
   if (operation == "settings.update")
   {
      for (const auto &argument : arguments)
         if (argument.key() != "section" && argument.key() != "fields" && argument.key() != "expected") throw RequestError{422};
      const auto *patch = arguments.if_contains("fields"), *expected = arguments.if_contains("expected");
      if (!patch || !expected || !patch->is_object() || !expected->is_object() || patch->as_object().empty()
          || patch->as_object().size() != expected->as_object().size()) throw RequestError{422};
      std::vector<PropertySet::CheckedChange> changes;
      for (const auto &entry : patch->as_object())
      {
         const ServerSetting *rule = nullptr;
         for (const auto &field : ServerSettings) if (section == field.section && entry.key() == field.name) rule = &field;
         if (!rule) throw RequestError{422};
         const auto *before = expected->as_object().if_contains(entry.key());
         if (!before || before->kind() != values.at(entry.key()).kind()) throw RequestError{422};
         if (*before != values.at(entry.key())) throw RequestError{409};
         if (rule->choices)
         {
            const auto options = SettingChoices(*rule);
            if (std::none_of(options.begin(), options.end(), [&](const json::value &option) { return option.as_object().at("value") == entry.value(); }))
               throw RequestError{422};
         }
         if (rule->direct)
         {
            if (patch->as_object().size() != 1 || entry.value().kind() != before->kind()) throw RequestError{422};
            if (entry.key() == "hierarchyDelimiter")
            {
               // Refused while a folder or rule action already contains the character;
               // otherwise rule folder references are rewritten and rule caches cleared.
               if (!Configuration::Instance()->GetIMAPConfiguration()->SetHierarchyDelimiter(Wide(entry.value()))) throw RequestError{409};
            }
            else if (entry.key() == "messageIndexing") Configuration::Instance()->SetMessageIndexing(entry.value().as_bool());
            else throw RequestError{422};
            values[entry.key()] = ReadServerSetting(*rule, *properties, masks);
            if (values.at(entry.key()) != entry.value()) throw RequestError{503};
            return json::object{{"section", section}, {"values", std::move(values)}, {"schema", std::move(schema)}};
         }
         PropertySet::CheckedChange change{rule->property, std::string_view(rule->kind) == "string", 0, 0, {}, {}};
         if (change.text)
         {
            const std::string name(entry.key());
            const auto text = Text(patch->as_object(), name.c_str(), rule->maximum);
            for (const unsigned char character : text)
               if ((character < 32 || character == 127) && !(section == "tls" && name == "cipherList" && (character == '\r' || character == '\n'))) throw RequestError{422};
            change.expectedString = Wide(*before); change.value = Wide(entry.value());
            if (name == "deliveryBindAddress" && !text.empty())
            {
               boost::system::error_code error;
               boost::asio::ip::make_address(text, error);
               if (error) throw RequestError{422};
            }
            if (name == "defaultDomain" && !text.empty() && !StringParser::IsValidDomainName(change.value)) throw RequestError{422};
            if (name == "masterUser" && !text.empty())
            {
               // A bare local part takes the domain of the authenticating account.
               const String user = change.value.Find(_T("@")) >= 0 ? change.value : change.value + _T("@example.com");
               if (!StringParser::IsValidEmailAddress(user) || user.FindOneOf(_T(" \t,;\"")) != -1) throw RequestError{422};
            }
            if ((name == "hostName" || name == "clamAvHost" || name == "smartHost") && !text.empty())
            {
               boost::system::error_code error;
               boost::asio::ip::make_address(text, error);
               if (error && OperationDomain(patch->as_object(), name.c_str()).Find(_T("*")) >= 0) throw RequestError{422};
            }
            if (name == "destination" && !text.empty())
            {
               // An existing, absolute local folder: no UNC share, device path, link or the Windows folder.
               const std::wstring path = change.value;
               wchar_t windows[MAX_PATH] = {};
               GetWindowsDirectoryW(windows, MAX_PATH);
               const DWORD attributes = GetFileAttributesW(path.c_str());
               if (path.size() < 3 || path[1] != L':' || path[2] != L'\\' || path.find(L"..") != std::wstring::npos
                   || attributes == INVALID_FILE_ATTRIBUTES || !(attributes & FILE_ATTRIBUTE_DIRECTORY) || (attributes & FILE_ATTRIBUTE_REPARSE_POINT)
                   || _wcsnicmp(path.c_str(), windows, wcslen(windows)) == 0) throw RequestError{422};
            }
            if (name == "mirrorAddress" && !text.empty() && !StringParser::IsValidEmailAddress(change.value)) throw RequestError{422};
            if (name == "publicFolderName" && text.empty()) throw RequestError{422};
         }
         else
         {
            const bool boolean = std::string_view(rule->kind) == "boolean";
            if (boolean ? !entry.value().is_bool() : !entry.value().is_int64()) throw RequestError{422};
            const auto number = boolean ? (entry.value().as_bool() ? 1LL : 0LL) : entry.value().as_int64();
            if (number < rule->minimum || number > rule->maximum) throw RequestError{422};
            change.expectedNumber = boolean ? (before->as_bool() ? 1L : 0L) : static_cast<long>(before->as_int64());
            change.number = static_cast<long>(number);
            if (rule->mask)
            {
               const auto originalMask = masks.at(rule->property);
               change.expectedNumber = originalMask;
               change.number = number ? originalMask | rule->mask : originalMask & ~rule->mask;
               auto existing = std::find_if(changes.begin(), changes.end(), [&](const auto &item) { return item.name == change.name; });
               if (existing != changes.end())
               {
                  existing->number = number ? existing->number | rule->mask : existing->number & ~rule->mask;
                  continue;
               }
            }
         }
         changes.push_back(std::move(change));
      }
      if (section == "tls")
      {
         auto effective = values;
         for (const auto &entry : patch->as_object()) effective[entry.key()] = entry.value();
         const bool modern = effective.at("tls12Enabled").as_bool() || effective.at("tls13Enabled").as_bool();
         if (!modern && !effective.at("tls10Enabled").as_bool() && !effective.at("tls11Enabled").as_bool()) throw RequestError{422};
         if (effective.at("prioritizeChaCha").as_bool() && (!modern || !effective.at("preferServerCiphers").as_bool())) throw RequestError{422};
         // Match the existing transport's whitespace normalization. Probe before
         // persistence without replacing any active listener or client context.
         std::string cipher(effective.at("cipherList").as_string());
         cipher.erase(std::remove_if(cipher.begin(), cipher.end(), [](char c) { return c == ' ' || c == '\r' || c == '\n'; }), cipher.end());
         if (!cipher.empty())
         {
            std::unique_ptr<SSL_CTX, decltype(&SSL_CTX_free)> context(SSL_CTX_new(TLS_method()), SSL_CTX_free);
            if (!context) throw RequestError{503};
            const bool valid = SSL_CTX_set_cipher_list(context.get(), cipher.c_str()) == 1;
            ERR_clear_error();
            if (!valid) throw RequestError{422};
         }
      }
      if (section == "backup")
      {
         auto effective = values;
         for (const auto &entry : patch->as_object()) effective[entry.key()] = entry.value();
         // Messages are restored into domains, so a backup cannot hold them without domains.
         if (effective.at("includeMessages").as_bool() && !effective.at("includeDomains").as_bool()) throw RequestError{422};
      }
      if (section == "passwordhash")
      {
         auto effective = values;
         for (const auto &entry : patch->as_object()) effective[entry.key()] = entry.value();
         const auto algorithm = effective.at("algorithm").as_int64();
         const auto memory = effective.at("memoryKiB").as_int64(), iterations = effective.at("iterations").as_int64();
         if (memory != 0 && memory < PasswordHasher::MinArgon2idMemoryCostKb) throw RequestError{422};
         const bool pbkdf2 = algorithm == PasswordHasher::AlgorithmPBKDF2SHA256;
         if (iterations != 0 && (iterations < (pbkdf2 ? PasswordHasher::MinPBKDF2Iterations : PasswordHasher::MinArgon2idIterations)
             || iterations > (pbkdf2 ? PasswordHasher::MaxPBKDF2Iterations : PasswordHasher::MaxArgon2idIterations))) throw RequestError{422};
         // Guard dependent settings even if omitted from the patch: a concurrent
         // algorithm change must not invalidate the iteration range we just checked.
         for (const auto &field : ServerSettings) if (section == field.section && !patch->as_object().if_contains(field.name))
         {
            const auto &before = values.at(field.name);
            const auto number = before.is_bool() ? (before.as_bool() ? 1L : 0L) : static_cast<long>(before.as_int64());
            changes.push_back({field.property, false, number, number, {}, {}});
         }
      }
      if (section == "tls")
      {
         // Guard every dependency, including omitted bits and the cipher string.
         // A concurrent legacy/API edit must invalidate this validated proposal.
         for (const auto &field : ServerSettings) if (section == field.section)
         {
            if (std::any_of(changes.begin(), changes.end(), [&](const auto &item) { return item.name == field.property; })) continue;
            if (std::string_view(field.kind) == "string")
            {
               const auto text = Wide(values.at(field.name));
               changes.push_back({field.property, true, 0, 0, text, text});
            }
            else
            {
               const auto &before = values.at(field.name);
               const long number = field.mask ? masks.at(field.property) : (before.as_bool() ? 1L : 0L);
               changes.push_back({field.property, false, number, number, {}, {}});
            }
         }
      }
      const auto result = properties->ApplyChecked(changes);
      if (result != PropertySet::CheckedResult::Success)
         throw RequestError{result == PropertySet::CheckedResult::Conflict ? 409 : result == PropertySet::CheckedResult::Unsupported ? 412 : 503};
      values.clear();
      for (auto &entry : masks) entry.second = properties->GetLong(entry.first);
      for (const auto &field : ServerSettings) if (section == field.section) values[field.name] = ReadServerSetting(field, *properties, masks);
   }
   json::object result{{"section", section}, {"values", std::move(values)}, {"schema", std::move(schema)}};
   // Write-only secrets report only whether one is stored.
   if (section == "backup")
      result["credentials"] = json::object{{"passphraseConfigured", !Configuration::Instance()->GetBackupPassphrase().IsEmpty()}};
   if (section == "smtp")
      result["credentials"] = json::object{{"smartHostConfigured", !Configuration::Instance()->GetSMTPConfiguration()->GetSMTPRelayerPassword().IsEmpty()}};
   return result;
}
