# Showing a def called inside a def

Planned on 2026-10-13, on `main` at `d1632f1`. It is on TODO.md; take it off there, and
delete this file, in the commit that lands it.

- **Kind:** Plan
- **Status:** Open, parked; no option chosen

## What is wanted

Each outermost def call is drawn as a shut group (ADR-0065's amendment of 2026-10-13).
A def called inside another def's body has no box of its own: its modules are loose in
the outer call's box. The user wants the inner calls visible as structure inside an open
box, without nesting real groups.

## Ruled out

- **Real nested groups.** A box inside a box inside a box: wires cross several edges, and
  finding a module means remembering which boxes to open. `NodeGroup.Members` is modules
  only, and nesting would change the saved format and the painter.
- **A faint dashed outline per inner call, always open.** Declined by the user.
- **Opening a box into a view of its own**, with a breadcrumb back out. Too drastic.
- **Highlighting on hover over a module.** Distracts in ordinary patching, and there is
  no hover or Alt on a touchscreen.

## The constraint

`PatchLayout` moves a group whole, but inside a group places each module in the column
its wires ask for. An inner call's modules can land far apart, so anything drawn round
them (an outline, a hull, a shaded patch) cuts across modules that are not in it unless
the layout first keeps each inner call together.

## Options still open

1. **Grouping by distance.** The layout lays each inner call out as a block inside its
   group, the way it already lays a group out inside the patch, with a wider gap between
   calls. Nothing is drawn. Needed by every option that draws round modules.
2. **Layered shading.** On top of 1: a faint backdrop behind each call's modules, one
   shade deeper per level.
3. **Colored wires.** A wire running inside a call takes the call's color; one crossing
   its edge stays neutral.
4. **A name chip.** The call's name in the module's header.
5. **Stripes.** A thin stripe per level down each module's left edge, colored by the def,
   outermost on the left; past three levels the two innermost and a notch.
6. **The path in the inspector.** A selected module's inspector header reads
   `voice › env › shape`; tapping a level selects that call's modules. Works the same
   with a mouse and a finger.
7. **A structure panel.** The calls as an outline tree beside the canvas; tapping an
   entry selects its modules.

Recommended last: 1 with 7, shading in reserve for crowded patches. Before that, 5 with
6. Interactive mockups of 5 and of 1 with 7 were shown in the project thread; none is
in the repo.

## What it would take

- The binder records each inner call's path and members (it already knows them in
  `Binder.Expand`; only the outermost reaches `Boxes`). Where they are kept decides
  whether they survive a save: on `NodeGroup` they are saved data, and printing drops
  them either way, since a printed def comes back as a `group` block.
- Option 1 is the bulk of the work, in `PatchLayout`.
- Options 6 and 7 are drivable by an agent: both are a list of calls and members that a
  command can print.
