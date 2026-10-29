using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace ArzoieMail.Management;

// Browser cookies contain only an opaque ID. Passwords are never serialized into cookies or files.
// Ephemeral encrypted server-side sessions deliberately expire on process restart.
public sealed class CredentialVault(TimeProvider? timeProvider = null) : IDisposable
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private sealed record Entry(byte[] Cipher, byte[] Nonce, byte[] Tag, string SecurityStamp, DateTimeOffset Created, DateTimeOffset LastUsed);
    private readonly ConcurrentDictionary<string, Entry> entries = new();
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
    public string Add(Login credentials, string securityStamp)
    {
        if (securityStamp.Length != 64 || !securityStamp.All(char.IsAsciiHexDigit)) throw new ManagementError(503, "The native identity version is unavailable.");
        Prune();
        if (entries.Count >= 1024) throw new ManagementError(503, "Session capacity reached.");
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(credentials);
        try
        {
            byte[] cipher = new byte[plain.Length], nonce = RandomNumberGenerator.GetBytes(12), tag = new byte[16];
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plain, cipher, tag);
            string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var now = clock.GetUtcNow();
            entries[id] = new(cipher, nonce, tag, securityStamp, now, now);
            return id;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public Login Get(string? id)
    {
        var now = clock.GetUtcNow();
        if (id is null || !entries.TryGetValue(id, out var entry) || now - entry.LastUsed > TimeSpan.FromMinutes(30)
            || now - entry.Created > TimeSpan.FromHours(8))
        {
            Remove(id);
            throw new ManagementError(401, "Please sign in again.");
        }
        byte[] plain = new byte[entry.Cipher.Length];
        try
        {
            using var aes = new AesGcm(key, entry.Tag.Length);
            aes.Decrypt(entry.Nonce, entry.Cipher, entry.Tag, plain);
            entries.TryUpdate(id, entry with { LastUsed = now }, entry);
            return JsonSerializer.Deserialize<Login>(plain) ?? throw new ManagementError(401, "Please sign in again.");
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public bool MatchesStamp(string? id, string securityStamp) => id is not null
        && entries.TryGetValue(id, out var entry) && string.Equals(entry.SecurityStamp, securityStamp, StringComparison.Ordinal);
    public void Remove(string? id) { if (id is not null) entries.TryRemove(id, out _); }
    private void Prune()
    {
        var now = clock.GetUtcNow();
        foreach (var pair in entries)
            if (now - pair.Value.LastUsed > TimeSpan.FromMinutes(30) || now - pair.Value.Created > TimeSpan.FromHours(8)) Remove(pair.Key);
    }
    public void Dispose() { entries.Clear(); CryptographicOperations.ZeroMemory(key); }
}

public sealed record Login(string Username, string Password);
