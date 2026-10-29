using System.Buffers.Binary;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace ArzoieMail.Management;

public sealed class ManagementError(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public interface IManagementChannel
{
    Task<JsonElement> SendAsync(string operation, string username, string password,
        object? arguments, CancellationToken cancellationToken);
}

// The web process has no COM, database, or mail-store access. The native service owns authorization.
public sealed class ManagementChannel : IManagementChannel
{
    public const string PipeName = "ArzoieMail.Management.v1";
    public const int MaximumFrame = 65536;

    public async Task<JsonElement> SendAsync(string operation, string username, string password,
        object? arguments, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new ManagementError(503, "The management service requires Windows.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(timeout.Token);
        VerifyServer(pipe.SafePipeHandle);
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, operation, username, password, arguments = arguments ?? new { } });
        try
        {
            if (body.Length > MaximumFrame) throw new ManagementError(413, "Request is too large.");
            byte[] size = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(size, body.Length);
            await pipe.WriteAsync(size, timeout.Token);
            await pipe.WriteAsync(body, timeout.Token);
            await pipe.FlushAsync(timeout.Token);
            await pipe.ReadExactlyAsync(size, timeout.Token);
            int length = BinaryPrimitives.ReadInt32LittleEndian(size);
            if (length is < 2 or > MaximumFrame) throw new SecurityException("Invalid management frame.");
            byte[] response = new byte[length];
            await pipe.ReadExactlyAsync(response, timeout.Token);
            // Acknowledge receipt before the server disconnects its byte-mode pipe.
            // DisconnectNamedPipe may otherwise discard a response that has not been consumed.
            await pipe.WriteAsync(new byte[] { 6 }, timeout.Token);
            await pipe.FlushAsync(timeout.Token);
            using var document = JsonDocument.Parse(response, new JsonDocumentOptions { MaxDepth = 16 });
            int status = document.RootElement.GetProperty("status").GetInt32();
            if (status != 200)
                throw new ManagementError(status is >= 400 and <= 599 ? status : 503,
                    status switch { 401 => "Incorrect username or password.", 403 => "Access denied.",
                        404 => "Resource not found.", 409 => "The operation conflicts with the current state. Reload and try again; your own account or domain cannot be deleted.",
                        412 => "Checked updates require a transactional database. MySQL mail databases require InnoDB tables; migrate with the service stopped, then restart.",
                        422 => "Invalid settings, duplicate address/name, or domain limits exceeded. Review the fields and try again.", _ => "Management operation failed." });
            return document.RootElement.GetProperty("data").Clone();
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(body); }
    }

    // Defeat pipe-name squatting: the peer must be the currently running SCM mail service.
    private static void VerifyServer(SafePipeHandle pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe, out uint pipePid)) throw new Win32Exception();
        using var manager = OpenSCManager(null, null, 1);
        if (manager.IsInvalid) throw new Win32Exception();
        using var service = OpenService(manager, "hMailServer", 4);
        if (service.IsInvalid) throw new Win32Exception();
        if (!QueryServiceStatusEx(service, 0, out ServiceStatus status, Marshal.SizeOf<ServiceStatus>(), out _))
            throw new Win32Exception();
        if (status.State != 4 || status.ProcessId == 0 || status.ProcessId != pipePid)
            throw new SecurityException("Management endpoint is not the mail service.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint Type, State, Controls, Win32ExitCode, ServiceExitCode, Checkpoint, WaitHint, ProcessId, Flags;
    }
    private sealed class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ServiceHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenService(ServiceHandle manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(ServiceHandle service, int level, out ServiceStatus status, int size, out int needed);
    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
