# ADR-0210: The assistant edits the patch's text

**Status:** Accepted · 2026-10-18 · *user-directed* · rests on
[0065](0065-a-text-language-that-parses-to-a-patch.md) and
[0067](0067-a-module-keeps-its-name-and-its-memory-across-a-rebuild.md); changes the
tools [0202](0202-a-coding-assistant-the-person-runs-edits-the-open-patch-over-mcp.md)
serves; implemented in `src/Flyback.Plugins/Assist/WholePatchEdits.cs` and
`KeptModules.cs`

## Context

The workbench had a tool for each kind of change: `add_module`, `set_knobs`,
`connect`, `disconnect`, `remove_module`, `switch_module`, `set_steps`, `set_scale`,
`set_arrangement`, `set_extra`, `set_sample`, `set_picture`, `set_keyboard` and
`set_length`. Each existed because the thing it set once had no other way in. The
text language now writes every one of them, and `describe_patch` already answered in
it. Rewriting the whole patch with `write_patch` to change one knob was the other
road, and a measured Gemini run took it: about 2.5k output tokens for "make it
blue", every module renamed.

Two things stood in the way. An assistant's edit to a patch the text owned was
printed back into the document, losing the person's comments and `def`s. And a
patch rebuilt from text was a set of strangers to the one it replaced wherever the
text was a printing.

## Decision

**One `edit_patch` replaces the fourteen.** It takes `{find, replace}` pairs against
the patch's text, each found exactly once, applied in order, and adopts the result
only if every piece is found and the whole reads. It answers with the changed lines
and what the compiler says.

**The text is the person's own where there is one.** The workbench carries the text
the patch is written in: the document's while the text owns the patch and nothing
typed waits to be applied, a `.fbks` file's for `flyback-cli tool` and `ask`, and
otherwise a printing with every module bound to its handle. What the assistant
edited goes back into the document as it is. A setting carried in from the canvas
between turns is not in that text, so the next edit works on a printing.

**A module the text still names is the same module.** After a write or an edit, a
module bound to a handle the patch already had, of the same type, takes the old id,
and the Output always does.

## Consequences

- Twenty-four tools become eleven, and the per-module code goes with them.
- An edit costs the lines it touches, not the whole patch.
- A model has to quote the text exactly; the refusal says which piece missed and why.
- Canvas positions are laid out afresh by every assistant edit, as before.

## Amendment, 2026-10-20: an extra no longer reports itself

The per-module tools were the only readers of `NodeExtra.Report` and `ExtraField.Format`,
the prose an extra wrote about what an instance held. The patch's text says the same, so
both leave the plugin contract, with their overrides. The contract stays at 1.0.0: no
plugin outside this repo exists yet.
