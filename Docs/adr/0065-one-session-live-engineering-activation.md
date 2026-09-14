---
cssclasses:
  - executive-brief
---
# ADR-0065 — One-session Delphi Live engineering activation

- Date: 2026-09-08
- Status: Applied at 14:50 Toronto; runtime correction and two full portfolio cycles verified by 15:36
- Parent: [Delphi Live V1](0053-delphi-live-v1.md)

> [!summary] Decision in brief
> Start the authorized September 8 Live paper trial using the recovered daily candidate list. Preserve its failed daily eligibility and label the exception so the test can exercise collection and portfolio processing without making the original recommendations appear approved.
>
> **Interpretation:** the late start and runtime gaps exclude this session from clean research evidence. Activation does not guarantee a qualifying trade.

| Question | Answer |
|---|---|
| What is admitted? | The 38-stock union of published candidates, tied to this exact trial and source. |
| What stays active? | Fresh signal confirmation, position and cash limits, protective exits and the buying cutoff. |
| What was verified by 15:36? | Two complete portfolio cycles after a narrowly scoped eligibility correction; no Live fill at that checkpoint. |

**Design consequence:** retain the source failures and real gaps in every interpretation of this trial. The record below captures activation and its 15:36 verification checkpoint, rather than end-of-session performance.

## Problem and goal

The operator requested Delphi Live today with CAD 1,000 simulated capital, after
seeing the Shadow engineering test work. Normal activation begins at the next
session, and today's recovered official daily run was created after 09:30 with
no eligible published candidates. The immediate goal is to exercise current Live
collection and paper execution. The broader goal is reliable, auditable session
handling without presenting an intervention as ordinary strategy evidence.

## Decision

Migration 031 admits only the explicitly authorized September 8 trial
`c6d4639d-9c06-4c61-bde3-d76a6fdf1908`. It creates one CAD 1,000 cash-only Operational
Champion and freezes the 38-stock union of the recovered report's 25 published
rows per lens. All fifty original Hold/Breadth, eligibility, ranking and trace
facts are copied unchanged. The original calibration run and Shadow records
are preserved. No recommendation or fill is fabricated.

The trial record and nullable foreign-key references distinguish the exceptional
generation, session and candidate-lens rows. SQL constraints permit only this
trial/date/source and capital. The session uses `OperatorEngineeringTest`, real
authorization/freeze timestamps, the real 09:30 session boundary, and a mandatory
host-gap flag. Normal activation APIs and daily source selection are unchanged.

The existing WPF host discovers the new assignment on its next tick. Installed Core types produce the initial ledger
and daily baselines; no model training or external acquisition is performed by
the preparation helper. The already-running Live engine makes its ordinary
authorized market requests after admission.

Initial collection exposed a second source-eligibility guard in decision-dossier
validation: evaluations saved, but portfolio candidate-state processing failed on
the honestly retained ineligible Breadth rows. The runtime correction annotates
this exact session/run/strategy with the trial identity in derived facts, and
admits only its published, ranked Breadth failures. Other dates, sources and gates
remain rejected. Original entry provenance remains readable for later protection
of any carried test position. This does not change signal scoring or fill rules.

The app was rebuilt/restarted at 15:11 (PID 13920) with the tested Core library.
The old process's lease expired normally at 15:24:56; the replacement acquired
fence 6 at 15:25. No lease was forcibly released. The additional gap is retained
and fresh confirmation must restart; entries may not mature before 15:45.

Fresh observations must rebuild momentum/confirmation. Missing opening intervals
remain gaps; late-start VWAP limitations, source quality, causal quotes/fills,
sizing, position limits, buying cutoff, loss holds and protective exits remain.
The coverage gap excludes today from clean engineering or promotion evidence.
This is not a forced-buy operation and may end with no Live fills.

The account remains enabled for ordinary future-session selection. Its initial
trial provenance and any carried financial effects remain visible in saved
evidence; this is not a clean comparison with an unmodified strategy. No deposits,
withdrawals, broker execution or Trading strategy approval are introduced.

## Verification and recovery

The isolated helper build and SQL/SSDT Release build passed. A serializable
rehearsal rolled back schema and data, matched fourteen protected-table hashes,
round-tripped the cash-only ledger, and obtained 39 valid daily baselines.
Five negative checks rejected removing the trial links, removing the host gap,
and moving the trial to another date. All constraints remained enabled/trusted.

Application requires the reviewed migration/preparation hashes and a fresh
checksum-verified backup with a hash-matched secondary copy. The transaction
fences the current engine cycle. After commit, verify the existing process's
lease, real collection receipts and evaluations; a created account alone does
not establish successful operation. Also verify advancing portfolio revision,
candidate states and completed portfolio cycles; fresh receipts can coexist with
a later failure in the same workflow, as this activation demonstrated.

All 344 focused Delphi Live tests passed, including three added admission tests
covering preserved failed facts, rejection outside the admitted session/source/gate,
and carried-position provenance. The test build has zero errors and existing
compiler warnings. Separately, cached dependency advisories remain for SqlClient,
DirectoryServices.Protocols, Drawing.Common and Cryptography.Xml; no fresh package
audit or full solution build was performed. The SQL/SSDT Release build passed.

On a runtime problem, pause Live new buys through its normal control and retain
holdings, protective exits, immutable history and diagnostics. Do not delete
history, backdate facts or restore an entire backup over later trading activity.
Evidence is under ignored `artifacts/live-engineering-20260908/`.

Migration SHA-256: `137DD26F0ED0E7804DFC558C07486CF855220EE1DF55CA7B2CDA671D28169B35`.
Application committed at 18:50:38Z after matching the same fourteen protected
tables and initial-ledger checks. The current WPF process acquired one Live lease
and entered Monitoring by 18:50:57Z, with the expected late-start coverage gap.

At 19:36Z, fence 6 remains stable on PID 13920. The corrected host completed both
19:25 and 19:30 bar-end portfolio cycles: each settled 39 slots, with 36 usable
receipts and three missing completed bars. All 38 candidate states persisted;
ledger revision reached 8 with three marks and a matching last completed cycle.
The requested cash is intact, with no Live actions, positions or fills. The first
35 dossiers with usable stock evidence carry the trial marker. This verifies
collection, evaluation and portfolio persistence; it does not verify a Live fill.
