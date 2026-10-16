# Diagrams

Flyback's [C4 model](https://c4model.com) is drawn in Mermaid (ADR-0204), one view
to a page, and GitHub draws each one inline. A view is a flowchart in C4's notation:
people, software systems, containers, components and stores, each in its own color.

| View | Level | Shows |
|---|---|---|
| [`context.md`](context.md) | C1, system context | Who uses Flyback, and what it talks to |
| [`desktop.md`](desktop.md) | C2, containers | The editor, flyback-viewer and flyback-cli, and the folders they keep |
| [`web.md`](web.md) | C2, containers | The web editor, the web viewer and the Android editor |
| [`site.md`](site.md) | C2, containers | The preset site's Worker and stores, and how a submission is checked |
| [`editor.md`](editor.md) | C3, components | The projects the editor is built from, and the plugin contract between them |

The CI pipeline is [`pipeline.md`](pipeline.md), drawn the same way.

An element that appears in several views is written in each; a rename is made in
every page that names it. A component named for a project is that project:
`ComponentDiagramTests` fails when the editor's components or the arrows between them
disagree with the project files.
