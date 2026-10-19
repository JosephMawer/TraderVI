# Delphi Live: checkpoint for the operator's Codex restart

**Resumed October 18 at 23:43 Toronto:** the operator restarted Codex and explicitly resumed this
task. Both installed helpers now run without an approval request and exit 0; SQL hash/scalar and
broker read success are checked. The earlier pause/instructions below remain historical checkpoint
evidence. Current progress is in the [morning record](delphi-live-morning-decisions-20261019.md).

**October 18, 2026, Toronto.** The operator's newer instruction in “Explain recurring sandbox
issues” requests this checkpoint and a pause for restarting Codex. That human instruction was
verified directly in the source chat. Do not resume implementation until the operator resumes.
Do not restart Codex or TraderVI automatically.

## Saved work

- `9bdacb8`: exact Normal preview and performance/readiness assessment.
- `76f79af8`: tested Core Live source/protection, original-ID opening recovery and conditional
  first-day foundations. Last complete validation: 179 focused tests; 2,501 full Core tests pass,
  eight native skips, isolated Release WPF build passes with 262 existing compiler warnings.
  These results precede the new host draft. Existing cached Pkcs advisories remain separate;
  no fresh dependency audit or full solution/SSDT claim.
- `7ba0160`: the separate chat's installed broker-read helper, following SQL helper `23bd680`.
- Ready tracking/index changes from the previous increment are reconciled for a documentation
  checkpoint. [Morning decisions](delphi-live-morning-decisions-20261019.md) records decisions made
  on the operator's behalf and remaining work.

## Unfinished work preserved in the checkout

- `Core/Trader/Promotion/BrokerExecutionHost.cs`: new default-off host draft. Exact approved Normal
  SQL assignment/build grant, saved stopped mode, regular-session checks, non-overlapping poll and
  bounded failure backoff. It delegates orders to the existing managed workflow.
- `Core/Trader/Promotion/BrokerExecutionHostGrantFileStore.cs`: protected atomic host-permission
  store with expected-current comparison. Neither file has host-specific unit tests yet; neither is
  committed as a finished feature, integrated into WPF, installed, enabled or operationally verified.
- The first draft build found three `Core.Math` namespace-resolution errors. The mechanical fix
  uses `System.Math`; the checkpoint Core build now passes with zero errors/260 existing compiler
  warnings. Log: ignored `artifacts/delphi-live-tfsa-20261018/host-checkpoint-build.log`. No host tests,
  fresh WPF build, native checks or full suite were run for this new draft.
- No desktop coordinator, activation/review UI or opening-to-baseline composition was added yet.
  Start by testing the draft host, then connect it to the existing broker timer/shared journal/gate.
  Never manufacture an approved SQL assignment or native launch qualification from a host grant.
- Preserve the unrelated `Docs/.obsidian/workspace.json` modification.

## First action after restart and explicit resume

Read `AGENTS.md`, [SQL helper procedure](../../Operations/ReadOnlyChecks/README.md) and
[broker helper procedure](../../Operations/BrokerReadOnlyChecks/README.md). Verify actual prompt-free
execution, not just policy matching or prior mutable NativeRead/sqlcmd success.

1. Coordinate the one SQL mailbox before replacing `artifacts/read-only-db/request.sql`. Submit
   UTF-8 `SELECT 1 AS HelperSmokeCheck;`. Run this exact standalone command, with no added arguments,
   chaining or redirection. Verify receipt `QuerySha256` against the submitted bytes before accepting
   the returned result:

   ```powershell
   & 'C:\Users\joseph.mawer\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.exe' -NoProfile -File 'C:\Users\joseph.mawer\.codex\tools\tradervi-read-only-db\v2\Invoke.ps1'
   ```

2. Run the bounded broker helper as its exact standalone command:

   ```powershell
   & 'C:\Users\joseph.mawer\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.exe' -NoProfile -File 'C:\Users\joseph.mawer\.codex\tools\tradervi-read-only-broker\v1\Invoke.ps1'
   ```

For **each** command separately record whether an approval prompt occurred, exit status and read
success. Authentication failure is separate from rule loading. If the exact launcher still prompts,
report failed prompt-free verification; do not widen rules or use an unrestricted fallback. No
token refresh, credential write, SQL mutation or broker order is authorized by this check. Keep
account identifiers, symbols, amounts, tokens and private snapshots out of the handoff.

Only after recording both results, resume the authorized software task, keeping actual activation
off. Prepare a built working copy with explicit operator controls; keep tests/build and native
verification separate. Native risk/storage/order/source qualifications remain unfinished.

## Overnight continuation

Automation `delphi-live-morning-preparation`, target this chat
`01a14f7b-ec59-73d0-9f56-bd3652d2b5de`, is **PAUSED** at the operator's restart request.
Its prior schedule was hourly through **08:00 Toronto, October 19**. The full existing prompt is
preserved in the app. After explicit resume, its prior overnight software-preparation authorization
can resume before that cutoff; do not let it restart work before the operator resumes. It authorizes
no real financial activation, order queue, trade, transfer or risk reset.
