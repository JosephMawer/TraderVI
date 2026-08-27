# ADR-0036: Native read-only project documentation reader

- **Status:** Accepted
- **Date:** 2026-08-27
- **Domains:** architecture
- **Related:** ADR-0017, ADR-0033, ADR-0035

## Context

The immediate problem is that TraderVI's architecture, operating instructions,
ADRs, glossary, roadmap, concepts, and status are spread across Markdown files
that must be opened outside the desktop shell. The parent problem is making the
system's design and operating knowledge easy to consult while using its
operator workspace. The root goal is informed operation without adding another
state-changing workflow or weakening the safety boundaries around trading,
market data, models, and SQL.

The reader must preserve Markdown's value as repository-native, reviewable
source. It must not edit files, call an external service merely to display a
document, or turn arbitrary links into an unsafe route outside the repository.

## Decision

Add a **Project Docs** tab to `TraderVI.WPF` as a polished, read-only Markdown
reader.

### Confirmed direction

- Display Markdown attractively inside `TraderVI.WPF`.
- Make project documentation easy to browse and read.
- Include Markdown under `Docs` and other useful repository Markdown files.
- Keep the entire surface read-only.

### Accepted first-slice defaults

1. Discover repository Markdown and present it in a searchable navigation tree
   grouped by folder. Search document titles, repository-relative paths, and
   contents.
2. Open `Docs/project-status.md` by default and provide Refresh so files edited
   outside TraderVI can be re-enumerated and reloaded without restarting.
3. Exclude `.git`, `.vs`, `bin`, `obj`, `packages`, and `node_modules` directory
   segments from discovery.
4. Render headings, paragraphs, emphasis, inline code, fenced code, lists,
   checklists, blockquotes, tables, horizontal rules, and links with native WPF
   `FlowDocument` elements. Do not add an embedded browser or an external
   Markdown package for this bounded feature set.
5. Resolve relative Markdown links only inside the discovered repository root.
   Navigate document fragments and same-document headings inside the tab.
   Reject absolute filesystem paths, traversal outside the root, and links to
   undiscovered local files.
6. Treat `http` and `https` links as external. Open one only in the system
   browser after the operator explicitly clicks that link. Passive rendering,
   filtering, refresh, and document selection never open a web page.
7. Keep file discovery, filtering, heading identifiers, and link resolution in
   host-neutral Core code so their safety behavior can be tested without WPF.
   Keep `FlowDocument` construction and shell interaction in `TraderVI.WPF`.

## Alternatives considered

- **Embedded browser plus rendered HTML.** Rejected because it adds a larger
  runtime and security surface for a local, read-only documentation feature.
- **External Markdown package.** Rejected for the first slice because the
  required subset is bounded and a dependency would not remove the need for
  repository-safe link handling and WPF styling.
- **Plain-text viewer.** Rejected because it preserves source but makes long
  operating and design documents unnecessarily difficult to scan.
- **Docs-only catalog.** Rejected because root instructions and useful Markdown
  colocated with code are part of the project's operating knowledge.

## Consequences

- Operators can consult design and running guidance without leaving the tabbed
  shell, while Markdown remains the single editable source of truth.
- The parser is deliberately a supported Markdown subset, not a claim of full
  CommonMark conformance. Unsupported syntax remains readable as text.
- Local navigation is constrained by both canonical root checks and catalog
  membership. Newly added documents appear after Refresh.
- No SQL migration, model artifact, market request, or database operation is
  introduced.

## Review questions

1. Which reader behaviors live in Core, and which remain WPF presentation?
2. Why must a relative link pass both repository-boundary and catalog checks?
3. What user action is required before an external web link can open?
