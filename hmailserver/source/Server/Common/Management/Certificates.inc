// Mail listener certificates, their TLS assignment, and the native half of ACME issuance.
// Server administrators only; the dispatcher checks that first, except for the narrow renewal
// automation scope at the end of this file.
//
// Managed certificates live as PEM files in a folder beside the data directory that only
// SYSTEM, administrators and the service can read, like the DKIM keys. Requests never name a
// path, and responses never contain one or any private key. Private keys for ACME orders are
// generated here and never leave the service: the API receives a certificate signing request
// and returns the issued chain. The ACME account key also stays here; the API asks the service
// to sign its ACME requests.
using X509Ptr = std::unique_ptr<X509, decltype(&X509_free)>;
using KeyPtr = std::unique_ptr<EVP_PKEY, decltype(&EVP_PKEY_free)>;
using BioPtr = std::unique_ptr<BIO, decltype(&BIO_free)>;

std::wstring CertificateFolder()
{
   std::wstring data = IniFileSettings::Instance()->GetDataDirectory();
   while (!data.empty() && (data.back() == L'\\' || data.back() == L'/')) data.pop_back();
   const auto parent = data.find_last_of(L"\\/");
   return (parent == std::wstring::npos ? data : data.substr(0, parent)) + L"\\Certificates";
}

bool IsManagedCertificateFile(const String &path)
{
   const std::wstring folder = CertificateFolder() + L"\\";
   return path.GetLength() > static_cast<int>(folder.size()) && _wcsnicmp(path.c_str(), folder.c_str(), folder.size()) == 0;
}

std::string RandomHex(size_t bytes)
{
   std::vector<unsigned char> random(bytes);
   if (RAND_bytes(random.data(), static_cast<int>(random.size())) != 1) throw RequestError{503};
   static const char digits[] = "0123456789abcdef";
   std::string text;
   for (const auto byte : random) { text += digits[byte >> 4]; text += digits[byte & 15]; }
   return text;
}

// An object ID as a number or as the decimal string the views return.
__int64 CertificateIdArgument(const json::object &arguments, const char *name = "id")
{
   const auto *value = arguments.if_contains(name);
   if (!value) throw RequestError{422};
   if (value->is_int64() && value->as_int64() > 0) return value->as_int64();
   if (!value->is_string() || value->as_string().empty() || value->as_string().size() > 19) throw RequestError{422};
   const std::string text(value->as_string());
   if (text.find_first_not_of("0123456789") != std::string::npos) throw RequestError{422};
   return std::stoll(text);
}

std::string Base64Url(const unsigned char *data, size_t length)
{
   std::string encoded(4 * ((length + 2) / 3) + 1, '\0');
   const int written = EVP_EncodeBlock(reinterpret_cast<unsigned char *>(encoded.data()), data, static_cast<int>(length));
   encoded.resize(written > 0 ? written : 0);
   for (auto &character : encoded) { if (character == '+') character = '-'; else if (character == '/') character = '_'; }
   while (!encoded.empty() && encoded.back() == '=') encoded.pop_back();
   return encoded;
}

std::string ReadSmallFile(const std::wstring &path, size_t maximum)
{
   std::ifstream input(path, std::ios::binary);
   if (!input) return {};
   std::string content((std::istreambuf_iterator<char>(input)), std::istreambuf_iterator<char>());
   return content.size() > maximum ? std::string() : content;
}

// Creates the file, which inherits the protected folder's permissions, and writes it whole.
bool WriteProtectedFile(const std::wstring &path, const std::string &content, bool replace = false)
{
   const std::wstring temporary = replace ? path + L".tmp" : path;
   HANDLE file = CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, nullptr, replace ? CREATE_ALWAYS : CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
   if (file == INVALID_HANDLE_VALUE) return false;
   DWORD written = 0;
   const bool ok = WriteFile(file, content.data(), static_cast<DWORD>(content.size()), &written, nullptr) && written == content.size() && FlushFileBuffers(file);
   CloseHandle(file);
   if (ok && (!replace || MoveFileExW(temporary.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))) return true;
   DeleteFileW(temporary.c_str());
   return false;
}

struct CertificateMaterial
{
   std::vector<X509Ptr> chain;
   KeyPtr key{nullptr, EVP_PKEY_free};
};

int NoPassword(char *, int, int, void *) { return 0; }

// The leaf with its chain and its private key, checked before anything is stored: the key
// matches and is strong enough, the leaf is not a CA, is valid now, and each further
// certificate issued the one before it. Supplied roots are kept as chain only, never trusted.
CertificateMaterial ParseCertificateMaterial(const std::string &certificatePem, const std::string &keyPem, EVP_PKEY *knownKey = nullptr)
{
   if (certificatePem.empty() || certificatePem.size() > 40000 || keyPem.size() > 16000) throw RequestError{422};
   CertificateMaterial material;
   BioPtr certificates(BIO_new_mem_buf(certificatePem.data(), static_cast<int>(certificatePem.size())), BIO_free);
   if (!certificates) throw RequestError{503};
   while (X509 *certificate = PEM_read_bio_X509(certificates.get(), nullptr, NoPassword, nullptr))
   {
      material.chain.emplace_back(certificate, X509_free);
      if (material.chain.size() > 8) { ERR_clear_error(); throw RequestError{422}; }
   }
   ERR_clear_error();
   if (material.chain.empty()) throw RequestError{422};
   if (knownKey)
   {
      if (EVP_PKEY_up_ref(knownKey) != 1) throw RequestError{503};
      material.key.reset(knownKey);
   }
   else
   {
      BioPtr key(BIO_new_mem_buf(keyPem.data(), static_cast<int>(keyPem.size())), BIO_free);
      if (!key) throw RequestError{503};
      // Encrypted keys are refused: the service has no way to ask for their password.
      material.key.reset(PEM_read_bio_PrivateKey(key.get(), nullptr, NoPassword, nullptr));
      ERR_clear_error();
      if (!material.key) throw RequestError{422};
   }
   X509 *leaf = material.chain.front().get();
   const bool matches = X509_check_private_key(leaf, material.key.get()) == 1;
   ERR_clear_error();
   if (!matches) throw RequestError{422};
   const int type = EVP_PKEY_get_base_id(material.key.get()), bits = EVP_PKEY_get_bits(material.key.get());
   if (!((type == EVP_PKEY_RSA && bits >= 2048 && bits <= 8192) || (type == EVP_PKEY_EC && bits >= 256))) throw RequestError{422};
   if (X509_check_ca(leaf) != 0) throw RequestError{422};
   if (X509_cmp_current_time(X509_get0_notAfter(leaf)) <= 0) throw RequestError{422};
   time_t tomorrow = time(nullptr) + 86400;
   if (X509_cmp_time(X509_get0_notBefore(leaf), &tomorrow) > 0) throw RequestError{422};
   for (size_t index = 1; index < material.chain.size(); ++index)
      if (X509_check_issued(material.chain[index].get(), material.chain[index - 1].get()) != X509_V_OK) throw RequestError{422};
   ERR_clear_error();
   return material;
}

std::string ChainPem(const CertificateMaterial &material)
{
   BioPtr output(BIO_new(BIO_s_mem()), BIO_free);
   for (const auto &certificate : material.chain)
      if (!output || PEM_write_bio_X509(output.get(), certificate.get()) != 1) throw RequestError{503};
   char *data = nullptr;
   const long length = BIO_get_mem_data(output.get(), &data);
   return std::string(data, length);
}

// The private key as unencrypted PKCS#8 PEM, in memory cleared when the caller is done.
std::string PrivateKeyPem(EVP_PKEY *key)
{
   BioPtr output(BIO_new(BIO_s_secmem()), BIO_free);
   if (!output || PEM_write_bio_PrivateKey(output.get(), key, nullptr, nullptr, 0, nullptr, nullptr) != 1) throw RequestError{503};
   char *data = nullptr;
   const long length = BIO_get_mem_data(output.get(), &data);
   return std::string(data, length);
}

std::string Utf8Of(const ASN1_STRING *value)
{
   unsigned char *text = nullptr;
   const int length = ASN1_STRING_to_UTF8(&text, value);
   if (length < 0) return {};
   std::string result(reinterpret_cast<char *>(text), length);
   OPENSSL_free(text);
   return result;
}

std::string CommonName(const X509_NAME *name)
{
   const int index = X509_NAME_get_index_by_NID(name, NID_commonName, -1);
   if (index < 0) return {};
   return Utf8Of(X509_NAME_ENTRY_get_data(X509_NAME_get_entry(name, index)));
}

std::string TimeText(const ASN1_TIME *value)
{
   struct tm parts{};
   if (ASN1_TIME_to_tm(value, &parts) != 1) return {};
   char text[32];
   strftime(text, sizeof(text), "%Y-%m-%dT%H:%M:%SZ", &parts);
   return text;
}

std::vector<std::string> DnsNames(X509 *certificate)
{
   std::vector<std::string> names;
   auto *alternatives = static_cast<GENERAL_NAMES *>(X509_get_ext_d2i(certificate, NID_subject_alt_name, nullptr, nullptr));
   if (!alternatives) return names;
   for (int index = 0; index < sk_GENERAL_NAME_num(alternatives) && names.size() < 100; ++index)
   {
      const GENERAL_NAME *name = sk_GENERAL_NAME_value(alternatives, index);
      if (name->type == GEN_DNS) names.push_back(Utf8Of(name->d.dNSName));
   }
   GENERAL_NAMES_free(alternatives);
   return names;
}

// What the panel shows about a certificate file: never its path or key.
json::object CertificateFacts(const String &file)
{
   json::object facts{{"readable", false}};
   const std::string content = ReadSmallFile(std::wstring(file.c_str()), 65536);
   if (content.empty()) return facts;
   BioPtr bio(BIO_new_mem_buf(content.data(), static_cast<int>(content.size())), BIO_free);
   X509Ptr leaf(bio ? PEM_read_bio_X509(bio.get(), nullptr, NoPassword, nullptr) : nullptr, X509_free);
   ERR_clear_error();
   if (!leaf) return facts;
   unsigned char digest[EVP_MAX_MD_SIZE];
   unsigned int digestLength = 0;
   X509_digest(leaf.get(), EVP_sha256(), digest, &digestLength);
   std::string fingerprint;
   static const char digits[] = "0123456789ABCDEF";
   for (unsigned int index = 0; index < digestLength; ++index) { fingerprint += digits[digest[index] >> 4]; fingerprint += digits[digest[index] & 15]; }
   json::array names;
   for (const auto &name : DnsNames(leaf.get())) names.push_back(json::value(name));
   int days = 0, seconds = 0;
   ASN1_TIME_diff(&days, &seconds, nullptr, X509_get0_notAfter(leaf.get()));
   EVP_PKEY *key = X509_get0_pubkey(leaf.get());
   const int type = key ? EVP_PKEY_get_base_id(key) : 0;
   const std::string keyType = (type == EVP_PKEY_RSA ? "RSA " : type == EVP_PKEY_EC ? "EC " : "Other ") + std::to_string(key ? EVP_PKEY_get_bits(key) : 0);
   facts = {{"readable", true}, {"subject", CommonName(X509_get_subject_name(leaf.get()))}, {"issuer", CommonName(X509_get_issuer_name(leaf.get()))},
      {"dnsNames", std::move(names)}, {"notBefore", TimeText(X509_get0_notBefore(leaf.get()))}, {"notAfter", TimeText(X509_get0_notAfter(leaf.get()))},
      {"daysRemaining", days}, {"fingerprintSha256", fingerprint}, {"keyType", keyType},
      {"selfSigned", X509_check_issued(leaf.get(), leaf.get()) == X509_V_OK}};
   ERR_clear_error();
   return facts;
}

// Per-certificate state kept beside the files: the previous generation, for rollback, and the
// ACME hostnames and renewal state of certificates issued here.
std::wstring CertificateMetadataFile(__int64 id) { return CertificateFolder() + L"\\certificate-" + std::to_wstring(id) + L".json"; }

json::object ReadCertificateMetadata(__int64 id)
{
   const auto content = ReadSmallFile(CertificateMetadataFile(id), 16384);
   boost::system::error_code error;
   auto value = content.empty() ? json::value() : json::parse(content, error);
   return !error && value.is_object() ? value.as_object() : json::object();
}

void WriteCertificateMetadata(__int64 id, const json::object &metadata)
{
   if (!WriteProtectedFile(CertificateMetadataFile(id), json::serialize(metadata), true)) throw RequestError{503};
}

std::vector<std::shared_ptr<TCPIPPort>> ListenersUsing(__int64 certificateId)
{
   std::vector<std::shared_ptr<TCPIPPort>> users;
   for (const auto &port : Configuration::Instance()->GetTCPIPPorts()->GetSnapshot())
      if (port->GetSSLCertificateID() == certificateId) users.push_back(port);
   return users;
}

std::shared_ptr<SSLCertificate> FindCertificate(__int64 id)
{
   auto certificates = Configuration::Instance()->GetSSLCertificates();
   certificates->Refresh();
   auto certificate = id > 0 ? certificates->GetItemByDBID(id) : nullptr;
   if (!certificate) throw RequestError{404};
   return certificate;
}

json::object CertificateView(SSLCertificate &certificate)
{
   json::object view = CertificateFacts(certificate.GetCertificateFile());
   view["id"] = std::to_string(certificate.GetID());
   view["name"] = Utf8(certificate.GetName());
   view["managed"] = IsManagedCertificateFile(certificate.GetCertificateFile()) && IsManagedCertificateFile(certificate.GetPrivateKeyFile());
   json::array listeners;
   for (const auto &port : ListenersUsing(certificate.GetID())) listeners.push_back(json::value(std::to_string(port->GetID())));
   view["listeners"] = std::move(listeners);
   const auto metadata = ReadCertificateMetadata(certificate.GetID());
   view["previousAvailable"] = metadata.if_contains("previousCertificate") != nullptr;
   view["acme"] = metadata.if_contains("acme") ? metadata.at("acme") : json::value(nullptr);
   return view;
}

// Stores the material as a new generation of managed files and returns their paths.
std::pair<std::wstring, std::wstring> StoreCertificateMaterial(const CertificateMaterial &material)
{
   const std::wstring folder = CertificateFolder();
   if (!CreateProtectedFolder(folder)) throw RequestError{503};
   const std::string token = RandomHex(12);
   const std::wstring certificateFile = folder + L"\\certificate-" + std::wstring(token.begin(), token.end()) + L".pem";
   const std::wstring keyFile = folder + L"\\key-" + std::wstring(token.begin(), token.end()) + L".pem";
   std::string key = PrivateKeyPem(material.key.get());
   const bool stored = WriteProtectedFile(certificateFile, ChainPem(material)) && WriteProtectedFile(keyFile, key);
   SecureZeroMemory(key.data(), key.size());
   if (!stored)
   {
      DeleteFileW(certificateFile.c_str());
      DeleteFileW(keyFile.c_str());
      throw RequestError{503};
   }
   return {certificateFile, keyFile};
}

void DeleteManagedFile(const String &path)
{
   if (IsManagedCertificateFile(path)) DeleteFileW(path.c_str());
}

void SaveCertificate(const std::shared_ptr<SSLCertificate> &certificate)
{
   String error;
   if (!PersistentSSLCertificate::SaveObject(certificate, error, PersistenceModeNormal) || certificate->GetID() <= 0)
      throw RequestError{error.IsEmpty() ? 503 : 422};
   Configuration::Instance()->GetSSLCertificates()->Refresh();
}

String CertificateName(const json::object &fields, __int64 exceptId = 0)
{
   const auto name = CleanText(fields, "name", 255);
   if (name.IsEmpty()) throw RequestError{422};
   auto certificates = Configuration::Instance()->GetSSLCertificates();
   certificates->Refresh();
   for (const auto &item : certificates->GetSnapshot())
      if (item->GetID() != exceptId && item->GetName().CompareNoCase(name) == 0) throw RequestError{409};
   return name;
}

std::shared_ptr<SSLCertificate> CreateCertificate(const String &name, const CertificateMaterial &material)
{
   const auto files = StoreCertificateMaterial(material);
   auto certificate = std::make_shared<SSLCertificate>();
   certificate->SetName(name);
   certificate->SetCertificateFile(files.first);
   certificate->SetPrivateKeyFile(files.second);
   try { SaveCertificate(certificate); }
   catch (...)
   {
      DeleteFileW(files.first.c_str());
      DeleteFileW(files.second.c_str());
      throw;
   }
   return certificate;
}

// Installs a new generation and keeps the current one for rollback. A generation older than
// that is deleted, if it is managed.
void ReplaceCertificate(const std::shared_ptr<SSLCertificate> &certificate, const CertificateMaterial &material)
{
   const auto files = StoreCertificateMaterial(material);
   auto metadata = ReadCertificateMetadata(certificate->GetID());
   const String oldCertificate = certificate->GetCertificateFile(), oldKey = certificate->GetPrivateKeyFile();
   certificate->SetCertificateFile(files.first);
   certificate->SetPrivateKeyFile(files.second);
   try { SaveCertificate(certificate); }
   catch (...)
   {
      certificate->SetCertificateFile(oldCertificate);
      certificate->SetPrivateKeyFile(oldKey);
      DeleteFileW(files.first.c_str());
      DeleteFileW(files.second.c_str());
      throw;
   }
   if (metadata.if_contains("previousCertificate"))
   {
      DeleteManagedFile(Wide(metadata.at("previousCertificate")));
      DeleteManagedFile(Wide(metadata.at("previousKey")));
   }
   metadata["previousCertificate"] = Utf8(oldCertificate);
   metadata["previousKey"] = Utf8(oldKey);
   WriteCertificateMetadata(certificate->GetID(), metadata);
}

// A certificate with its key, as the listeners would load it.
bool CertificateLoads(const SSLCertificate &certificate)
{
   try
   {
      boost::asio::ssl::context context(boost::asio::ssl::context::sslv23);
      context.use_certificate_chain_file(AnsiString(certificate.GetCertificateFile()));
      context.use_private_key_file(AnsiString(certificate.GetPrivateKeyFile()), boost::asio::ssl::context::pem);
      ERR_clear_error();
      return true;
   }
   catch (...)
   {
      ERR_clear_error();
      return false;
   }
}

json::object ListenerView(const TCPIPPort &port)
{
   const auto protocol = port.GetProtocol();
   return {{"id", std::to_string(port.GetID())}, {"protocol", protocol == STSMTP ? "SMTP" : protocol == STIMAP ? "IMAP" : protocol == STPOP3 ? "POP3" : "Unknown"},
      {"protocolCode", static_cast<int>(protocol)}, {"address", Utf8(port.GetAddressString())}, {"port", port.GetPortNumber()},
      {"securityCode", static_cast<int>(port.GetConnectionSecurity())},
      {"certificateId", port.GetSSLCertificateID() > 0 ? json::value(std::to_string(port.GetSSLCertificateID())) : json::value(nullptr)}};
}

// Listener changes take effect when the mail services restart, which builds every listener's
// TLS context again. The response says so; the panel offers the restart.
json::value ManageListeners(const std::string &operation, json::object &arguments)
{
   auto ports = Configuration::Instance()->GetTCPIPPorts();
   std::shared_ptr<TCPIPPort> port;
   if (operation != "listeners.create")
   {
      port = ports->GetItemByDBID(CertificateIdArgument(arguments));
      if (!port) throw RequestError{404};
   }
   if (operation == "listeners.delete")
   {
      if (arguments.size() != 1) throw RequestError{422};
      if (!PersistentTCPIPPort::DeleteObject(port)) throw RequestError{503};
      return json::object{{"deleted", true}, {"restartRequired", true}};
   }
   if (operation != "listeners.create" && operation != "listeners.update") throw RequestError{404};
   for (const auto &argument : arguments)
      if (argument.key() != "id" && argument.key() != "fields") throw RequestError{422};
   auto &fields = OperationFields(arguments);
   AllowedFields(fields, {"protocolCode", "address", "port", "securityCode", "certificateId"});
   if (!port)
   {
      port = std::make_shared<TCPIPPort>();
      for (const auto key : {"protocolCode", "address", "port", "securityCode"}) if (!fields.if_contains(key)) throw RequestError{422};
   }
   if (fields.if_contains("protocolCode"))
   {
      const auto protocol = Number(fields, "protocolCode", 5);
      if (protocol != STSMTP && protocol != STPOP3 && protocol != STIMAP) throw RequestError{422};
      port->SetProtocol(static_cast<SessionType>(protocol));
   }
   if (fields.if_contains("address") && !port->SetAddress(CleanText(fields, "address", 64))) throw RequestError{422};
   if (fields.if_contains("port"))
   {
      const auto number = Number(fields, "port", 65535);
      if (number < 1) throw RequestError{422};
      port->SetPortNumber(static_cast<int>(number));
   }
   if (fields.if_contains("securityCode")) port->SetConnectionSecurity(static_cast<ConnectionSecurity>(Number(fields, "securityCode", CSSTARTTLSRequired)));
   if (fields.if_contains("certificateId"))
   {
      const auto *value = fields.if_contains("certificateId");
      if (value->is_null()) port->SetSSLCertificateID(0);
      else
      {
         if (!value->is_string() || value->as_string().size() > 19) throw RequestError{422};
         const std::string text(value->as_string());
         if (text.empty() || text.find_first_not_of("0123456789") != std::string::npos) throw RequestError{422};
         port->SetSSLCertificateID(std::stoll(text));
      }
   }
   // A TLS listener needs a certificate the listener can actually load.
   if (port->GetConnectionSecurity() != CSNone)
   {
      if (port->GetSSLCertificateID() <= 0) throw RequestError{422};
      if (!CertificateLoads(*FindCertificate(port->GetSSLCertificateID()))) throw RequestError{409};
   }
   else if (port->GetSSLCertificateID() > 0) FindCertificate(port->GetSSLCertificateID());
   for (const auto &other : ports->GetSnapshot())
      if (other->GetID() != port->GetID() && other->GetPortNumber() == port->GetPortNumber() && other->GetAddress() == port->GetAddress()) throw RequestError{409};
   String error;
   if (!PersistentTCPIPPort::SaveObject(port, error, PersistenceModeNormal) || port->GetID() <= 0) throw RequestError{error.IsEmpty() ? 503 : 422};
   auto view = ListenerView(*port);
   view["restartRequired"] = true;
   return view;
}

// ACME: the account key and settings stay with the service.
std::wstring AcmeAccountKeyFile() { return CertificateFolder() + L"\\acme-account.pem"; }
std::wstring AcmeSettingsFile() { return CertificateFolder() + L"\\acme.json"; }

json::object ReadAcmeSettings()
{
   const auto content = ReadSmallFile(AcmeSettingsFile(), 16384);
   boost::system::error_code error;
   auto value = content.empty() ? json::value() : json::parse(content, error);
   return !error && value.is_object() ? value.as_object() : json::object();
}

KeyPtr AcmeAccountKey()
{
   const std::wstring file = AcmeAccountKeyFile();
   std::string pem = ReadSmallFile(file, 16384);
   if (pem.empty())
   {
      if (!CreateProtectedFolder(CertificateFolder())) throw RequestError{503};
      KeyPtr generated(EVP_EC_gen("P-256"), EVP_PKEY_free);
      if (!generated) throw RequestError{503};
      pem = PrivateKeyPem(generated.get());
      const bool stored = WriteProtectedFile(file, pem);
      SecureZeroMemory(pem.data(), pem.size());
      if (!stored) throw RequestError{503};
      return generated;
   }
   BioPtr bio(BIO_new_mem_buf(pem.data(), static_cast<int>(pem.size())), BIO_free);
   KeyPtr key(bio ? PEM_read_bio_PrivateKey(bio.get(), nullptr, NoPassword, nullptr) : nullptr, EVP_PKEY_free);
   SecureZeroMemory(pem.data(), pem.size());
   ERR_clear_error();
   if (!key || EVP_PKEY_get_base_id(key.get()) != EVP_PKEY_EC) throw RequestError{503};
   return key;
}

// The public account key as an RFC 7517 JWK (P-256).
json::object AcmeJwk(EVP_PKEY *key)
{
   unsigned char point[65];
   size_t length = 0;
   if (EVP_PKEY_get_octet_string_param(key, OSSL_PKEY_PARAM_ENCODED_PUBLIC_KEY, point, sizeof(point), &length) != 1 || length != 65 || point[0] != 4)
      throw RequestError{503};
   return {{"crv", "P-256"}, {"kty", "EC"}, {"x", Base64Url(point + 1, 32)}, {"y", Base64Url(point + 33, 32)}};
}

json::object AcmeAccountView()
{
   auto key = AcmeAccountKey();
   auto settings = ReadAcmeSettings();
   const auto text = [&](const char *name) { const auto *value = settings.if_contains(name); return value && value->is_string() ? *value : json::value(""); };
   const auto *agreed = settings.if_contains("termsAgreed");
   return {{"directoryUrl", text("directoryUrl")}, {"contact", text("contact")}, {"termsAgreed", agreed && agreed->is_bool() && agreed->as_bool()},
      {"accountUrl", text("accountUrl")}, {"jwk", AcmeJwk(key.get())}};
}

// ES256 over the JWS signing input, as the raw 64-byte r||s value JWS requires.
std::string AcmeSign(const std::string &input)
{
   auto key = AcmeAccountKey();
   std::unique_ptr<EVP_MD_CTX, decltype(&EVP_MD_CTX_free)> context(EVP_MD_CTX_new(), EVP_MD_CTX_free);
   size_t length = 0;
   if (!context || EVP_DigestSignInit(context.get(), nullptr, EVP_sha256(), nullptr, key.get()) != 1
       || EVP_DigestSign(context.get(), nullptr, &length, reinterpret_cast<const unsigned char *>(input.data()), input.size()) != 1)
      throw RequestError{503};
   std::vector<unsigned char> der(length);
   if (EVP_DigestSign(context.get(), der.data(), &length, reinterpret_cast<const unsigned char *>(input.data()), input.size()) != 1) throw RequestError{503};
   const unsigned char *cursor = der.data();
   std::unique_ptr<ECDSA_SIG, decltype(&ECDSA_SIG_free)> signature(d2i_ECDSA_SIG(nullptr, &cursor, static_cast<long>(length)), ECDSA_SIG_free);
   if (!signature) throw RequestError{503};
   unsigned char raw[64];
   if (BN_bn2binpad(ECDSA_SIG_get0_r(signature.get()), raw, 32) != 32 || BN_bn2binpad(ECDSA_SIG_get0_s(signature.get()), raw + 32, 32) != 32)
      throw RequestError{503};
   return Base64Url(raw, sizeof(raw));
}

// Host names an HTTP-01 order can prove: lower-case DNS names, no wildcards or addresses.
std::vector<std::string> AcmeHostnames(const json::value &value)
{
   if (!value.is_array() || value.as_array().empty() || value.as_array().size() > 10) throw RequestError{422};
   static const std::regex pattern("^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z][a-z0-9-]{0,61}[a-z0-9]$");
   std::vector<std::string> names;
   for (const auto &item : value.as_array())
   {
      if (!item.is_string()) throw RequestError{422};
      std::string name(item.as_string());
      std::transform(name.begin(), name.end(), name.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
      if (!std::regex_match(name, pattern) || std::find(names.begin(), names.end(), name) != names.end()) throw RequestError{422};
      names.push_back(name);
   }
   return names;
}

struct PendingAcmeOrder
{
   KeyPtr key{nullptr, EVP_PKEY_free};
   std::vector<std::string> hostnames;
   __int64 certificateId = 0;
   String name;
   bool autoRenew = true;
   bool automation = false;
   ULONGLONG created = 0;
};

std::map<std::string, PendingAcmeOrder> &PendingAcmeOrders()
{
   static std::map<std::string, PendingAcmeOrder> orders;
   return orders;
}

// A fresh P-256 key and its certificate signing request. The key waits here for the issued
// certificate; it is dropped after two hours or when more than eight orders wait.
json::object PrepareAcmeOrder(const std::vector<std::string> &hostnames, __int64 certificateId, const String &name, bool autoRenew, bool automation)
{
   auto &orders = PendingAcmeOrders();
   const ULONGLONG now = GetTickCount64();
   for (auto order = orders.begin(); order != orders.end();)
      order = now - order->second.created > 2 * 3600 * 1000ULL ? orders.erase(order) : std::next(order);
   while (orders.size() >= 8)
      orders.erase(std::min_element(orders.begin(), orders.end(), [](const auto &left, const auto &right) { return left.second.created < right.second.created; }));
   KeyPtr key(EVP_EC_gen("P-256"), EVP_PKEY_free);
   std::unique_ptr<X509_REQ, decltype(&X509_REQ_free)> request(X509_REQ_new(), X509_REQ_free);
   if (!key || !request) throw RequestError{503};
   X509_NAME *subject = X509_REQ_get_subject_name(request.get());
   std::string alternatives;
   for (const auto &host : hostnames) alternatives += (alternatives.empty() ? "DNS:" : ",DNS:") + host;
   std::unique_ptr<STACK_OF(X509_EXTENSION), void (*)(STACK_OF(X509_EXTENSION) *)> extensions(sk_X509_EXTENSION_new_null(),
      [](STACK_OF(X509_EXTENSION) *stack) { sk_X509_EXTENSION_pop_free(stack, X509_EXTENSION_free); });
   X509_EXTENSION *names = X509V3_EXT_conf_nid(nullptr, nullptr, NID_subject_alt_name, alternatives.c_str());
   if (!extensions || !names || sk_X509_EXTENSION_push(extensions.get(), names) <= 0
       || X509_NAME_add_entry_by_NID(subject, NID_commonName, MBSTRING_ASC, reinterpret_cast<const unsigned char *>(hostnames.front().c_str()), -1, -1, 0) != 1
       || X509_REQ_add_extensions(request.get(), extensions.get()) != 1 || X509_REQ_set_pubkey(request.get(), key.get()) != 1
       || X509_REQ_sign(request.get(), key.get(), EVP_sha256()) <= 0)
   {
      ERR_clear_error();
      throw RequestError{503};
   }
   unsigned char *der = nullptr;
   const int length = i2d_X509_REQ(request.get(), &der);
   if (length <= 0) throw RequestError{503};
   const std::string csr = Base64Url(der, length);
   OPENSSL_free(der);
   const std::string id = RandomHex(16);
   PendingAcmeOrder order;
   order.key = std::move(key);
   order.hostnames = hostnames;
   order.certificateId = certificateId;
   order.name = name;
   order.autoRenew = autoRenew;
   order.automation = automation;
   order.created = now;
   orders[id] = std::move(order);
   json::array identifiers;
   for (const auto &host : hostnames) identifiers.push_back(json::value(host));
   return {{"pendingId", id}, {"csr", csr}, {"hostnames", std::move(identifiers)}};
}

json::object AcmeMetadataOf(const json::object &metadata)
{
   const auto *acme = metadata.if_contains("acme");
   return acme && acme->is_object() ? acme->as_object() : json::object();
}

// Installs an issued chain for a waiting order: it must use the order's key and name every host.
json::object CompleteAcmeOrder(const std::string &pendingId, const std::string &chain, bool automation)
{
   auto &orders = PendingAcmeOrders();
   const auto found = orders.find(pendingId);
   if (found == orders.end() || found->second.automation != automation) throw RequestError{404};
   PendingAcmeOrder order = std::move(found->second);
   orders.erase(found);
   auto material = ParseCertificateMaterial(chain, {}, order.key.get());
   const auto names = DnsNames(material.chain.front().get());
   for (const auto &host : order.hostnames)
      if (std::find(names.begin(), names.end(), host) == names.end()) throw RequestError{422};
   std::shared_ptr<SSLCertificate> certificate;
   if (order.certificateId > 0)
   {
      certificate = FindCertificate(order.certificateId);
      ReplaceCertificate(certificate, material);
   }
   else certificate = CreateCertificate(order.name, material);
   auto metadata = ReadCertificateMetadata(certificate->GetID());
   json::array hostnames;
   for (const auto &host : order.hostnames) hostnames.push_back(json::value(host));
   metadata["acme"] = json::object{{"hostnames", std::move(hostnames)}, {"autoRenew", order.autoRenew},
      {"lastAttempt", Utf8(Time::GetCurrentDateTime())}, {"lastError", ""}};
   WriteCertificateMetadata(certificate->GetID(), metadata);
   String message;
   message.Format(_T("An ACME certificate was installed for %s through the management API."), certificate->GetName().c_str());
   LOG_APPLICATION(message);
   auto view = CertificateView(*certificate);
   view["restartRequired"] = !ListenersUsing(certificate->GetID()).empty();
   return view;
}


// Certificates set to renew automatically that expire within 30 days. After a failed attempt a
// certificate waits 12 hours before the next; a successful renewal is not due again until it,
// too, nears expiry.
json::object AcmeRenewalsDue()
{
   json::array items;
   auto certificates = Configuration::Instance()->GetSSLCertificates();
   certificates->Refresh();
   const __int64 now = static_cast<__int64>(time(nullptr));
   for (const auto &certificate : certificates->GetSnapshot())
   {
      const auto acme = AcmeMetadataOf(ReadCertificateMetadata(certificate->GetID()));
      const auto *renew = acme.if_contains("autoRenew");
      if (!renew || !renew->is_bool() || !renew->as_bool()) continue;
      const auto facts = CertificateFacts(certificate->GetCertificateFile());
      if (facts.at("readable").as_bool() && facts.at("daysRemaining").as_int64() > 30) continue;
      const auto *attempt = acme.if_contains("lastAttemptEpoch");
      if (attempt && attempt->is_int64() && now - attempt->as_int64() < 12 * 3600) continue;
      items.push_back(json::object{{"certificateId", std::to_string(certificate->GetID())}, {"name", Utf8(certificate->GetName())},
         {"hostnames", acme.at("hostnames")}, {"daysRemaining", facts.if_contains("daysRemaining") ? facts.at("daysRemaining") : json::value(0)}});
   }
   return {{"items", std::move(items)}};
}

void RecordAcmeAttempt(__int64 certificateId, const std::string &error)
{
   auto metadata = ReadCertificateMetadata(certificateId);
   auto acme = AcmeMetadataOf(metadata);
   if (acme.empty()) throw RequestError{404};
   acme["lastAttempt"] = Utf8(Time::GetCurrentDateTime());
   acme["lastAttemptEpoch"] = static_cast<__int64>(time(nullptr));
   acme["lastError"] = error.substr(0, 500);
   metadata["acme"] = std::move(acme);
   WriteCertificateMetadata(certificateId, metadata);
}

std::string TextArgument(const json::object &arguments, const char *name, size_t maximum)
{
   const auto text = Text(arguments, name, maximum);
   if (text.empty()) throw RequestError{422};
   return text;
}

void UpdateAcmeSettings(const json::object &fields)
{
   AllowedFields(fields, {"directoryUrl", "contact", "termsAgreed"});
   auto settings = ReadAcmeSettings();
   if (fields.if_contains("directoryUrl"))
   {
      // Empty clears the choice; otherwise an https URL.
      const auto url = Text(fields, "directoryUrl", 500);
      for (const unsigned char character : url) if (character <= 32 || character == 127) throw RequestError{422};
      if (!url.empty() && (url.rfind("https://", 0) != 0 || url.size() < 12)) throw RequestError{422};
      const auto *current = settings.if_contains("directoryUrl");
      // A different directory is a different ACME account.
      if (!current || !current->is_string() || current->as_string() != url) settings["accountUrl"] = "";
      settings["directoryUrl"] = url;
   }
   if (fields.if_contains("contact"))
   {
      const auto contact = Text(fields, "contact", 254);
      static const std::regex address("^$|^[A-Za-z0-9._%+-]{1,64}@[A-Za-z0-9.-]{1,253}\\.[A-Za-z]{2,63}$");
      if (!std::regex_match(contact, address)) throw RequestError{422};
      settings["contact"] = contact;
   }
   if (fields.if_contains("termsAgreed")) settings["termsAgreed"] = Boolean(fields, "termsAgreed", false);
   if (!CreateProtectedFolder(CertificateFolder()) || !WriteProtectedFile(AcmeSettingsFile(), json::serialize(settings), true)) throw RequestError{503};
}

json::value ManageCertificates(const std::string &operation, json::object &arguments)
{
   if (operation.rfind("listeners.", 0) == 0) return ManageListeners(operation, arguments);
   if (operation == "certificates.list")
   {
      if (!arguments.empty()) throw RequestError{422};
      auto certificates = Configuration::Instance()->GetSSLCertificates();
      certificates->Refresh();
      json::array items;
      for (const auto &certificate : certificates->GetSnapshot())
      {
         if (items.size() >= 100) break;
         items.push_back(CertificateView(*certificate));
      }
      json::array listeners;
      for (const auto &port : Configuration::Instance()->GetTCPIPPorts()->GetSnapshot()) listeners.push_back(ListenerView(*port));
      return json::object{{"items", std::move(items)}, {"listeners", std::move(listeners)}};
   }
   if (operation == "certificates.import")
   {
      AllowedFields(arguments, {"name", "certificatePem", "privateKeyPem"});
      const auto name = CertificateName(arguments);
      std::string key = TextArgument(arguments, "privateKeyPem", 16000);
      auto &secret = arguments.at("privateKeyPem").as_string();
      SecureZeroMemory(secret.data(), secret.size());
      try
      {
         auto material = ParseCertificateMaterial(TextArgument(arguments, "certificatePem", 40000), key);
         SecureZeroMemory(key.data(), key.size());
         auto certificate = CreateCertificate(name, material);
         String message;
         message.Format(_T("A certificate was imported through the management API: %s."), name.c_str());
         LOG_APPLICATION(message);
         return CertificateView(*certificate);
      }
      catch (...) { SecureZeroMemory(key.data(), key.size()); throw; }
   }
   if (operation == "acme.account.read")
   {
      if (!arguments.empty()) throw RequestError{422};
      return AcmeAccountView();
   }
   if (operation == "acme.account.update")
   {
      UpdateAcmeSettings(OperationFields(arguments));
      return AcmeAccountView();
   }
   if (operation == "acme.account.registered")
   {
      // The API registers the account with the directory, then records the account URL it got.
      AllowedFields(arguments, {"directoryUrl", "accountUrl"});
      auto settings = ReadAcmeSettings();
      const auto directory = TextArgument(arguments, "directoryUrl", 500), account = TextArgument(arguments, "accountUrl", 500);
      const auto *current = settings.if_contains("directoryUrl");
      if (!current || !current->is_string() || current->as_string() != directory) throw RequestError{409};
      if (account.rfind("https://", 0) != 0) throw RequestError{422};
      settings["accountUrl"] = account;
      if (!WriteProtectedFile(AcmeSettingsFile(), json::serialize(settings), true)) throw RequestError{503};
      return AcmeAccountView();
   }
   if (operation == "acme.sign")
   {
      AllowedFields(arguments, {"input"});
      return json::object{{"signature", AcmeSign(TextArgument(arguments, "input", 32768))}};
   }
   if (operation == "certificates.acme.prepare")
   {
      AllowedFields(arguments, {"hostnames", "name", "certificateId", "autoRenew"});
      if (arguments.if_contains("certificateId"))
      {
         // Renewing: the host names and renewal setting recorded at issuance, unless given.
         if (arguments.if_contains("name")) throw RequestError{422};
         auto certificate = FindCertificate(CertificateIdArgument(arguments, "certificateId"));
         const auto acme = AcmeMetadataOf(ReadCertificateMetadata(certificate->GetID()));
         const auto *recorded = acme.if_contains("hostnames");
         if (!arguments.if_contains("hostnames") && !recorded) throw RequestError{422};
         const auto *renew = acme.if_contains("autoRenew");
         const bool autoRenew = Boolean(arguments, "autoRenew", renew && renew->is_bool() ? renew->as_bool() : true);
         return PrepareAcmeOrder(AcmeHostnames(arguments.if_contains("hostnames") ? arguments.at("hostnames") : *recorded),
            certificate->GetID(), certificate->GetName(), autoRenew, false);
      }
      if (!arguments.if_contains("hostnames")) throw RequestError{422};
      return PrepareAcmeOrder(AcmeHostnames(arguments.at("hostnames")), 0, CertificateName(arguments), Boolean(arguments, "autoRenew", true), false);
   }
   if (operation == "certificates.acme.complete")
   {
      AllowedFields(arguments, {"pendingId", "certificatePem"});
      return CompleteAcmeOrder(TextArgument(arguments, "pendingId", 64), TextArgument(arguments, "certificatePem", 40000), false);
   }
   if (operation == "certificates.acme.report")
   {
      AllowedFields(arguments, {"id", "error"});
      const auto id = FindCertificate(CertificateIdArgument(arguments))->GetID();
      RecordAcmeAttempt(id, Text(arguments, "error", 500));
      return CertificateView(*FindCertificate(id));
   }

   auto certificate = FindCertificate(CertificateIdArgument(arguments));
   if (operation == "certificates.read")
   {
      if (arguments.size() != 1) throw RequestError{422};
      return CertificateView(*certificate);
   }
   if (operation == "certificates.update")
   {
      AllowedFields(arguments, {"id", "name", "autoRenew"});
      if (arguments.if_contains("name"))
      {
         certificate->SetName(CertificateName(arguments, certificate->GetID()));
         SaveCertificate(certificate);
      }
      if (arguments.if_contains("autoRenew"))
      {
         auto metadata = ReadCertificateMetadata(certificate->GetID());
         auto acme = AcmeMetadataOf(metadata);
         if (acme.empty()) throw RequestError{409};
         acme["autoRenew"] = Boolean(arguments, "autoRenew", true);
         metadata["acme"] = std::move(acme);
         WriteCertificateMetadata(certificate->GetID(), metadata);
      }
      return CertificateView(*FindCertificate(certificate->GetID()));
   }
   if (operation == "certificates.replace")
   {
      AllowedFields(arguments, {"id", "certificatePem", "privateKeyPem"});
      std::string key = TextArgument(arguments, "privateKeyPem", 16000);
      auto &secret = arguments.at("privateKeyPem").as_string();
      SecureZeroMemory(secret.data(), secret.size());
      try
      {
         auto material = ParseCertificateMaterial(TextArgument(arguments, "certificatePem", 40000), key);
         SecureZeroMemory(key.data(), key.size());
         ReplaceCertificate(certificate, material);
      }
      catch (...) { SecureZeroMemory(key.data(), key.size()); throw; }
      // A manual replacement is not an ACME certificate any more.
      auto metadata = ReadCertificateMetadata(certificate->GetID());
      metadata.erase("acme");
      WriteCertificateMetadata(certificate->GetID(), metadata);
      auto view = CertificateView(*certificate);
      view["restartRequired"] = !ListenersUsing(certificate->GetID()).empty();
      return view;
   }
   if (operation == "certificates.rollback")
   {
      if (arguments.size() != 1) throw RequestError{422};
      auto metadata = ReadCertificateMetadata(certificate->GetID());
      if (!metadata.if_contains("previousCertificate")) throw RequestError{409};
      const String previousCertificate = Wide(metadata.at("previousCertificate")), previousKey = Wide(metadata.at("previousKey"));
      auto restored = std::make_shared<SSLCertificate>(*certificate);
      restored->SetCertificateFile(previousCertificate);
      restored->SetPrivateKeyFile(previousKey);
      if (!CertificateLoads(*restored)) throw RequestError{409};
      metadata["previousCertificate"] = Utf8(certificate->GetCertificateFile());
      metadata["previousKey"] = Utf8(certificate->GetPrivateKeyFile());
      certificate->SetCertificateFile(previousCertificate);
      certificate->SetPrivateKeyFile(previousKey);
      SaveCertificate(certificate);
      WriteCertificateMetadata(certificate->GetID(), metadata);
      LOG_APPLICATION("A certificate was rolled back to its previous generation through the management API.");
      auto view = CertificateView(*certificate);
      view["restartRequired"] = !ListenersUsing(certificate->GetID()).empty();
      return view;
   }
   if (operation == "certificates.delete")
   {
      if (arguments.size() != 1) throw RequestError{422};
      if (!ListenersUsing(certificate->GetID()).empty()) throw RequestError{409};
      const auto metadata = ReadCertificateMetadata(certificate->GetID());
      if (!PersistentSSLCertificate::DeleteObject(certificate)) throw RequestError{503};
      Configuration::Instance()->GetSSLCertificates()->Refresh();
      DeleteManagedFile(certificate->GetCertificateFile());
      DeleteManagedFile(certificate->GetPrivateKeyFile());
      if (metadata.if_contains("previousCertificate"))
      {
         DeleteManagedFile(Wide(metadata.at("previousCertificate")));
         DeleteManagedFile(Wide(metadata.at("previousKey")));
      }
      DeleteFileW(CertificateMetadataFile(certificate->GetID()).c_str());
      return json::object{{"deleted", true}};
   }
   throw RequestError{404};
}

// Renewal automation. The pipe accepts only the configured API identity, so the API's renewal
// task may call these without an administrator's password. They can only renew certificates an
// administrator issued with automatic renewal, for the host names recorded then, and restart
// the mail services when a renewed certificate is in use.
json::value ManageCertificateAutomation(const std::string &operation, json::object &arguments)
{
   if (operation == "acme.renewals.due")
   {
      if (!arguments.empty()) throw RequestError{422};
      return AcmeRenewalsDue();
   }
   if (operation == "acme.account.read")
   {
      if (!arguments.empty()) throw RequestError{422};
      return AcmeAccountView();
   }
   if (operation == "acme.sign")
   {
      AllowedFields(arguments, {"input"});
      return json::object{{"signature", AcmeSign(TextArgument(arguments, "input", 32768))}};
   }
   if (operation == "acme.renewals.prepare")
   {
      AllowedFields(arguments, {"certificateId"});
      auto certificate = FindCertificate(CertificateIdArgument(arguments, "certificateId"));
      const auto acme = AcmeMetadataOf(ReadCertificateMetadata(certificate->GetID()));
      const auto *renew = acme.if_contains("autoRenew");
      if (!renew || !renew->is_bool() || !renew->as_bool() || !acme.if_contains("hostnames")) throw RequestError{403};
      return PrepareAcmeOrder(AcmeHostnames(acme.at("hostnames")), certificate->GetID(), certificate->GetName(), true, true);
   }
   if (operation == "acme.renewals.complete")
   {
      AllowedFields(arguments, {"pendingId", "certificatePem"});
      auto view = CompleteAcmeOrder(TextArgument(arguments, "pendingId", 64), TextArgument(arguments, "certificatePem", 40000), true);
      bool restarted = false;
      if (view.at("restartRequired").as_bool() && ServerStatus::Instance()->GetState() == ServerStatus::StateRunning)
      {
         LOG_APPLICATION("Restarting the mail services to load a renewed certificate.");
         Application::Instance()->StopServers();
         restarted = Application::Instance()->StartServers();
      }
      view["restarted"] = restarted;
      return view;
   }
   if (operation == "acme.renewals.report")
   {
      AllowedFields(arguments, {"certificateId", "error"});
      auto certificate = FindCertificate(CertificateIdArgument(arguments, "certificateId"));
      RecordAcmeAttempt(certificate->GetID(), Text(arguments, "error", 500));
      return json::object{{"recorded", true}};
   }
   throw RequestError{403};
}
