# Windows machine-key storage feasibility probe

Research utility for the [certificate storage proposal](../../../certificate-storage-design.md).
It is not shipped with ArzoieMail, invoked by ordinary CI, or a production certificate importer.
Its assertions do not validate native OpenSSL, IIS, backup, renewal or listener activation.

Compile on the development host; run only in the authorized disposable Hyper-V VM. The VM
must be running, have .NET 10 and an hMailServer service SID, and allow the supplied VM
administrator to create a temporary local user and machine keys. The runner refuses an existing
ArzoieMail-phase1b-* checkpoint. It restores/removes only its own temporary checkpoint.

```powershell
dotnet publish docs/agents/tools/certificate-key-store-probe/Probe.csproj -c Release -r win-x64 --self-contained false --artifacts-path out/certificate-key-store-probe-artifacts -o out/certificate-key-store-probe
powershell.exe -NoProfile -ExecutionPolicy Bypass -File docs/agents/tools/certificate-key-store-probe/Invoke-DisposableVmProbe.ps1 -VMName Windows-Server-2025 -PasswordFile <path-to-authorized-password-file>
```

The password file is authorized local input, never committed or printed. For another authorized
disposable environment, specify its VM name, credential-file path and optionally -UserName.
Do not point this utility at a development host or an installation that must retain test changes.

The runner creates a protected scratch directory and temporary ordinary user, passes the generated
proof through the child environment, and marks that process as disposable-VM execution. The
marker prevents accidental direct execution; it is not a security boundary. The child has a
two-minute timeout and produces only algorithm/operation/status/assertion output. No private
keys or passwords are captured in evidence. It checks RSA-2048 and ECDSA-P256 named PKCS#8
imports, pre-finalization DACL/export policy, protected ACL readback, reopen/export key identity,
and denied access under the ordinary user's token. Explicit cleanup deletes generated keys,
removes the user and clears the process environment; checkpoint restoration covers failed runs.

If checkpoint restoration fails, retain the reported temporary checkpoint and restore it before
further VM work. Never delete Ready, Ready (public DNS), or another session's checkpoint.
