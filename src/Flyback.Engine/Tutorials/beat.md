# A beat you can see

From: Empty

A bass line on a tempo, plucked and filtered, and rings that flash on every note.

## Set a tempo

Add a Tempo. It counts beats at 120 a minute and hands them on, so everything wired to it keeps time together.

```fbks
let beat = tempo()
```

## Step a sequence on it

Add a Note Sequencer and wire the Tempo's beats into its in. Turn its rate to 2, for two steps a beat.

```fbks
let steps = tempo().beats |> notes(rate: 2)
```

## Play the notes

Add a Note and a Saw. Wire the sequencer's out into the Note's note, the Note's hz into the Saw's freq, and the Saw's out into the Output's left. Now each step plays its note, end to end.

```fbks
tempo().beats |> notes(rate: 2) |> note(note: _) |> saw(freq: _) |> out.left
```

## Pluck each note

Add an ADSR and wire the sequencer's gate into its gate and its out into the Saw's amp. Turn its decay to 150 ms and its sustain to 0, so each note falls away instead of holding.

```fbks
let steps = tempo().beats |> notes(rate: 2)
let pluck = steps.gate |> adsr(gate: _, decay: 150ms, sustain: 0)
steps |> note(note: _) |> saw(freq: _, amp: pluck) |> out.left
```

## Round it off

Add a Filter between the Saw and the Output, with its cutoff at 600, to take the edge off.

```fbks
let steps = tempo().beats |> notes(rate: 2)
let pluck = steps.gate |> adsr(gate: _, decay: 150ms, sustain: 0)
steps |> note(note: _) |> saw(freq: _, amp: pluck) |> filter(cutoff: 600) |> out.left
```

## See it

Add a Rings and an HSV. Wire the Rings into the HSV's hue, the ADSR's out into the HSV's value, and the HSV into the Output's color. On the picture an ADSR is its gate, so the rings flash on with every note.

```fbks
let steps = tempo().beats |> notes(rate: 2)
let pluck = steps.gate |> adsr(gate: _, decay: 150ms, sustain: 0)
rings() |> hsv(hue: _, value: pluck) |> out.color
```

## Pick the pace

Turn the Tempo's bpm to 132. The notes and the flashes speed up together, because both count the same beats.

```fbks
let beat = tempo(bpm: 132)
```
