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

## High-pressure scanning across the message lifecycle

The maintainer clarified that "real time" means high concurrent activity across received mail,
outbound mail, queued delivery, drafts and intermediate states, not a promise of a particular
messages-per-second rate. Support thousands of messages in flight without unbounded resource use.
Sustained rate, message sizes and latency budgets still require measured capacity planning.

### Coverage and verdict boundaries

- Cover incoming SMTP, authenticated submission, queued/retried delivery, external POP3 fetching,
  IMAP APPEND (including server-saved drafts and Sent folders), and supported import/restore paths.
  Audit every path that creates or changes stored message bytes before enabling the feature.
- Client-only unsaved/local drafts are invisible to the server. Scan when those bytes reach it.
  A server-saved draft edit creates a new content version requiring a new verdict; do not reuse
  a verdict from its previous version.
- Keep unscanned, pending, error, infected and unscannable content separate from confirmed clean
  content. Do not release pending/infected content for SMTP delivery, normal IMAP/POP3 retrieval,
  attachment download or automatic forwarding. Define protocol-compatible staging and completion
  semantics for IMAP APPEND and other ingestion paths before implementation; do not expose a
  message first and scan it later. Show pending/error state without exposing unsafe content.
- Perform recipient-policy checks and MIME transformations in a documented order. Bind verdicts
  to immutable bytes, content version and effective scan policy; changed bytes invalidate the
  verdict. Immediately before delivery, enforce that the current version has an acceptable verdict.
  Do not repeatedly rescan unchanged bytes at every internal hop just to label them "real time".
- If verdict caching is introduced, key it by cryptographic content identity, engine/signature
  version, relevant policy and expiry. Define refresh rules after engine/signature/policy updates,
  and a resumable, rate-limited rescan for existing mail/imports. Cache hits must not bypass a
  changed policy, missing payload checks or an earlier unscannable/error outcome.

### Scheduling, overload and recovery

- Use a durable scan-work queue linked to durable message state, separate from web requests and
  SMTP connection threads, with a bounded worker pool. Acknowledge accepted mail only after its
  required durable state exists. Persist and recover pending/in-progress work after crashes.
- Bound memory, staging/spool bytes, worker/process count, per-message processing time, extraction
  depth/expanded bytes, attachment count and retries. Do not launch one unconstrained process per
  arriving email or attachment. A persistent orchestration worker does not establish that Defender
  itself has a persistent or parallel scanning API; prove the chosen engine interface's behavior.
- Apply backpressure at high-water marks, reserving capacity for recovery and avoiding disk-full
  acceptance. Use temporary SMTP failure before acceptance when necessary; already accepted mail
  must remain durably held for bounded retry and operator recovery, never silently dropped or
  sent unscanned. Specify distinct actions for clean, infected, timeout, unavailable and unscannable.
- Schedule fairly across users/domains and message sizes so archive-heavy traffic, draft edits,
  bulk imports and background rescans cannot starve new mail. Prevent duplicate work for the same
  immutable version where safe; use leases and idempotent state transitions to prevent duplicate
  delivery, conflicting verdicts or repeated mitigation after worker failure.
- Coordinate quarantine/attachment-removal actions with the message store and delivery pipeline.
  Preserve consistent database/file state; avoid destructive engine actions against live storage.
  Limit quarantine growth, restrict access and provide audited retention/recovery controls.
- The existing synchronous `VirusScanner` path has an intended ten-scan concurrency ceiling.
  It is not evidence that thousands of in-flight messages are supported. Do not simply raise that
  ceiling or promise capacity based on CPU count. Select worker counts from measured engine,
  disk, memory and mail-protocol performance, including Defender contention and platform updates.
- Separate scheduler/provider contracts so another verified scanner can be selected if local
  Defender cannot meet the required load. Any multi-host scanning design requires its own secure
  transport, message privacy, deployment and failure assessment; it is not delivered here.

### Capacity evidence and operator controls

Publish benchmark hardware/VM allocation, OS and Defender versions/signatures, configuration,
message/attachment size distribution, archive mix, concurrency, sustained arrival rate, burst
size and test duration. Include at least a 10,000-message in-flight/backlog scenario, mixed
receive/send/IMAP-draft activity, steady load, bursts, prolonged engine slowdown/outage, restart
recovery and a long soak. This is an acceptance scenario, not a supported-capacity claim.

Record messages/sec, bytes/sec and payload scans/sec separately; queue wait and scan duration
p50/p95/p99; oldest pending age, backlog bytes/count, CPU/RAM/disk usage, error/timeout/unscannable
rates, recovery drain time and impact on SMTP/IMAP/POP3 responsiveness. Count complete message
verdicts, not just individual file scans. Use [Microsoft's performance analyzer](https://learn.microsoft.com/en-us/defender-endpoint/tune-performance-defender-antivirus)
to help locate engine scan costs; whole-pipeline measurements remain necessary.

Size capacity from measured sustained service rate above arrival rate with explicit headroom.
For example, 1,000 messages/minute is about 16.7 messages/second; with three scanned payloads per
message it creates about 50 payload scans/second before retries. This arithmetic is illustrative,
not a Defender benchmark. For stable rates, backlog drain time is approximately backlog divided
by (measured completion rate minus continuing arrival rate); if that difference is nonpositive,
adding queue space does not solve sustained overload.

Expose queue depth/bytes/age, health, effective limits, throughput, latency and overload/expiry
alerts in the panel. Set numeric throughput and latency acceptance targets from the agreed load
profile and benchmark evidence before production approval. Require bounded resources, zero lost
accepted messages, no duplicate deliveries and no release without the required verdict under
fault injection. If Defender falls short, report the tested envelope and select a proven engine
or revised deployment; do not advertise unmeasured "real-time" capacity.

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
