# The patch as text

From: Drone

The same patch written out as text, changed by typing, and given a knob.

## Open the text

Press F2. The patch is written out in Flyback's language beside the canvas: each `let` names a module, and `|>` is a wire. Change a line and press Ctrl+Enter to apply it; the canvas follows.

## Lower the drone

Find `sine(freq: 110)` and make it `sine(freq: 55)`, then press Ctrl+Enter. The drone drops an octave.

```fbks
let swell = sine(freq: 0.15, amp: 0.5, bias: 0.5)
sine(freq: 55) * swell |> out.left
```

## A second voice

Add a line under it: `sine(freq: 55.5) * sine |> out.right`, and apply it. The right speaker is half a hertz off the left, so the two beat slowly against each other.

```fbks
let swell = sine(freq: 0.15, amp: 0.5, bias: 0.5)
sine(freq: 55) * swell |> out.left
sine(freq: 55.5) * swell |> out.right
```

## Give it a knob

Add `panel pitch = 0.5` at the top, change the left voice to `sine(freq: pitch(40..80))`, and apply it. The knob panel (Ctrl+K) has a knob called pitch, and turning it sweeps the left voice from 40 to 80 Hz against the steady right one.

```fbks
panel pitch = 0.5
let swell = sine(freq: 0.15, amp: 0.5, bias: 0.5)
sine(freq: pitch(40..80)) * swell |> out.left
sine(freq: 55.5) * swell |> out.right
```
