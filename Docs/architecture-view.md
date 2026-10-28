# Maintaining the WPF Architecture view

The Architecture workspace explains component ownership and evidence boundaries. It is an embedded,
reviewed repository guide, never live execution status. The first version is prepared in source on
October 28, 2026; installation is a separate coordinated app update.

## Implementation and choice

The sidebar entry in `TraderVI.WPF/PaperDashboardWindow.xaml` opens
`TraderVI.WPF/Views/ArchitectureView.xaml`. It preserves **Collector & trading**, **Components**,
**Evidence** and **AI authority** from the reference interactive diagram. Cards and directed,
labelled connection rows open the same persistent component description. A connection opens its
destination. Descriptions include purpose, inputs, outputs, responsibilities, important boundaries,
dated evidence and repository references; the reference paths can be selected/copied and read in
Project Docs. No browser or external document is launched by the view.

Native WPF reuses `WorkspaceStyles.xaml`, standard tabs/buttons, vector connection arrows and a
selectable `FlowDocument`. Although the app already references WebView2, this view needs no browser
runtime, JavaScript bridge, HTML theme synchronization or new dependency. Grouped connection rows
name both endpoints instead of drawing crossing wires; they remain readable as cards reflow.
The original HTML is a design reference, not a deployed content dependency.

Cards use standard button keyboard and automation behavior. Tab reaches controls; arrow keys select
focused tabs; Enter/Space selects a card; F6 moves between the map and persistent details. The pane
has no hover timeout and selection is remembered for each tab while the view exists. Each surface
scrolls independently. At narrower widths, details move below the map; cards reflow to one, two or
three columns. Text wraps, layout uses device-independent units, and pending edges have both dashed
arrows and explicit text. The dark palette and icons follow the existing workspace.

## Content contract

| File / field | Responsibility |
|---|---|
| `TraderVI.WPF/Architecture/architecture.json` | All component prose, view/group membership, labelled connections, dates and document references |
| `components[].id` | Stable identity reused by every view; define each description once |
| `components[].kind` | Visible ownership/type label and icon category; unknown categories receive a generic icon |
| `purpose`, `inputs`, `outputs`, `responsibilities`, `boundaries` | Required plain-language explanation; keep credentials and private financial identifiers out |
| `evidence`, `references` | Explicit dated/qualification limits and repository-relative Markdown paths, optionally with heading anchors |
| `views[].groups[].components` | Ordered IDs that determine group membership and reading order; no coordinates |
| `views[].connections` | `from`, `to`, `label`, optional `pending`; both endpoints must appear in that view |
| `reviewedAsOf`, `notice` | Review date and prominent source/non-live warning; review does not renew operational evidence |
| `ArchitectureCatalog.cs` | Typed deserialization and validation, with no WPF/runtime dependencies |
| `ArchitectureView.xaml` / `.xaml.cs` | Theme, responsive presentation, selection and keyboard interaction only |

To add a component, add one full definition with a stable ID and valid references, then include its
ID in the appropriate groups and connect it. Reuse that ID across views. To add a tab, add one `views`
entry; the UI builds it automatically. Existing four required tabs are protected by a regression test.
Changing descriptions, relationships or order normally needs only JSON, not rendering code. The
catalog is an embedded resource, so an installed guide changes only with a reviewed app build.

Validation rejects duplicate IDs, missing fields, unknown endpoints and non-repository references.
Tests also verify that cited files exist. When changing an anchor, verify its heading manually;
reference existence tests check the file, not the fragment. Malformed embedded content produces a
local explanatory error instead of trying a live fallback.

## Source ownership and future changes

Update the authoritative source first. Component responsibilities belong in
[system design](system-design.md) and applicable [ADRs](adr/README.md); dated installation and
observations belong in [project status](project-status.md); acceptance remains in its owning checklist.
**When changing component responsibilities, process lifetime, data paths, evidence ownership or AI
authority, review the matching catalog definitions and connections in the same checkpoint.**
Update this document only if the maintenance or interaction contract changes.

The catalog is a concise explanatory projection of those documents, not another operational tracker.
Avoid copying volatile account details, routine monitoring observations or permission state into it.
If a dated architecture distinction changes, cite the actual evidence date and source; do not change
an old assertion to “active” just because code or documentation was edited. The first version uses
the October 28 **13:45 Toronto** operational checkpoint: installed collector and resumed five-minute
strategy, with one-minute qualification/adoption still pending. It deliberately omits private account
details. Earlier ADR implementation headers describe their own dates and do not override that later
status checkpoint. AI deployment remains explicitly unverified: the September 30 implementation
snapshot is historical, while ADR-0087 supplies the accepted authority boundary.

## Offline validation and later installation

The independent check host links only the actual Architecture control, content and workspace styles.
It does not reference Core, construct the production App/dashboard, start a service, read account
state or call a database/provider. Run it from the repository root:

```powershell
dotnet run --project Tools/ArchitectureViewChecks/ArchitectureViewChecks.csproj --artifacts-path artifacts/architecture-view/check-build
dotnet test TraderVI.Core.Tests/TraderVI.Core.Tests.csproj --filter FullyQualifiedName~ArchitectureCatalogTests
```

Screenshots go to ignored `artifacts/architecture-view/screenshots` by default. The host exercises
all tabs/cards, automation invocation/names, Enter/Space routed events, F6 handling, persistent selection,
connection targets, sidebar selection/collapse, map scrolling, adaptive layout and 96/144/192-DPI
bitmap renders. It checks the actual production navigation markup without constructing its window.

October 28 validation: eight catalog tests pass; **250** offline WPF assertions pass; all four wide
and narrow previews were generated and representative collector/AI/compact renders visually reviewed.
The serialized Core suite passes **3,369**, with **ten** opt-in native tests skipped. Isolated WPF
Release build passes with zero errors and **260 existing compiler warnings**. NuGet access required
an approved test-dependency restore; dependency audit was disabled, so this is not a fresh security
audit. No solution-wide/SSDT claim is made; no database schema changed.

Offscreen rendering and routed-key/automation checks do not qualify physical multi-monitor DPI
transitions, an actual screen reader, or keyboard focus through the running production shell. At the
later coordinated update, smoke-check sidebar access, all four tabs, keyboard focus/F6, details
scrolling and resizing on the operator's display. Use the existing normal update/build-review workflow;
an isolated build is not an installation and cannot inherit execution approval. This UI work did not
restart the app/engine, change settings, poll services, read production accounts, train models or run
database operations.
