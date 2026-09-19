---
name: played-presets
description: Use when building or checking a Flyback preset that has MIDI voices or panel knobs (like Dub) - feedback-loop order, what a knob may link to, and how to test a played preset without a keyboard.
---

# Presets with MIDI voices and panel knobs

Dub (`DubPreset.cs`) is the first shipped preset with panel knobs; `PresetBench` has `Panel`, `Follows` and `Dial` for them. It has 1,680 audio ops and 226 modules.

- **Feedback reads whatever reached the Output, not the Trails' own output.** A Grade, scanlines or Vignette wired after a Trails is inside the loop and compounds every frame; at persist over ~0.9 it burns to one saturated color with striped artifacts. Put the Trails last. Outrun gets away with a Grade after it only because its persist is 0.6.
- **Do not link a knob to Reverb `size`**: it is a delay length, so turning it while it rings bends the tail. Link `decay` and the return level. A linked socket is not clamped to its port range at compile, so keep link ends inside it (`DubPresetTests` asserts this). A `value` module is -4..4, so dial 0..1 and multiply.
- A knob that is heard wants a Slew (~30 ms) after its `Dial`; on the screen a Slew is a wire, so the same dial serves the picture.
- An envelope has no memory on the video path. For a played voice use a Meter on the voice (its input is swept, costs the picture nothing) plus a floor from the MIDI gate, which is live on both sinks.
- A MIDI voice's pitch and velocity hold after release, and velocity is 0 until the first strike, which makes it usable as a "has anything been played" flag. Hold clamps to ±16, so it cannot hold a note number.

## Checking without a keyboard

A scratch xunit test compiles with `played: true`, calls `patch.Seed(live)`, sets `MidiSignal.Key(MidiSources.Keyboard, voice, ...)` and knob keys on a `LiveValues`, then renders audio per sample and frames with `SynthRenderer.Render(..., live)` + `PngWriter.WriteBgra`. Set `Meters.Key(node.Id, Meters.Level)` by hand to fake a voice's level. The CLI cannot do this (no live block).

`dotnet test --filter` is ignored by this test platform, so the whole project runs, and `-v q` prints nothing on success. Read `TestResults/*.log` (UTF-16).

See `authoring-presets` and `convenience-modules`.
