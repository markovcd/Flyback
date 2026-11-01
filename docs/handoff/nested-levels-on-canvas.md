# Showing nested levels on the canvas

Planned on 2026-10-13, chosen on 2026-11-01 on `main` at `b2e4e24`. It is on TODO.md; take
it off there, and delete this file, in the commit that lands the last step.

- **Kind:** Plan
- **Status:** Open; design chosen, nothing built

## What is wanted

The text nests: a def inside a def, a loop or a group, a group inside a group or a loop
(ADR-0065's amendment of 2026-11-01). The canvas draws only the outermost box. A group inside
a group, a def called inside a def, and each inner call's modules are loose in the outer
box, with nothing showing which level they belong to.

## Chosen

Arek's Claude Design canvas: https://claude.ai/artifact/EYooZ2cSuPtx6XxTUidWnZ.

- **Bands.** Inside an open box, each inner level (group, def call, loop pass) is laid out as
  a band of its own, a level inside it as a band indented within it.
- **Shelf chips.** Each band has a chip on a thin rule above it. A group's chip is its name; a
  call's is the call as written, `bass = voice(A1)`; a pass's is its group name and pass,
  `Pad 1/3`. Tapping a chip selects every module in that level.
- **The path in the inspector.** A selected module's inspector reads
  `Band › Bass › bass = voice(A1) › tone(1)`, each level a button that selects it. A selected
  level shows its arguments, what is inside it, the sockets leaving it, and the other calls
  of the same def.
- **Shut boxes** list their first inner levels as chips. A loop's passes carry `1/3` badges
  and the item each pass took, `pitch = A2`.
- **Depth.** Past three chips, a level holding only one level shares its chip:
  `Bass › bass = voice(A1)`.
- **Live patching** draws chips only along the selected module's path, without rules.
  **Zoomed far out**, only the top level's chips.
- **Switching.** The banded layout lands dark behind a setting, with `shot --levels`, until
  it is good; then the setting goes and there is one layout. A patch with no inner level
  looks as it does today. Only showing the chips may stay a View toggle.

## Ruled out

- **Real nested groups.** A box inside a box: wires cross several edges, and finding a
  module means remembering which boxes to open. `NodeGroup.Members` is modules only.
- **A faint dashed outline per inner call, always shown.** Declined by the user.
- **Opening a box into a view of its own**, with a breadcrumb back out. Too drastic.
- **Highlighting on hover.** Distracts in ordinary patching, and there is no hover on touch.
- **Two layouts kept side by side for good.** Every layout fix twice, and modules jump when
  switched.

## Steps, each useful alone

1. **Record the levels.** The binder keeps each level's kind, name, path, members and, for a
   pass, its index, count and item. It knows them in `Binder.Expand`, `Box` and `Loop`, but
   only the outermost reaches `Boxes`. Where they are kept decides whether they survive a
   save; printing loses them either way. A pass's item prints plainly for a number, a note
   or a text, and needs a shape for a signal or a tuple. `flyback-cli levels <patch>` prints
   the tree with each level's modules, and `--of <module>` its path, with `--json`.
2. **The inspector's path**, and selecting a level from it. No layout change.
3. **The banded layout** in `PatchLayout`, with the shelf chips, behind the setting. Wires
   between levels cross the rules. Decide what a module dragged out of its band does.
4. **Shut boxes, live patching and zoomed far out.**

## Corrections to the design

- Chips are drawn 20 px tall; on touch each needs a 44 px tap area.
- The command is `flyback-cli levels`, and modules are named by their `let` names.
- A pass's box is named "Pad 1" in the patch, so the `1/3` badge needs the pass number and
  count kept, not read back out of the name.
