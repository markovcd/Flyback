# The MCP assistant flow misleads the assistant and edits the person's canvas unasked

Found on 2026-11-06, on `main` at `9688c230`, in Flyback 0.12.0. It is on TODO.md; take an item off here as it lands, and delete this file, and its TODO.md item, in the commit that lands the last.

- **Severity:** Critical (item 4), Medium or Low for the rest, marked on each
- **Status:** Open; items 1 to 12, 14, 16 to 18, 20 to 22, 24 to 26, 31, 33, 34 and 36 to 39 fixed

Everything below came from driving `flyback-cli mcp --editor` for an hour and a half while building a patch with the user. It was all confirmed by running; none of it was read from the code. The session's own client was a stdio script that started one `flyback-cli mcp` process per call, because `flyback` was not registered for Claude Code (item 33). Arrangement chaining is being built separately and is left out.

## Critical

1. **Fixed.** `reset` went back to the patch open when the bench was first made. It now goes back to the patch as last proposed, or as the person last opened or changed it, whichever is later; with `flyback-cli tool`, the last proposal or the file as it stood before the tools touched it.
2. **Fixed.** Over MCP in the editor the program now works on its own copy, as the column does, and only `propose` puts the patch on the canvas, as one undo step (ADR-0202's 2026-11-06 amendment). Over a patch file each edit is still written, and the tools say so.
3. **Fixed.** `propose` is refused while the last edit failed, across calls and processes; an edit that lands, or `describe_patch`, lets it through. Refusing a proposal on an unchanged bench is not done: a live proposal of an unchanged patch is harmless.
4. **Fixed.** Nothing waited on the person: `measure` with no handles was the slow call. Watching every output costs about as much again per output, so on `pentagram.fbk` (445 outputs) every module over 60 s took 87 s, and one module over the same window 12 s. A window over more than 1000 outputs times seconds is now shortened to fit, and the answer says so and asks for named modules; every module of `pentagram.fbk` measures 2.2 s in a few seconds.

## The text the assistant edits changes under it (Medium)

5. **Fixed** for a drawn patch, which `pentagram.fbk` is: the canvas keeps no text, so the bench was taken up afresh after each proposal as a printing of it. It now goes on with the text it proposed, comments, aliases and spelling as written (items 5 to 9), proven against the headless editor. Once the person changes a drawn patch by hand, the next call still reads a printing, since there is no other text of it; that call now says what changed (10). As found: **Accepting a proposal rewrites the patch text.** The editor reserializes it: `remap(0..1, 45..260)` becomes `remap(in_low: 0, out_low: 45, out_high: 260)`, a positional `mixer(kick, 0.5)` becomes `let mixer1 = kick |> mixer(in_1: _, level_1: 0.5)`, and long calls are wrapped. The next `edit_patch` written against the text it just wrote misses. The briefing promises the person's own text is kept.
6. **The rewrite drops comments.** A `# Parts: 1 pad, 2 lead, …` key above an Arrangement vanished on accept, twice. An Arrangement's parts have no names, so the comment was the only record of what part 3 is.
7. **The rewrite drops `let` aliases.** `let hat = shine.high` disappeared and the Mixer read `shine.high` directly.
8. **The rewrite drops explicit outputs and default knobs.** `hiss.white |> filter(in: _, cutoff: 1800, resonance: 0.2)` came back as `hiss |> filter(cutoff: 1800)`, and `drive(drive: 2)` as `drive()`. They're equivalent, but a reader can't see it's the white output.
9. **The rewrite reorders lines and splits groups.** `let` lines moved within a group, and the Pad group came back as two blocks. A diff before and after is mostly noise.
10. **Fixed.** The first answer after the person changed the canvas opens with what changed, knobs by handle (`tone.freq=330`), or that modules or wires changed. As found: **Nothing tells the assistant what the person changed.** The briefing says a later message opens "with whatever the person changed on the canvas meanwhile". Over MCP there's no such message, and spotting a manual Echo edit took a diff of the whole patch, buried in the noise from 5 and 9.
11. **Settled by item 2:** `propose` puts the patch on the canvas and says so; there is nothing left to accept or decline. As found: **`propose` doesn't report the outcome.** There's no way to learn whether the person accepted or declined, short of polling `describe_patch`.

## Missing safety and capability (Medium)

12. **Fixed.** `write_patch` with `source: ""` clears the patch, and a patch of nothing but the Output can be proposed. As found: **A patch can't be cleared.** `write_patch` treats `""` as a missing `source`, and `propose` refuses a patch with nothing wired to the Output.
13. **There's no scratch bench.** Trying visual variants meant edit, `render`, then `write_patch` the whole saved patch back each time. A `render` that takes a temporary edit, or a `try_patch` that leaves the bench alone, would make iteration safe.
14. **Fixed.** `measure` says a signal of many values that sat flat at its highest or lowest is clipped, and for how much of the window: `flat at 1 for 23% of it: clipped`. A square or a step, which holds its edge by design, is left alone. As found: **Nothing warns when the sum clips.** With a Speak at full level on a Desk channel, the master measured exactly `..1`, clamped. Neither the compiler nor `measure` said so, though the briefing asks the assistant to keep the peak under -1 dBFS.
15. Where the signal was held at a limit, the clipped note from 14 now marks it; a range that only reaches a socket's edge without sitting there is still a plain range, and the four-saw patch was not kept to check which this was. As found: **`measure` shows a pinned signal as a plain range.** A four-saw Mixer read `-2..1.968`, and 2 is the socket's edge, not the real peak. Nothing marks it.

## `measure` (Low)

16. **Fixed.** `measure` takes `module.output` for one output.
17. **Fixed.** A stepped output lists the values it holds, up to twelve, and the Note Sequencer's, Note's, Quantiser's and Tune's note outputs name them: `holds C4 (60), E4 (64)`.
18. **Fixed.** A `let` name that stands for one output, `hat` in `let hat = shine.high`, measures that output.
19. **It printed `master.left` twice** before `master.right`. Not reproduced: a Desk named `master` printed each output once, and an output named twice in `handles` is now measured once. On `pentagram.fbk` too, every module and both Desks by name print each output once.

## `edit_patch`'s answer (Low)

20. **Fixed.** "It now reads" lists lines in the order they stand, numbered where they end up.
21. **Fixed.** A line two edits touched is printed once, as it ends.
22. **Fixed.** "Those with no let name answer to" starts a line of its own, and an issue list ends in one period.

## Modules and their descriptions (Low)

24. **Fixed:** an Arrangement's and a Song's description and `fade` say that a level written `>` glides across its section, so `0 >0.25 >0.5 >0.75 >1` fades a part in over four. As found: **There's no plain way to fade in over several sections.** A Slew starts settled (25), and Fade's `from` and `to` only span 0..1 of its level. Fading a pad in over four bars took an Expression on `section + progress`.
25. **Fixed:** the Slew's description says it starts where `in` is, and points at a gliding level for a fade-in. As found: **Slew starts settled.** Fed a constant from the first sample it never rises from 0, and neither the module nor the briefing says so.
26. **Fixed:** `drift` says it arrives at a new value every 1/`rate` seconds. As found: **Noise's `drift` has no stated speed.** At `rate: 0.047` it barely moved in a minute, and nothing says how `rate` maps to how far and how fast drift wanders.
27. **Fixed:** a rest holds the nearest sounding note before it, round the end of the pattern; Arek chose this over note 0. As found: **A Note Sequencer's rest drops `out` to note 0** (8 Hz). That's harmless while the gate is shut, but a Slew or a long release after it sweeps down on every rest. Holding the last note may be the better rest.

## The briefing (Medium)

28. **Partly fixed:** an index of every module's type id by category now comes before the long list, a third of the way in, so a reader that stops early still knows what exists. The size itself is [assistant-request-cost.md](assistant-request-cost.md)'s measured plan. As found: **It is about 70 KB.** The session read only its first two-thirds and missed the whole Mastering plugin, built a Drive hack, and asked for modules that already exist. A short index of modules by category at the top would have pointed straight at them. `assistant-request-cost.md` already wants a smaller briefing.
29. **Fixed** by 28's index. As found: **`speak` wasn't in the part that was read.** It turned up only through `describe_patch` after the user added one.

## Editor (Low)

30. **Fixed:** the column's "not set up" footer and beacon are drawn in the scheme's warning color, not the green of Phosphor's accent, and the header already reads "Driven by" the program while one is connected. `mcp --editor` now keeps one connection for its whole run, so the churn comes only from a client that starts it afresh for each call; collapsing that would change what the driving spec asserts, and Arek chose to leave it as it is. As found: **The assistant panel misreports an outside MCP client.** It showed "mcpc stopped driving the editor." and then "No assistant is selected. Pick one in Settings." in success-green. An outside client is a legitimate assistant, and with a reconnect per call, "stopped driving" likely followed every call. *Fix:* show who is connected, collapse connect/disconnect churn, and draw the warning as a warning.
31. **Fixed.** The cells had half a pixel of margin at each side, which layout rounding turned into a one-pixel gap at some edges and none at others. They meet edge to edge now.

## Setup (Low, maybe not Flyback)

32. **`flyback` was missing from `~/.claude.json`** although it was registered with `claude mcp add -s user` on 2026-10-19, so Claude Code had no native tools. Worth checking whether an install or update removes it. It was gone again on the same day, an hour after it was added back.

## Found on `main` at `876fa272` (Medium)

The same patch, reopened in an editor and a `flyback-cli` built from source.

33. **Fixed.** The Loudness module counted the first evaluation after a seek, a loop or a measurement started partway in as lasting the whole jump, and that share never left its windows: on the pentagram patch, measured from 60 s, momentary read +7.8 LUFS where `render --loudness` read -14.6 before the Output's volume. A jump in the clock now counts as no time, and each evaluation's share leaves the window exactly as it came in. The same patch now reads -14.3 momentary.

34. **Fixed.** A `flyback-cli` built in Debug or Release has no plugins beside it, so its own briefing named only the built-in modules. With the editor open, `mcp` now asks the editor for its briefing and hands the client that; with none, or an editor too old to answer, it falls back to its own.
35. **Not needed:** ADR-0224 refuses `#` outright by Arek's choice, with a message naming `//`, and before 1.0 no upgrade step is owed (the `saved-data` skill). As found: **`#` comments are refused now** (`//` starts one; `#` is a sharp). A saved patch, preset or conversation with `#` comments needs the upgrade step the `saved-data` skill asks for, if there isn't one yet.

## Found on `main` at `51107f9d` (Low)

36. **Fixed.** `measure` named a socket the way the catalog spells it, `desk1.bus left`, and refused `desk1.bus_left`, which is how the language writes it. It now takes and prints the language's spelling; `flyback-cli measure` takes either.

## Found on `main` at `b50021d6` (Medium)

37. **Fixed.** Closing the connection to an editor that had gone threw `Pipe is broken.` out of the dispose, so the dead connection was kept and every call after it threw the same. As found: **`mcp --editor` never reconnects after the editor restarts.** Once the editor it was attached to closed, every tool answered `Pipe is broken.` until the client restarted the server; reopening the editor did not help. *Fix:* on a broken pipe, drop the connection and attach to whichever editor is open on the next call, saying so if none is.

## Found on `main` at `979db514` (Medium)

An editor built from `main`, driven through the installed 0.13.0 `Flyback.app`'s `flyback-cli mcp --editor`.

38. **Fixed.** The editor reads on while it answers, so a client that hangs up mid-call cancels the call and frees the editor; a client that gives up on a call hangs up; and a second program is told who holds the editor. As found: **A `measure` that times out leaves the editor locked against the same client.** A 56 s `measure` of six handles ended in `Request timed out`; every call after it, `describe_module` included, answered `another program is driving the editor already.` The only other `flyback-cli mcp` processes on the machine were idle. It recurs: `measure loudness1.short` alone over 56 s on this 6,300-op patch times out the same way, while nine section-level outputs over the same window answer at once, so the time goes into running the whole sound graph. *Fix:* when a request is abandoned, release the editor or let the same server reattach to its own session; and say who holds the editor rather than "another program".
39. **Fixed** as far as the wording: with no editor answering, the CLI says `no editor found at <path>` and that the editor has to be the same version. Trying the old path too was left out: each release ships its CLI beside its own editor, so only a hand-mixed pair meets this. As found: **An editor and a `flyback-cli` from either side of 26e1888 cannot find each other.** The installed 0.13.0 CLI looks for `$TMPDIR/CoreFxPipe_flyback-editor-<user>` (and the desktop app starts it without `TMPDIR`, so `/tmp`), while an editor from `main` listens on `~/Library/Caches/Flyback/flyback-editor.sock`. The CLI says the editor's setting is off, which sends the user to the wrong fix. *Fix:* have the CLI try both paths until the old one is gone, and word the error as "no editor found at <path>".

## What worked

The language reads well, and `edit_patch` applies whole or says exactly why not (`[used-before-bound]` with line and column). `measure` was trustworthy for levels, step rates and section timing, and `render` for checking a picture. The editor adds `requires` lines itself, and on 0.12.0 the Mastering plugin's EQ, Compressor, Limiter and Loudness behaved like a mastering chain should.
