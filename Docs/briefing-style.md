---
cssclasses:
  - executive-brief
---
# TraderVI document style

Executive briefings should make the outcome and next decision clear in one minute. Keep the full evidence available below that first view.

> [!summary] Default structure
> **Conclusion → essential results → next decision → supporting detail.** Use a short opening, one compact comparison, and up to three priorities. Put technical records in expandable sections or linked reference notes.

## Writing pattern

| First view | Supporting detail |
|---|---|
| A plain-language title and dated status | Source identities, exact calculations and logs |
| The outcome and its practical implication | Mechanism, alternatives and implementation detail |
| Three or four facts that justify the conclusion | Complete evidence, checks and limitations |
| A clear recommendation or decision needed | Follow-up questions and deferred options |

Keep material limitations visible beside the conclusion. Separate observed results from explanations and proposed changes. Define unfamiliar terms when they first matter. Round money to cents and returns to two decimals in the briefing; retain full precision in the evidence.

For design decisions, lead with **problem, decision, consequence and status**. Preserve existing section names when other notes link to them. For performance reports, lead with **result, drivers and next tests**. Never sum independent portfolios into one result or present a descriptive stock basket as a replayed strategy.

Use the [executive brief template](templates/executive-brief.md) for new reports and design notes. Short factual reference notes do not need extra sections merely to fit the template.

## Obsidian presentation

The local **tradervi-briefing** CSS snippet gives this vault a slate-and-blue palette, readable headings, roomier tables and consistent callouts. It follows Obsidian's light/dark base appearance and uses installed system fonts. Notes with `cssclasses: [executive-brief]` get a slightly wider briefing layout.

The snippet is enabled in the vault's appearance settings. If an already-open Obsidian window has not picked it up, use **Settings → Appearance → CSS snippets → Reload snippets**, then enable **tradervi-briefing**. Disable that snippet to restore the previous presentation. These are Obsidian's [supported snippet controls](https://help.obsidian.md/snippets).

Use `[!summary]` for the visible conclusion and `[!note]-` for optional evidence. Obsidian supports these [expandable callouts](https://help.obsidian.md/callouts) in Reading view and Live Preview. Reading view is best for the finished brief; source mode remains ordinary Markdown. Before exporting a full report, expand its evidence sections and inspect the export.

## Private report location

Session reports belong under `Docs/reports/local/` so they are searchable inside this vault while excluded from Git. Ignoring a file does not prevent Obsidian or filesystem sync; this change does not alter sync settings. Raw data and analysis scripts remain under the existing ignored `artifacts/` directory. Link to evidence instead of copying raw payloads into shared design documents.
