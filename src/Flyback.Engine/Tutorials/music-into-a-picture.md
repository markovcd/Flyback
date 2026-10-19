# Turn any music into a picture

From: Empty

Whatever the Line In hears, folded into a turning kaleidoscope that the kick flashes and the loudness colors.

## Listen

Add a Line In and a Meter. Wire the Line In's left into the Meter's in, and the Meter's level into the Output's color. Choose what the Line In hears under Settings → Sound, play some music, and the screen brightens as it gets louder. Nothing goes to the speakers, so the patch never hears itself.

```fbks
audio.in() |> meter() |> out.color
```

## Just the kick

Add a Filter between the Line In and the Meter, with its cutoff at 150, so only the low end gets through. Turn the Meter's window to 20 ms and its scale to 0.3, and wire its peak into the Output's color in place of its level. The screen now jumps on the kick drum and falls away between.

```fbks
let kick = audio.in() |> filter(cutoff: 150) |> meter(window: 20ms, scale: 0.3)
kick.peak |> out.color
```

## Clouds

Add a Clouds and a Time. Wire the Time's t into the Clouds' z, turn its scale to 2.5, and wire it into the Output's color in place of the Meter. The clouds boil slowly; leave the Meter where it is for later.

```fbks
clouds(z: t, scale: 2.5) |> out.color
```

## Fold it

Add a Kaleidoscope, turn its segments to 8, and wire its x and y into the Clouds' x and y. The clouds fold into eight wedges round the middle.

```fbks
kaleidoscope(segments: 8) |> clouds(z: t, scale: 2.5) |> out.color
```

## Turn it

Add a Rotate in front of the Kaleidoscope, its x and y into the Kaleidoscope's. Add a Saw with its freq at 0.03 and its amp at 3.1416, and wire it into the Rotate's angle. Half a turn either way: the whole pattern goes round once every 33 seconds.

```fbks
saw(freq: 0.03, amp: 3.1416) |> rotate(angle: _) |> kaleidoscope(segments: 8)
  |> clouds(z: t, scale: 2.5) |> out.color
```

## Colors that go together

Add a Palette between the Clouds and the Output, with its cycles at 2. The gray clouds become bands of a few colors that suit each other.

```fbks
kaleidoscope(segments: 8) |> clouds(z: t, scale: 2.5) |> palette(t: _, cycles: 2) |> out.color
```

## Flash on the kick

Add a Gain between the Palette and the Output, and wire the kick's Meter peak into its gain. The kaleidoscope dims between kicks and flashes as each one lands.

```fbks
let kick = audio.in() |> filter(cutoff: 150) |> meter(window: 20ms, scale: 0.3)
clouds(z: t, scale: 2.5) |> palette(t: _, cycles: 2) |> gain(gain: kick.peak) |> out.color
```

## Loudness spreads the colors

Add a second Meter, wire the Line In's left into it, and turn its window to 2 s and its scale to 0.25. Wire its level into the Palette's spread. Quiet music keeps to tints of one color; loud music spreads them across the rainbow.

```fbks
let loud = audio.in() |> meter(window: 2s, scale: 0.25)
clouds(z: t, scale: 2.5) |> palette(t: _, cycles: 2, spread: loud)
```

## Trails outward

Add a Trails between the Gain and the Output, with its zoom at 0.97 and its persist at 0.82. Each flash leaves a ghost that spreads outward and fades before the next kick.

```fbks
color.gain() |> trails(zoom: 0.97, persist: 0.82) |> out.color
```
