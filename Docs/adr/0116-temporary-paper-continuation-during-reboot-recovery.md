# ADR-0116: Temporary Paper continuation during reboot recovery

- **Status:** Implemented and tested; installation pending
- **Date:** October 29, 2026
- **Domains:** architecture, data-pipeline
- **Related:** ADR-0103, ADR-0104, ADR-0115

## Context

A laptop reboot raised a new global startup-continuity hold immediately after completed Paper
verification and clearance. The parent problem is that a full historical audit blocks current
simulation for hours. The root goal is continued Paper observation without pretending that
unverified history is clean or granting financial authority. Joseph explicitly asks to remove
the holds temporarily instead of waiting for another multi-hour recovery.

## Decision

Provide an explicit, temporary **Paper continuation**, bounded to at most 24 hours and the
current engine process. This narrowly supersedes ADR-0103/0104's operational Paper block for
an operator-selected reboot incident and its full-recovery pending incident. It does not resolve
either incident, change archive evidence, qualify historical performance, or enable Real trading.

The native `ResumePaperPendingRecovery` control requires a reason, explicit UTC expiry, recovery
run ID and both incident IDs. Activation requires normal engine mode, Real execution Off,
verified current SQL/local continuity and immutable guards, the same database identity as the
selected full recovery, and exactly two global incidents: EngineRecoveryRequired with a valid
startup continuity key predating the recovery boundary, and that recovery's pending incident.
The recovery must be Running or Complete. Unknown incidents, active continuity loss, failed
proof, guard failure and changed identity cannot receive this permission.

Admission compares the complete original incident records on every read/write check. Any added,
removed or changed incident ends the exception; expiry, clock rollback and engine restart also
end it. Normal access still works after genuine native clearance. The exception allows Paper
workers and explicitly opted-in shared market inputs only. Broker/manual order/automatic
protection checks, research processing and generic global checks retain their original blocks.
Paper current-state decoding, checkpoints, append validation, immutable guards, record identities,
freshness, source conflicts, cash/risk and trading rules remain unchanged.

The private control receipt records the permission and its exact incident evidence. IntegrityStatus
and engine notices disclose temporary Paper continuation, expiry and unverified history. The
permission is intentionally not a persistent grant and cannot silently renew across a restart.
The planned first operational use expires at the October 29 session close; source installation
and operational activation must be recorded separately.

Background recovery keeps its original plan and cursor. Clearing still requires completed proof,
exact current heads, database identity and immutable guards. New simulation/shared writes may
advance those heads, preventing clearance of the original boundary; this is a disclosed limitation,
not authority to bypass the proof. A permanent bounded restart/recovery design remains separate.
Do not promote results produced with incomplete integrity coverage as verified research evidence.

## Alternatives

Deleting incident/resolution rows or fabricating a successful recovery is rejected. Ignoring all
holds would hide actual corruption and affect Real authority. An indefinite global configuration
switch would outlive the temporary request. Reusing pre-reboot proof as if it covered the new
interruption is not supported by the existing recovery contract.

## Review questions

1. Why can Paper resume while the original incidents remain unresolved?
2. Which new conditions immediately stop the temporary continuation?
3. Why does this permission neither qualify research nor enable Real trading?
