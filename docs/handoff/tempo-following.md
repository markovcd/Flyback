# Tempo following: a patch keeps time to taps or to what it hears

Written on 2026-11-16, against `main` at `882fff5`. It is on TODO.md; take it off there, and delete this file, in the commit that lands the last of it.

- **Kind:** Plan
- **Status:** Open

## What is wanted

A patch keeps to its own clock, or to an instrument's MIDI clock through a Clock In (ADR-0139). A drummer, a record, a phone playing a loop or another app on the same machine sends no MIDI clock, so today Flyback cannot play along with any of them. Two ways in would fix that, one small and one larger:

- **Tap tempo.** Somebody taps on the beat, and the patch takes the tempo and the phase of the taps.
- **Beat following.** The patch listens through the sound input (the Line In's microphone or line, ADR-0178), finds the beat in what it hears, and keeps to it as the tempo drifts.

Either one should feel to the patch exactly like a Clock In: `beats`, `bpm`, `running` and `reset`, so a sequencer, an Arrangement or a Euclid patched to it needs no change.

## What is there already

Read from the code, not run:

- **Clock In** (`midi.clock`, `MidiModules.EmitClock` in `src/Flyback.Core/Graph/MidiModules.cs`). The shell publishes five live values per source (beat as of the latest tick, rate, tempo, running, count of Starts), and the program draws `beat + rate × (in − at)` against its own clock, letting each re-anchoring's jump go over a quarter of a second through a slip cell. Its `nudge` socket runs it ahead of or behind the source in milliseconds, on top of the sound device's latency. This is the part a follower should reuse whole: it already turns a jittery, late, outside estimate of the beat into a smooth `beats` on the exact sample.
- **`MidiClock`** (`src/Flyback.Engine/Graph/MidiClock.cs`) measures tempo from tick spacing with one-pole smoothing and a clamp on how far one gap moves it, and writes the five values with `WriteTo(LiveValues, source)`. `MidiHub.Tick` (`src/Flyback.Ui/Midi/MidiHub.cs`) timestamps each tick on a `Stopwatch` and writes every following block. A beat tracker is a second writer of the same five keys.
- **Which source a Clock In follows** is `MidiClockExtra`'s `follows` field, a choice over `MidiSources.All` that the shell installs (`src/Flyback.Core/Graph/MidiSources.cs`).
- **Tempo** (`SequencerModules`, `src/Flyback.Core/Graph/SequencerModules.cs`) is only `bpm / 60` and `in × rate`: no state, nothing to follow.
- **The sound input.** `IAudioInput` and `IAudioCapture` in the plugin contract (`src/Flyback.Plugins/Audio/`), with backends on Linux (ALSA), Windows (WASAPI), macOS (Core Audio) and Android (AudioRecord); the page hears the browser's microphone through the speakers' worklet (`PageMicrophone`, `JsSound`). The iOS editor has no sound input yet. `LineIn` (`src/Flyback.Ui/Audio/LineIn.cs`) opens the capture only while the playing program reads `line-in/left` or `line-in/right`, and writes what it hears into a `LineInFeed` ring the renderer drains, skipping ahead past about 85 ms of lag.
- **Offline**, `flyback-cli render --input <file>` plays a file as the Line In (`RecordedLineIn`), so whatever follows the sound can be checked with no hardware.
- **No FFT in the engine.** There is one in `Flyback.Assist/ClipSpectrum.cs`, the wrong layer to depend on, and ADR-0019 rules out a package.

## The shape proposed

### Tap tempo: a module, all in the program

A **Tap** module (name open; Timing category) with a `tap` socket: each rise is a tap. It keeps the time of the last rise and the smoothed gap in cells, the way Sample & Hold and the Clock In keep theirs, and outputs `beats`, `bpm`, `running` and `reset` like a Clock In. The first tap sets the phase (beat 0 is the tap), later taps set the tempo from the spacing, and a gap more than twice the current beat starts over rather than halving the tempo.

Keeping it in the program, rather than in the shell, means whatever makes a gate can tap it: a panel Button (ADR-0205) for a person, a MIDI In's gate from a drum pad, or a Line In through a Slew and an Expression that thresholds it, for a crude follower that needs no new code. It works the same in `render`, on the page and on every backend, and a Pulse wired into `tap` is its test. It needs no new op if it is built from the cells the existing modules use; check that before starting.

### Beat following: a tracker in the shell, read by Clock In

A **beat tracker** that runs beside `LineIn` on the capture thread's buffers, not on the renderer's ring (which skips ahead and has no timestamps), and writes the same five values `MidiClock` writes, under a source of its own. A Clock In then follows it by picking it in `follows`, next to the MIDI instruments: "what the Line In hears". `nudge`, the slip cell and the line through the beat are reused unchanged. The tracker, like `MidiClock`, lives in `Flyback.Engine` so `flyback-cli render --input` can run it on a file.

The usual three stages, each cheap:

1. **Onset strength.** Mono, a handful of band-pass bands (biquads, so no FFT), each band's energy rectified as a rising difference of its log, summed, at a hop of about 5 to 10 ms. Spectral flux over an FFT is the better-known version and would need an engine FFT.
2. **Tempo.** Autocorrelation of the last six to eight seconds of onset strength, a few times a second, weighted toward a likely tempo (a log-Gaussian around 120 BPM, as in Ellis, *Beat Tracking by Dynamic Programming*, 2007) and folded into a range the module sets (`slowest`, `fastest`), which is the main defense against locking at half or double.
3. **Phase.** Predict the next beat from the last and the tempo; when it comes due, move it toward the strongest onset near the prediction, by a fraction, like a phase-locked loop. Publish the beat and rate each time it moves, and let the Clock In's slip cell smooth the correction.

Confidence (how far the autocorrelation's peak stands above the rest) decides `running`: high once locked, and on losing it the tracker holds the last tempo and phase and freewheels rather than stopping, so a break in the drums does not stop the patch. `reset` fires on first lock.

Write the tracker from the papers, not from code: aubio and BTrack, the best-known open trackers, are GPL.

### Bars, not only beats

Neither the taps nor the sound say where the bar starts, so beat 0 is wherever lock was first found. A Tap module fed one tap on the downbeat could set the phase of a beat tracker that sets the tempo; whether that is a socket on the Clock In or a second module is open.

## Latency

- **Input**: the capture buffer, plus the device's own input latency, which the backends do not report today. Timestamp each buffer on the shell's `Stopwatch` as it arrives, less its own length.
- **Analysis**: a hop or two, plus peak picking. Once locked the tracker predicts, so this costs only the time to the first lock (several seconds of steady beat), not the timing of the beats after it.
- **Output**: the sound device's latency, which Clock In already adds.

The round trip is what `nudge` exists for, and a tracker that knows its input latency should subtract it the way `MidiHub` relies on its tick timestamps. The page is the worst case: the worklet's cushion of 2048 frames and `AudioContext.outputLatency` vary by browser, so expect a larger default nudge there.

**Feedback.** A microphone that hears Flyback's own speakers will follow itself, which holds the tempo but drowns the drummer. Say so in the module's help as the Line In does: headphones, or a line input.

## Phones

Android has a Line In (AudioRecord), so following works there unchanged; the tracker's cost is a few filters a sample at the input's rate, small beside the patch itself even on a Pixel 9 Pro whose sound engine runs about eleven times slower than a desktop's. The built-in microphone sits next to the speaker, so the feedback point above is sharper on a phone. Tap tempo works on any platform, a finger on a panel Button. iOS waits for a sound input.

## What to check first

- **That a Clock In source can be something other than a MIDI instrument.** `MidiSources` is a list of MIDI devices with a `Conducts` flag. Either the shell adds a source for the tracker, or `follows` grows a second kind. The `saved-data` skill governs what `follows` stores, and the glossary needs a word for the tracker before any of it is named in code.
- **A command an agent can drive.** `flyback-cli beats <file> --json` printing the tracked beat times, tempo and confidence over time, so the tracker is tuned and tested against files without the editor. Test fixtures: generated click tracks at fixed and drifting tempos, a swung one, and one with a two-bar break; score each in milliseconds of error against the known beats, the way `measure` scores a patch.
- **How well a simple tracker does on real music.** Run the fixtures and a few real drum loops before building the module, and set the bar it has to meet (say, locked within 8 s and within 20 ms after that on steady drums).

## Sequence

1. Tap module, in the program, with its spec and a teaching preset that plays along to a panel Button.
2. The tracker in the engine and `flyback-cli beats --json`, measured against the fixtures.
3. The tracker as a Clock In source in the editor, through `LineIn`, desktop first.
4. The page and Android, with a default nudge measured on each.
5. Setting the bar's phase from a tap.
