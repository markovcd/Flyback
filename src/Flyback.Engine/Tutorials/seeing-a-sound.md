# Seeing a sound

From: Empty

A tone drawn three ways: as a wave, as its frequencies, and as a knot two tones tie together.

## A tone to look at

Add a Sine, turn its freq to 220 and wire its out into the Output's left.

```fbks
sine(freq: 220) |> out.left
```

## Draw the wave

Add a Scope. Wire the Sine's out into the Scope's in as well, and the Scope's out into the Output's color. The screen shows the last 20 ms of what the speakers played, newest at the right: four and a bit turns of the Sine.

```fbks
let tone = sine(freq: 220)
tone |> out.left
tone |> scope() |> out.color
```

## Draw its frequencies

Add an Analyzer, wire the Sine into its in, and wire its out into the Output's color in place of the Scope's. A Sine is one frequency, so the chart is one spike, at 220 Hz: about a third of the way from the 100 Hz line to the 1 kHz one.

```fbks
let tone = sine(freq: 220)
tone |> out.left
tone |> analyzer() |> out.color
```

## Tie a knot

Add a second Sine with its freq at 331 and wire it into the Output's right. Add a Beam, wire the first Sine into its x, the second into its y, and the Beam into the Output's color. Three turns up for every two across draw a knot, and the extra hertz turns it round once a second.

```fbks
let left = sine(freq: 220)
let right = sine(freq: 331)
left |> out.left
right |> out.right
beam(x: left, y: right) |> out.color
```

## Let it glow

Turn the Beam's persistence to 200 ms. The trace fades ten times slower, so the whole knot stays lit as it turns.

```fbks
let left = sine(freq: 220)
let right = sine(freq: 331)
beam(x: left, y: right, persistence: 200ms) |> out.color
```
