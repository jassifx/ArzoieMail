using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ArzoieMail.Management;

public interface ITxtResolver
{
    Task<string[]> QueryAsync(string name, CancellationToken token);
}

// Use the administrator's Windows DNS configuration, not a public resolver or telemetry service.
public sealed class WindowsTxtResolver : ITxtResolver
{
    private static readonly SemaphoreSlim Outstanding = new(4);
    public async Task<string[]> QueryAsync(string name, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new ManagementError(503, "Windows x64 DNS resolver is required.");
        await Outstanding.WaitAsync(token);
        // DnsQuery is synchronous. Bound outstanding OS calls even when an HTTP request times out.
        var work = Task.Run(() =>
        {
            try { return Query(name); }
            finally { Outstanding.Release(); }
        }, CancellationToken.None);
        return await work.WaitAsync(token);
    }
    private static string[] Query(string name)
    {
        int status = DnsQuery(name, 16, 0, IntPtr.Zero, out IntPtr records, IntPtr.Zero);
        if (status is 9003 or 9501) return [];
        if (status != 0) throw new Win32Exception(status);
        var result = new List<string>();
        try
        {
            for (IntPtr current = records; current != IntPtr.Zero; current = Marshal.ReadIntPtr(current))
            {
                // DNS_RECORDW header is 32 bytes on x64. TXT union holds a count and pointer array.
                if (Marshal.ReadInt16(current, 16) != 16) continue;
                int count = Marshal.ReadInt32(current, 32);
                if (count is < 0 or > 256) throw new ManagementError(503, "Invalid DNS resolver response.");
                var chunks = new string[count];
                for (int i = 0; i < count; ++i)
                    chunks[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(current, 40 + i * IntPtr.Size)) ?? "";
                result.Add(string.Concat(chunks));
            }
            return result.ToArray();
        }
        finally { if (records != IntPtr.Zero) DnsRecordListFree(records, 1); }
    }
    [DllImport("dnsapi.dll", EntryPoint = "DnsQuery_W", CharSet = CharSet.Unicode)]
    private static extern int DnsQuery(string name, ushort type, uint options, IntPtr extra, out IntPtr records, IntPtr reserved);
    [DllImport("dnsapi.dll")]
    private static extern void DnsRecordListFree(IntPtr records, int freeType);
}
