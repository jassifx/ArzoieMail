// Existing antivirus providers. Server administrators only; the dispatcher checks that first.
// ClamAV settings are typed in the "antivirus" settings section. The custom-scanner and ClamWin
// providers run a program the service starts, so their paths stay read-only here: changing them
// from a web session would let it run any program as the service. Tests only use the
// configured values, never a path or host from the request.
json::object AntiVirusStatus()
{
   auto &config = Configuration::Instance()->GetAntiVirusConfiguration();
   return {{"clamAv", json::object{{"enabled", config.GetClamAVEnabled()}, {"host", Utf8(config.GetClamAVHost())}, {"port", config.GetClamAVPort()}}},
      {"customScanner", json::object{{"enabled", config.GetCustomScannerEnabled()}, {"executable", Utf8(config.GetCustomScannerExecutable())},
         {"returnValue", config.GetCustomScannerReturnValue()}}},
      {"clamWin", json::object{{"enabled", config.ClamWinEnabled()}, {"executable", Utf8(config.ClamWinExecutable())},
         {"database", Utf8(config.ClamWinDatabase())}}}};
}

json::value ManageAntiVirus(const std::string &operation, json::object &arguments)
{
   if (operation == "antivirus.status")
   {
      if (!arguments.empty()) throw RequestError{422};
      return AntiVirusStatus();
   }
   if (operation != "antivirus.test") throw RequestError{404};
   // Scans a generated EICAR test file, and a clean one, with the configured provider.
   if (arguments.size() != 1) throw RequestError{422};
   const auto scanner = Text(arguments, "scanner", 16);
   auto &config = Configuration::Instance()->GetAntiVirusConfiguration();
   VirusScannerTester tester;
   String message;
   bool passed = false;
   if (scanner == "clamav")
   {
      if (config.GetClamAVHost().IsEmpty()) throw RequestError{412};
      passed = tester.TestClamAVConnect(config.GetClamAVHost(), config.GetClamAVPort(), message);
   }
   else if (scanner == "custom")
   {
      if (config.GetCustomScannerExecutable().IsEmpty()) throw RequestError{412};
      passed = tester.TestCustomVirusScanner(config.GetCustomScannerExecutable(), config.GetCustomScannerReturnValue(), message);
   }
   else if (scanner == "clamwin")
   {
      if (config.ClamWinExecutable().IsEmpty()) throw RequestError{412};
      passed = tester.TestClamWinVirusScanner(config.ClamWinExecutable(), config.ClamWinDatabase(), message);
   }
   else throw RequestError{422};
   return json::object{{"scanner", scanner}, {"passed", passed}, {"message", Utf8(message.Left(4000))}};
}
