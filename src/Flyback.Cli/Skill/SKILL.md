---
name: flyback
description: Use when asked to build, change, look at or check a Flyback patch (.fbk, .fbks or .fbkb) - driving Flyback's own patch tools through flyback-cli tool, one call at a time, with undo.
---

# Building Flyback patches

Flyback is a patchable synthesizer: one graph of modules makes both a picture and a sound. `flyback-cli tool` gives you the same tools Flyback's own assistant has. Each command is one tool call on a patch file: it reads the file, runs the call, and writes the patch back if the call changed it.

## Before the first call

Read the briefing once. It is the conventions, every module and what each socket is for, and the presets to learn from. Nothing else tells you these, so do not guess module names.

```sh
flyback-cli tool --briefing
flyback-cli tool --list          # every tool and its --args; --json for the schemas
```

## Calling a tool

```sh
flyback-cli tool <name> <patch> --args '<json object>'
```

- `describe_patch` first, on a patch that already exists: it lists every module by handle, what each input is set to or wired from, and what the compiler says. Edits name modules by these handles.
- `write_patch` builds a whole patch from Flyback's patch language. Use it for a new patch. To change one, use `set_knobs`, `connect`, `add_module` and the rest, so the modules somebody placed stay where they are.
- `render` draws the picture to a PNG and prints where it is (`--picture <file>` says where). Open it and look before saying a picture is right.
- `measure` says what an output carries: levels, pitch, how it moves. Use it on the sound, which you cannot hear.
- `propose` checks the patch compiles cleanly and something reaches the Output. Run it before saying a patch is finished.

A refused call exits 1 and says why on standard error; read it and try again. `--json` prints one object with `ok`, `text`, `wrote`, `step` and any `picture` or `sound` path.

## Undo

Every call that changes the patch is one edit, kept in Flyback's data folder, not beside the patch.

```sh
flyback-cli tool history <patch>       # each edit, with its tool and arguments
flyback-cli tool undo <patch>          # take the last edit back
flyback-cli tool undo <patch> --to 3   # back to after edit 3; --to 0 is before any
flyback-cli tool redo <patch>
```

Your own checkpoints do not cover these files: undo through these commands. A patch changed by anything else starts its history again at the next edit.

## Checking the patch from outside

- `flyback-cli check <patch>` compiles it and names the line of anything wrong.
- `flyback-cli render <patch> -o clip.mp4 --seconds 4` records it, picture and sound.

## Over MCP instead

`flyback-cli mcp <patch>` serves these tools over MCP on a patch file, and `flyback-cli mcp --editor` on the patch open in Flyback, once its Settings → Assistant lets a program edit it. There each edit is one the person can undo in the editor, and `say` puts what you tell them in its assistant column.
