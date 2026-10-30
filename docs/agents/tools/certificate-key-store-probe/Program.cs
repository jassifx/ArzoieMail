using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

// Disposable-VM storage feasibility probe only; not a production import adapter.
internal static class Program
{
    const uint Machine = 0x20, Silent = 0x40, DeferFinalize = 0x400, Dacl = 4;
    const string Provider = "Microsoft Software Key Storage Provider";
    const string Blob = "PKCS8_PRIVATEKEY";
    static int assertions;

    static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        assertions++;
    }

    static void Success(uint status, string operation)
    {
        if (status != 0) throw new InvalidOperationException($"{operation}: 0x{status:X8}");
        assertions++;
    }

    static int Main()
    {
        if (!OperatingSystem.IsWindows()
            || Environment.GetEnvironmentVariable("ARZOIEMAIL_CNG_PROBE_DISPOSABLE_VM") != "1") return 2;
        try
        {
            string user = Environment.GetEnvironmentVariable("ARZOIEMAIL_CNG_PROBE_USER") ?? throw new InvalidOperationException("Missing disposable identity.");
            string password = Environment.GetEnvironmentVariable("ARZOIEMAIL_CNG_PROBE_PASSWORD") ?? throw new InvalidOperationException("Missing disposable proof.");
            if (!user.StartsWith("CngProbe", StringComparison.Ordinal) || user.Length > 20) throw new InvalidOperationException("Refusing non-probe identity.");
            if (!LogonUser(user, ".", password, 3, 0, out var token)) throw new InvalidOperationException("Cannot acquire the disposable restricted token.");
            using (token)
            {
                var serviceSid = (SecurityIdentifier)new NTAccount("NT SERVICE", "hMailServer").Translate(typeof(SecurityIdentifier));
                using var rsa = RSA.Create(2048);
                using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                Probe(rsa, serviceSid, token, "RSA-2048");
                Probe(ec, serviceSid, token, "ECDSA-P256");
            }
            Console.WriteLine($"Passed {assertions} CNG storage feasibility assertions; generated machine keys deleted.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
            return 1;
        }
    }

    static void Probe(AsymmetricAlgorithm source, SecurityIdentifier serviceSid, SafeAccessTokenHandle token, string label)
    {
        string name = "ArzoieMail-StorageProbe-" + Guid.NewGuid().ToString("N");
        byte[] privateDer = source.ExportPkcs8PrivateKey();
        nint provider = 0, key = 0;
        nint namePointer = 0, bufferPointer = 0;
        bool persisted = false;
        try
        {
            Success(NCryptOpenStorageProvider(out provider, Provider, 0), "Open software KSP");
            namePointer = Marshal.StringToHGlobalUni(name);
            var buffer = new NcryptBuffer { Size = checked((uint)((name.Length + 1) * 2)), Type = 45, Data = namePointer };
            bufferPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NcryptBuffer>());
            Marshal.StructureToPtr(buffer, bufferPointer, false);
            var descriptor = new NcryptBufferDesc { Version = 0, Count = 1, Buffers = bufferPointer };
            Success(NCryptImportKey(provider, 0, Blob, ref descriptor, out key, privateDer, (uint)privateDer.Length, Machine | Silent | DeferFinalize), "Import named PKCS8 before finalization");
            // OpenSSL would need a private-key object in process. This deliberately
            // tests exportable software storage, never claims non-exportability/HSM.
            var policy = BitConverter.GetBytes(3u);
            Success(NCryptSetProperty(key, "Export Policy", policy, (uint)policy.Length, Silent), "Set export policy before finalization");
            var sd = new RawSecurityDescriptor($"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;{serviceSid.Value})");
            var sdBytes = new byte[sd.BinaryLength];
            sd.GetBinaryForm(sdBytes, 0);
            Success(NCryptSetProperty(key, "Security Descr", sdBytes, (uint)sdBytes.Length, Dacl | Silent), "Set protected key DACL before finalization");
            Success(NCryptFinalizeKey(key, Silent), "Finalize named key");
            persisted = true;
            Success(NCryptFreeObject(key), "Close imported key");
            key = 0;
            Success(NCryptOpenKey(provider, out key, name, 0, Machine | Silent), "Reopen machine key");
            Success(NCryptGetProperty(key, "Security Descr", null, 0, out uint sdLength, Dacl | Silent), "Size persisted DACL");
            Check(sdLength is > 0 and < 65536, "Bound persisted DACL");
            var actualBytes = new byte[sdLength];
            Success(NCryptGetProperty(key, "Security Descr", actualBytes, sdLength, out uint actualLength, Dacl | Silent), "Read persisted DACL");
            Check(actualLength == sdLength, "Complete persisted DACL");
            var actual = new RawSecurityDescriptor(actualBytes, 0);
            Check(actual.DiscretionaryAcl is { Count: 3 }, "Exactly three key access entries");
            Check(actual.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected), "Key DACL remains protected");
            var allowed = new HashSet<string>(StringComparer.Ordinal) { "S-1-5-18", "S-1-5-32-544", serviceSid.Value };
            foreach (QualifiedAce ace in actual.DiscretionaryAcl!)
                Check(ace.AceQualifier == AceQualifier.AccessAllowed && allowed.Remove(ace.SecurityIdentifier.Value), "Only SYSTEM, administrators and service SID have key access");
            Check(allowed.Count == 0, "All intended identities retained");
            Success(NCryptExportKey(key, 0, Blob, 0, null, 0, out uint derLength, Silent), "Size reopened PKCS8 export");
            Check(derLength is > 0 and <= 16384, "Bound private-key export");
            var exported = new byte[derLength];
            try
            {
                Success(NCryptExportKey(key, 0, Blob, 0, exported, derLength, out uint written, Silent), "Export reopened PKCS8 for OpenSSL adapter");
                Check(written == derLength, "Complete private-key export");
                using AsymmetricAlgorithm restored = source is RSA ? RSA.Create() : ECDsa.Create();
                restored.ImportPkcs8PrivateKey(exported, out int consumed);
                Check(consumed == exported.Length, "Exact exported PKCS8 parse");
                Check(CryptographicOperations.FixedTimeEquals(source.ExportSubjectPublicKeyInfo(), restored.ExportSubjectPublicKeyInfo()), "Public-key identity survives reopen/export");
            }
            finally { CryptographicOperations.ZeroMemory(exported); }

            WindowsIdentity.RunImpersonated(token, () =>
            {
                nint restrictedProvider = 0, restrictedKey = 0;
                try
                {
                    Success(NCryptOpenStorageProvider(out restrictedProvider, Provider, 0), "Restricted identity can open software provider");
                    uint status = NCryptOpenKey(restrictedProvider, out restrictedKey, name, 0, Machine | Silent);
                    Check(status == 0x80090010u || status == 0x80090016u, "Unprivileged identity cannot open the protected machine key");
                }
                finally
                {
                    if (restrictedKey != 0) NCryptFreeObject(restrictedKey);
                    if (restrictedProvider != 0) NCryptFreeObject(restrictedProvider);
                }
            });
            Console.WriteLine(label + ": named machine import, protected DACL persistence, reopen/export and restricted-user denial passed.");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(label + " probe operation: " + error.Message);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateDer);
            if (bufferPointer != 0) Marshal.FreeHGlobal(bufferPointer);
            if (namePointer != 0) Marshal.FreeHGlobal(namePointer);
            uint cleanup = 0;
            if (key != 0)
            {
                cleanup = NCryptDeleteKey(key, Silent);
                if (cleanup != 0) NCryptFreeObject(key);
                key = 0;
            }
            else if (persisted && provider != 0)
            {
                cleanup = NCryptOpenKey(provider, out var reopened, name, 0, Machine | Silent);
                if (cleanup == 0)
                {
                    cleanup = NCryptDeleteKey(reopened, Silent);
                    if (cleanup != 0) NCryptFreeObject(reopened);
                }
            }
            if (provider != 0) NCryptFreeObject(provider);
            if (cleanup != 0) throw new InvalidOperationException($"Probe-key cleanup failed: 0x{cleanup:X8}; restore the disposable checkpoint.");
        }
    }

    [StructLayout(LayoutKind.Sequential)] struct NcryptBuffer { public uint Size, Type; public nint Data; }
    [StructLayout(LayoutKind.Sequential)] struct NcryptBufferDesc { public uint Version, Count; public nint Buffers; }
    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)] static extern uint NCryptOpenStorageProvider(out nint provider, string name, uint flags);
    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)] static extern uint NCryptImportKey(nint provider, nint importKey, string type, ref NcryptBufferDesc parameters, out nint key, byte[] data, uint length, uint flags);
    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)] static extern uint NCryptSetProperty(nint key, string property, byte[] data, uint length, uint flags);
    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)] static extern uint NCryptGetProperty(nint key, string property, byte[]? data, uint length, out uint written, uint flags);
    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)] static extern uint NCryptOpenKey(nint provider, out nint key, string name, uint legacySpec, uint flags);
    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)] static extern uint NCryptExportKey(nint key, nint exportKey, string type, nint parameters, byte[]? data, uint length, out uint written, uint flags);
    [DllImport("ncrypt.dll")] static extern uint NCryptFinalizeKey(nint key, uint flags);
    [DllImport("ncrypt.dll")] static extern uint NCryptDeleteKey(nint key, uint flags);
    [DllImport("ncrypt.dll")] static extern uint NCryptFreeObject(nint handle);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool LogonUser(string user, string domain, string password, uint type, uint provider, out SafeAccessTokenHandle token);
}
