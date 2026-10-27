using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ArzoieMail.Management;

static class AddressingTests
{
    public static async Task Run(IManagementChannel native, string username, string password, Action<bool, string> check)
    {
        Task<JsonElement> Send(string operation, object fields, string? actor = null) =>
            native.SendAsync(operation, actor ?? username, password, fields, CancellationToken.None);
        async Task Refuse(string operation, object fields, int status, string? actor = null)
        {
            try { await Send(operation, fields, actor); throw new Exception($"Unexpected {operation} success."); }
            catch (ManagementError error) { check(error.Status == status, $"{operation}: expected {status}, got {error.Status}."); }
        }
        string name = "addressing-" + Guid.NewGuid().ToString("N") + ".test";
        var domain = await Send("domains.create", new { fields = new { name, active = true } });
        long domainId = domain.GetProperty("id").GetInt64();
        try
        {
            var identity = await Send("authenticate", new { });
            long foreignDomain = identity.GetProperty("domainId").GetInt64();
            string manager = "manager@" + name, mailbox = "mailbox@" + name;
            await Send("accounts.create", new { domainId, fields = new { address = manager, password, active = true, level = 1 } });
            await Send("accounts.create", new { domainId, fields = new { address = mailbox, password, active = true } });
            string source = "alias@" + name, listAddress = "list@" + name;
            await Refuse("aliases.create", new { domainId, fields = new { address = source } }, 422, manager);
            foreach (string bad in new[] { "", "broken", "one@example.test,two@example.test", "one@example.test\r\nMAIL FROM:x", "\u0001user@example.test", "user\u007f@example.test" })
                await Refuse("aliases.create", new { domainId, fields = new { address = source, target = bad } }, 422, manager);
            await Refuse("aliases.create", new { domainId, fields = new { address = "alias@elsewhere.test", target = mailbox } }, 422, manager);
            await Refuse("aliases.create", new { domainId, fields = new { address = source, target = source } }, 422, manager);
            await Refuse("aliases.create", new { domainId, fields = new { address = mailbox, target = manager } }, 422, manager);
            var alias = await Send("aliases.create", new { domainId, fields = new { address = source, target = mailbox } }, manager);
            long aliasId = alias.GetProperty("id").GetInt64();
            check(!alias.GetProperty("active").GetBoolean(), "New aliases default disabled.");
            await Refuse("aliases.create", new { domainId, fields = new { address = source.ToUpperInvariant(), target = mailbox } }, 422, manager);
            var page = await Send("aliases.list", new { domainId, search = "ALIAS@", limit = 1 }, manager);
            check(page.GetProperty("total").GetInt64() == 1 && page.GetProperty("items")[0].GetProperty("id").GetInt64() == aliasId, "Alias search and pagination.");
            await Refuse("aliases.list", new { domainId, limit = 101 }, 422, manager);
            await Refuse("aliases.update", new { domainId, id = aliasId, fields = new { active = true, unknown = 1 } }, 422, manager);
            check(!(await Send("aliases.read", new { domainId, id = aliasId })).GetProperty("active").GetBoolean(), "Invalid alias update is not partially saved.");
            await Refuse("aliases.update", new { domainId, id = aliasId, fields = new { address = "renamed@" + name } }, 422, manager);
            // SMTP RCPT exercises the real routing cache without depositing mail in the queue.
            check(await RecipientStatus(source) == 550, "Disabled alias rejects SMTP recipients.");
            await Send("aliases.update", new { domainId, id = aliasId, fields = new { active = true } }, manager);
            check(await RecipientStatus(source) == 250, "Alias activation immediately invalidates the routing cache.");
            await Send("aliases.update", new { domainId, id = aliasId, fields = new { target = "external@example.net" } }, manager);
            check((await Send("aliases.read", new { domainId, id = aliasId })).GetProperty("target").GetString() == "external@example.net", "External forwarding target persists.");
            await Send("aliases.update", new { domainId, id = aliasId, fields = new { target = mailbox } }, manager);

            var list = await Send("lists.create", new { domainId, fields = new { address = listAddress } }, manager);
            long listId = list.GetProperty("id").GetInt64();
            check(!list.GetProperty("active").GetBoolean() && list.GetProperty("requireAuth").GetBoolean()
                  && list.GetProperty("mode").GetInt32() == 1, "New lists default disabled, authenticated and members-only.");
            await Refuse("lists.create", new { domainId, fields = new { address = source } }, 422, manager);
            await Refuse("lists.create", new { domainId, fields = new { address = mailbox } }, 422, manager);
            await Refuse("aliases.create", new { domainId, fields = new { address = listAddress, target = mailbox } }, 422, manager);
            await Refuse("lists.create", new { domainId, fields = new { address = listAddress.ToUpperInvariant() } }, 422, manager);
            await Refuse("lists.update", new { domainId, id = listId, fields = new { active = true, mode = 4 } }, 422, manager);
            await Refuse("lists.update", new { domainId, id = listId, fields = new { mode = 2 } }, 422, manager);
            check(!(await Send("lists.read", new { domainId, id = listId })).GetProperty("active").GetBoolean(), "Invalid list updates do not partially activate it.");
            await Refuse("members.create", new { domainId, listId, fields = new { address = "bad-address" } }, 422, manager);
            await Refuse("members.create", new { domainId, listId, fields = new { address = listAddress } }, 422, manager);
            var member = await Send("members.create", new { domainId, listId, fields = new { address = manager } }, manager);
            long memberId = member.GetProperty("id").GetInt64();
            await Refuse("members.create", new { domainId, listId, fields = new { address = manager.ToUpperInvariant() } }, 422, manager);
            var secondMember = await Send("members.create", new { domainId, listId, fields = new { address = mailbox } }, manager);
            long secondMemberId = secondMember.GetProperty("id").GetInt64();
            await Refuse("members.update", new { domainId, listId, id = memberId, fields = new { address = mailbox } }, 422, manager);
            page = await Send("members.list", new { domainId, listId, search = "MAILBOX", limit = 1 }, manager);
            check(page.GetProperty("total").GetInt64() == 1 && page.GetProperty("items")[0].GetProperty("id").GetInt64() == secondMemberId, "Member search and paging.");
            page = await Send("members.list", new { domainId, listId, offset = 2, limit = 1 }, manager);
            check(page.GetProperty("total").GetInt64() == 2 && page.GetProperty("items").GetArrayLength() == 0, "Member pagination past the last item.");
            await Refuse("members.read", new { domainId, listId, id = long.MaxValue }, 404, manager);
            var otherList = await Send("lists.create", new { domainId, fields = new { address = "other@" + name } }, manager);
            long otherListId = otherList.GetProperty("id").GetInt64();
            foreach (string action in new[] { "read", "update", "delete" })
                await Refuse("members." + action, new { domainId, listId = otherListId, id = memberId, fields = new { address = mailbox } }, 404, manager);
            foreach (string kind in new[] { "aliases", "lists", "members" })
            {
                foreach (string action in new[] { "list", "read", "create", "update", "delete" })
                {
                    var arguments = new { domainId, listId, id = kind == "aliases" ? aliasId : kind == "lists" ? listId : memberId, fields = new { active = false } };
                    await Refuse(kind + "." + action, arguments, 403, mailbox);
                    await Refuse(kind + "." + action, new { domainId = foreignDomain, listId, id = arguments.id, fields = arguments.fields }, 403, manager);
                }
                await Refuse(kind + ".read", new { domainId = foreignDomain, listId, id = kind == "aliases" ? aliasId : kind == "lists" ? listId : memberId }, 404);
            }
            await Send("lists.update", new { domainId, id = listId, fields = new { active = true } }, manager);
            check(await RecipientStatus(listAddress) == 550, "SMTP authentication requirement applies to a newly activated list.");
            await Send("lists.update", new { domainId, id = listId, fields = new { requireAuth = false, mode = 2, requireAddress = manager } }, manager);
            check(await RecipientStatus(listAddress, manager) == 250 && await RecipientStatus(listAddress, "outsider@example.net") == 550, "Announcement mode enforces its envelope-sender setting.");
            await Send("lists.update", new { domainId, id = listId, fields = new { mode = 1, requireAddress = "" } }, manager);
            check(await RecipientStatus(listAddress, manager) == 250 && await RecipientStatus(listAddress, "outsider@example.net") == 550, "Members-only mode uses saved members.");
            await Send("members.update", new { domainId, listId, id = memberId, fields = new { address = "changed@example.net" } }, manager);
            check(await RecipientStatus(listAddress, manager) == 550 && await RecipientStatus(listAddress, "changed@example.net") == 250, "Member updates invalidate the cached posting policy.");
            await Send("lists.update", new { domainId, id = listId, fields = new { mode = 3 } }, manager);
            check(await RecipientStatus(listAddress, manager) == 250 && await RecipientStatus(listAddress, "changed@example.net") == 550, "Domain-sender mode persists.");
            await Send("lists.update", new { domainId, id = listId, fields = new { mode = 0 } }, manager);
            check(await RecipientStatus(listAddress, "outsider@example.net") == 250, "Explicit public mode persists.");
            await Send("lists.update", new { domainId, id = listId, fields = new { active = false } }, manager);
            check(await RecipientStatus(listAddress) == 550, "List deactivation invalidates the routing cache.");
            await Send("members.delete", new { domainId, listId, id = memberId }, manager);
            await Refuse("members.read", new { domainId, listId, id = memberId }, 404, manager);
            await Send("lists.delete", new { domainId, id = listId }, manager);
            await Refuse("lists.read", new { domainId, id = listId }, 404, manager);
            await Refuse("members.read", new { domainId, listId = otherListId, id = secondMemberId }, 404, manager);
            await Send("aliases.delete", new { domainId, id = aliasId }, manager);
            await Refuse("aliases.read", new { domainId, id = aliasId }, 404, manager);
            check(await RecipientStatus(source) == 550, "Alias deletion immediately removes routing.");
        }
        finally { await Send("domains.delete", new { id = domainId }); }
    }

    internal static async Task<int> RecipientStatus(string recipient, string sender = "outsider@example.net")
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", 25, timeout.Token);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
        async Task<int> Reply()
        {
            string line;
            do { line = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("SMTP closed the test connection."); }
            while (line.Length >= 4 && line[3] == '-');
            return int.Parse(line.AsSpan(0, 3));
        }
        if (await Reply() != 220) throw new IOException("SMTP test greeting failed.");
        await writer.WriteLineAsync("EHLO management.test");
        if (await Reply() != 250) throw new IOException("SMTP test EHLO failed.");
        await writer.WriteLineAsync($"MAIL FROM:<{sender}>");
        if (await Reply() != 250) throw new IOException("SMTP test sender failed.");
        await writer.WriteLineAsync($"RCPT TO:<{recipient}>");
        int result = await Reply();
        await writer.WriteLineAsync("QUIT");
        await Reply();
        return result; // Never send DATA: no queued message or external delivery side effects.
    }
}
