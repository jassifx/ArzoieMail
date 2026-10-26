# Microsoft Defender Antivirus email scanning

## Assessment (2026-10-26)

**Feasible in principle; optional integration approved as a requirement, implementation pending.**
This assessment combines repository inspection and Microsoft's documentation. No Defender commands,
policy changes or malware-test files were executed on the development host. End-to-end feasibility,
service permissions and performance still need validation in the disposable VM.

The product here is local Microsoft Defender Antivirus, not a dependency on the separate
Microsoft Defender for Office 365 service. Do not promise identical email-security capabilities.

## Existing integration points

`Server/Common/AntiVirus/VirusScanner.cpp` scans the message file and then decoded attachments.
`CustomVirusScanner.cpp` launches an external executable but maps only one configured exit code
to a detection. It is a possible prototype hook, not the proposed production Defender adapter:
indeterminate outcomes need explicit handling throughout the delivery pipeline.

## Supported mechanism and prerequisites

Microsoft documents targeted custom scans through `MpCmdRun.exe`, including a detection-only
mode, and recommends locating the current platform executable instead of assuming PATH.
Its default exit codes combine several outcomes: zero can include successful remediation;
two can indicate an unremediated detection or an error. Therefore a single numeric comparison
cannot reliably classify results. Detection-only scans report detections in command output;
the adapter must validate available result signals on supported platforms and fail as an
error if classification is ambiguous. See [Microsoft's command-line reference](https://learn.microsoft.com/en-us/defender-endpoint/command-line-arguments-microsoft-defender-antivirus).

Defender must be installed and usable under the selected service identity. Windows Server
availability and active/passive/disabled state depend on installation and organization-managed
security configuration. Detect these states and explain incompatibilities; do not force a mode
change or override another security product. See [Microsoft's Windows Server guidance](https://learn.microsoft.com/en-us/defender-endpoint/microsoft-defender-antivirus-on-windows-server).

Email-format and archive inspection depend on scanning capabilities and policy. Preserve decoded
attachment scanning rather than assuming a raw message scan inspects every payload. Encrypted,
unsupported or limit-exceeded content must remain visibly unscanned. See [Microsoft's scanning options](https://learn.microsoft.com/en-us/defender-endpoint/configure-advanced-scan-types-microsoft-defender-antivirus).

## Proposed delivery design

- Add a dedicated native scanner adapter and server-admin-only REST configuration/status/test
  operations. Do not accept arbitrary commands, executables or filesystem paths from the browser.
- Select the installed trusted Microsoft executable, launch with safely constructed arguments
  without a shell, and use protected temporary copies. Prefer detection-only custom scans so the
  server controls message handling. This does not disable independent real-time protection;
  handle files being removed or locked without silently marking them clean.
- Return distinct clean, infected, error and unscannable results. Require a confirmed clean result
  for clean classification. Preserve detection details without logging message contents.
- Expose detection actions separately from scan-error actions. Default errors to bounded hold/retry,
  with visible operator intervention at exhaustion. Do not invent SMTP rejection after acceptance:
  define inbound rejection timing and already-queued message handling before implementation.
- Bound wall-clock duration, process count, attachment size and extraction work. Test cancellation
  and cleanup. Launching a process for each payload may limit throughput; benchmark before choosing
  defaults or claiming suitability for high-volume servers.
- Show installation, engine/signature health, effective scanning availability, last result/error
  and performance diagnostics. Retain other scanner choices and document multi-engine ordering.
- Keep web hosting unprivileged. Establish the minimum scan-service privileges in VM testing;
  any elevated helper must expose only narrow authenticated operations. Leave global security,
  cloud-protection, sample-submission and exclusion policy under system-administrator control.

## Acceptance before implementation is considered complete

Run all executable validation in the disposable Hyper-V VM or CI. Start with harmless clean data
and the standard EICAR antivirus test string; do not use live malware. Use disposable fixtures
outside production mail storage and restore the VM after tests.

Cover raw messages, base64/encoded attachments, archives, encrypted/unscannable content, confirmed
detections, clean files, disabled/missing engine, stale signatures, permission failures, ambiguous
results, localized output, timeouts and Defender platform updates. Verify service-identity access,
real-time scanner interference, bounded concurrency, throughput and no loss/corruption of queued
messages. Test panel/native authorization and each configured detection/error policy.

Production enablement remains gated on these results and regression coverage; adding the
requirement does not enable Defender scanning in the current build.
