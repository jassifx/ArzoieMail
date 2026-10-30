// DKIM signing: per-domain settings and server-generated keys. Domain administrators may read
// their own domain's DKIM state; only server administrators change it, because keys live in a
// protected folder on the server. Keys are chosen by selector, never by a caller-supplied path.
std::wstring DkimKeyFolder(const Domain &domain)
{
   // Beside the data directory, not inside it, so data-directory tools never take it for a domain.
   std::wstring data = IniFileSettings::Instance()->GetDataDirectory();
   while (!data.empty() && (data.back() == L'\\' || data.back() == L'/')) data.pop_back();
   const auto parent = data.find_last_of(L"\\/");
   const std::wstring base = (parent == std::wstring::npos ? data : data.substr(0, parent)) + L"\\DKIM";
   String name = domain.GetName(); name.ToLower();
   return base + L"\\" + std::wstring(name);
}

bool IsDkimSelector(const std::string &selector)
{
   static const std::regex pattern("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$");
   return std::regex_match(selector, pattern);
}

// Readable by SYSTEM, administrators and the service's own identity only.
bool CreateProtectedFolder(const std::wstring &path)
{
   HANDLE token = nullptr;
   if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return false;
   DWORD size = 0;
   GetTokenInformation(token, TokenUser, nullptr, 0, &size);
   std::vector<unsigned char> user(size);
   const bool ok = GetTokenInformation(token, TokenUser, user.data(), size, &size) != FALSE;
   CloseHandle(token);
   if (!ok) return false;
   LPWSTR sid = nullptr;
   if (!ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER *>(user.data())->User.Sid, &sid)) return false;
   const std::wstring sddl = L"D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FA;;;" + std::wstring(sid) + L")";
   LocalFree(sid);
   PSECURITY_DESCRIPTOR descriptor = nullptr;
   if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.c_str(), SDDL_REVISION_1, &descriptor, nullptr)) return false;
   SECURITY_ATTRIBUTES attributes{sizeof(SECURITY_ATTRIBUTES), descriptor, FALSE};
   const auto parent = path.find_last_of(L'\\');
   if (parent != std::wstring::npos) CreateDirectoryW(path.substr(0, parent).c_str(), &attributes);
   const bool created = CreateDirectoryW(path.c_str(), &attributes) != FALSE || GetLastError() == ERROR_ALREADY_EXISTS;
   LocalFree(descriptor);
   const DWORD found = GetFileAttributesW(path.c_str());
   return created && found != INVALID_FILE_ATTRIBUTES && (found & FILE_ATTRIBUTE_DIRECTORY) && !(found & FILE_ATTRIBUTE_REPARSE_POINT);
}

// "v=DKIM1; k=rsa; p=..." for the public half of a PEM private key, or empty.
std::string DkimDnsRecord(const std::wstring &file)
{
   std::unique_ptr<BIO, decltype(&BIO_free)> bio(BIO_new_file(Utf8(String(file.c_str())).c_str(), "r"), BIO_free);
   if (!bio) return {};
   std::unique_ptr<EVP_PKEY, decltype(&EVP_PKEY_free)> key(PEM_read_bio_PrivateKey(bio.get(), nullptr, nullptr, nullptr), EVP_PKEY_free);
   if (!key) return {};
   unsigned char *der = nullptr;
   const int length = i2d_PUBKEY(key.get(), &der);
   if (length <= 0) return {};
   std::string encoded(4 * ((length + 2) / 3) + 1, '\0');
   const int written = EVP_EncodeBlock(reinterpret_cast<unsigned char *>(encoded.data()), der, length);
   OPENSSL_free(der);
   encoded.resize(written > 0 ? written : 0);
   return encoded.empty() ? std::string() : "v=DKIM1; k=rsa; p=" + encoded;
}

json::object DkimView(const Domain &domain)
{
   const std::wstring folder = DkimKeyFolder(domain);
   const std::wstring active = domain.GetDKIMPrivateKeyFile();
   const std::string activeSelector(domain.GetDKIMSelector());
   json::array keys;
   WIN32_FIND_DATAW found{};
   HANDLE search = FindFirstFileW((folder + L"\\*.pem").c_str(), &found);
   if (search != INVALID_HANDLE_VALUE)
   {
      do
      {
         if (found.dwFileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) continue;
         std::string selector = Utf8(String(found.cFileName));
         selector.resize(selector.size() - 4);
         if (!IsDkimSelector(selector) || keys.size() >= 20) continue;
         const std::wstring file = folder + L"\\" + found.cFileName;
         keys.push_back(json::object{{"selector", selector}, {"dnsName", selector + "._domainkey." + Utf8(domain.GetName())},
            {"dnsRecord", DkimDnsRecord(file)}, {"active", _wcsicmp(file.c_str(), active.c_str()) == 0}});
      } while (FindNextFileW(search, &found));
      FindClose(search);
   }
   const DWORD activeAttributes = active.empty() ? INVALID_FILE_ATTRIBUTES : GetFileAttributesW(active.c_str());
   return {{"domainId", domain.GetID()}, {"domain", Utf8(domain.GetName())}, {"signEnabled", domain.GetDKIMEnabled()},
      {"signAliases", domain.GetDKIMAliasesEnabled()}, {"selector", activeSelector},
      {"headerCanonicalization", domain.GetDKIMHeaderCanonicalizationMethod()}, {"bodyCanonicalization", domain.GetDKIMBodyCanonicalizationMethod()},
      {"algorithm", domain.GetDKIMSigningAlgorithm()},
      {"keyConfigured", activeAttributes != INVALID_FILE_ATTRIBUTES && !(activeAttributes & FILE_ATTRIBUTE_DIRECTORY)},
      {"keyManaged", !active.empty() && _wcsnicmp(active.c_str(), (folder + L"\\").c_str(), folder.size() + 1) == 0},
      {"keys", std::move(keys)}};
}

json::value ManageDkim(const std::string &operation, json::object &arguments, const Account &actor)
{
   if (actor.GetAdminLevel() == Account::NormalUser) throw RequestError{403};
   const bool server = actor.GetAdminLevel() == Account::ServerAdmin;
   const auto domainId = Number(arguments, "domainId", 9223372036854775807LL);
   if (!domainId) throw RequestError{422};
   if (!server && domainId != actor.GetDomainID()) throw RequestError{403};
   auto domain = std::make_shared<Domain>();
   if (!PersistentDomain::ReadObject(domain, domainId)) throw RequestError{404};
   if (operation == "dkim.read") return DkimView(*domain);
   if (!server) throw RequestError{403};
   const auto save = [&]()
   {
      String error;
      if (!PersistentDomain::SaveObject(domain, error, PersistenceModeNormal)) throw RequestError{error.IsEmpty() ? 503 : 422};
      return DkimView(*domain);
   };
   if (operation == "dkim.update")
   {
      auto &fields = OperationFields(arguments);
      AllowedFields(fields, {"signEnabled", "signAliases", "headerCanonicalization", "bodyCanonicalization", "algorithm"});
      for (const auto key : {"headerCanonicalization", "bodyCanonicalization"})
         if (fields.if_contains(key) && Number(fields, key, 2) < 1) throw RequestError{422};
      if (fields.if_contains("algorithm") && Number(fields, "algorithm", 2) < 1) throw RequestError{422};
      const bool sign = Boolean(fields, "signEnabled", domain->GetDKIMEnabled());
      // Signing without a readable key would fail every outgoing message's signature.
      if (sign && (domain->GetDKIMSelector().IsEmpty() || !DkimView(*domain).at("keyConfigured").as_bool())) throw RequestError{409};
      domain->SetDKIMEnabled(sign);
      domain->SetDKIMAliasesEnabled(Boolean(fields, "signAliases", domain->GetDKIMAliasesEnabled()));
      if (fields.if_contains("headerCanonicalization")) domain->SetDKIMHeaderCanonicalizationMethod(static_cast<int>(Number(fields, "headerCanonicalization", 2)));
      if (fields.if_contains("bodyCanonicalization")) domain->SetDKIMBodyCanonicalizationMethod(static_cast<int>(Number(fields, "bodyCanonicalization", 2)));
      if (fields.if_contains("algorithm")) domain->SetDKIMSigningAlgorithm(static_cast<int>(Number(fields, "algorithm", 2)));
      return save();
   }
   const auto selector = Text(arguments, "selector", 63);
   if (!IsDkimSelector(selector)) throw RequestError{422};
   const std::wstring folder = DkimKeyFolder(*domain);
   const std::wstring file = folder + L"\\" + std::wstring(Wide(arguments.at("selector"))) + L".pem";
   const DWORD existing = GetFileAttributesW(file.c_str());
   if (operation == "dkim.generate")
   {
      for (const auto &argument : arguments)
         if (argument.key() != "domainId" && argument.key() != "selector" && argument.key() != "bits") throw RequestError{422};
      // RSA 2048 only: larger keys can take longer to generate than a management request may
      // run, and this pipe serves one request at a time. 2048 is the size RFC 8301 and the
      // large receivers expect, and its DNS record fits without splitting.
      const auto bits = Number(arguments, "bits", 4096, 2048);
      if (bits != 2048) throw RequestError{422};
      if (existing != INVALID_FILE_ATTRIBUTES) throw RequestError{409};
      if (!CreateProtectedFolder(folder)) throw RequestError{503};
      std::unique_ptr<EVP_PKEY, decltype(&EVP_PKEY_free)> key(EVP_RSA_gen(static_cast<unsigned int>(bits)), EVP_PKEY_free);
      if (!key) throw RequestError{503};
      std::unique_ptr<BIO, decltype(&BIO_free)> bio(BIO_new_file(Utf8(String(file.c_str())).c_str(), "w"), BIO_free);
      if (!bio) throw RequestError{503};
      const bool written = PEM_write_bio_PrivateKey(bio.get(), key.get(), nullptr, nullptr, 0, nullptr, nullptr) == 1;
      bio.reset();
      ERR_clear_error();
      if (!written) { DeleteFileW(file.c_str()); throw RequestError{503}; }
      String message;
      message.Format(_T("A DKIM key was generated through the management API for %s."), domain->GetName().c_str());
      LOG_APPLICATION(message);
      return DkimView(*domain);
   }
   if (existing == INVALID_FILE_ATTRIBUTES) throw RequestError{404};
   if (existing & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) throw RequestError{422};
   const bool isActive = _wcsicmp(file.c_str(), std::wstring(domain->GetDKIMPrivateKeyFile()).c_str()) == 0;
   if (operation == "dkim.activate")
   {
      // The API checks the published DNS record before calling this, unless told not to.
      if (arguments.size() != 2) throw RequestError{422};
      if (DkimDnsRecord(file).empty()) throw RequestError{422};
      domain->SetDKIMSelector(Wide(arguments.at("selector")));
      domain->SetDKIMPrivateKeyFile(file);
      return save();
   }
   if (operation == "dkim.delete")
   {
      if (arguments.size() != 2) throw RequestError{422};
      if (isActive) throw RequestError{409};
      if (!DeleteFileW(file.c_str())) throw RequestError{503};
      return DkimView(*domain);
   }
   throw RequestError{404};
}
