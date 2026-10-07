# Videos for the YouTube channel

Started on 2026-10-07, on `main` at `67cfbd96`. A backlog that grows: add an idea when one comes up, move it to Made when it is made, and onto `site/tutorials.html` when it is posted. It is on TODO.md; it stays as long as the channel wants videos.

- **Kind:** Backlog
- **Status:** Open: two made, the rest proposed

## How one is made

[explainroo](https://github.com/vincentsch/explainroo) turns a `script.md` and a `scenes.js` into a narrated MP4 on this machine. It is installed in `~/.explainroo`, with each video's project in `~/.explainroo/videos/<name>` and its working files in `~/.explainroo/work/<name>`; never in a session's scratchpad, which is wiped between turns. What Flyback adds on top of it:

- **A `flyback` look.** A sixth theme in explainroo's `engine/themes.js` (with its name in `src/project.js` and `engine/runtime.js`), kept as `~/.explainroo/flyback-look.patch`: the canvas `#1A1C20` with the editor's grid, the module-family colors as its named colors, the attention amber as its accent, Inter for text. Every value is from `site/assets/site.css`. Inter's variable font goes in the project's `assets/fonts/` as `InterV`, since explainroo bundles Inter at 400 only.
- **The real editor, not a drawing of it.** `flyback-cli shot <patch> --editor <Flyback> --size 1920x1080 --at <s> --select <module>` draws the whole window headless at the video's own size, so a screen is a full frame and the camera zooms into it. Write each step as `.fbks`, `flyback-cli save` it to `.fbk` so the canvas is the document, and shoot it; a module with no wire is dropped by the text, so that step is the next one's `.fbk` with its wire taken out. The preview in a shot is one frame: lay the step's `flyback-cli render` over it, at x 1254, y 88, 666x375 in a 1920x1080 shot. What Is Flyback drew its modules in `scenes.js` instead; ADR-0119's reason holds for videos too, and a drawing goes stale when a module changes.
- **Real output, not illustrations of it.** Clips from `flyback-cli render`, cut into JPEG frames (24 fps at 960x540 is plenty; 30 where it must stay in step with sound) and drawn by time. Stills for a gallery from `--size 480x270 --at 6`.
- **Flyback's sound, not explainroo's music.** `"music": false` and `"sfx": "minimal"`, then `~/.explainroo/work/mix-bed.sh <project> <bed.wav> [offset] [gain]` lays a preset's render (Slow weather so far) about 16 LU under the voice with a limiter. A preset at full level in a scene with no narration is aligned to that scene's start in `build/timeline.json`. `verify` on the remuxed file checks the voice still reads.
- **Truth.** Every claim is from the site, the glossary or the code (`InspectorHelp.cs` for gestures and shortcuts); the voice says *picture* and *sound*, *socket*, *wire*. Check what a picture does before saying it: the Rings in Your first patch travel inward, not out. A homophone the speech check flags ("Sine" heard as "sign", freq as "freak") is fine.

`flyback-cli render` cannot start part way in, so a stretch from the middle of a preset is rendered from 0 and cut (TODO.md's `--from` item).

## Made

| Video | YouTube | Length | Notes |
|---|---|---|---|
| What is Flyback? | `FhKpYZxMlec` | 2:03 | Modules, sockets, wires, the Output, one Sine driving both, normalled inputs, per pixel and per sample, live rebuilds, F2, the assistant, the presets, 7 s of Whole band. Sources lost with the scratchpad; the MP4 is on the channel. |
| Your first patch | not posted | 1:27 | Empty, Space and sine, a wire into color, `in` following the clock, Coordinates for stripes, freq to four, Rings with no wires, Time into offset, Ctrl+S. Every screen a `flyback-cli shot`. `~/.explainroo/videos/first-patch`. |

## Proposed

Roughly in the order worth making. Each is 60 to 120 seconds unless it says otherwise.

- **Your first sound.** A Sine at 220 into `left`, `right` following it, and `volume`. The Scope beside it so the ear and the eye agree.
- **One signal, both sinks.** The Beat you can see preset taken apart: the kick that is heard is the flash that is seen.
- **Feedback for the eyes and the ears.** A loop is a delay: a comb or an echo in the sound, a trail or a tunnel in the picture. Feedback tunnel, Echo chamber and Two echoes side by side.
- **Seeing a sound.** Scope, Analyzer and Beam: what the speakers play, its spectrum, and two channels drawn against each other. Waveform and the Flyback Theme preset.
- **Hearing a picture.** Probe and Scan: a field read along a path at audio rate, so the picture is the waveform. Ring scan and Shape scan.
- **The patch as text.** F2, the language line by line on Plasma, editing a number and pressing Ctrl + Enter, `.fbks` files in git.
- **Ask the assistant.** One real conversation, recorded: a short ask, Expand, the patch it wires, the frames it looks at, a change asked for after, Ctrl + Z. Which models it works with and how to set one up. Needs a real run, never a staged one.
- **Play it.** The computer keyboard as an instrument, a MIDI controller, chords down a polyphonic wire, knobs learned to a controller, the die. Played and No Sense Dub.
- **Inside the Whole band.** A hundred and sixty-two modules in fourteen boxes: the clock, the song, seven instruments, the room, the desk and three boxes for the picture, opened one at a time while it plays. Could run three minutes.
- **Record and render.** Ctrl + R for a live take with the knobs in it, then `flyback-cli render` to PNG, MP4 and WAV with no window, `--loudness`, the viewer, `check` and `pack`.
- **Write a plugin.** A module in C# from a folder, the contract, signing, the preset site. Longer, and for a narrower audience.
- **Shorts: one preset each.** Vertical, 15 to 30 seconds, no narration, the preset's own picture and sound with its name and one line from the gallery. A series, cheap to make once the first is done.
