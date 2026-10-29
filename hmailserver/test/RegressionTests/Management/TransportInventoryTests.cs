using System.Globalization;
using System.Text.Json;
using ArzoieMail.Management;

static class TransportInventoryTests
{
    public static async Task Run(IManagementChannel native, string username, string password, Action<bool, string> check)
    {
        Task<JsonElement> Send(string operation, object fields, string? actor = null) =>
            native.SendAsync(operation, actor ?? username, password, fields, CancellationToken.None);
        async Task Refuse(string operation, object fields, int status, string? actor = null)
        {
            try { await Send(operation, fields, actor); throw new Exception("Unexpected transport inventory success."); }
            catch (ManagementError error) { check(error.Status == status, "Transport inventory enforces role/schema bounds."); }
        }
        foreach (string actor in new[] { Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_DOMAIN_USERNAME"),
            Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_USER_USERNAME") }.OfType<string>())
            foreach (string operation in new[] { "transport.listeners.list", "transport.certificates.list" })
                await Refuse(operation, new { }, 403, actor);
        foreach (string operation in new[] { "transport.listeners.list", "transport.certificates.list" })
        {
            foreach (object invalid in new object[] { 0, -1, 26, "25", 1.5, true }) await Refuse(operation, new { limit = invalid }, 422);
            await Refuse(operation, new { offset = -1 }, 422);
            await Refuse(operation, new { offset = long.MaxValue }, 422);
            await Refuse(operation, new { search = new string('x', 256) }, 422);
            await Refuse(operation, new { path = "C:\\Windows\\win.ini" }, 422);
            var empty = await Send(operation, new { offset = int.MaxValue, limit = 1 });
            check(empty.GetProperty("items").GetArrayLength() == 0 && empty.GetProperty("limit").GetInt32() == 1,
                "Past-end paging remains an empty bounded configuration page.");
        }
        await Refuse("transport.listeners.update", new { }, 404);
        await Refuse("transport.certificates.export", new { }, 404);
        var listeners = await Send("transport.listeners.list", new { });
        check(listeners.GetProperty("source").GetString() == "configured" && listeners.GetProperty("maximumLimit").GetInt32() == 25,
            "Listener metadata distinguishes configured state and the bounded page size.");
        check(listeners.GetProperty("limit").GetInt32() == 25 && listeners.GetProperty("items").GetArrayLength() is > 0 and <= 25,
            "The installed-server fixture has a nonempty listener page.");
        foreach (var item in listeners.GetProperty("items").EnumerateArray())
        {
            check(item.EnumerateObject().Select(field => field.Name).Order().SequenceEqual(
                new[] { "address", "certificateId", "id", "port", "protocol", "protocolCode", "security", "securityCode" }.Order()),
                "Listener fields are explicit and exclude private paths.");
            check(item.GetProperty("id").ValueKind == JsonValueKind.String && long.TryParse(item.GetProperty("id").GetString(),
                NumberStyles.None, CultureInfo.InvariantCulture, out long id) && id > 0, "Listener identities stay decimal strings.");
            check(item.GetProperty("address").ValueKind == JsonValueKind.String && item.GetProperty("port").TryGetInt32(out _),
                "Listener addresses and ports have stable types.");
            var certificate = item.GetProperty("certificateId");
            check(certificate.ValueKind is JsonValueKind.Null or JsonValueKind.String, "Certificate references never pass through floating-point IDs.");
        }
        string fixtureName = Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_CERTIFICATE_NAME")
            ?? throw new Exception("Seed the explicit certificate inventory fixture before running native integration tests.");
        string fixtureId = Environment.GetEnvironmentVariable("ARZOIEMAIL_TEST_CERTIFICATE_ID")!;
        var certificates = await Send("transport.certificates.list", new { search = fixtureName, limit = 1 });
        check(certificates.GetProperty("total").GetInt32() == 1 && certificates.GetProperty("items").GetArrayLength() == 1,
            "A persisted certificate registration can be found without opening its nonexistent files.");
        var registered = certificates.GetProperty("items")[0];
        check(registered.EnumerateObject().Select(field => field.Name).Order().SequenceEqual(new[] { "id", "name" }),
            "Certificate inventory returns only public registration ID and name.");
        check(registered.GetProperty("id").GetString() == fixtureId && registered.GetProperty("name").GetString() == fixtureName,
            "Certificate registration identity and name match persisted fixture metadata.");
        var byId = await Send("transport.certificates.list", new { search = fixtureId, limit = 25 });
        check(byId.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("id").GetString() == fixtureId),
            "Certificate search supports exact decimal identity text without narrowing.");
    }
}
