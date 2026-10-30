# Guided certificate storage and installation design

Status: **2026-10-30 implementation proposal with a VM feasibility result**, not delivered
certificate installation. [Requirements](webadmin-requirements.md#certificate-setup-and-lifecycle)
own the product obligations; [the ledger](phase-1b-status.md) owns A06/E03/E07 acceptance.
The implemented [REST contract](../hmailserver/source/Management/README.md) currently provides
configured transport inventory and bounded PFX inspection only. No import/assign/renew API,
Windows-store-backed native TLS adapter or production installer provisioner exists yet.

## Preferred storage direction

Use Windows machine keys in the **Microsoft Software Key Storage Provider**, with a native
adapter for OpenSSL. Keep existing manually configured PEM certificates working during
migration. A browser supplies certificate material and expected hostnames, never filesystem
paths, arbitrary key-container names, security descriptors or provider settings.

This is a software, exportable key design: native OpenSSL currently consumes a private-key
object and does not automatically use a Windows key handle. The adapter would export bounded
PKCS#8 into owned memory during context construction, parse/check it with OpenSSL and clear
the owned transfer bytes. It must not write an application plaintext-key file or return key
bytes through ordinary management responses. Do not describe this as HSM/non-exportable key
support or claim that every library/OS internal copy is erased.

Windows owns key persistence and access control. Use project-generated unique key names,
machine scope, silent operation and no overwrite flag. Before finalization, set the deliberate
export policy and a protected DACL for SYSTEM, administrators and the native service SID.
Reopen and verify the effective DACL, key identity and export behavior before registration.
The unprivileged web identity receives no mail-key access. Verify that the actual service token
can use its SID; a successful elevated probe does not establish service/IIS provisioning.

The [NCryptImportKey contract](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptimportkey)
supports naming PKCS#8 imports and delaying finalization to set properties.
[NCryptSetProperty](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptsetproperty)
defines security-descriptor and export-policy properties. Our VM result supports this direction
for the tested provider/algorithms; it does not establish every Windows/provider combination.

The alternative is encrypted managed key files. Existing SecretProtector.Protect can fall back
to legacy Blowfish without an installation key, so it cannot simply be reused for certificate
keys. An AES-only file implementation would also need physical-file re-protection when importing
an installation key, plus portable backup/recovery. The machine-key direction avoids coupling
new certificate keys to that installation-key import, while still requiring its own portable
encrypted export and tested cross-machine restoration.

## Feasibility evidence and limits

The initial disposable Windows Server 2025 probe passed **50 assertions** for RSA-2048 and
ECDSA-P256: named machine PKCS#8 import; export/DACL properties before finalization; protected
three-principal DACL after reopen; matching public keys after private export; and denial under
a temporary unprivileged local user's impersonation token. Generated keys and the user were
deleted, then the original checkpoint was restored. No certificate/root trust store, mail
registration, listener or service configuration was changed.

Repeatable source and the VM-only runner are in the
[probe directory](agents/tools/certificate-key-store-probe/README.md). The final tracked runner
adds an explicit disposable-VM marker, bounded child-process wait and parameterized VM selection.
Its exact run also passed 50 assertions with exit 0 in local ignored
`out/phase1b-cng-storage-final-vm.log`; the original checkpoint was restored and the temporary
checkpoint removed. The earlier result is `out/phase1b-cng-storage-ready-vm.log`. These are not part
of the application's 1685 CI assertion count or production acceptance. The C# probe exercises
Windows API contracts; it is not the production C++ adapter.

Still unproved: native OpenSSL parsing/handshakes, service-token and IIS access, other algorithms/
curves/providers, Windows 10/Server 2016 and the supported-platform matrix, restarts/outages,
certificate associations/chain serving, backup/restore, throughput and the installation workflow.
The first two fixture attempts failed before key testing on PowerShell 5.1 RNG API compatibility
and a local-user description length limit; corrected before the successful run. Their checkpoints
were restored and they are not successful test evidence. A later runner reported all 50
checks but rejected an unavailable PowerShell Start-Process exit code; the final runner owns
a .NET Process directly and checks its actual exit status. That rejected run is not accepted.

## Transfer and native authority

The existing native frame cap is 64 KiB. PFX inspection has a route-specific 1 MiB JSON cap,
512 KiB PFX limit and bounded loader work; that exception does not increase native frame limits.
Do not send unbounded PFX/KDF work into the mail service or widen every command's limit.

Proposed transfer: the restricted web process prepares bounded leaf/chain DER and PKCS#8, then
uses an explicit native upload operation with small bounded chunks. Native-generated opaque
upload IDs bind to the actor and current credential stamp, require server-admin authorization
on every operation, have fixed expiry and aggregate memory/concurrency limits, and are cancelled/
cleared on failure, revocation or expiry. No resumable secret material belongs in an ordinary
cookie/session ticket. Secret-owning DTOs and wire buffers need clearing on every error path;
existing password cleanup does not automatically cover private-key chunks.

Before any persistent effect, the native side must independently parse the complete bounded
material, compare the private/public key, validate host/SAN, time, key strength, leaf/CA,
EKU/key usage and chain structure, and apply explicit self-signed/internal-CA policy. Web-host
trust inspection is advisory. Never automatically install a supplied root as trusted.
Exact transfer fields/limits and error contracts remain to be implemented and tested, then
published in the REST/native contract; the prose above is not an available endpoint.

## Installation, persistence and recovery

Installation is a durable native job, not a sequence of unrelated browser writes:

1. Authorize and validate; allocate a server-owned operation ID and persist a protected journal
   containing bounded operation metadata/ownership. Keep private input out of the journal and
   secrets out of logs, URLs and diagnostics.
2. Import the unique protected key and verify it on an owned worker, outside the Windows service
   control/start callback (the NCrypt contracts warn about service-start deadlock). Persist the public chain and certificate/key
   association under a fixed managed location/reference with protected ownership. Preserve
   intermediates without making bundled roots trusted. The public-chain representation and
   journal persistence format still need a concrete implementation/migration decision.
3. Register through checked native persistence with one leased transaction, no automatic retry
   of an ambiguous insert/commit, full-width IDs and exact readback before publication. The
   existing generic persistence success boolean is insufficient proof of recoverable import.
4. Reconcile after acknowledgement loss/restart using the operation-owned locator and actual
   database/key state. An uncertain result must not delete a possibly registered or active key.
   Detect duplicate/conflicting registrations and surface an actionable repair state.
5. Preview affected listeners and validate bind/protocol/security/certificate dependencies.
   Apply only with explicit interruption acknowledgement and retained rollback material. Probe
   actual SMTP/IMAP/POP3 implicit TLS and STARTTLS behavior; distinguish configured and active
   state. Retain the previous key until references/active use and rollback retention permit cleanup.

The native certificate/listener ID-width correction has a reproduced Compact VM regression;
other legacy objects and COM long properties are separate migration work. It does not implement
these transaction, journal, high-ID persisted-import or listener-activation guarantees.

Backup/export must produce portable passphrase-protected private material and the full chain,
with role/confirmation/size controls and no ordinary key read endpoint. Test restoration to a
different machine and service identity, new container names, listener references and failed/
interrupted recovery. Deleting the local machine key without a portable copy can prevent recovery;
the wizard and retention policy must make the recovery choice clear before destructive actions.

## Issuance, hosting and acceptance

ACME issuance/renewal should use the same native storage, reconciliation and activation path.
Select and document the client/provider/challenge and secret-handling model before delivery;
do not expose unrestricted shell commands or add another private-key persistence mechanism.
Test renewal outages, changed keys/chains, old-key retention, expiry notices and rollback.

Keep web HTTPS binding independent of mail listeners. Define the IIS/HTTP.sys or service-host
identity and certificate association explicitly; do not grant a web pool access to mail keys
just to make installation succeed. Fresh/offline install, upgrade, identity changes, uninstall,
rollback and inaccessible old PHP URLs remain E07/E08 obligations.

Before declaring E03 complete: native/API/browser ownership and malformed-input tests; bounded
transfer cleanup and revoked actors; key/chain/host mismatch and usage tests; effective ACL and
service identity tests; four-backend write/commit failure recovery; crash/orphan reconciliation;
real listener and independent web handshakes; portable backup; ACME/renewal; supported Windows
matrix; manual wizard/error/recovery review; and documented operator procedures. The standalone
probe can inform these tests, but cannot substitute for them.
