# ADR-0221: A group may hold one module

**Status:** Accepted · 2026-11-02 · *user-directed*

## Context

A group needed two modules or more. Ctrl+G on one module was declined, a group
worn down to one by a delete or a move stopped existing, and a `group` block in the
text that placed one module was left out with a warning. The reason given was that a
box round one module shows the same sockets the module does. Arek found the rule
got in the way when live patching.

## Decision

- **A group holds one module or more.** `Patch.Group` refuses only a selection with
  nothing groupable in it, and a group goes only once it is empty.
- **Ctrl+G and the panel's group button work on one module**, as on several.
- **A `group` block round one module draws its box.** A block that places no module
  is still left out with the `group-too-small` warning.
- **A def call that makes one module draws no box**, as before. The call's box is
  drawn without being asked for, and round one module it would only add a name.
