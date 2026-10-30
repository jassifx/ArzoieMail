using System.Text.Json;
using ArzoieMail.Management;

static class MailboxSelfServiceTests
{
    public static async Task Run(IManagementChannel native, string username, string password, Action<bool, string> check)
    {
        Task<JsonElement> Admin(string op, object data) => native.SendAsync(op, username, password, data, CancellationToken.None);
        string domain = "self-" + Guid.NewGuid().ToString("N") + ".test";
        long domainId = (await Admin("domains.create", new { fields = new { name = domain, active = true } })).GetProperty("id").GetInt64();
        string owner = "owner@" + domain, currentSecret = password;
        Task<JsonElement> Own(string op, object data, string? secret = null) => native.SendAsync(op, owner, secret ?? currentSecret, data, CancellationToken.None);
        async Task Refuse(string op, object data, int status, string? secret = null)
        {
            try { await Own(op, data, secret); throw new Exception("Unexpected mailbox self-service success: " + op); }
            catch (ManagementError error) { check(error.Status == status, $"Self-service {op}: expected {status}, received {error.Status}."); }
        }
        Exception? bodyFailure = null;
        try
        {
            long ownerId = (await Admin("accounts.create", new { domainId, fields = new { address = owner, password, active = true, level = 0 } })).GetProperty("id").GetInt64();
            long otherId = (await Admin("accounts.create", new { domainId, fields = new { address = "other@" + domain, password, active = true, level = 0 } })).GetProperty("id").GetInt64();
            var identity = await Own("self.read", new { });
            check(identity.GetProperty("address").GetString() == owner && identity.GetProperty("passwordChangeable").GetBoolean(), "Native self-service binds the authenticated local mailbox.");
            check(!identity.TryGetProperty("password", out _) && !identity.TryGetProperty("passwordHash", out _), "Own identity never exposes stored password material.");
            string stamp = (await Own("authenticate", new { })).GetProperty("securityStamp").GetString()!;
            check(stamp.Length == 64 && stamp.All(char.IsAsciiHexDigit), "Native authentication returns an opaque credential binding, not the stored hash.");
            foreach (var data in new object[] { new { id = otherId }, new { domainId }, new { accountId = otherId }, new { domainId = long.MaxValue } })
                await Refuse("self.profile.read", data, 422);
            await Refuse("accounts.profile.read", new { domainId, id = ownerId }, 403);
            await Refuse("self.profile.update", new { fields = new { level = 2 } }, 422);
            await Own("self.profile.update", new { fields = new { firstName = "Self", lastName = "Owner" } });
            var profile = await Admin("accounts.profile.read", new { domainId, id = ownerId });
            check(profile.GetProperty("firstName").GetString() == "Self", "Self profile persists through the existing business layer.");
            check((await Admin("accounts.profile.read", new { domainId, id = otherId })).GetProperty("firstName").GetString() == "", "Self profile does not modify a second mailbox.");
            string markup = "<b>Source only</b>";
            await Own("self.signature.update", new { fields = new { signatureEnabled = false, signaturePlainText = "Own\r\nsignature", signatureHtml = markup } });
            check((await Own("self.signature.read", new { })).GetProperty("signatureHtml").GetString() == markup, "Own signature preserves inert HTML source.");
            await Own("self.behavior.update", new { fields = new { forwardEnabled = false, forwardAddress = "other@" + domain, autoReplyEnabled = false, autoReplyBody = "Away" } });
            check((await Own("self.behavior.read", new { })).GetProperty("forwardAddress").GetString() == "other@" + domain, "Own forwarding/reply settings round-trip without activating delivery changes.");
            await Own("self.behavior.update", new { fields = new { autoReplyEnabled = true } });
            check((await Own("self.behavior.read", new { })).GetProperty("autoReplyEnabled").GetBoolean(), "Self reply enablement persists without a full account save.");
            await Own("self.behavior.update", new { fields = new { autoReplyEnabled = false } });
            check(!(await Own("self.behavior.read", new { })).GetProperty("autoReplyEnabled").GetBoolean(), "Self reply disablement persists before reply-history notification.");
            var unchanged = await Admin("accounts.read", new { domainId, id = ownerId });
            check(unchanged.GetProperty("active").GetBoolean() && unchanged.GetProperty("level").GetInt32() == 0,
                "Self profile/signature/behavior updates preserve privileged account fields.");
            check((await Own("authenticate", new { })).GetProperty("securityStamp").GetString() == stamp, "Preference edits preserve the native credential binding.");
            long ownRule = (await Own("self.rules.create", new { fields = new { name = "Own disabled draft" } })).GetProperty("id").GetInt64();
            long otherRule = (await Admin("rules.create", new { domainId, accountId = otherId, fields = new { name = "Other mailbox draft" } })).GetProperty("id").GetInt64();
            check(!(await Own("self.rules.read", new { id = ownRule })).GetProperty("active").GetBoolean(), "Self-created rules begin disabled.");
            await Refuse("self.rules.read", new { id = otherRule }, 404);
            await Refuse("self.rules.update", new { id = ownRule, accountId = otherId, fields = new { active = false } }, 422);
            await Refuse("self.rules.update", new { id = ownRule, fields = new { actions = new[] { new { type = 8, routeId = 1 } } } }, 403);
            await Own("self.rules.delete", new { id = ownRule });
            await Refuse("self.rules.read", new { id = ownRule }, 404);
            await Refuse("self.password.update", new { fields = new { currentPassword = "incorrect", newPassword = "must-not-be-applied" } }, 403);
            await Refuse("self.password.update", new { fields = new { currentPassword = password, newPassword = "" } }, 422);
            await Refuse("self.password.update", new { fields = new { currentPassword = password, newPassword = "must-not-be-applied", level = 2 } }, 422);
            string replacement = Guid.NewGuid().ToString("N") + "!é漢";
            check((await Own("self.password.update", new { fields = new { currentPassword = password, newPassword = replacement } })).GetProperty("changed").GetBoolean(), "Own password changes after a separate current-password proof.");
            currentSecret = replacement;
            await Refuse("authenticate", new { }, 401, password);
            check((await Own("authenticate", new { })).GetProperty("level").GetInt32() == 0, "New UTF-8 password works and cannot elevate the mailbox role.");
            check((await Own("authenticate", new { })).GetProperty("securityStamp").GetString() != stamp, "Password changes rotate the native credential binding.");
        }
        catch (Exception error) { bodyFailure = error; throw; }
        finally
        {
            try { await Admin("domains.delete", new { id = domainId }); }
            catch (Exception cleanup) when (bodyFailure is not null) { throw new AggregateException("Self-service and cleanup failed.", bodyFailure, cleanup); }
        }
    }
}
