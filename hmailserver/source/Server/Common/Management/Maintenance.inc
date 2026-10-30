// Backup, restore and the installation secret key. Server administrators only; the dispatcher
// checks that first. Archives are chosen by exact name from the configured backup folder.
bool IsBackupArchiveName(const std::string &name)
{
   static const std::regex pattern("^HMBackup [0-9A-Za-z _.-]{1,64}\\.7z$");
   return name.size() <= 80 && std::regex_match(name, pattern) && name.find("..") == std::string::npos;
}

json::array BackupArchives(const std::wstring &folder)
{
   std::vector<json::object> files;
   WIN32_FIND_DATAW found{};
   HANDLE search = FindFirstFileW((folder + L"\\HMBackup *.7z").c_str(), &found);
   if (search != INVALID_HANDLE_VALUE)
   {
      do
      {
         if (found.dwFileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) continue;
         const auto name = Utf8(String(found.cFileName));
         if (!IsBackupArchiveName(name)) continue;
         files.push_back(json::object{{"name", name}, {"size", (static_cast<__int64>(found.nFileSizeHigh) << 32) | found.nFileSizeLow},
            {"modified", FileTimeText(found.ftLastWriteTime)}});
      } while (FindNextFileW(search, &found));
      FindClose(search);
   }
   std::sort(files.begin(), files.end(), [](const json::object &left, const json::object &right)
      { return left.at("modified").as_string() > right.at("modified").as_string(); });
   if (files.size() > 100) files.resize(100);
   return json::array(files.begin(), files.end());
}

json::object BackupView()
{
   auto manager = Application::Instance()->GetBackupManager();
   const std::wstring folder = Configuration::Instance()->GetBackupDestination();
   return {{"running", manager && manager->IsRunning()}, {"destination", Utf8(folder)},
      {"archives", folder.empty() ? json::array() : BackupArchives(folder)}};
}

json::value ManageMaintenance(const std::string &operation, json::object &arguments)
{
   if (operation == "backup.status")
   {
      if (!arguments.empty()) throw RequestError{422};
      return BackupView();
   }
   if (operation == "backup.start")
   {
      // The archive name, contents and passphrase come from the backup settings, not the request.
      if (!arguments.empty()) throw RequestError{422};
      auto manager = Application::Instance()->GetBackupManager();
      if (!manager) throw RequestError{503};
      if (manager->IsRunning()) throw RequestError{409};
      const std::wstring folder = Configuration::Instance()->GetBackupDestination();
      const DWORD attributes = folder.empty() ? INVALID_FILE_ATTRIBUTES : GetFileAttributesW(folder.c_str());
      if (attributes == INVALID_FILE_ATTRIBUTES || !(attributes & FILE_ATTRIBUTE_DIRECTORY)) throw RequestError{412};
      if (!manager->StartBackup()) throw RequestError{503};
      return BackupView();
   }
   if (operation == "backup.restore")
   {
      // Restoring replaces the chosen parts of the configuration and message store. The caller
      // repeats the archive name as confirmation and must open an encrypted archive's passphrase.
      SecretWipe wipe{arguments.if_contains("passphrase")};
      for (const auto &argument : arguments)
         if (argument.key() != "name" && argument.key() != "confirmName" && argument.key() != "passphrase"
             && argument.key() != "settings" && argument.key() != "domains" && argument.key() != "messages") throw RequestError{422};
      const auto name = Text(arguments, "name", 80);
      if (!IsBackupArchiveName(name) || Text(arguments, "confirmName", 80) != name) throw RequestError{422};
      const auto passphrase = arguments.if_contains("passphrase") ? Text(arguments, "passphrase", 1024) : std::string();
      const bool settings = Boolean(arguments, "settings", false), domains = Boolean(arguments, "domains", false), messages = Boolean(arguments, "messages", false);
      if (!settings && !domains && !messages) throw RequestError{422};
      if (messages && !domains) throw RequestError{422};
      auto manager = Application::Instance()->GetBackupManager();
      if (!manager) throw RequestError{503};
      if (manager->IsRunning()) throw RequestError{409};
      const std::wstring folder = Configuration::Instance()->GetBackupDestination();
      if (folder.empty()) throw RequestError{412};
      const std::wstring path = folder + L"\\" + std::wstring(Wide(arguments.at("name")));
      const DWORD attributes = GetFileAttributesW(path.c_str());
      if (attributes == INVALID_FILE_ATTRIBUTES) throw RequestError{404};
      if (attributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) throw RequestError{422};
      String secret = passphrase.empty() ? String() : Wide(arguments.at("passphrase"));
      auto backup = manager->LoadBackup(path, secret);
      SecureZeroMemory(secret.data(), secret.size() * sizeof(wchar_t));
      // An encrypted archive opened with the wrong passphrase loads without a file.
      if (!backup || (!passphrase.empty() && backup->GetBackupFile().IsEmpty())) throw RequestError{422};
      if ((settings && !backup->GetContains(Backup::BOSettings)) || (domains && !backup->GetContains(Backup::BODomains))
          || (messages && !backup->GetContains(Backup::BOMessages))) throw RequestError{422};
      backup->SetRestoreOption(Backup::BOSettings, settings);
      backup->SetRestoreOption(Backup::BODomains, domains);
      backup->SetRestoreOption(Backup::BOMessages, messages);
      if (!manager->StartRestore(backup)) throw RequestError{409};
      LOG_APPLICATION("A restore was started through the management API.");
      return BackupView();
   }
   if (operation == "secretkey.read")
   {
      if (!arguments.empty()) throw RequestError{422};
      return json::object{{"keyId", Utf8(String(SecretProtector::Instance()->GetKeyId()))}};
   }
   if (operation == "secretkey.export")
   {
      // The export is protected by the caller's passphrase; it is what another installation
      // needs to read this installation's protected settings and backups.
      SecretWipe wipe{arguments.if_contains("passphrase")};
      if (arguments.size() != 1) throw RequestError{422};
      const auto passphrase = Text(arguments, "passphrase", 1024);
      if (passphrase.size() < 12) throw RequestError{422};
      String secret = Wide(arguments.at("passphrase")), exported, error;
      const bool ok = SecretProtector::Instance()->ExportKey(secret, exported, error);
      SecureZeroMemory(secret.data(), secret.size() * sizeof(wchar_t));
      if (!ok) throw RequestError{503};
      LOG_APPLICATION("The installation secret key was exported through the management API.");
      json::object result{{"keyId", Utf8(String(SecretProtector::Instance()->GetKeyId()))}, {"exported", Utf8(exported)}};
      SecureZeroMemory(exported.data(), exported.size() * sizeof(wchar_t));
      return result;
   }
   if (operation == "secretkey.import")
   {
      SecretWipe wipe{arguments.if_contains("passphrase")}, key{arguments.if_contains("exported")};
      if (arguments.size() != 2) throw RequestError{422};
      Text(arguments, "passphrase", 1024);
      if (Text(arguments, "exported", 16384).empty()) throw RequestError{422};
      String secret = Wide(arguments.at("passphrase")), exported = Wide(arguments.at("exported")), error;
      const bool ok = SecretProtector::Instance()->ImportKey(exported, secret, error);
      SecureZeroMemory(secret.data(), secret.size() * sizeof(wchar_t));
      SecureZeroMemory(exported.data(), exported.size() * sizeof(wchar_t));
      // A wrong passphrase or a damaged export leaves the current key in place.
      if (!ok) throw RequestError{422};
      // As in the desktop tool: re-protect everything under the imported key.
      if (Application::Instance()->GetDBManager()) SecretMigration::Run();
      LOG_APPLICATION("An installation secret key was imported through the management API.");
      return json::object{{"keyId", Utf8(String(SecretProtector::Instance()->GetKeyId()))}};
   }
   throw RequestError{404};
}
