# Your first sound

From: Empty

A tone, then a brighter one, then a filter sweeping it.

## Wire a Sine into the left

Add a Sine and wire its out into the Output's left. The right follows the left when nothing is wired into it, so both speakers get it. There is nothing to hear yet: the Sine runs once a second, far below hearing.

```fbks
sine() |> out.left
```

## Make it a tone

Turn the Sine's freq to 220. Running on Time, freq is in hertz, and 220 is the A below middle C.

```fbks
sine(freq: 220) |> out.left
```

## A brighter wave

Add a Saw at a freq of 110 and wire it into the Output's left in place of the Sine. A Saw carries every harmonic, so it buzzes where the Sine hummed.

```fbks
saw(freq: 110) |> out.left
```

## Filter it

Add a Filter between the Saw and the Output: the Saw's out into the Filter's in, the Filter's low into the Output's left. Turn its cutoff to 400 to take the top off the buzz.

```fbks
saw(freq: 110) |> filter(cutoff: 400) |> out.left
```

## Make it ring

Turn the Filter's resonance up to 0.8. The filter now peaks at its cutoff and whistles there.

```fbks
saw(freq: 110) |> filter(cutoff: 400, resonance: 0.8) |> out.left
```

## Sweep it

Add a Sine at a freq of 0.25 and a Remap, and wire the Sine into the Remap's in. Its in low and in high already span the Sine's -1 to 1; set its out low to 200 and out high to 1200, and wire its out into the Filter's cutoff. The cutoff now rises and falls once every four seconds.

```fbks
let sweep = sine(freq: 0.25) |> remap(in: _, out_low: 200, out_high: 1200)
saw(freq: 110) |> filter(cutoff: sweep, resonance: 0.8) |> out.left
```
