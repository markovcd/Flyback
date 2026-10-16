# ADR-0204: The C4 views are Mermaid flowcharts

**Status:** Accepted · 2026-10-16 · *user-directed* · supersedes
[0189](0189-the-architecture-is-a-c4-model-in-structurizr-dsl.md)

## Context

[0189](0189-the-architecture-is-a-c4-model-in-structurizr-dsl.md) kept the C4 model in
Structurizr DSL and drew it to committed SVGs through two pinned Docker images,
Structurizr and PlantUML. Redrawing needed Docker and a script, a diff of a view was a
diff of an SVG nobody reads, and the pipeline diagram beside it was already Mermaid.
The user asked for the C4 diagrams in Mermaid, with the dependency gone.

## Decision

**Each C4 view is a page in `docs/diagrams/`, holding one Mermaid diagram.** GitHub
draws it inline, so nothing is rendered or committed beside it, and there is no script,
image or package to keep.

**A view is a `flowchart` in C4's notation, not Mermaid's `C4Context` diagrams.** Each
element carries its name, its kind and its description, and a class gives each kind
C4's color: people, software systems, containers, components, stores as cylinders,
external systems in grey, and the plugin contract in orange. Mermaid's C4 diagrams
place elements in rows by the order they are written and route every arrow straight
across them; at Flyback's size the labels overprint. A flowchart is laid out by dagre.

## Consequences

- The five views (C1, three C2s, the editor's C3) are `context.md`, `desktop.md`,
  `web.md`, `site.md` and `editor.md`.
- There is no model behind the views: an element in several views is written in each,
  and a rename touches every page that names it.
- `ComponentDiagramTests` reads `editor.md`, so the C3 view still cannot drift from
  the project references.
- Nothing checks that a view parses; a broken one shows as an error where GitHub draws it.
