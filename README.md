# TraderVI

TraderVI is a TSX momentum research and trading system. Its goal is to compare complete
strategies in independent paper portfolios, deliberately approve a strategy for the real TFSA,
and manage that account under explicit risk and execution controls.

A strategy includes selection, entry, exit, sizing, risk and data requirements. A model is
one possible input. A paper result, recommendation or approval is not an actual broker fill.

## Start here

- **Understand the system:** the map below, then [Paper and Real](Docs/system-design.md#paper-and-real-share-rules-not-account-state).
- **See what is true now:** the opening [status summary](Docs/project-status.md), with dated evidence.
- **Choose or resume work:** the relevant [roadmap](Docs/roadmap.md) entry and its owning checklist.
  Use the [session register](Docs/session-work-register.md) only to recover a chat or unfinished idea.
- **Change or operate something:** [agent rules](AGENTS.md), the task-specific
  [behavior rules](Docs/design-rules.md) and [operating procedure](Docs/running.md).

Read the sections needed for the task. Older milestones inside the large reference files are
dated history; they do not override newer evidence. Their consolidation is still in progress.

## System map

```text
Daily market data -> Hermes -> SQL market history
                                 |       |
                                 |       +-> Hercules -> candidate trained models
                                 |                        -> reviewed model selection
                                 +------------------------------+
                                                                v
                                                      Delphi daily selection
                                                                |
Intraday market data -> shared inputs in the independent engine <-+
                          |                      |
                          v                      v
                    Paper portfolios      Real approved strategy
                    paper trade history   actual account + own confirmation
                          |                      |
                    research evidence     risk / permission / price checks
                                                 |
                                          broker order -> fill -> reconciliation

Dashboard <-> engine: reviews, controls and saved-state inspection
```

Hermes collects daily market history. Hercules trains candidate models; training does not
automatically select them. Delphi combines selected models with deterministic indicators and
gates to produce daily candidates. Intraday rules evaluate fresh observations separately.

The independent engine owns background collection, Real execution, Paper processing and audit
schedules. Closing the dashboard leaves it running; stopping the engine is a separate action.
SQL stores market facts and durable trading/research records; protected local journals and
continuity records also participate in recovery. The dashboard is the operator's review and
control surface. Scheduled nightly work has separate preparation and validation procedures.

**Evidence boundary:** installation and automated tests do not prove a complete live trading
lifecycle. The [October 23 closing report](Docs/reviews/independent-engine-implementation-20261022.md#october-23-final-1605-watch-report)
records no first attributable Delphi fill. The [October 26 assessment](Docs/reviews/independent-engine-implementation-20261022.md#october-26-readiness-assessment--october-23)
preserves unfinished data/quote qualification; it grants no operational or financial authority.

## Where information belongs

| Need | Authoritative home |
|---|---|
| Component responsibilities and relationships | [System design](Docs/system-design.md) |
| Dated implementation, installation and operational state | [Project status](Docs/project-status.md) |
| Priority order | [Roadmap](Docs/roadmap.md) |
| Acceptance criteria and remaining dependencies | The task's checklist, including [strategy alignment](Docs/strategy-alignment-implementation-checklist.md) |
| Behavior, risk constraints and decision rationale | [Design rules](Docs/design-rules.md), applicable [ADRs](Docs/adr/README.md) |
| Commands, prerequisites and side effects | [Running guide](Docs/running.md), linked Operations instructions |
| Detailed evidence, unresolved choices and historical context | Linked reviews, [open questions](Docs/reviews/open-questions.md), dated archives |

Update a second document only when its own meaning or navigation changes. The
[consolidation review](Docs/reviews/documentation-consolidation-20261023.md) records the initial
inventory, remaining cleanup and workflow examples. It is a bounded review, not another backlog.
