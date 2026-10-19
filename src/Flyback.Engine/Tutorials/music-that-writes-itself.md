# Music that writes itself

From: Empty

A melody nobody wrote: a wandering value held one beat, snapped to a scale, each note its own length, and drawn as a score.

## A voice that wanders

Add a Wander, a Note and a Triangle. Wire the Wander's out into the Note's note, the Note's hz into the Triangle's freq, and the Triangle's out into the Output's left. Turn the Wander's low to 50 and its high to 76, two octaves from D3. The pitch drifts smoothly, like a siren nobody is steering.

```fbks
wander(low: 50, high: 76) |> note(note: _) |> triangle(freq: _) |> out.left
```

## One note a beat

Add a Tempo, turn its bpm to 300, and add a Floor. Wire the Tempo's beats into the Floor's in, and the Floor's out into the Wander's in. The Wander now walks across whole beats instead of time, so it holds one value for each beat and steps to the next.

```fbks
let tempo = tempo(bpm: 300)
floor(tempo.beats) |> wander(low: 50, high: 76) |> note(note: _) |> triangle(freq: _) |> out.left
```

## In key

Add a Quantiser between the Wander and the Note. In its inspector, click the keys so only C, D, E, G and A are lit. Every note it picks is now in a pentatonic, and no two of them can clash.

```fbks
let tempo = tempo(bpm: 300)
floor(tempo.beats) |> wander(low: 50, high: 76) |> quantiser() [ C D E G A ]
  |> note(note: _) |> triangle(freq: _) |> out.left
```

## Strike each note

Add a Fraction, a Threshold and an ADSR. Wire the Tempo's beats into the Fraction's in, the Fraction's out into the Threshold's edge, the Threshold's out into the ADSR's gate, and the ADSR's out into the Triangle's amp. Turn the Threshold's in to 0.5, the ADSR's decay to 150 ms and its sustain to 0.6. The Fraction climbs from 0 to 1 across each beat, and the gate is open while it is under the Threshold's in: every note lasts the first half of its beat.

```fbks
let tempo = tempo(bpm: 300)
let strike = step(fract(tempo.beats), 0.5) |> adsr(gate: _, decay: 150ms, sustain: 0.6)
floor(tempo.beats) |> wander(low: 50, high: 76) |> quantiser() [ C D E G A ]
  |> note(note: _) |> triangle(freq: _, amp: strike) |> out.left
```

## Each note its own length

Add a second Wander, with its rate at 3.1, its seed at 11, its low at -0.4 and its high at 0.8. Wire the Floor's out into its in, and its out into the Threshold's in in place of the 0.5. Each beat it picks how much of the beat the note lasts; below 0 the gate never opens, and the beat is a rest.

```fbks
let tempo = tempo(bpm: 300)
let beat = floor(tempo.beats)
let length = beat |> wander(rate: 3.1, seed: 11, low: -0.4, high: 0.8)
let strike = step(fract(tempo.beats), length) |> adsr(gate: _, decay: 150ms, sustain: 0.6)
beat |> wander(low: 50, high: 76) |> quantiser() [ C D E G A ]
  |> note(note: _) |> triangle(freq: _, amp: strike) |> out.left
```

## Echo it

Add an Echo between the Triangle and the Output: wire the Triangle's out into the Echo's in, the Echo's left into the Output's left and its right into the Output's right. Wire the Tempo's out into the Echo's tempo, so the repeats land on the steps of the tune.

```fbks
let tempo = tempo(bpm: 300)
let voice = floor(tempo.beats) |> wander(low: 50, high: 76) |> quantiser() [ C D E G A ]
  |> note(note: _) |> triangle(freq: _)
let echo = voice |> echo(tempo: tempo)
echo |> out.left
echo.right |> out.right
```

## A dot for each note

Add a Remap, a Translate, a Circle and a Fill. Wire the Quantiser's note into the Remap's in, and turn its in low to 50, its in high to 76, its out low to -0.85 and its out high to 0.85. Wire the Remap's out into the Translate's dy, turn the Translate's dx to 1.4, and wire its x and y into the Circle's. Turn the Circle's radius to 0.06, and wire it through the Fill into the Output's color. A dot sits near the right edge at the height of the note playing.

```fbks
floor(tempo().beats) |> wander(low: 50, high: 76) |> quantiser() [ C D E G A ]
  |> remap(in_low: 50, in_high: 76, out_low: -0.85, out_high: 0.85)
  |> translate(dx: 1.4, dy: _) |> circle(radius: 0.06) |> fill(distance: _) |> out.color
```

## Color it

Add an HSV and a second Remap, its in low at 50 and its in high at 76. Wire the Quantiser's note into the new Remap, its out into the HSV's hue, the Fill's fill into the HSV's value, and the HSV into the Output's color. Each note has a color of its own, going round the color wheel from the lowest note to the highest.

```fbks
let pitch = floor(tempo().beats) |> wander(low: 50, high: 76) |> quantiser() [ C D E G A ]
let hue = pitch |> remap(in_low: 50, in_high: 76)
let dot = pitch |> remap(in_low: 50, in_high: 76, out_low: -0.85, out_high: 0.85)
  |> translate(dx: 1.4, dy: _) |> circle(radius: 0.06) |> fill(distance: _)
color.hsv(hue: hue, value: dot) |> out.color
```

## A score that scrolls

Add a Trails between the HSV and the Output's color, with its dx at -0.02 and its persist at 0.99. Each frame slides a little left and fades slowly, so the dots draw the tune as a score that scrolls as it plays.

```fbks
color.hsv() |> trails(dx: -0.02, persist: 0.99) |> out.color
```
