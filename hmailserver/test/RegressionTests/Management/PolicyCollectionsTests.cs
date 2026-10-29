using System.Text.Json;
using ArzoieMail.Management;

static class PolicyCollectionsTests
{
    public static async Task Run(IManagementChannel native, string username, string password, Action<bool, string> check)
    {
        Task<JsonElement> Send(string op, object data, string? actor = null) => native.SendAsync(op, actor ?? username, password, data, CancellationToken.None);
        async Task Refuse(string op, object data, int status, string? actor = null)
        {
            try { await Send(op, data, actor); throw new Exception("Unexpected policy success: " + op); }
            catch (ManagementError error) { check(error.Status == status, $"{op} expected {status}, received {error.Status}."); }
        }
        foreach (string resource in new[] { "surbl", "blockedattachments", "incomingrelays" })
        {
            foreach (string actor in new[] { Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_DOMAIN_USERNAME"), Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_USER_USERNAME") }.OfType<string>())
                foreach (string action in new[] { "list", "read", "create", "update", "delete" })
                    await Refuse(resource + "." + action, new { id = 1, fields = new { } }, 403, actor);
            var list = await Send(resource + ".list", new { limit = 1 });
            check(list.GetProperty("schema").GetArrayLength() > 0 && list.GetProperty("items").GetArrayLength() <= 1, "Policy schema and bounded page are returned.");
            check(list.GetProperty("defaults").GetProperty("id").GetString() == "0", "New-policy template does not reference a persisted object.");
            await Refuse(resource + ".list", new { limit = 101 }, 422);
            await Refuse(resource + ".read", new { id = long.MaxValue }, 422);
            await Refuse(resource + ".create", new { fields = new { unknown = true } }, 422);
        }
        string tag = Guid.NewGuid().ToString("N");
        var fixtures = new[] {
            (Resource: "surbl", Fields: (object)new { zone = "policy-" + tag + ".invalid", active = false, score = 3, rejectMessage = "Fixture blocklist" }),
            (Resource: "blockedattachments", Fields: (object)new { pattern = "*.arzoie-" + tag, description = "Fixture attachment policy" }),
            (Resource: "incomingrelays", Fields: (object)new { name = "Fixture relay " + tag, lowerAddress = "192.0.2.10", upperAddress = "192.0.2.20" })
        };
        foreach (var fixture in fixtures)
        {
            JsonElement? current = null;
            try
            {
                current = await Send(fixture.Resource + ".create", new { fields = fixture.Fields });
                long id = long.Parse(current.Value.GetProperty("id").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                check(id > 0, "Created policy uses a positive exact decimal ID.");
                var loaded = await Send(fixture.Resource + ".read", new { id });
                check(loaded.GetRawText() == current.Value.GetRawText(), "A fresh policy request reads the persisted object.");
                await Refuse(fixture.Resource + ".create", new { fields = fixture.Fields }, 409);
                await Refuse(fixture.Resource + ".delete", new { id, fields = new { } }, 422);
                await Refuse(fixture.Resource + ".update", new { id, fields = new { expected = current.Value, unknown = true } }, 422);
                string field = fixture.Resource == "surbl" ? "score" : fixture.Resource == "incomingrelays" ? "name" : "description";
                object replacement = field == "score" ? 7 : "Changed fixture " + tag;
                var patch = new Dictionary<string, object?> { ["expected"] = current.Value, [field] = replacement };
                var updated = await Send(fixture.Resource + ".update", new { id, fields = patch });
                check(updated.GetProperty(field).ToString() == replacement.ToString(), "Policy update persists the changed field.");
                await Refuse(fixture.Resource + ".delete", new { id, fields = new { expected = current.Value } }, 409);
                current = updated;
                if (fixture.Resource == "incomingrelays")
                {
                    await Refuse("incomingrelays.update", new { id, fields = new { expected = current.Value, upperAddress = "192.0.2.1" } }, 422);
                    await Refuse("incomingrelays.update", new { id, fields = new { expected = current.Value, upperAddress = "2001:db8::1" } }, 422);
                }
                if (fixture.Resource == "surbl")
                    await Refuse("surbl.update", new { id, fields = new { expected = current.Value, rejectMessage = "bad\r\nHEADER" } }, 422);
            }
            finally
            {
                if (current.HasValue)
                {
                    long id = long.Parse(current.Value.GetProperty("id").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                    var latest = await Send(fixture.Resource + ".read", new { id });
                    await Send(fixture.Resource + ".delete", new { id, fields = new { expected = latest } });
                    await Refuse(fixture.Resource + ".read", new { id }, 404);
                }
            }
        }
    }
}
