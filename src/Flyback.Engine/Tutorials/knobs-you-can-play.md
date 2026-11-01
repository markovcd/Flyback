# Knobs you can play

From: Empty

A filtered saw and a picture of rings, played from the knob panel: one knob that opens the filter and adds rings, a second for the pitch, and a MIDI controller to turn them with.

## A sound to play

Add a Saw and a Filter. Turn the Saw's freq to 55, wire its out into the Filter's in, and the Filter's low into the Output's left. A low buzz, dulled by the filter.

```fbks
saw(freq: 55) |> filter() |> out.left
```

## A knob for the cutoff

Show the knob panel with Ctrl+K and add a knob with +. Click its name, click the Filter's cutoff on the canvas, and press Esc. Turning the knob now opens and closes the filter.

```fbks
panel cutoff = 0.5
saw(freq: 55) |> filter(cutoff: cutoff) |> out.left
```

## Over a range

Select the Filter. In its inspector, the cutoff follows the knob between two numbers: make them 100 and 2000. The whole turn of the knob is now the part of the sweep worth hearing, from muffled to bright.

```fbks
panel cutoff = 0.5
saw(freq: 55) |> filter(cutoff: cutoff(100..2000)) |> out.left
```

## One knob, two sockets

Click the knob's name again, click the Filter's resonance, and press Esc. In the inspector, have the resonance follow it from 0.1 to 0.8. One turn now opens the filter and sharpens it at once, the way a synth's macro knob does.

```fbks
panel cutoff = 0.5
saw(freq: 55) |> filter(cutoff: cutoff(100..2000), resonance: cutoff(0.1..0.8)) |> out.left
```

## The picture turns too

Add a Rings and wire it into the Output's color. Link its freq to the same knob, from 2 to 12. Turning the knob up brightens the sound and crowds the screen with rings; turning it down does both the other way.

```fbks
panel cutoff = 0.5
saw(freq: 55) |> filter(cutoff: cutoff(100..2000), resonance: cutoff(0.1..0.8)) |> out.left
rings(freq: cutoff(2..12)) |> out.color
```

## A knob for the pitch

Add a second knob and link the Saw's freq to it, from 40 to 80. Play the two knobs against each other: the pitch with one hand, the brightness with the other.

```fbks
panel cutoff = 0.5
panel pitch = 0.5
saw(freq: pitch(40..80)) |> filter(cutoff: cutoff(100..2000), resonance: cutoff(0.1..0.8)) |> out.left
rings(freq: cutoff(2..12)) |> out.color
```

## Turn them by hand

With a MIDI controller plugged in, open a knob's menu with the dots under it, pick Learn MIDI controller, and turn a knob on the controller. That knob now turns this one, and the patch saves which it was.
