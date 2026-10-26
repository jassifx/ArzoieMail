---
title: "Antivirus integrations and Defender plan"
slug: reference_antivirus
parent: book_configuration
index: 0
is_book: false
---

## Implemented integrations

ArzoieMail retains ClamAV, ClamWin and external command-line scanner integrations. Configure and
validate the selected engine independently; its presence on Windows does not prove that the
mail pipeline is using it. Native scanning reads the message file and decoded attachments.

The desktop settings expose engine configuration/testing, maximum scan size, detection actions
and attachment blocking. A size limit can cause a message to be skipped; it is not a clean verdict.
Review the engine's actual result/error behavior and the existing adapter's supported return-code
mapping before relying on a custom command. Do not assume arbitrary command-line products are
interchangeable or suitable for high concurrency. Old AVG/Norton command examples are historical.

## Microsoft Defender Antivirus - planned

Optional local Defender scanning and mitigation are accepted requirements, not an implemented
feature. A dedicated native adapter must distinguish clean, infected, error and unscannable
results. It must cover receive/send queues, external fetch, server-saved drafts/IMAP APPEND,
imports and changed content; client-local drafts are invisible until upload.

Required work includes durable bounded scheduling, verdict gates before normal release,
backpressure, fair workers, safe mitigation/recovery, admin-only panel/API controls and measured
capacity acceptance with at least 10,000 queued/in-flight messages. That test has not been run
and no supported Defender throughput has been established.

See [Defender feasibility](https://github.com/jassifx/ArzoieMail/blob/master/docs/defender-antivirus-feasibility.md)
and [requirements](https://github.com/jassifx/ArzoieMail/blob/master/docs/webadmin-requirements.md).

## Operating boundaries

Avoid engine remediation that deletes or locks live mail-store/database files outside coordinated
message handling. Do not add blanket exclusions or disable organizational security controls as
an automatic setup action. Keep detection and scanner-failure policies distinct. Test clean data
and the standard EICAR test artifact only inside the disposable VM/CI; do not run scanner probes
or test executables on the development host. Existing antivirus CI tests do not establish future
Defender support or high-pressure performance.
