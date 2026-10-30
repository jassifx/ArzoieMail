using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArzoieMail.Management;

// Own decoded sensitive buffers, including when JSON validation fails partway
// through the object. The input type cannot accidentally be serialized back.
[JsonConverter(typeof(CertificateInspectionRequest.Converter))]
public sealed class CertificateInspectionRequest : IDisposable
{
    internal const int MaximumBodyBytes = 1048576;
    private byte[] pfx = [];
    private char[] password = [];
    private int passwordLength;
    internal string[] Hosts { get; private set; } = [];
    internal ReadOnlySpan<byte> Pfx => pfx;
    internal ReadOnlySpan<char> Password => password.AsSpan(0, passwordLength);
    internal static async Task<CertificateInspectionRequest> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] wire = new byte[MaximumBodyBytes + 1];
        try
        {
            int length = 0;
            while (length < wire.Length)
            {
                int read = await stream.ReadAsync(wire.AsMemory(length), cancellationToken);
                if (read == 0) break;
                length += read;
            }
            if (length > MaximumBodyBytes) throw new ManagementError(413, "Certificate inspection requests must not exceed 1 MiB.");
            return ReadCompleteObject(wire.AsSpan(0, length));
        }
        finally { CryptographicOperations.ZeroMemory(wire); }
    }

    private static CertificateInspectionRequest ReadCompleteObject(ReadOnlySpan<byte> wire)
    {
        var reader = new Utf8JsonReader(wire, new JsonReaderOptions { MaxDepth = 16 });
        CertificateInspectionRequest? input = null;
        try
        {
            if (!reader.Read()) throw new ManagementError(422, "Supply a PFX inspection object.");
            input = new Converter().Read(ref reader, typeof(CertificateInspectionRequest), JsonSerializerOptions.Default);
            // Check the complete document while we still own the decoded buffers.
            // Generic async deserialization can discover trailing JSON only after
            // returning the disposable value from its converter.
            if (reader.Read()) throw new JsonException("Supply one JSON object.");
            return input;
        }
        catch { input?.Dispose(); throw; }
    }
    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(pfx);
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
        pfx = []; password = []; passwordLength = 0;
    }

    public sealed class Converter : JsonConverter<CertificateInspectionRequest>
    {
        public override CertificateInspectionRequest Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var input = new CertificateInspectionRequest();
            try
            {
                if (reader.TokenType != JsonTokenType.StartObject) throw Invalid();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName) throw Invalid();
                    string name = reader.ValueTextEquals("pfx") ? "pfx" : reader.ValueTextEquals("password") ? "password"
                        : reader.ValueTextEquals("hosts") ? "hosts" : throw Invalid();
                    if (!seen.Add(name) || !reader.Read()) throw Invalid();
                    if (name == "pfx")
                    {
                        if (reader.TokenType != JsonTokenType.String) throw Invalid();
                        input.pfx = reader.GetBytesFromBase64();
                        if (input.pfx.Length > CertificateInspection.MaximumPfxBytes)
                            throw new ManagementError(422, "The decoded PFX must not exceed 512 KiB.");
                    }
                    else if (name == "password")
                    {
                        if (reader.TokenType != JsonTokenType.String) throw Invalid();
                        long length = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;
                        if (length > 6144) throw Invalid(); // Maximum escaped representation of 1024 ASCII characters.
                        input.password = new char[(int)length];
                        input.passwordLength = reader.CopyString(input.password);
                        if (Encoding.UTF8.GetByteCount(input.Password) > 1024 || input.Password.Contains('\0')) throw Invalid();
                    }
                    else
                    {
                        if (reader.TokenType != JsonTokenType.StartArray) throw Invalid();
                        var hosts = new List<string>();
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                        {
                            if (reader.TokenType != JsonTokenType.String || hosts.Count == 32) throw Invalid();
                            long length = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;
                            if (length > 6144) throw Invalid();
                            string host = reader.GetString()!;
                            if (host.Length > 1024) throw Invalid();
                            hosts.Add(host);
                        }
                        if (reader.TokenType != JsonTokenType.EndArray) throw Invalid();
                        input.Hosts = hosts.ToArray();
                    }
                }
                if (reader.TokenType != JsonTokenType.EndObject || seen.Count != 3) throw Invalid();
                return input;
            }
            catch (Exception error) when (error is FormatException or InvalidOperationException)
            {
                input.Dispose();
                throw Invalid();
            }
            catch { input.Dispose(); throw; }
        }

        private static ManagementError Invalid() => new(422, "Supply exactly pfx (base64), password (string) and hosts (1-32 strings), without duplicate or extra fields.");
        public override void Write(Utf8JsonWriter writer, CertificateInspectionRequest value, JsonSerializerOptions options) =>
            throw new NotSupportedException("Sensitive certificate input cannot be serialized.");
    }
}
