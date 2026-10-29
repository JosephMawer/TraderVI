# ADR-0112: Read documentation and architecture on GitHub

- **Status:** Accepted
- **Date:** 2026-10-28
- **Domains:** architecture, user-interface
- **Supersedes:** [ADR-0036](0036-native-project-documentation-reader.md)

## Decision and rationale

Remove the desktop **Project Docs** and **Architecture** sections and their dedicated code.
Read and maintain the documentation in the repository on GitHub. Preserve the Architecture view's
four maps, component explanations, relationships, source references and dated qualification limits
in the existing [architecture guide](../architecture-view.md), using Markdown and Mermaid diagrams.

The immediate problem is redundant documentation UI now that the user reads the repository on GitHub.
The parent problem is maintaining a custom reader and an embedded guide that require application
updates. The root goal is accessible, reviewable explanations while keeping the desktop focused on
operational evidence, controls and account workflows. The user explicitly accepted removing both views.

## Consequences and boundaries

- Remove reader-only Core discovery/link helpers, WPF views/view model/renderer, embedded architecture
  catalog, and their dedicated tests and offline preview/check host. Preserve shared navigation/styles.
- Documentation remains repository source. The guide retains the October 28 13:45 Toronto checkpoint
  and historical September 30 AI limits; migration does not renew operational verification.
- Preserve dated original UI installation evidence in project status and the guide's historical section.
- Existing operational screens and trading behavior remain unchanged. No replacement browser or new
  documentation service is introduced. Updating the docs no longer requires an application rebuild.
- Local source removal and validation do not install a runtime, change financial permission, or publish
  to GitHub. A push and runtime installation remain separately authorized operations.

Keeping an in-app link or reduced reader was unnecessary: direct repository access already satisfies
the user's workflow. Retaining the embedded architecture catalog would keep a duplicate maintenance path.

## Review questions

1. Where do architecture explanations live, and where do current operational facts belong?
2. Why does migrating a dated guide neither verify a runtime nor renew execution authority?
