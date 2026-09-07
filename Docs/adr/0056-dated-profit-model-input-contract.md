# ADR-0056: Dated profit-model input contract

- **Status:** Accepted; operationally adopted on 2026-09-06 through ADR-0057
- **Date:** 2026-09-06
- **Domains:** architecture, machine-learning, data-pipeline, decision-engine
- **Related:** ADR-0019, ADR-0020, ADR-0022, ADR-0041, ADR-0042, ADR-0055

## Context

The immediate problem is that Delphi's daily ML predictions do not receive the same dated inputs as
training. The parent problem is trustworthy evidence for an identifiable complete strategy. The root goal
is dependable, human-approved real-account recommendations before separately authorized broker execution.

Source inspection confirmed audit F2: Hercules supplies XIU to `EnhancedFeatureBuilder`, but Delphi's
registry loader did not. Two existing stock-versus-XIU return features could consequently become zero at
prediction time. The original market-window filter also allowed differing stock and XIU session sequences.
The initial source review did not inspect private models. The later authorized artifact review could not
prove their training-input contract, so separate replacements were trained; see the
[cutover review](../reviews/model-input-cutover-20260906.md).

The operator accepted excluding only affected stocks, stopping the corrected set for unusable shared XIU
input, and requiring proof of compatibility before reusing artifacts. After discussing the distinction
between ML inputs and deterministic trading rules, the operator authorized implementing the correction.
The initial implementation excluded operational inspection, training, registration and activation. The
operator subsequently authorized those specific steps, including replacement training when compatibility
could not be established. ADR-0057 records that authorization and the completed selection mechanism.

## Decision

1. **One dated input contract.** `ProfitInputs.XiuDatedV1` uses the same feature calculation path for
   training windows and prediction inputs. Preserve the existing active tasks, feature formulas/order,
   vector dimensions, labels, thresholds, role weights and gates. The current Enhanced and ATR feature
   families are supported explicitly; adding a family requires reviewing its input dependencies.
   Enhanced's two relative-return features remain distinct from database relative-strength ranking and
   the planned integration of that ranking into ML. Deterministic rules are not new training targets.
2. **Match required sessions exactly.** Copy XIU facts into an immutable per-run context. Each task uses
   its existing lookback (55 sessions for BreakoutEnhanced, 30 for the other three active profit tasks).
   Each required stock session must have exactly one valid OHLCV bar and match the canonical XIU date.
   Stock prices must be finite and positive with consistent high/low bounds; volume must be nonnegative.
   Required XIU closes must be finite and positive, with exactly one bar per required date. Never shorten
   a return period or substitute zero for a required missing input. Return vectors must be finite.
3. **Preserve the canonical-session source.** As in ADR-0041, production uses observed XIU dates as its
   canonical sequence. Corrected Delphi selects the latest observed XIU session strictly before the
   recommendation date and rejects stock histories ending elsewhere. Future XIU observations do not
   enter that snapshot. This does **not** independently verify the exchange calendar: a session absent
   from XIU and its derived sequence together, or a stale shared latest date, remains a data-quality risk.
   Tests can supply an independent canonical sequence to prove missing required-row rejection.
4. **Contain stock failures; reject shared failures.** Validate the shared model-input window before
   loading models or optional operational writes. A bad required XIU window stops the corrected daily
   set with a reason. Validate every active task's stock inputs before scoring; omit only the affected
   stock, record its reason/session and count, and continue with complete stocks. Training skips invalid
   input windows and reports reasons/counts. Historical outcome-definition dispatch is separate F8 work.
5. **Require explicit reviewed model identity.** The corrected workflow requires a newly registered
   strategy whose `DecisionRef` is `ADR-0056`, plus a JSON compatibility binding naming that exact
   strategy GUID and `ProfitInputs.XiuDatedV1`. The record contains a review reference, reviewer, UTC
   review time and exactly one model ID/hash per active task. It binds the registry schema/feature name,
   lookback, horizon, kind and both signal thresholds. Runtime selects those IDs, checks metadata and
   validates the hash against the exact bytes supplied to ML.NET. A corrected strategy without its
   binding fails; a dated candidate cannot load through the legacy path. Matching vector length or
   filling in a JSON record is not proof of compatibility: the cited review must actually establish it.
6. **Keep source staging separate from operational adoption.** The initial source phase used
   `Delphi --model-input-binding <path>` and the shared workflow's `ModelInputBindingPath`. ADR-0057
   now stores the reviewed assignment with the strategy, so nightly CLI and WPF resolve it automatically;
   an optional file cannot override that stored assignment. The initial `v3.3-dated-profit-inputs` and
   current ADR-0058 `v3.4-observed-benchmarks` both use this corrected ML path with the same model set.
   A pre-existing unbound strategy retains its prior inputs, reported as
   `ProfitInputs.LegacyUnverified`; this fallback is not compatibility approval. Reusing an old strategy
   row by rewriting its decision identity is prohibited.
7. **Training saves candidates without selecting them.** Hercules now uses the dated builder by
   default; `--dated-candidates` remains an optional alias. Candidate files use a unique run directory
   and create-new semantics; Hercules registers those rows disabled and records the dated feature name.
   All profit-artifact saves reject overwriting an existing file. ADR-0057 adds the immutable evidence,
   predecessor preservation, explicit selection transaction and rollback identity. Training remains a
   separately authorized operation and cannot select its results automatically.
8. **Separate new evidence.** Successful corrected-run context records the input contract, complete
   binding and stock exclusions. Diagnostic and human reports show the contract and omissions. F3
   provenance records the exact loaded bytes in both paths. No historical cohort, label, fill or model
   registry row is rewritten by this source change.

## Consequences and validation

Correction can change predictions, even with identical model bytes. It therefore needs new strategy
evidence and the existing family-specific review requirements. Frozen Delphi Live V1 behavior and account
ownership are unchanged. This compatibility record is an input safeguard, not the common strategy
promotion/selection mechanism required by ADR-0055. ADR-0057 supplies the model manifest and selection
boundary; broader policy versioning and common strategy comparison remain separate work.

Isolated tests exercise the actual training-window and prediction-input methods for all four active tasks,
known nonzero 10/20-session relative returns, stable feature order, future data, duplicate boundary dates,
stock-only failure, shared XIU rejection, reporting, registry drift, strategy identity and rejection of
unreviewed synthetic bytes before ML deserialization. No model fitting or private artifact is used.
Build/test results and remaining work are in the [implementation checklist](../strategy-alignment-implementation-checklist.md).

The authorized operational checks subsequently verified replacement training, stored assignments,
selection and actual Delphi bootstrap loading. The next normal scheduled run will use the corrected
strategy; no full nightly pipeline was launched for validation. The cutover review distinguishes these
operational results from the isolated tests and retains the XIU calendar/freshness limitation above.
