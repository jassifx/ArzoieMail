using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace ArzoieMail.Management;

// Browser cookies contain only an opaque ID. Passwords are never serialized into cookies or files.
// Ephemeral encrypted server-side sessions deliberately expire on process restart.
public sealed class CredentialVault : IDisposable
{
    private sealed record Entry(byte[] Cipher, byte[] Nonce, byte[] Tag, DateTimeOffset Created, DateTimeOffset LastUsed);
    private readonly ConcurrentDictionary<string, Entry> entries = new();
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
    public string Add(Login credentials)
    {
        Prune();
        if (entries.Count >= 1024) throw new ManagementError(503, "Session capacity reached.");
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(credentials);
        try
        {
            byte[] cipher = new byte[plain.Length], nonce = RandomNumberGenerator.GetBytes(12), tag = new byte[16];
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plain, cipher, tag);
            string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var now = DateTimeOffset.UtcNow;
            entries[id] = new(cipher, nonce, tag, now, now);
            return id;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public Login Get(string? id)
    {
        var now = DateTimeOffset.UtcNow;
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
    public void Remove(string? id) { if (id is not null) entries.TryRemove(id, out _); }
    private void Prune()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in entries)
            if (now - pair.Value.LastUsed > TimeSpan.FromMinutes(30) || now - pair.Value.Created > TimeSpan.FromHours(8)) Remove(pair.Key);
    }
    public void Dispose() { entries.Clear(); CryptographicOperations.ZeroMemory(key); }
}

public sealed record Login(string Username, string Password);
