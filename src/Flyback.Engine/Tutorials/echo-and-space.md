# Echo and space

From: Sequence

A tune given echoes, a room to ring in, and a trail on the picture, with a knob for how much echo.

## An echo

Add a Delay between the Multiply and the Output: wire the Multiply's out into the Delay's in, and the Delay's out into the Output's left. Each note comes back fainter, a quarter of a second later.

```fbks
let voice = sine() * notes().gate
voice |> delay() |> out.left
```

## Echo in time

Turn the Delay's time to 0.667, two steps of the tune, and its feedback to 0.6. The echoes land on the beat and answer the notes two steps behind.

```fbks
let voice = sine() * notes().gate
voice |> delay(time: 0.667, feedback: 0.6) |> out.left
```

## A room

Add a Reverb between the Delay and the Output's left.

```fbks
let voice = sine() * notes().gate
voice |> delay(time: 0.667, feedback: 0.6) |> reverb() |> out.left
```

## Both sides of the room

Wire the Reverb's wide into the Output's right, and turn its size and decay to 0.8. The tail is smeared one way on the left and the other on the right, so the room opens out around you.

```fbks
let room = sine() * notes().gate |> delay(time: 0.667, feedback: 0.6) |> reverb(size: 0.8, decay: 0.8)
room |> out.left
room.wide |> out.right
```

## Trails on the picture

Add a Trails between the HSV and the Output's color. Each frame is left behind, fading, so the rings smear as they change, the way the echoes do.

```fbks
color.hsv() |> trails() |> out.color
```

## A knob for the echo

Show the knob panel with Ctrl+K and add a knob with +. Click it, click the Delay's mix on the canvas, and press Esc. Turning the knob now runs the tune from dry to all echo.

```fbks
panel wet = 0.5
sine() * notes().gate |> delay(time: 0.667, feedback: 0.6, mix: wet) |> reverb()
```
