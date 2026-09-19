---
name: convenience-modules
description: Use when adding, changing or porting presets onto Flyback's convenience wrapper modules (Stroke, Fade, Wander, Drum, Desk, Trails, Echo, Hiss, Bell, Transform, Ink, Vignette, Tune, Euclid, Tempo) - the exactness rules, what not to wire, and how a port is verified bit-for-bit.
---

# Convenience modules: exactness and porting

ADR-0095 (Stroke, Fade, Wander, Drum in Voice; Desk, Trails in the engine) and ADR-0097 (Echo in Effects; Hiss and Bell in Voice; Transform, Ink, Vignette, Tune in the engine; Euclid `stroke`; Tempo `beats`; `SettingsExtra` in Core) each emit exactly the arithmetic of the primitives they replace, and each has a bit-equality test against the long form. ADR-0097 holds the counts, the socket-versus-setting rule and what was left out. Nine showcases went 1,586 -> 1,450 modules, all bit-exact.

## Rules found porting the presets

- **Never read a Fade's `gate` from the picture when the Fade's `in` carries a voice.** It drags the whole voice into the video program (Outrun grew 134 video ops). Use a second Fade on a Stroke, or a plain Smoothstep, for the picture.
- **A Desk has one level per channel.** Parts that leaned on different left/right levels (Acid hats/clap, Mycelium arp/drips, Whole band hats) lean by a Multiply or a small Mixer on one side instead; that costs ~1e-16 (or 2e-8 in Whole band) of exactness, accepted.
- Desks chain drums -> music -> master through the `bus` sockets. That order reproduces `(a + b) + c` of the old master Mixer bit-for-bit because a two-term sum commutes.
- Acid's kick has two ADSRs (pitch and level apart), so it is not a Drum. Presets reading the last frame twice with a color split (Mycelium, Whole band) keep Scale/Rotate/Feedback; Trails' tail at persist 1 is exact but costs per-pixel ops and is not what Trails is for.
- Core presets (Nebula, Whole band) have text forms in `docs/language.md` and `LanguageTests` (`Same`/`Alike`) plus GLSL and PNG snapshots; the PNG staying approved is the proof the picture is unchanged.
- **Exactness survives reassociation only one way.** `(level x band) x gain` is what Hiss emits; a preset that summed bands before the envelope (Fracture's snare wires) or had no filter (Mycelium/Acid hats) stays long-form. A level-less use sets `level` to 1 and lets what follows play it.
- **A Hiss's private noise equals a shared Random's** because white is a stateless hash of clock and seed, but match the seed (Fracture's Random was seed 1). Hiss was first built and removed the same day: unread ops became free (ADR-0096), so use Random's white.
- **Tune's `note` output equals a Quantiser's** only with a non-empty scale (integers survive the Note's floor).
- **Tempo `beats` is in no preset**: `PresetBench` helpers wire output 0, and ~18 call sites a track read the count. Left for hand-made patches.
- **The engine catalogue's assistant briefing is 85 characters under `Handbook.ProseBudget`** (40,000). The next engine module must raise the budget or trim prose; `PatchWorkbenchTests.The_briefing_carries_what_each_module_is_for` is what fails.
- Appending ports (Tempo `in`, Euclid `curve`) is safe for saved patches; old JSON dumps loaded and played identically. It renumbers GLSL snapshot registers, which is a re-approve, not a regression.
- Choice settings in the text language are quoted strings (`order: "turn"`), not bare words.

## How a port is verified

Dump every preset with `PatchIO.ToJson` first. Then a throwaway xunit test compares each preset built now against its dump: audio sample by sample with `new DelayState(program, rate)` over six windows, video byte by byte via `SynthRenderer` frames. It needs the `DelayState(CompiledPatch, int)` constructor, or presets with planes throw. Give the test an option to splice the old arithmetic hiss back in, so everything but the noise can be shown exact.

See `authoring-presets`. `site/assets/shots/whole-band.webp` still reads 92 modules in its status bar (it is 85); it has sound, so it is the user's to recapture (see `site-screenshots`).
