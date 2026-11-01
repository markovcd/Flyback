# A song in sections

From: Empty

A kick, a clap and a bass line, brought in and out by an Arrangement across an intro, a break and a drop, with a picture that follows the song.

## A kick

Add an Easy Drum and wire its left into the Output's left. Left on Auto, it plays a kick on every beat at 120 bpm, heard in both ears.

```fbks
drummer() |> out.left
```

## A clap on two and four

Add a second Easy Drum and pick Clap as its sound in its inspector; on Auto, a clap lands on the second and fourth beats. Add a Desk to mix them: wire the kick's left into the Desk's left 1, the clap's left into its left 2, the Desk's left into the Output's left and its right into the Output's right.

```fbks
let kick = drummer()
let clap = drummer(sound: "clap")
let desk = desk(left_1: kick, left_2: clap)
desk |> out.left
desk.right |> out.right
```

## A bass line

Add a Note Sequencer and an Easy Synth. Wire the sequencer's out into the Synth's pitch, its gate into the Synth's gate, and the Synth's left into the Desk's left 3. Turn the Synth's octave to -2, so the tune sits under the drums as a bass line, and the Desk's trim to 0.6, to leave room for all three.

```fbks
let kick = drummer()
let clap = drummer(sound: "clap")
let riff = notes()
let bass = synth(pitch: riff, gate: riff.gate, octave: -2)
let desk = desk(left_1: kick, left_2: clap, left_3: bass, trim: 0.6)
desk |> out.left
desk.right |> out.right
```

## Sections

Add an Arrangement. Wire its part 1 into the kick's velocity, its part 2 into the clap's velocity, and its part 3 into the Desk's level 3. In its inspector, give it six sections and fill in a row of levels for each part: the kick 1 1 1 0 1 1, the clap 0 1 1 0 1 0, the bass 0 0 1 1 1 0. Each section lasts eight seconds, four bars at 120 bpm: the kick alone, the clap joins, the bass joins, a break with the bass alone, everything again, and the kick alone to lead back round to the start.

```fbks
let song = arrangement() [
    1 1 1 0 1 1 |
    0 1 1 0 1 0 |
    0 0 1 1 1 0 ]
let kick = drummer(velocity: song)
let clap = drummer(velocity: song.part_2, sound: "clap")
let riff = notes()
let bass = synth(pitch: riff, gate: riff.gate, octave: -2)
let desk = desk(left_1: kick, left_2: clap, left_3: bass, level_3: song.part_3, trim: 0.6)
desk |> out.left
desk.right |> out.right
```

## Ease in

Turn the Arrangement's fade to 0.25. A part coming in now swells up across the first quarter of its section, and one leaving dies away, instead of starting and stopping on the bar.

```fbks
let song = arrangement(fade: 0.25) [
    1 1 1 0 1 1 |
    0 1 1 0 1 0 |
    0 0 1 1 1 0 ]
let kick = drummer(velocity: song)
let clap = drummer(velocity: song.part_2, sound: "clap")
let riff = notes()
let bass = synth(pitch: riff, gate: riff.gate, octave: -2)
let desk = desk(left_1: kick, left_2: clap, left_3: bass, level_3: song.part_3, trim: 0.6)
desk |> out.left
desk.right |> out.right
```

## See the song

Add an HSV. Wire the Arrangement's progress into its hue, the kick's env into its value, and the HSV into the Output's color. The screen flashes on every kick, its color sweeps round the wheel through each section and starts again at the next, and it goes dark for the break, where the kick sits out.

```fbks
let song = arrangement(fade: 0.25) [
    1 1 1 0 1 1 |
    0 1 1 0 1 0 |
    0 0 1 1 1 0 ]
let kick = drummer(velocity: song)
color.hsv(hue: song.progress, value: kick.env) |> out.color
```
