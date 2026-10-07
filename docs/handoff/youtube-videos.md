# Videos for the YouTube channel

Started on 2026-10-07, on `main` at `67cfbd96`. A backlog that grows: add an idea when one comes up, move it to Made when it is posted. It is on TODO.md; it stays as long as the channel wants videos.

- **Kind:** Backlog
- **Status:** Open: one made, the rest proposed

## How one is made

[explainroo](https://github.com/vincentsch/explainroo) turns a `script.md` and a `scenes.js` into a narrated MP4 on this machine. What the first video added on top of it:

- **A `flyback` look.** A sixth theme in explainroo's `engine/themes.js` (with its name in `src/project.js` and `engine/runtime.js`): the canvas `#1A1C20` with the editor's grid, the module-family colors as its named colors, the attention amber as its accent, Inter for text. Every value is from `site/assets/site.css`. Inter's variable font goes in the project's `assets/fonts/` as `InterV`, since explainroo bundles Inter at 400 only.
- **Modules drawn as the canvas draws them.** A `module()` helper in `scenes.js` draws a header in the category's color, sockets as dots with their real names, and a normalled input's source in faint text; `wire()` draws the curve and runs dots along it. Socket names come from `NodeCatalog`, never from memory.
- **Real output, not illustrations of it.** Clips from `flyback-cli render --preset`, cut into JPEG frames (15 fps at 960x540 for slow pictures, 30 fps where it must stay in step with sound) and drawn by time in a panel. Stills for a gallery from `--size 480x270 --at 6`.
- **Flyback's sound, not explainroo's music.** `"music": false` and `"sfx": "minimal"`, then ffmpeg mixes a preset under the narration about 16 LU below the voice, and a preset at full level in a scene with no narration, aligned to that scene's start in `build/timeline.json`, with a limiter to keep the true peak under -1 dBTP. `verify` on the remuxed file checks the voice still reads.
- **Truth.** Every claim is from the site, the glossary or the code; the voice says *picture* and *sound*, *socket*, *wire*. A homophone the speech check flags ("Sine" heard as "sign") is fine.

`flyback-cli render` cannot start part way in, so a stretch from the middle of a preset is rendered from 0 and cut (TODO.md's `--from` item).

## Made

| Video | Length | Notes |
|---|---|---|
| What is Flyback? One patch makes the picture and the sound | 2:03 | Modules, sockets, wires, the Output, one Sine driving both, normalled inputs, per pixel and per sample, live rebuilds, F2, the assistant, the presets, 7 s of Whole band. |

## Proposed

Roughly in the order worth making. Each is 60 to 120 seconds unless it says otherwise, and follows a tutorial on the site where there is one, so the description can link it.

- **Your first patch.** From the blank canvas to a moving picture: place a Sine, see it already move because its `in` follows the clock, wire Coordinates into it for stripes, then into Rings. Follows tutorials 02 and 03.
- **Your first sound.** A Sine at 220 into `left`, `right` following it, and `volume`. The Scope beside it so the ear and the eye agree. Follows tutorial 04.
- **One signal, both sinks.** The Beat you can see preset taken apart: the kick that is heard is the flash that is seen. Follows tutorial 05.
- **Feedback for the eyes and the ears.** A loop is a delay: a comb or an echo in the sound, a trail or a tunnel in the picture. Feedback tunnel, Echo chamber and Two echoes side by side.
- **Seeing a sound.** Scope, Analyzer and Beam: what the speakers play, its spectrum, and two channels drawn against each other. Waveform and the Flyback Theme preset.
- **Hearing a picture.** Probe and Scan: a field read along a path at audio rate, so the picture is the waveform. Ring scan and Shape scan.
- **The patch as text.** F2, the language line by line on Plasma, editing a number and pressing Ctrl + Enter, `.fbks` files in git. Follows tutorial 06.
- **Ask the assistant.** One real conversation, recorded: a short ask, Expand, the patch it wires, the frames it looks at, a change asked for after, Ctrl + Z. Which models it works with. Follows tutorial 11. Needs a real run, never a staged one.
- **Play it.** The computer keyboard as an instrument, a MIDI controller, chords down a polyphonic wire, knobs learned to a controller, the die. Played and No Sense Dub. Follows tutorials 07 and 08.
- **Inside the Whole band.** A hundred and sixty-two modules in fourteen boxes: the clock, the song, seven instruments, the room, the desk and three boxes for the picture, opened one at a time while it plays. Follows tutorial 09. Could run three minutes.
- **Render from the command line.** `flyback-cli render` to PNG, MP4 and WAV with no window, `--loudness`, the viewer, `check` and `pack`. For people scripting it. Follows tutorial 10.
- **Write a plugin.** A module in C# from a folder, the contract, signing, the preset site. Longer, and for a narrower audience.
- **Shorts: one preset each.** Vertical, 15 to 30 seconds, no narration, the preset's own picture and sound with its name and one line from the gallery. A series, cheap to make once the first is done.
