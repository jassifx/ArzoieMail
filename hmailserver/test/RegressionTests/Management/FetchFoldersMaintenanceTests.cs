using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ArzoieMail.Management;

// External POP3 fetch accounts, groups, public folders and their permissions, backup settings,
// backup/restore and the installation key, against the real native dispatcher and service.
static class FetchFoldersMaintenanceTests
{
    public static async Task Run(IManagementChannel native, string username, string password, Action<bool, string> check)
    {
        Task<JsonElement> Send(string op, object? data, string? actor = null) => native.SendAsync(op, actor ?? username, password, data, CancellationToken.None);
        async Task Refuse(string op, object? data, int status, string? actor = null)
        {
            try { await Send(op, data, actor); throw new Exception("Unexpected success: " + op); }
            catch (ManagementError error) { check(error.Status == status, $"{op}: expected {status}, received {error.Status}."); }
        }
        string? otherManager = Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_DOMAIN_USERNAME"), ordinary = Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_USER_USERNAME");
        foreach (string actor in new[] { otherManager, ordinary }.OfType<string>())
        {
            await Refuse("groups.list", new { }, 403, actor);
            await Refuse("publicfolders.list", new { }, 403, actor);
            await Refuse("backup.status", new { }, 403, actor);
            await Refuse("secretkey.export", new { passphrase = "long enough passphrase" }, 403, actor);
            await Refuse("settings.backup.passphrase", new { passphrase = "" }, 403, actor);
        }

        string name = "fetch-" + Guid.NewGuid().ToString("N") + ".test";
        long domainId = (await Send("domains.create", new { fields = new { name, active = true } })).GetProperty("id").GetInt64();
        try
        {
            async Task<long> Create(string local, int level = 0) =>
                (await Send("accounts.create", new { domainId, fields = new { address = local + "@" + name, password, active = true, level } })).GetProperty("id").GetInt64();
            long owner = await Create("owner"), member = await Create("member");
            await Create("manager", 1);
            string manager = "manager@" + name;
            await FetchAccounts(domainId, owner, manager);
            await GroupsAndFolders(owner, member, "member@" + name);
        }
        finally { await Send("domains.delete", new { id = domainId }); }
        await Backups();
        await SecretKey();

        async Task FetchAccounts(long domain, long account, string manager)
        {
            var scope = new { domainId = domain, accountId = account };
            if (ordinary is not null) await Refuse("fetchaccounts.list", scope, 403, ordinary);
            if (otherManager is not null) await Refuse("fetchaccounts.list", scope, 403, otherManager);
            await Refuse("fetchaccounts.list", new { domainId = domain, accountId = long.MaxValue }, 404);
            object Fields(object fields) => new { domainId = domain, accountId = account, fields };
            await Refuse("fetchaccounts.create", Fields(new { name = "x", serverAddress = "pop.example.test", username = "u" }), 422);
            await Refuse("fetchaccounts.create", Fields(new { name = "x", serverAddress = "pop.example.test", username = "u", password = "secret", security = 0 }), 422);
            await Refuse("fetchaccounts.create", Fields(new { name = "x", serverAddress = "pop.example.test", username = "u", password = "secret", port = 0 }), 422);
            await Refuse("fetchaccounts.create", Fields(new { name = "x", serverAddress = "bad host*", username = "u", password = "secret" }), 422);
            await Refuse("fetchaccounts.create", Fields(new { name = "x", serverAddress = "pop.example.test", username = "u", password = "secret", mimeRecipientHeaders = "To,,CC" }), 422);
            await Refuse("fetchaccounts.create", Fields(new { name = "x", serverAddress = "pop.example.test", username = "u", password = "secret", routeRecipients = true }), 422);
            await Refuse("fetchaccounts.create", Fields(new { name = "x", serverAddress = "pop.example.test", username = "u", password = "secret", daysToKeep = -2 }), 422);
            await Refuse("fetchaccounts.create", Fields(new { name = "x", serverAddress = "pop.example.test", username = "u", password = "secret", script = "x" }), 422);
            // A domain administrator manages fetch accounts for mailboxes in their own domain.
            var created = await native.SendAsync("fetchaccounts.create", manager, password, new { domainId = domain, accountId = account, fields = new {
                name = "Old provider", serverAddress = "pop.example.test", username = "remote-user", password = "fetch secret \u00e9", daysToKeep = 7 } }, CancellationToken.None);
            long id = created.GetProperty("id").GetInt64();
            check(!created.GetProperty("active").GetBoolean() && created.GetProperty("security").GetInt32() == 1 && created.GetProperty("port").GetInt32() == 995,
                "New fetch accounts start disabled with implicit TLS on port 995.");
            check(created.GetProperty("passwordConfigured").GetBoolean() && !created.GetRawText().Contains("fetch secret", StringComparison.Ordinal),
                "The fetch password is stored and never returned.");
            check(created.GetProperty("daysToKeep").GetInt32() == 7, "Keep-days policy persists.");
            await Refuse("fetchaccounts.run", new { domainId = domain, accountId = account, id }, 409);
            var updated = await Send("fetchaccounts.update", new { domainId = domain, accountId = account, id, fields = new { active = true, security = 3, port = 110, minutesBetweenFetch = 600, processMimeRecipients = true, routeRecipients = true } });
            check(updated.GetProperty("active").GetBoolean() && updated.GetProperty("security").GetInt32() == 3 && updated.GetProperty("passwordConfigured").GetBoolean(),
                "Updating other fields keeps the stored password.");
            var run = await Send("fetchaccounts.run", new { domainId = domain, accountId = account, id });
            check(run.GetProperty("id").GetInt64() == id, "A download can be requested for an enabled account.");
            check((await Send("fetchaccounts.list", scope)).GetProperty("total").GetInt64() == 1, "The mailbox lists its fetch account.");
            await Send("fetchaccounts.update", new { domainId = domain, accountId = account, id, fields = new { active = false } });
            // The run request may have started a download against the unreachable host; wait for it to release the account.
            for (int attempt = 0; attempt < 60 && (await Send("fetchaccounts.read", new { domainId = domain, accountId = account, id })).GetProperty("running").GetBoolean(); ++attempt)
                await Task.Delay(1000);
            check((await Send("fetchaccounts.delete", new { domainId = domain, accountId = account, id })).GetProperty("deleted").GetBoolean(), "The fetch account is deleted.");
            await Refuse("fetchaccounts.read", new { domainId = domain, accountId = account, id }, 404);
        }

        async Task GroupsAndFolders(long owner, long memberId, string memberAddress)
        {
            string groupName = "Group " + Guid.NewGuid().ToString("N")[..8];
            await Refuse("groups.create", new { fields = new { name = "" } }, 422);
            long groupId = (await Send("groups.create", new { fields = new { name = groupName } })).GetProperty("id").GetInt64();
            var folderIds = new List<long>();
            try
            {
                await Refuse("groups.create", new { fields = new { name = groupName.ToUpperInvariant() } }, 409);
                check((await Send("groups.update", new { id = groupId, fields = new { name = groupName + " renamed" } })).GetProperty("name").GetString() == groupName + " renamed", "A group is renamed.");
                await Refuse("groupmembers.create", new { groupId, fields = new { accountId = long.MaxValue } }, 422);
                long membership = (await Send("groupmembers.create", new { groupId, fields = new { accountId = memberId } })).GetProperty("id").GetInt64();
                await Refuse("groupmembers.create", new { groupId, fields = new { accountId = memberId } }, 409);
                var members = await Send("groupmembers.list", new { groupId });
                check(members.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("address").GetString() == memberAddress), "Group members are listed by address.");

                string folderName = "Shared " + Guid.NewGuid().ToString("N")[..6] + " \u00e4";
                await Refuse("publicfolders.create", new { fields = new { name = "a/b", parentId = 0 } }, 422);
                await Refuse("publicfolders.create", new { fields = new { name = "child", parentId = long.MaxValue } }, 404);
                long folderId = (await Send("publicfolders.create", new { fields = new { name = folderName, parentId = 0 } })).GetProperty("id").GetInt64();
                folderIds.Add(folderId);
                await Refuse("publicfolders.create", new { fields = new { name = folderName, parentId = 0 } }, 409);
                long childId = (await Send("publicfolders.create", new { fields = new { name = "Child", parentId = folderId } })).GetProperty("id").GetInt64();
                folderIds.Insert(0, childId);
                var listed = (await Send("publicfolders.list", new { })).GetProperty("items").EnumerateArray().ToList();
                check(listed.Any(item => item.GetProperty("id").GetInt64() == childId && item.GetProperty("parentId").GetInt64() == folderId
                    && item.GetProperty("path").GetString()!.StartsWith(folderName, StringComparison.Ordinal)), "Public folders are listed as a tree with Unicode names.");
                await Refuse("publicfolders.delete", new { id = folderId }, 409);

                await Refuse("folderpermissions.create", new { folderId, fields = new { type = 0, accountId = owner, rights = new { fly = true } } }, 422);
                await Refuse("folderpermissions.create", new { folderId, fields = new { type = 1, accountId = owner, rights = new { read = true } } }, 422);
                long groupPermission = (await Send("folderpermissions.create", new { folderId, fields = new { type = 1, groupId, rights = new { lookup = true, read = true } } })).GetProperty("id").GetInt64();
                await Refuse("folderpermissions.create", new { folderId, fields = new { type = 1, groupId, rights = new { read = true } } }, 409);
                long userPermission = (await Send("folderpermissions.create", new { folderId, fields = new { type = 0, accountId = owner, rights = new { lookup = true, read = true, insert = true } } })).GetProperty("id").GetInt64();
                var saved = await Send("folderpermissions.update", new { folderId, id = userPermission, fields = new { rights = new { insert = false, administer = true } } });
                check(saved.GetProperty("rights").GetProperty("administer").GetBoolean() && !saved.GetProperty("rights").GetProperty("insert").GetBoolean()
                    && saved.GetProperty("rights").GetProperty("read").GetBoolean(), "A permission update changes only the named rights.");
                check(await ImapLists(memberAddress, folderName), "A group member sees the public folder over IMAP.");
                await Send("groupmembers.delete", new { groupId, id = membership });
                check(!await ImapLists(memberAddress, folderName), "Removing the membership hides the folder again.");
                check((await Send("folderpermissions.delete", new { folderId, id = groupPermission })).GetProperty("deleted").GetBoolean(), "A permission is removed.");
                check((await Send("folderpermissions.list", new { folderId })).GetProperty("items").GetArrayLength() == 1, "One permission remains.");
                check((await Send("publicfolders.update", new { id = childId, fields = new { name = "Renamed child" } })).GetProperty("name").GetString() == "Renamed child", "A public folder is renamed.");
            }
            finally
            {
                foreach (long id in folderIds) { try { await Send("publicfolders.delete", new { id }); } catch (ManagementError) { } }
                await Send("groups.delete", new { id = groupId });
            }
            await Refuse("groups.read", new { id = groupId }, 404);
        }

        async Task<bool> ImapLists(string address, string folder)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var client = new TcpClient(); await client.ConnectAsync("127.0.0.1", 143, deadline.Token);
            using var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII);
            using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            async Task<List<string>> Command(string tag, string text)
            {
                await writer.WriteLineAsync(tag + " " + text); var lines = new List<string>();
                for (string? line; (line = await reader.ReadLineAsync(deadline.Token)) is not null;) { lines.Add(line); if (line.StartsWith(tag + " ", StringComparison.Ordinal)) break; }
                if (!lines.Last().StartsWith(tag + " OK", StringComparison.Ordinal)) throw new IOException("IMAP probe: " + lines.Last());
                return lines;
            }
            await reader.ReadLineAsync(deadline.Token);
            await Command("A1", $"LOGIN \"{address}\" \"{password.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"");
            var listing = await Command("A2", "LIST \"\" \"*\"");
            await Command("A3", "LOGOUT");
            // Folder names travel in modified UTF-7; the ASCII prefix is enough to find it.
            string prefix = folder[..folder.IndexOf(' ', 7)];
            return listing.Any(line => line.StartsWith("* LIST", StringComparison.Ordinal) && line.Contains(prefix, StringComparison.Ordinal));
        }

        async Task Backups()
        {
            var section = await Send("settings.read", new { section = "backup" });
            var original = section.GetProperty("values").Clone();
            bool passphraseBefore = section.GetProperty("credentials").GetProperty("passphraseConfigured").GetBoolean();
            string destination = original.GetProperty("destination").GetString()!;
            string folder = Path.Combine(Path.GetTempPath(), "arzoiemail-backup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            Dictionary<string, object> Expected(JsonElement values, params string[] keys) => keys.ToDictionary(key => key, key => (object)values.GetProperty(key).Clone());
            try
            {
                foreach (string invalid in new[] { @"\\server\share", Environment.GetFolderPath(Environment.SpecialFolder.Windows), Path.Combine(folder, "missing"), "relative" })
                    await Refuse("settings.update", new { section = "backup", fields = new { destination = invalid }, expected = Expected(original, "destination") }, 422);
                await Refuse("settings.update", new { section = "backup", fields = new { includeMessages = true, includeDomains = false },
                    expected = Expected(original, "includeMessages", "includeDomains") }, 422);
                await Refuse("settings.backup.passphrase", new { passphrase = "short" }, 422);
                var saved = await Send("settings.update", new { section = "backup", fields = new { destination = folder, includeSettings = true, includeDomains = false, includeMessages = false },
                    expected = Expected(original, "destination", "includeSettings", "includeDomains", "includeMessages") });
                check(saved.GetProperty("values").GetProperty("destination").GetString() == folder, "The backup destination and contents persist.");
                check((await Send("settings.backup.passphrase", new { passphrase = "backup passphrase for tests" })).GetProperty("configured").GetBoolean(), "A backup passphrase is stored.");
                check(!(await Send("settings.read", new { section = "backup" })).GetRawText().Contains("backup passphrase for tests", StringComparison.Ordinal), "The backup passphrase is never returned.");
                await Refuse("backup.start", new { destination = "C:\\" }, 422);
                await Send("backup.start", new { });
                JsonElement status;
                using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(180)))
                    do { await Task.Delay(1000, deadline.Token); status = await Send("backup.status", new { }); }
                    while (status.GetProperty("running").GetBoolean() || status.GetProperty("archives").GetArrayLength() == 0);
                string archive = status.GetProperty("archives")[0].GetProperty("name").GetString()!;
                check(archive.StartsWith("HMBackup ", StringComparison.Ordinal) && archive.EndsWith(".7z", StringComparison.Ordinal), "A backup archive is written and listed.");
                // Restore is checked up to the point where it would replace data.
                await Refuse("backup.restore", new { name = archive, confirmName = "other", settings = true }, 422);
                await Refuse("backup.restore", new { name = "..\\" + archive, confirmName = "..\\" + archive, settings = true }, 422);
                await Refuse("backup.restore", new { name = "HMBackup missing.7z", confirmName = "HMBackup missing.7z", settings = true }, 404);
                await Refuse("backup.restore", new { name = archive, confirmName = archive, settings = true, passphrase = "wrong passphrase" }, 422);
                await Refuse("backup.restore", new { name = archive, confirmName = archive, domains = true, passphrase = "backup passphrase for tests" }, 422);
                await Refuse("backup.restore", new { name = archive, confirmName = archive, messages = true, passphrase = "backup passphrase for tests" }, 422);
                await Refuse("backup.restore", new { name = archive, confirmName = archive, passphrase = "backup passphrase for tests" }, 422);
            }
            finally
            {
                var current = (await Send("settings.read", new { section = "backup" })).GetProperty("values");
                var restore = new Dictionary<string, object>();
                foreach (string key in new[] { "includeSettings", "includeDomains", "includeMessages", "compress" }) restore[key] = original.GetProperty(key).GetBoolean();
                if (destination.Length == 0 || Directory.Exists(destination)) restore["destination"] = destination;
                await Send("settings.update", new { section = "backup", fields = restore, expected = restore.Keys.ToDictionary(key => key, key => (object)current.GetProperty(key).Clone()) });
                if (!passphraseBefore) await Send("settings.backup.passphrase", new { passphrase = "" });
                else Console.WriteLine("Fetch/folders/maintenance: a backup passphrase existed before the test and was replaced.");
                try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        async Task SecretKey()
        {
            string keyId = (await Send("secretkey.read", new { })).GetProperty("keyId").GetString()!;
            check(keyId.Length > 0, "The installation key has an identifier.");
            await Refuse("secretkey.export", new { passphrase = "too short" }, 422);
            var exported = await Send("secretkey.export", new { passphrase = "export passphrase for tests" });
            string export = exported.GetProperty("exported").GetString()!;
            check(export.Length > 0 && exported.GetProperty("keyId").GetString() == keyId, "The key exports under a passphrase.");
            await Refuse("secretkey.import", new { exported = export, passphrase = "wrong passphrase here" }, 422);
            await Refuse("secretkey.import", new { exported = "not an export", passphrase = "export passphrase for tests" }, 422);
            check((await Send("secretkey.import", new { exported = export, passphrase = "export passphrase for tests" })).GetProperty("keyId").GetString() == keyId,
                "Re-importing this installation's own key keeps the same key.");
            check((await Send("status", null)).GetProperty("state").GetInt32() == 3, "The service keeps running after a key import.");
        }
    }
}
