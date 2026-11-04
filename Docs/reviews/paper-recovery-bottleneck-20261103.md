# Paper recovery throughput — November 3, 2026

## Finding

The running Paper recovery is dominated by legacy snapshot fingerprint calculation. A fingerprint
is the checksum of the original full saved portfolio state. This is distinct from the compact
real-account restart installed by the **Redesign engine restart recovery** chat
(`01a18def-9f8a-7c10-b18e-a05986e17746`). Its retrieved deployment and final explanation report
2.88-second real-account readiness. They do not establish replacement of Paper history verification.

The older [prefix-hash repair](../adr/0115-exact-audit-prefix-hash-reuse.md) is present in the running
stack. It reuses checksum work only before the first changed byte. The earlier native recovery with
that repair still took 2h51m; its synthetic quote-append speedups were not whole-archive promises.
The [compact restart contract](../adr/0117-compact-trading-current-storage.md) does not resolve an
existing Paper incident or turn the old Paper archive into compact current-state recovery.

## Native evidence and limits

Run `28f8f730-76ce-471f-94d2-cde1904cef0d` remains the single operator-authorized Paper recovery.
A bounded EventPipe sample attached to the existing engine for 15.43 seconds without stopping it.
The sample enabled method metadata and sampled stacks only, with no exception-message, heap,
broker-payload or trading-value collection. The engine consumed 13.86 CPU seconds during that
window, approximately 0.90 logical-core equivalents. Sampling adds some overhead; this is a short
observation, not a controlled benchmark or an estimate of completion time.

Of 6,236 samples containing resolved `Core.*` methods, 6,022 (96.6%) included
`AuditFingerprint.Verify`, and 4,979 included its `HashStream.Write`. These are inclusive sampled
stack counts, not an exact CPU-time partition; unresolved and non-Core stacks are excluded from
that denominator. Transition validation appeared in three such samples. The dominant observed
cost is checksum computation, not evidence that the audit is idle or waiting for a scheduling slot.

The installed read-only SQL helper returned hash-matched receipts:

- `7D5149498F34629041A57261E7C9E6D845A7A3489410AAC1E5AC401D3F0AD31A`: at 22:12 Toronto,
  recovery receipt hashes passed. The current group's revisions 15093/15094 used storage version 2,
  with 8,866 quotes, 1,506 marks and 22 positions in each reconstructed state. Its boundary revision
  29277 used version 3. These are aggregate counts, not exported portfolio contents.
- `92F95E791D41775D61DAB4947B89D02267552EC0B45394CBC13959143FCE2877`: revision 15094 changed
  one position item; adjacent revisions appended quote items. Version-2 canonical field ordering
  places positions before quotes, so that position change invalidates prefix reuse for the entire
  subsequent quote history. The matching collection counts alone did not mean unchanged content.

This explains a concrete remaining limit of the earlier optimization. Smaller unchanged-prefix
tails also replay bytes between the closest 512-token digest checkpoint and the first change.
The sample does not separately quantify that secondary cost. No representative new optimization
has been benchmarked, installed or claimed.

Private diagnostic source, stack trace, aggregate SQL receipts and control reads remain in ignored
`artifacts/paper-recovery-20261103` and `artifacts/read-only-db`; none are committed.

## Live constraints and outcome

Recovery has priority over ordinary audits and uses the installed maximum configured 4,096 steps
or five seconds per batch, then a one-second yield. The time budget is checked between records.
There is no exposed live control for this algorithm, batch budget or worker count. Raising process
priority does not eliminate repeated hashing and affects the same host that owns real execution.
A second verifier has no supported safe handoff into this pass's private cache or durable cursor.
No hot patch, concurrent verifier, history conversion, skipped check or priority change was attempted.

At 22:15:20 Toronto, 24,126 units were verified, the run remained Running and the audit worker had
zero failures. Only the original Paper restriction and its matching pending hold remained. Engine
PID 21140 and collector PID 17980 retained their original starts. Real execution was waiting for
regular trading hours. Conditional-clearance monitoring remains active.

There is no identified supported no-restart change that removes the measured bottleneck. Further
algorithm work would need representative legacy workloads and a later reviewed engine replacement;
avoiding this scan on ordinary Paper restart is a separate recovery-policy/integration decision.
Neither is established or installed by this investigation. Keep the existing pass and guards intact.
