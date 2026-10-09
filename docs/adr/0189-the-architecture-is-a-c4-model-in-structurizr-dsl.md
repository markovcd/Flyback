# ADR-0189: The architecture is a C4 model in Structurizr DSL

**Status:** Accepted · 2026-10-09 · *user-directed*

## Context

The only diagrams were two Graphviz drawings of the desktop editor's classes,
drawn for one refactor and stale once it landed. Nothing showed Flyback from the
outside: who uses it, and which systems it talks to. A diagram here has to diff
like code, and an agent has to be able to redraw and check it with no window.

## Decision

**The architecture is a C4 model in `docs/diagrams/workspace.dsl`, in Structurizr
DSL.** Elements and relationships are written once, and each view is a selection
from them, so a rename is one line and a diff reads as a change to the
architecture rather than to a drawing.

**`scripts/diagrams.sh` draws it, through Docker only.** Structurizr validates the
model and exports PlantUML; PlantUML lays it out to SVG. Both images are pinned by
digest, so an unchanged model redraws byte for byte. The SVGs are committed beside
the model.

**The gate does not draw diagrams.** A stale picture is a documentation fault, not
a broken build, and the gate image stays free of Java.

Declined:

- **Mermaid's C4 diagrams**: GitHub renders them inline, but each is drawn on its
  own with no model behind it, and the support is experimental.
- **C4-PlantUML by hand**: one file per view, with each element repeated in every
  view it appears in.
- **LikeC4**: a model too, but a Node toolchain and a package in the repo, for
  output Structurizr already gives.

## Consequences

- C1, the system context, is the first view. Containers and components are added
  to the same model as they are drawn.
- Structurizr's current tooling exports no DOT, so the layout is PlantUML's.
