# Videos for the YouTube channel

Started on 2026-10-07, on `main` at `67cfbd96`. A backlog that grows: add an idea when one comes up, move it to Made when it is made, and onto `site/tutorials.html` when it is posted. It is on TODO.md; it stays as long as the channel wants videos.

- **Kind:** Backlog
- **Status:** Open: seven made, six posted, the rest proposed

## How one is made

[explainroo](https://github.com/vincentsch/explainroo) turns a `script.md` and a `scenes.js` into a narrated MP4 on this machine. It is installed in `~/.explainroo`, with each video's project in `~/.explainroo/videos/<name>` and its working files in `~/.explainroo/work/<name>`; never in a session's scratchpad, which is wiped between turns. What Flyback adds on top of it:

- **A `flyback` look.** A sixth theme in explainroo's `engine/themes.js` (with its name in `src/project.js` and `engine/runtime.js`), kept as `~/.explainroo/flyback-look.patch`: the canvas `#1A1C20` with the editor's grid, the module-family colors as its named colors, the attention amber as its accent, Inter for text. Every value is from `site/assets/site.css`. Inter's variable font goes in the project's `assets/fonts/` as `InterV`, since explainroo bundles Inter at 400 only.
- **The real editor, not a drawing of it.** `flyback-cli shot <patch> --editor <Flyback> --size 1920x1080 --at <s> --select <module>` draws the whole window headless at the video's own size, so a screen is a full frame and the camera zooms into it. Write each step as `.fbks`, `flyback-cli save` it to `.fbk` so the canvas is the document, and shoot it; a module with no wire is dropped by the text, so that step is the next one's `.fbk` with its wire taken out. The preview in a shot is one frame: lay the step's `flyback-cli render` over it, at x 1254, y 88, 666x375 in a 1920x1080 shot. What Is Flyback drew its modules in `scenes.js` instead; ADR-0119's reason holds for videos too, and a drawing goes stale when a module changes.
- **Real output, not illustrations of it.** Clips from `flyback-cli render`, cut into JPEG frames (24 fps at 960x540 is plenty; 30 where it must stay in step with sound) and drawn by time. Stills for a gallery from `--size 480x270 --at 6`.
- **Flyback's sound, not explainroo's music.** `"music": false` and `"sfx": "minimal"`, then `~/.explainroo/work/mix-bed.sh <project> <bed.wav> [offset] [gain]` lays a preset's render (Slow weather so far) about 16 LU under the voice with a limiter. A preset at full level in a scene with no narration is aligned to that scene's start in `build/timeline.json`. `verify` on the remuxed file checks the voice still reads.
- **Feedback is per frame.** A Trails or a Feedback dims once a frame, so a clip of one is rendered at 30 fps and looped by whole turns of its tune, never stretched to another rate.
- **Truth.** Every claim is from the site, the glossary or the code (`InspectorHelp.cs` for gestures and shortcuts); the voice says *picture* and *sound*, *socket*, *wire*. Check what a picture does before saying it: the Rings in Your first patch travel inward, not out. A homophone the speech check flags ("Sine" heard as "sign", freq as "freak") is fine.

`flyback-cli render` cannot start part way in, so a stretch from the middle of a preset is rendered from 0 and cut (TODO.md's `--from` item).

## Made

| Video | YouTube | Length | Notes |
|---|---|---|---|
| What is Flyback? | `FhKpYZxMlec` | 2:03 | Modules, sockets, wires, the Output, one Sine driving both, normalled inputs, per pixel and per sample, live rebuilds, F2, the assistant, the presets, 7 s of Whole band. Sources lost with the scratchpad; the MP4 is on the channel. |
| Your first patch | `npkO3aYA_CA` | 1:27 | Empty, Space and sine, a wire into color, `in` following the clock, Coordinates for stripes, freq to four, Rings with no wires, Time into offset, Ctrl+S. Every screen a `flyback-cli shot`. `~/.explainroo/videos/first-patch`. |
| Make a beat you can see | `SxKHeOr8l0g` | 1:54 | Tempo, a Stroke into a Drum, a Euclid into a Hiss, a Note Sequencer through a Saw, an ADSR and a Filter, the levels, Rings lit by the Stroke, a panel knob on the cutoff. Ends as the Beat you can see preset, bit for bit. Each scene plays its own step's sound, ducked under the voice by `~/.explainroo/work/beat/mix.py`. Replaced `tutorials/beat.html`. |
| One knob, two echoes | `s8Ys324Lv9I` | 2:12 | A Tempo stepping a Note Sequencer into a Triangle, an ADSR and an Expression for the pluck, an Echo on the tempo (taps, feedback), a Circle's outline lit by a Stroke with the hue on the tune, a Trails for the tunnel, a Repeats knob linked to feedback and persist, then turned to 0.1 and 0.95. Ends as the Two echoes preset, bit for bit. Every step is cut from the preset's own `.fbk` by `~/.explainroo/work/echoes/place.py`, so no module moves between screens. From the tunnel on, the picture and `mix.py`'s sound run on one clock (`clock.py`), so the rings flash on the notes. Replaced `tutorials/echoes.html`. |
| Seeing a sound | `6aPxkERSXdc` | 1:45 | One Sine on a Scope (two cycles in 18 ms), the same Sine on an Analyzer (one peak past 100 Hz), a second Sine at 165 Hz on a Beam, the 3:2 knot and its loops, then 165.2 Hz so the knot turns, back to the same shape every 2.5 s. Ends as the Lissajous preset, the picture within one step of 255 (the preset's persistence is 29.999 ms). `~/.explainroo/videos/seeing`. |
| The patch as text | `B9JqyT0UJlg` | 1:56 | The Drone preset on the canvas, F2, the text line by line (a `let`, pipes and `_`, `t`, a name read twice), `freq` from 0.15 to 0.6 and Ctrl+Enter, a text that does not read leaving the patch playing, `.fbks` as plain text in a diff. Ends on the Drone preset and points at Plasma on the home page. One clock for the whole video, switching to the edited patch at Ctrl+Enter. `~/.explainroo/videos/text`. |
| Ask the assistant | not posted | 2:20 | One real conversation through `flyback-cli ask` with Claude Code on Opus, High: what you choose in its settings (Claude Code and Codex on a plan, OpenAI, Gemini, any chat-completions endpoint, effort), that it sees and Gemini also hears while Claude Code says it cannot, Expand on "rain on a window at night", the column during a turn, the proposal and its report, a follow-up asking for 17 dB more and brighter drops, Ctrl+Z, the conversation kept, and sharing. Every column screen is `flyback-cli shot --assistant` of the patch each turn left, with its saved conversation (`~/.config/Flyback/sessions`, keyed by the patch's path, copied aside between turns). Ends on Rain on a Window, the second turn's patch, now a default on the preset site. `~/.explainroo/videos/assist2`, work in `~/.explainroo/work/assist3`. |

## Proposed

Roughly in the order worth making. Each is 60 to 120 seconds unless it says otherwise.

One of them replaces the written tutorial still under `site/tutorials/`. When it is posted, `tutorials/syntakt.html` goes, with every link to it and its `PatchShotTests` lines, in the same commit.

- **Play it from an Elektron Syntakt.** Replaces `tutorials/syntakt.html`. The box's MIDI and USB settings, the whole Syntakt added in one pick, a MIDI track sequencing a Flyback voice, a Flyback sequencer kept to its clock, a picture lit by its tracks, and its knobs on the panel. Needs the real box filmed or captured; the editor half can be `flyback-cli shot`.
- **Play it.** The computer keyboard as an instrument, a MIDI controller, chords down a polyphonic wire, knobs learned to a controller, the die. Played and No Sense Dub.
- **Inside the Whole band.** A hundred and sixty-two modules in fourteen boxes: the clock, the song, seven instruments, the room, the desk and three boxes for the picture, opened one at a time while it plays. Could run three minutes.
- **Record and render.** Ctrl + R for a live take with the knobs in it, then `flyback-cli render` to PNG, MP4 and WAV with no window, `--loudness`, the viewer, `check` and `pack`.
- **Write a plugin.** A module in C# from a folder, the contract, signing, the preset site. Longer, and for a narrower audience.
- **Shorts: one preset each.** Vertical, 15 to 30 seconds, no narration, the preset's own picture and sound with its name and one line from the gallery. A series, cheap to make once the first is done.
