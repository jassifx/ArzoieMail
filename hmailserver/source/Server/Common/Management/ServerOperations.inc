// Server status/control, server messages, administrator sendout, diagnostics and log access.
// Every operation here is server-administrator only; the dispatcher checks that first.
json::object ServerStatusView()
{
   Domains domains;
   domains.Refresh();
   auto status = ServerStatus::Instance();
   return {{"version", Utf8(Application::Instance()->GetVersionNumber())},
           {"architecture", Utf8(Application::Instance()->GetVersionArchitecture())},
           {"state", static_cast<int>(status->GetState())}, {"domains", domains.GetCount()},
           {"startTime", Utf8(Application::Instance()->GetStartTime())},
           {"processedMessages", status->GetNumberOfProcessedMessages()},
           {"spamMessagesDetected", status->GetNumberOfDetectedSpamMessages()},
           {"virusesRemoved", status->GetNumberOfRemovedViruses()},
           {"sessions", json::object{{"smtp", status->GetNumberOfSessions(STSMTP)},
              {"pop3", status->GetNumberOfSessions(STPOP3)}, {"imap", status->GetNumberOfSessions(STIMAP)}}},
           {"cacheHitRates", json::object{{"domain", Cache<Domain>::Instance()->GetHitRate()},
              {"account", Cache<Account>::Instance()->GetHitRate()}, {"alias", Cache<Alias>::Instance()->GetHitRate()},
              {"list", Cache<DistributionList>::Instance()->GetHitRate()}}}};
}

// Starting or stopping replaces the protocol listeners, delivery and fetch workers in this
// process. The management endpoint and its configuration stay available throughout.
json::value ControlServer(const std::string &operation, const json::object &arguments)
{
   const bool start = operation == "server.start";
   if (arguments.size() != 1) throw RequestError{422};
   const auto expected = Number(arguments, "expectedState", ServerStatus::StateStopping, -1);
   if (expected != (start ? ServerStatus::StateStopped : ServerStatus::StateRunning)) throw RequestError{422};
   static std::mutex control;
   std::lock_guard<std::mutex> guard(control);
   if (ServerStatus::Instance()->GetState() != expected) throw RequestError{409};
   if (start)
   {
      if (!Application::Instance()->StartServers()) throw RequestError{503};
   }
   else Application::Instance()->StopServers();
   return ServerStatusView();
}

json::object ServerMessageView(const ServerMessage &message)
{
   return {{"id", std::to_string(message.GetID())}, {"name", Utf8(message.GetName())}, {"text", Utf8(message.GetText())}};
}

json::value ManageServerMessages(const std::string &operation, json::object &arguments)
{
   auto messages = Configuration::Instance()->GetServerMessages();
   messages->Refresh();
   if (operation == "servermessages.list")
   {
      json::array items;
      for (const auto &message : messages->GetSnapshot()) items.push_back(ServerMessageView(*message));
      return json::object{{"items", std::move(items)}, {"maximum", 8000}};
   }
   auto original = messages->GetItemByDBID(Id(arguments));
   if (!original) throw RequestError{404};
   if (operation == "servermessages.read") return ServerMessageView(*original);
   if (operation != "servermessages.update") throw RequestError{404};
   auto &fields = OperationFields(arguments);
   const auto *expected = fields.if_contains("expected");
   if (fields.size() != 2 || !expected || !expected->is_object()) throw RequestError{422};
   if (expected->as_object() != ServerMessageView(*original)) throw RequestError{409};
   const auto text = Text(fields, "text", 8000);
   for (const unsigned char character : text)
      if ((character < 32 && character != '\r' && character != '\n' && character != '\t') || character == 127) throw RequestError{422};
   // Names are fixed identifiers the server looks messages up by; only the text changes.
   auto target = std::make_shared<ServerMessage>(*original);
   target->SetText(Wide(fields.at("text")));
   if (!PersistentServerMessage::SaveObject(target)) throw RequestError{503};
   messages->Refresh();
   auto saved = messages->GetItemByDBID(target->GetID());
   if (!saved || saved->GetText() != target->GetText()) throw RequestError{503};
   return ServerMessageView(*saved);
}

json::value ManageSendout(const std::string &operation, json::object &arguments)
{
   const bool send = operation == "sendout.send";
   if (!send && operation != "sendout.preview") throw RequestError{404};
   const auto wildcard = Text(arguments, "recipients", 320);
   if (wildcard.empty() || wildcard.find_first_of(" \t\r\n,;\"<>") != std::string::npos) throw RequestError{422};
   for (const unsigned char character : wildcard) if (character < 32 || character == 127) throw RequestError{422};
   const auto recipients = EmailAllUsers::GetRecipients(Wide(arguments.at("recipients")));
   if (!send)
   {
      if (arguments.size() != 1) throw RequestError{422};
      return json::object{{"recipients", static_cast<__int64>(recipients.size())}};
   }
   if (arguments.size() != 7) throw RequestError{422};
   const auto single = [&](const char *name, size_t maximum, bool quotes)
   {
      const auto text = Text(arguments, name, maximum);
      for (const unsigned char character : text)
         if (character < 32 || character == 127 || (quotes && (character == '"' || character == '\\'))) throw RequestError{422};
      return Wide(arguments.at(name));
   };
   const auto fromAddress = single("fromAddress", 320, true), fromName = single("fromName", 255, true);
   const auto subject = single("subject", 998, false);
   if (!StringParser::IsValidEmailAddress(fromAddress) || fromAddress.FindOneOf(_T(" \t,;<>")) != -1) throw RequestError{422};
   const auto body = Text(arguments, "body", 12288);
   for (const unsigned char character : body)
      if ((character < 32 && character != '\r' && character != '\n' && character != '\t') || character == 127) throw RequestError{422};
   // The administrator confirms the exact audience the preview showed.
   const auto *expected = arguments.if_contains("expectedRecipients");
   if (!expected || !expected->is_int64() || expected->as_int64() < 1) throw RequestError{422};
   if (static_cast<size_t>(expected->as_int64()) != recipients.size()) throw RequestError{409};
   if (!EmailAllUsers::Send(recipients, fromAddress, fromName, subject, Wide(arguments.at("body")))) throw RequestError{503};
   String message;
   message.Format(_T("Management sendout queued one message to %d accounts."), static_cast<int>(recipients.size()));
   LOG_APPLICATION(message);
   return json::object{{"queued", true}, {"recipients", static_cast<__int64>(recipients.size())}};
}

// Diagnostics include outbound connection tests that can outlast a management request,
// so they run as one background task at a time and are polled for their result.
struct DiagnosticsState
{
   std::mutex mutex;
   bool running = false;
   __int64 run = 0;
   std::string completed;
   json::array results;
};
DiagnosticsState &Diagnostics()
{
   static DiagnosticsState state;
   return state;
}
json::object DiagnosticsView()
{
   auto &state = Diagnostics();
   std::lock_guard<std::mutex> guard(state.mutex);
   return {{"running", state.running}, {"run", state.run}, {"completed", state.completed}, {"results", state.results}};
}
class DiagnosticsTask : public Task
{
public:
   DiagnosticsTask(const String &localDomain, const String &testDomain) : Task("DiagnosticsTask"), local_(localDomain), test_(testDomain) {}
protected:
   void DoWork() override
   {
      json::array results;
      try
      {
         Diagnostic diagnostic;
         diagnostic.SetLocalDomain(local_);
         diagnostic.SetTestDomain(test_);
         for (const auto &result : diagnostic.PerformTests())
            results.push_back(json::object{{"name", Utf8(result.GetName())}, {"description", Utf8(result.GetDescription())},
               {"success", result.GetSuccess()}, {"details", Utf8(result.GetDetails().Left(4000))}});
      }
      catch (...)
      {
         results.push_back(json::object{{"name", "Diagnostics"}, {"description", "The diagnostic run stopped unexpectedly."},
            {"success", false}, {"details", ""}});
      }
      auto &state = Diagnostics();
      std::lock_guard<std::mutex> guard(state.mutex);
      state.results = std::move(results);
      state.completed = Utf8(Time::GetCurrentDateTime());
      state.running = false;
   }
private:
   String local_, test_;
};

json::value ManageDiagnostics(const std::string &operation, json::object &arguments)
{
   if (operation == "diagnostics.read")
   {
      if (!arguments.empty()) throw RequestError{422};
      return DiagnosticsView();
   }
   if (operation != "diagnostics.start") throw RequestError{404};
   if (arguments.size() != 2) throw RequestError{422};
   String names[2];
   const char *keys[] = {"localDomain", "testDomain"};
   for (int index = 0; index < 2; ++index)
   {
      Text(arguments, keys[index], 253);
      names[index] = Wide(arguments.at(keys[index]));
      if (!names[index].IsEmpty() && (!StringParser::IsValidDomainName(names[index]) || names[index].Find(_T("*")) >= 0)) throw RequestError{422};
   }
   if (!names[0].IsEmpty())
   {
      Domains domains;
      domains.Refresh();
      if (!domains.GetItemByName(names[0])) throw RequestError{422}; // MX and self-connection tests need a local domain.
   }
   auto queue = Application::Instance()->GetMaintenanceWorkQueue();
   if (!queue) throw RequestError{503};
   auto &state = Diagnostics();
   {
      std::lock_guard<std::mutex> guard(state.mutex);
      if (state.running) throw RequestError{409};
      state.running = true;
      ++state.run;
      state.results.clear();
      state.completed.clear();
   }
   queue->AddTask(std::make_shared<DiagnosticsTask>(names[0], names[1]));
   return DiagnosticsView();
}

// Logs are selected by an exact file name from the log directory listing, never by path.
bool IsServerLogName(const std::string &name)
{
   static const std::regex pattern("^(ERROR_)?hmailserver(_[A-Za-z0-9]{1,16})?_[0-9]{4}-[0-9]{2}-[0-9]{2}\\.log$|^hmailserver_(awstats|backup|events)\\.log$");
   return name.size() <= 64 && std::regex_match(name, pattern);
}

// Invalid UTF-8 becomes U+FFFD and control characters other than tab/CR/LF become spaces,
// so a page of log text always serializes within the management frame.
std::string LogText(const std::vector<char> &bytes)
{
   std::string text;
   text.reserve(bytes.size());
   for (size_t index = 0; index < bytes.size();)
   {
      const auto lead = static_cast<unsigned char>(bytes[index]);
      if (lead < 0x80)
      {
         text += (lead < 32 && lead != '\t' && lead != '\r' && lead != '\n') || lead == 127 ? ' ' : static_cast<char>(lead);
         ++index;
         continue;
      }
      const size_t length = lead >= 0xC2 && lead <= 0xDF ? 2 : lead >= 0xE0 && lead <= 0xEF ? 3 : lead >= 0xF0 && lead <= 0xF4 ? 4 : 0;
      bool valid = length && index + length <= bytes.size();
      for (size_t next = 1; valid && next < length; ++next)
         valid = (static_cast<unsigned char>(bytes[index + next]) & 0xC0) == 0x80;
      if (valid && length == 3)
      {
         const auto second = static_cast<unsigned char>(bytes[index + 1]);
         valid = !(lead == 0xE0 && second < 0xA0) && !(lead == 0xED && second > 0x9F);
      }
      if (valid && length == 4)
      {
         const auto second = static_cast<unsigned char>(bytes[index + 1]);
         valid = !(lead == 0xF0 && second < 0x90) && !(lead == 0xF4 && second > 0x8F);
      }
      if (valid) { text.append(bytes.data() + index, length); index += length; }
      else { text += "\xEF\xBF\xBD"; ++index; }
   }
   return text;
}

std::string FileTimeText(const FILETIME &time)
{
   SYSTEMTIME utc{};
   if (!FileTimeToSystemTime(&time, &utc)) return "";
   char buffer[32];
   snprintf(buffer, sizeof(buffer), "%04u-%02u-%02uT%02u:%02u:%02uZ", utc.wYear, utc.wMonth, utc.wDay, utc.wHour, utc.wMinute, utc.wSecond);
   return buffer;
}

json::value ManageLogs(const std::string &operation, json::object &arguments)
{
   const std::wstring directory = IniFileSettings::Instance()->GetLogDirectory();
   if (directory.empty()) throw RequestError{503};
   if (operation == "logs.list")
   {
      if (!arguments.empty()) throw RequestError{422};
      std::vector<json::object> files;
      WIN32_FIND_DATAW found{};
      HANDLE search = FindFirstFileW((directory + L"\\*.log").c_str(), &found);
      if (search != INVALID_HANDLE_VALUE)
      {
         do
         {
            if (found.dwFileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) continue;
            const auto name = Utf8(String(found.cFileName));
            if (!IsServerLogName(name)) continue;
            files.push_back(json::object{{"name", name},
               {"size", (static_cast<__int64>(found.nFileSizeHigh) << 32) | found.nFileSizeLow},
               {"modified", FileTimeText(found.ftLastWriteTime)}});
         } while (FindNextFileW(search, &found));
         FindClose(search);
      }
      std::sort(files.begin(), files.end(), [](const json::object &left, const json::object &right)
         { return left.at("modified").as_string() > right.at("modified").as_string(); });
      if (files.size() > 200) files.resize(200);
      json::array items(files.begin(), files.end());
      return json::object{{"items", std::move(items)}, {"pageBytes", 16384}};
   }
   if (operation != "logs.read") throw RequestError{404};
   for (const auto &argument : arguments)
      if (argument.key() != "name" && argument.key() != "before") throw RequestError{422};
   const auto name = Text(arguments, "name", 64);
   if (!IsServerLogName(name)) throw RequestError{422};
   const auto path = directory + L"\\" + std::wstring(Wide(arguments.at("name")));
   // Share with the writer; refuse links so a planted reparse point cannot redirect the read.
   HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr,
      OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
   if (file == INVALID_HANDLE_VALUE) throw RequestError{GetLastError() == ERROR_FILE_NOT_FOUND ? 404 : 503};
   std::unique_ptr<void, decltype(&CloseHandle)> owner(file, CloseHandle);
   BY_HANDLE_FILE_INFORMATION information{};
   if (!GetFileInformationByHandle(file, &information)
       || (information.dwFileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT))) throw RequestError{422};
   const __int64 size = (static_cast<__int64>(information.nFileSizeHigh) << 32) | information.nFileSizeLow;
   const auto end = arguments.if_contains("before") ? Number(arguments, "before", size) : size;
   const __int64 page = 16384, start = end > page ? end - page : 0;
   std::vector<char> bytes(static_cast<size_t>(end - start));
   LARGE_INTEGER position{};
   position.QuadPart = start;
   DWORD read = 0;
   if (!bytes.empty() && (!SetFilePointerEx(file, position, nullptr, FILE_BEGIN)
       || !ReadFile(file, bytes.data(), static_cast<DWORD>(bytes.size()), &read, nullptr))) throw RequestError{503};
   bytes.resize(read);
   // Begin at a line boundary unless this page starts the file.
   __int64 first = start;
   if (start > 0)
   {
      const auto newline = std::find(bytes.begin(), bytes.end(), '\n');
      if (newline != bytes.end())
      {
         first += (newline - bytes.begin()) + 1;
         bytes.erase(bytes.begin(), newline + 1);
      }
   }
   return json::object{{"name", name}, {"size", size}, {"start", first}, {"end", start + static_cast<__int64>(read)}, {"text", LogText(bytes)}};
}

json::value ManageServerOperations(const std::string &operation, json::object &arguments)
{
   if (operation == "status") return ServerStatusView();
   if (operation == "server.start" || operation == "server.stop") return ControlServer(operation, arguments);
   if (operation.rfind("servermessages.", 0) == 0) return ManageServerMessages(operation, arguments);
   if (operation.rfind("sendout.", 0) == 0) return ManageSendout(operation, arguments);
   if (operation.rfind("diagnostics.", 0) == 0) return ManageDiagnostics(operation, arguments);
   if (operation.rfind("logs.", 0) == 0) return ManageLogs(operation, arguments);
   throw RequestError{404};
}
