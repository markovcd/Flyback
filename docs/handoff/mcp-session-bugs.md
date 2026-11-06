# The MCP assistant flow misleads the assistant and edits the person's canvas unasked

Found on 2026-11-06, on `main` at `9688c230`, in Flyback 0.12.0. It is on TODO.md; take an item off here as it lands, and delete this file, and its TODO.md item, in the commit that lands the last.

- **Severity:** Critical (items 1 to 4), Medium or Low for the rest, marked on each
- **Status:** Open

Everything below came from driving `flyback-cli mcp --editor` for an hour and a half while building a patch with the user. It was all confirmed by running; none of it was read from the code. The session's own client was a stdio script that started one `flyback-cli mcp` process per call, because `flyback` was not registered for Claude Code (item 33). Arrangement chaining is being built separately and is left out.

## Critical

1. **`reset` puts a patch from hours ago on the person's canvas.** It went back to the Plasma preset that was open when the bench was first made, not to the last accepted proposal, and the user saw Plasma open in the editor. The description gives no hint of that scope. *Fix:* reset to the person's current accepted patch, or refuse past the last accept.
2. **Edits are live on the canvas, and `propose` gates nothing.** `propose` says "Nothing you have built reaches their editor until you call this", but the user saw `reset` and then a `write_patch` land with no proposal in between. Every trial edit, four test renders included, reached their canvas. The briefing's first line ("each edit is one the person can undo there") says the opposite of `propose`. *Fix:* make the bench private until proposed, or say plainly that edits are live and drop the claim.
3. **`propose` goes through after a failed edit, with a summary that isn't true.** Seen three times. An empty `write_patch` failed and "Clears the patch." was proposed on the unchanged patch. A repeated `edit_patch` missed and was proposed anyway. The user accepted mid-edit, the next edit missed, and they were shown "output Desk at 0.9, peaks -1.2 dBFS" on a patch peaking at -0.3. *Fix:* refuse `propose` when the session's last edit failed, or when the bench is unchanged since the last proposal.
4. **A call sequence hung for more than 90 s.** `edit_patch`, `measure`, then `propose` in one process never answered, and the edit had landed. Run alone afterwards, `measure` and `propose` each answered in under a second. Probably `propose` while the previous proposal was still open; that isn't confirmed. *Fix:* find the wait; a call should never block on the person.

## The text the assistant edits changes under it (Medium)

5. **Accepting a proposal rewrites the patch text.** The editor reserializes it: `remap(0..1, 45..260)` becomes `remap(in_low: 0, out_low: 45, out_high: 260)`, a positional `mixer(kick, 0.5)` becomes `let mixer1 = kick |> mixer(in_1: _, level_1: 0.5)`, and long calls are wrapped. The next `edit_patch` written against the text it just wrote misses. The briefing promises the person's own text is kept.
6. **The rewrite drops comments.** A `# Parts: 1 pad, 2 lead, …` key above an Arrangement vanished on accept, twice. An Arrangement's parts have no names, so the comment was the only record of what part 3 is.
7. **The rewrite drops `let` aliases.** `let hat = shine.high` disappeared and the Mixer read `shine.high` directly.
8. **The rewrite drops explicit outputs and default knobs.** `hiss.white |> filter(in: _, cutoff: 1800, resonance: 0.2)` came back as `hiss |> filter(cutoff: 1800)`, and `drive(drive: 2)` as `drive()`. They're equivalent, but a reader can't see it's the white output.
9. **The rewrite reorders lines and splits groups.** `let` lines moved within a group, and the Pad group came back as two blocks. A diff before and after is mostly noise.
10. **Nothing tells the assistant what the person changed.** The briefing says a later message opens "with whatever the person changed on the canvas meanwhile". Over MCP there's no such message, and spotting a manual Echo edit took a diff of the whole patch, buried in the noise from 5 and 9.
11. **`propose` doesn't report the outcome.** There's no way to learn whether the person accepted or declined, short of polling `describe_patch`.

## Missing safety and capability (Medium)

12. **A patch can't be cleared.** `write_patch` treats `""` as a missing `source`, and `propose` refuses a patch with nothing wired to the Output.
13. **There's no scratch bench.** Trying visual variants meant edit, `render`, then `write_patch` the whole saved patch back each time. A `render` that takes a temporary edit, or a `try_patch` that leaves the bench alone, would make iteration safe.
14. **Nothing warns when the sum clips.** With a Speak at full level on a Desk channel, the master measured exactly `..1`, clamped. Neither the compiler nor `measure` said so, though the briefing asks the assistant to keep the peak under -1 dBFS.
15. **`measure` shows a pinned signal as a plain range.** A four-saw Mixer read `-2..1.968`, and 2 is the socket's edge, not the real peak. Nothing marks it.

## `measure` (Low)

16. **It can't ask for one output.** Naming a module returns every output (a Filter's three, a Noise's four), 20 to 40 lines to check one peak. Accept `module.output` handles.
17. **It can't say which notes play.** A stepped output gets range, mean and step rate only, so confirming an arp's chord meant doing arithmetic on the mean. List the distinct values held, with note names on note outputs.
18. **It doesn't know `let` aliases.** `measure(["hat"])` on `let hat = shine.high` answers "no module called 'hat'", although the briefing says each module answers to its `let` name.
19. **It printed `master.left` twice** before `master.right`.

## `edit_patch`'s answer (Low)

20. **"It now reads" lists lines in edit order** (74, 79, 84, 85, 70, 71), not line order.
21. **A line two edits touched is printed twice**, before and after, both numbered 86.
22. **Glitches:** the last echoed line runs into "Those with no let name answer to: …" with no line break, and issue texts end in a double period ("…its 'left'..").

## Modules and their descriptions (Low)

24. **There's no plain way to fade in over several sections.** A Slew starts settled (25), and Fade's `from` and `to` only span 0..1 of its level. Fading a pad in over four bars took an Expression on `section + progress`.
25. **Slew starts settled.** Fed a constant from the first sample it never rises from 0, and neither the module nor the briefing says so.
26. **Noise's `drift` has no stated speed.** At `rate: 0.047` it barely moved in a minute, and nothing says how `rate` maps to how far and how fast drift wanders.
27. **A Note Sequencer's rest drops `out` to note 0** (8 Hz). That's harmless while the gate is shut, but a Slew or a long release after it sweeps down on every rest. Holding the last note may be the better rest.

## The briefing (Medium)

28. **It is about 70 KB.** The session read only its first two-thirds and missed the whole Mastering plugin, built a Drive hack, and asked for modules that already exist. A short index of modules by category at the top would have pointed straight at them. `assistant-request-cost.md` already wants a smaller briefing.
29. **`speak` wasn't in the part that was read.** It turned up only through `describe_patch` after the user added one.

## Editor (Low)

30. **The assistant panel misreports an outside MCP client.** It showed "mcpc stopped driving the editor." and then "No assistant is selected. Pick one in Settings." in success-green. An outside client is a legitimate assistant, and with a reconnect per call, "stopped driving" likely followed every call. *Fix:* show who is connected, collapse connect/disconnect churn, and draw the warning as a warning.
31. **The Arrangement panel draws seams between cells.** Cells are fractional pixels wide, so where an edge falls mid-pixel, anti-aliasing leaves a faint vertical line that moves when the panel is resized. Snap edges to whole pixels, or draw a run of equal cells as one rectangle.

## Setup (Low, maybe not Flyback)

32. **`flyback` was missing from `~/.claude.json`** although it was registered with `claude mcp add -s user` on 2026-10-19, so Claude Code had no native tools. Worth checking whether an install or update removes it. It was gone again on the same day, an hour after it was added back.

## Found on `main` at `876fa272` (Medium)

The same patch, reopened in an editor and a `flyback-cli` built from source.

33. **Loudness reads impossible values.** Where 0.12.0 read about -12 LUFS short-term, the build from `main` reads momentary +4.3 LUFS and short-term -4.1 with peaks held at -1 dBFS by the Limiter. Momentary loudness can't sit 5 dB above the peak. The means sit at the top of each range, as if the -70 floor were left out. It could be the Loudness module or how `measure` summarizes it; unconfirmed which.
34. **A source-built `flyback-cli`'s briefing lists no plugin modules.** The Effects, Mastering, Voice and other plugin sections were missing from `instructions`, although the editor loaded those plugins and the patch compiled with them. An assistant on a dev build doesn't know Echo or Limiter exist.
35. **`#` comments are refused now** (`//` starts one; `#` is a sharp). A saved patch, preset or conversation with `#` comments needs the upgrade step the `saved-data` skill asks for, if there isn't one yet.

## What worked

The language reads well, and `edit_patch` applies whole or says exactly why not (`[used-before-bound]` with line and column). `measure` was trustworthy for levels, step rates and section timing, and `render` for checking a picture. The editor adds `requires` lines itself, and on 0.12.0 the Mastering plugin's EQ, Compressor, Limiter and Loudness behaved like a mastering chain should.
