// Self-service derives ownership from reauthenticated native identity, never browser IDs.
json::value ManageSelf(const std::string &operation, json::object &arguments, const Account &actor)
{
   if (actor.GetID() <= 0 || actor.GetDomainID() <= 0) throw RequestError{404};
   const auto suffix = operation.substr(5);
   const bool rules = suffix.rfind("rules.", 0) == 0;
   for (const auto &entry : arguments)
      if (entry.key() != "fields" && !(rules && (entry.key() == "id" || entry.key() == "search"
          || entry.key() == "offset" || entry.key() == "limit"))) throw RequestError{422};
   auto target = std::make_shared<Account>();
   if (!PersistentAccount::ReadObject(target, actor.GetID()) || target->GetDomainID() != actor.GetDomainID()) throw RequestError{404};
   if (suffix == "read")
      return json::object{{"address", Utf8(target->GetAddress())}, {"directoryBacked", target->GetIsAD()},
         {"passwordChangeable", !target->GetIsAD()}};
   arguments["domainId"] = actor.GetDomainID();
   if (rules)
   {
      arguments["accountId"] = actor.GetID();
      return ManageRules(suffix, arguments, actor, true);
   }
   arguments["id"] = actor.GetID();
   if (suffix == "profile.read" || suffix == "profile.update" || suffix == "behavior.read" || suffix == "behavior.update"
       || suffix == "signature.read" || suffix == "signature.update")
      return ManageMailboxSettings("accounts." + suffix, arguments, actor, true);
   if (suffix != "password.update") throw RequestError{404};
   auto &fields = OperationFields(arguments);
   SecretWipe current{fields.if_contains("currentPassword")}, replacement{fields.if_contains("newPassword")};
   AllowedFields(fields, {"currentPassword", "newPassword"});
   if (fields.size() != 2 || Text(fields, "currentPassword", 1024).empty() || Text(fields, "newPassword", 1024).empty()) throw RequestError{422};
   if (target->GetIsAD()) throw RequestError{409};
   const auto proof = ManagementAuthentication::Authenticate(actor.GetAddress(), Wide(fields.at("currentPassword")));
   if (!proof || proof->GetID() != actor.GetID()) throw RequestError{403};
   // Authentication may upgrade a legacy hash. Reload after that write, and guard
   // the password update against concurrent resets, disablement and role changes.
   if (!PersistentAccount::ReadObject(target, actor.GetID()) || !target->GetActive()
       || target->GetDomainID() != actor.GetDomainID() || target->GetIsAD()
       || target->GetAdminLevel() != actor.GetAdminLevel()) throw RequestError{409};
   const auto expectedPassword = target->GetPassword();
   if (!Crypt::Instance()->Validate(Wide(fields.at("currentPassword")), expectedPassword,
       static_cast<Crypt::EncryptionType>(target->GetPasswordEncryption()))) throw RequestError{409};
   const auto hash = PasswordHasher::Hash(Wide(fields.at("newPassword")));
   if (hash.IsEmpty()) throw RequestError{503};
   SQLStatement statement(SQLStatement::STUpdate, "hm_accounts");
   statement.AddColumn("accountpassword", String(hash));
   statement.AddColumn("accountpwencryption", static_cast<int>(Crypt::ETPHC));
   SaveSelfPatch(target, statement, expectedPassword, [&](const Account &stored) {
      return stored.GetPassword() == String(hash) && stored.GetPasswordEncryption() == Crypt::ETPHC;
   });
   return json::object{{"changed", true}};
}
