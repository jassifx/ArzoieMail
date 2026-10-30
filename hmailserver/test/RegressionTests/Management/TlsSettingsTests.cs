using System.Text.Json;
using ArzoieMail.Management;

static class TlsSettingsTests
{
    public static async Task Run(IManagementChannel native, string username, string password, Action<bool, string> check)
    {
        Task<JsonElement> Send(string operation, object data, string? actor = null) =>
            native.SendAsync(operation, actor ?? username, password, data, CancellationToken.None);
        async Task<JsonElement> Read() => (await Send("settings.read", new { section = "tls" })).GetProperty("values").Clone();
        async Task<JsonElement> Patch(Dictionary<string, object> fields, int? refusal = null)
        {
            var current = await Read();
            var expected = fields.Keys.ToDictionary(key => key, key => current.GetProperty(key).Clone());
            try
            {
                var result = await Send("settings.update", new { section = "tls", fields, expected });
                if (refusal.HasValue) throw new Exception("Invalid TLS policy was accepted.");
                return result.GetProperty("values").Clone();
            }
            catch (ManagementError error) when (refusal.HasValue)
            {
                check(error.Status == refusal, "Invalid TLS policy returns the expected status.");
                return await Read();
            }
        }
        foreach (string actor in new[] { Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_DOMAIN_USERNAME"),
            Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_USER_USERNAME") }.OfType<string>())
        {
            try { await Send("settings.read", new { section = "tls" }, actor); throw new Exception("Non-server TLS read succeeded."); }
            catch (ManagementError error) { check(error.Status == 403, "TLS settings require server administration."); }
            try { await Send("settings.update", new { section = "tls", fields = new { verifyRemoteCertificate = false }, expected = new { verifyRemoteCertificate = true } }, actor); throw new Exception("Non-server TLS update succeeded."); }
            catch (ManagementError error) { check(error.Status == 403, "Non-server TLS changes are denied before validation."); }
        }
        var original = await Read();
        Exception? bodyFailure = null;
        try
        {
            await Patch(new() { ["tls10Enabled"] = false, ["tls11Enabled"] = false, ["tls12Enabled"] = true,
                ["tls13Enabled"] = true, ["preferServerCiphers"] = true, ["prioritizeChaCha"] = true });
            var baseline = await Read();
            var invalid = new Dictionary<string, object>[] {
                new() { ["tls10Enabled"] = false, ["tls11Enabled"] = false, ["tls12Enabled"] = false, ["tls13Enabled"] = false },
                new() { ["preferServerCiphers"] = false },
                new() { ["cipherList"] = "ARZOIEMAIL-NO-SUCH-CIPHER" },
                new() { ["cipherList"] = "HIGH\0:!aNULL" },
                new() { ["cipherList"] = "HIGH\t:!aNULL" },
                new() { ["cipherList"] = new string('X', 4097) },
                new() { ["tls12Enabled"] = "true" },
                new() { ["verifyRemoteCertificate"] = 1 }
            };
            foreach (var fields in invalid)
            {
                var after = await Patch(fields, 422);
                check(after.GetRawText() == baseline.GetRawText(), "Rejected TLS policy leaves every persisted setting unchanged.");
            }
            var changed = await Patch(new() { ["tls12Enabled"] = false, ["tls13Enabled"] = true,
                ["preferServerCiphers"] = false, ["prioritizeChaCha"] = false,
                ["cipherList"] = "HIGH:\r\n!aNULL:!eNULL" });
            check(!changed.GetProperty("tls12Enabled").GetBoolean() && changed.GetProperty("tls13Enabled").GetBoolean(),
                "Shared TLS version bits are merged without discarding an omitted enabled version.");
            check(!changed.GetProperty("preferServerCiphers").GetBoolean() && !changed.GetProperty("prioritizeChaCha").GetBoolean(),
                "Dependent cipher options can be disabled together as one checked batch.");
            check(changed.GetProperty("cipherList").GetString() == "HIGH:\r\n!aNULL:!eNULL", "Legacy multiline cipher syntax is preserved after validation.");
            try
            {
                await Send("settings.update", new { section = "tls", fields = new { tls12Enabled = true }, expected = new { tls12Enabled = true } });
                throw new Exception("Stale TLS snapshot succeeded.");
            }
            catch (ManagementError error) { check(error.Status == 409, "A stale TLS bit cannot overwrite the new mask."); }
            await Patch(new() { ["cipherList"] = "" });
            check((await Read()).GetProperty("cipherList").GetString() == "", "Empty cipher expression selects native library defaults.");
        }
        catch (Exception error) { bodyFailure = error; throw; }
        finally
        {
            try { await Patch(original.EnumerateObject().ToDictionary(item => item.Name, item => (object)item.Value.Clone())); }
            catch (Exception cleanup) when (bodyFailure is not null) { throw new AggregateException("TLS policy and restoration failed.", bodyFailure, cleanup); }
        }
    }
}
