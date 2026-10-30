// Groups, public folders and public-folder permissions (IMAP ACLs). Server administrators only;
// the dispatcher checks that first. Mailbox owners manage their own folders' ACLs over IMAP.
const std::pair<const char *, ACLPermission::ePermission> FolderRights[] = {
   {"lookup", ACLPermission::PermissionLookup}, {"read", ACLPermission::PermissionRead},
   {"writeSeen", ACLPermission::PermissionWriteSeen}, {"writeOthers", ACLPermission::PermissionWriteOthers},
   {"insert", ACLPermission::PermissionInsert}, {"post", ACLPermission::PermissionPost},
   {"create", ACLPermission::PermissionCreate}, {"deleteMailbox", ACLPermission::PermissionDeleteMailbox},
   {"writeDeleted", ACLPermission::PermissionWriteDeleted}, {"expunge", ACLPermission::PermissionExpunge},
   {"administer", ACLPermission::PermissionAdminister}};

json::object GroupView(Group &group)
{
   return {{"id", group.GetID()}, {"name", Utf8(group.GetName())}, {"members", group.GetMembers()->GetCount()}};
}
json::object GroupMemberView(GroupMember &member)
{
   auto account = std::make_shared<Account>();
   const bool found = PersistentAccount::ReadObject(account, member.GetAccountID());
   return {{"id", member.GetID()}, {"groupId", member.GetGroupID()}, {"accountId", member.GetAccountID()},
      {"address", found ? Utf8(account->GetAddress()) : std::string()}};
}

void RefreshGroups()
{
   if (auto groups = Configuration::Instance()->GetIMAPConfiguration()->GetGroups()) groups->Refresh();
}

json::value ManageGroups(const std::string &operation, json::object &arguments)
{
   Groups groups;
   groups.Refresh();
   if (operation.rfind("groupmembers.", 0) == 0)
   {
      auto group = groups.GetItemByDBID(Number(arguments, "groupId", 9223372036854775807LL));
      if (!group) throw RequestError{404};
      auto members = group->GetMembers();
      if (operation == "groupmembers.list")
      {
         json::array items;
         for (const auto &member : members->GetSnapshot()) { if (items.size() >= 500) break; items.push_back(GroupMemberView(*member)); }
         return json::object{{"items", std::move(items)}, {"total", members->GetCount()}};
      }
      if (operation == "groupmembers.delete")
      {
         auto member = members->GetItemByDBID(Id(arguments));
         if (!member) throw RequestError{404};
         if (!PersistentGroupMember::DeleteObject(member)) throw RequestError{503};
         RefreshGroups();
         return json::object{{"deleted", true}};
      }
      if (operation != "groupmembers.create") throw RequestError{404};
      auto &fields = OperationFields(arguments);
      AllowedFields(fields, {"accountId"});
      const auto accountId = Number(fields, "accountId", 9223372036854775807LL);
      auto account = std::make_shared<Account>();
      if (!accountId || !PersistentAccount::ReadObject(account, accountId)) throw RequestError{422};
      if (members->UserIsMember(accountId)) throw RequestError{409};
      auto member = std::make_shared<GroupMember>();
      member->SetGroupID(group->GetID());
      member->SetAccountID(accountId);
      String error;
      if (!PersistentGroupMember::SaveObject(member, error, PersistenceModeNormal) || member->GetID() <= 0) throw RequestError{error.IsEmpty() ? 503 : 422};
      RefreshGroups();
      return GroupMemberView(*member);
   }
   if (operation == "groups.list") return Page(groups.GetVector(), arguments, GroupView);
   const bool create = operation == "groups.create";
   auto group = create ? std::make_shared<Group>() : groups.GetItemByDBID(Id(arguments));
   if (!group) throw RequestError{404};
   if (operation == "groups.read") return GroupView(*group);
   if (operation == "groups.delete")
   {
      // Remove memberships first: the group delete itself only removes the group and its ACLs.
      for (const auto &member : group->GetMembers()->GetSnapshot())
         if (!PersistentGroupMember::DeleteObject(member)) throw RequestError{503};
      if (!PersistentGroup::DeleteObject(group)) throw RequestError{503};
      RefreshGroups();
      return json::object{{"deleted", true}};
   }
   if (!create && operation != "groups.update") throw RequestError{404};
   auto &fields = OperationFields(arguments);
   AllowedFields(fields, {"name"});
   const auto name = CleanText(fields, "name", 255);
   if (name.IsEmpty()) throw RequestError{422};
   for (const auto &item : groups.GetSnapshot())
      if (item->GetID() != group->GetID() && item->GetName().CompareNoCase(name) == 0) throw RequestError{409};
   group->SetName(name);
   String error;
   if (!PersistentGroup::SaveObject(group, error, PersistenceModeNormal) || group->GetID() <= 0) throw RequestError{error.IsEmpty() ? 503 : 422};
   RefreshGroups();
   return GroupView(*group);
}

void CollectPublicFolders(const std::shared_ptr<IMAPFolders> &folders, const std::string &prefix, json::array &items)
{
   const String delimiter = Configuration::Instance()->GetIMAPConfiguration()->GetHierarchyDelimiter();
   for (const auto &folder : folders->GetSnapshot())
   {
      if (items.size() >= 1000) return;
      const auto name = Utf8(ModifiedUTF7::Decode(folder->GetFolderName()));
      const auto path = prefix.empty() ? name : prefix + Utf8(delimiter) + name;
      items.push_back(json::object{{"id", folder->GetID()}, {"parentId", folder->GetParentFolderID() > 0 ? folder->GetParentFolderID() : 0},
         {"name", name}, {"path", path}});
      CollectPublicFolders(folder->GetSubFolders(), path, items);
   }
}

String PublicFolderName(const json::object &fields)
{
   const auto name = CleanText(fields, "name", 255);
   const String delimiter = Configuration::Instance()->GetIMAPConfiguration()->GetHierarchyDelimiter();
   if (name.IsEmpty() || name.Find(delimiter) >= 0 || name.CompareNoCase(_T("INBOX")) == 0) throw RequestError{422};
   return ModifiedUTF7::Encode(name);
}

json::object FolderPermissionView(ACLPermission &permission)
{
   json::object rights;
   for (const auto &right : FolderRights) rights[right.first] = permission.GetAllow(right.second);
   std::string holder;
   if (permission.GetPermissionType() == ACLPermission::PTUser)
   {
      auto account = std::make_shared<Account>();
      if (PersistentAccount::ReadObject(account, permission.GetPermissionAccountID())) holder = Utf8(account->GetAddress());
   }
   else if (permission.GetPermissionType() == ACLPermission::PTGroup)
   {
      auto group = std::make_shared<Group>();
      if (PersistentGroup::ReadObject(group, permission.GetPermissionGroupID())) holder = Utf8(group->GetName());
   }
   return {{"id", permission.GetID()}, {"folderId", permission.GetShareFolderID()}, {"type", static_cast<int>(permission.GetPermissionType())},
      {"accountId", permission.GetPermissionAccountID()}, {"groupId", permission.GetPermissionGroupID()}, {"holder", holder},
      {"rights", std::move(rights)}};
}

json::value ManagePublicFolders(const std::string &operation, json::object &arguments)
{
   auto root = Configuration::Instance()->GetIMAPConfiguration()->GetPublicFolders();
   if (!root) throw RequestError{503};
   if (operation == "publicfolders.list")
   {
      json::array items;
      CollectPublicFolders(root, "", items);
      return json::object{{"items", std::move(items)}, {"delimiter", Utf8(Configuration::Instance()->GetIMAPConfiguration()->GetHierarchyDelimiter())}};
   }
   if (operation == "publicfolders.create")
   {
      // Public folders are shared by every IMAP session, so they are changed in the live tree,
      // as the desktop tool does. New public folders are subscribed automatically.
      auto &fields = OperationFields(arguments);
      AllowedFields(fields, {"name", "parentId"});
      const auto parentId = Number(fields, "parentId", 9223372036854775807LL);
      auto parent = parentId ? root->GetItemByDBIDRecursive(parentId) : nullptr;
      if (parentId && !parent) throw RequestError{404};
      auto siblings = parent ? parent->GetSubFolders() : root;
      const auto name = PublicFolderName(fields);
      if (siblings->GetFolderByName(name)) throw RequestError{409};
      auto folder = std::make_shared<IMAPFolder>(0, siblings->GetParentID());
      folder->SetFolderName(name);
      folder->SetIsSubscribed(true);
      if (!PersistentIMAPFolder::SaveObject(folder) || folder->GetID() <= 0) throw RequestError{503};
      siblings->AddItem(folder);
      return json::object{{"id", folder->GetID()}, {"parentId", parentId}, {"name", Utf8(ModifiedUTF7::Decode(folder->GetFolderName()))}};
   }
   auto folder = root->GetItemByDBIDRecursive(Id(arguments));
   if (!folder) throw RequestError{404};
   const auto parentId = folder->GetParentFolderID() > 0 ? folder->GetParentFolderID() : 0;
   auto parent = parentId ? root->GetItemByDBIDRecursive(parentId) : nullptr;
   auto siblings = parent ? parent->GetSubFolders() : root;
   if (operation == "publicfolders.update")
   {
      auto &fields = OperationFields(arguments);
      AllowedFields(fields, {"name"});
      const auto name = PublicFolderName(fields);
      auto existing = siblings->GetFolderByName(name);
      if (existing && existing->GetID() != folder->GetID()) throw RequestError{409};
      const auto previous = folder->GetFolderName();
      folder->SetFolderName(name);
      if (!PersistentIMAPFolder::SaveObject(folder)) { folder->SetFolderName(previous); throw RequestError{503}; }
      return json::object{{"id", folder->GetID()}, {"parentId", parentId}, {"name", Utf8(ModifiedUTF7::Decode(folder->GetFolderName()))}};
   }
   if (operation == "publicfolders.delete")
   {
      // Deleting removes the folder's messages. Subfolders must be removed first, so one
      // confirmation never removes a whole tree.
      if (folder->GetSubFolders()->GetCount() > 0) throw RequestError{409};
      if (!siblings->DeleteItemByDBID(folder->GetID())) throw RequestError{503};
      return json::object{{"deleted", true}};
   }
   throw RequestError{404};
}

json::value ManageFolderPermissions(const std::string &operation, json::object &arguments)
{
   auto root = Configuration::Instance()->GetIMAPConfiguration()->GetPublicFolders();
   if (!root) throw RequestError{503};
   auto folder = root->GetItemByDBIDRecursive(Number(arguments, "folderId", 9223372036854775807LL));
   if (!folder) throw RequestError{404};
   auto permissions = folder->GetPermissions();
   if (operation == "folderpermissions.list")
   {
      json::array items;
      for (const auto &permission : permissions->GetSnapshot()) items.push_back(FolderPermissionView(*permission));
      return json::object{{"items", std::move(items)}, {"rights", [] { json::array names; for (const auto &right : FolderRights) names.push_back(json::string(right.first)); return names; }()}};
   }
   const bool create = operation == "folderpermissions.create";
   std::shared_ptr<ACLPermission> permission;
   if (create) permission = std::make_shared<ACLPermission>();
   else
   {
      permission = permissions->GetItemByDBID(Id(arguments));
      if (!permission) throw RequestError{404};
   }
   if (operation == "folderpermissions.delete")
   {
      if (!PersistentACLPermission::DeleteObject(permission)) throw RequestError{503};
      return json::object{{"deleted", true}};
   }
   if (!create && operation != "folderpermissions.update") throw RequestError{404};
   auto &fields = OperationFields(arguments);
   if (create) AllowedFields(fields, {"type", "accountId", "groupId", "rights"});
   else AllowedFields(fields, {"rights"});
   if (create)
   {
      const auto *type = fields.if_contains("type");
      if (!type || !type->is_int64() || type->as_int64() < 0 || type->as_int64() > 2) throw RequestError{422};
      const auto kind = static_cast<ACLPermission::ePermissionType>(type->as_int64());
      permission->SetShareFolderID(folder->GetID());
      permission->SetPermissionType(kind);
      for (const auto &existing : permissions->GetSnapshot())
         if (existing->GetPermissionType() == kind && kind == ACLPermission::PTAnyone) throw RequestError{409};
      if (kind == ACLPermission::PTUser)
      {
         if (fields.if_contains("groupId")) throw RequestError{422};
         const auto accountId = Number(fields, "accountId", 9223372036854775807LL);
         auto account = std::make_shared<Account>();
         if (!accountId || !PersistentAccount::ReadObject(account, accountId)) throw RequestError{422};
         if (permissions->GetPermissionForAccount(accountId)) throw RequestError{409};
         permission->SetPermissionAccountID(accountId);
      }
      else if (kind == ACLPermission::PTGroup)
      {
         if (fields.if_contains("accountId")) throw RequestError{422};
         const auto groupId = Number(fields, "groupId", 9223372036854775807LL);
         auto group = std::make_shared<Group>();
         if (!groupId || !PersistentGroup::ReadObject(group, groupId)) throw RequestError{422};
         if (permissions->GetPermissionForGroup(groupId)) throw RequestError{409};
         permission->SetPermissionGroupID(groupId);
      }
      else if (fields.if_contains("accountId") || fields.if_contains("groupId")) throw RequestError{422};
   }
   const auto *rights = fields.if_contains("rights");
   if (!rights || !rights->is_object()) throw RequestError{422};
   for (const auto &entry : rights->as_object())
   {
      const auto match = std::find_if(std::begin(FolderRights), std::end(FolderRights), [&](const auto &right) { return entry.key() == right.first; });
      if (match == std::end(FolderRights) || !entry.value().is_bool()) throw RequestError{422};
      permission->SetAllow(match->second, entry.value().as_bool());
   }
   String error;
   if (!PersistentACLPermission::SaveObject(permission, error, PersistenceModeNormal) || permission->GetID() <= 0) throw RequestError{error.IsEmpty() ? 503 : 422};
   return FolderPermissionView(*permission);
}
