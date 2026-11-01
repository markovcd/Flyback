# Text that builds itself

From: Empty

A voice written once as a def, called for a chord, then stamped out by a loop over a list of notes, each voice drawing a ring that breathes with it.

## One voice

Press F2 to open the text, type `sine(freq: note(A2), amp: 0.2) |> out.left` and press Ctrl+Enter to apply it. A Note turns A2 into hertz for a quiet Sine.

```fbks
sine(freq: note(A2), amp: 0.2) |> out.left
```

## A voice you can call

Replace the line with a def and two calls to it:

`def voice(pitch) = sine(freq: note(pitch), amp: 0.2)`

`voice(A2) + voice(E3) |> out.left`

Apply it. A def is a stencil: each call stamps out its own Note and Sine, so two calls are two voices, a fifth apart, and the canvas draws a box round each.

```fbks
def voice(pitch) = sine(freq: note(pitch), amp: 0.2)
voice(A2) + voice(E3) |> out.left
```

## A chord

Add `+ voice(C4)` to the end of the sum and apply it. A third voice makes it an A minor chord, for the cost of a word.

```fbks
def voice(pitch) = sine(freq: note(pitch), amp: 0.2)
voice(A2) + voice(E3) + voice(C4) |> out.left
```

## Each at its own pace

Give the def a second argument, a slow Sine that swells the voice in and out:

`def voice(pitch, rate) = sine(freq: note(pitch), amp: sine(freq: rate, amp: 0.1, bias: 0.1))`

Then call it as `voice(A2, 0.07) + voice(E3, 0.11) + voice(C4, 0.13)` and apply it. Each voice breathes at its own rate, so the chord never sounds the same twice in a row.

```fbks
def voice(pitch, rate) = sine(freq: note(pitch), amp: sine(freq: rate, amp: 0.1, bias: 0.1))
voice(A2, 0.07) + voice(E3, 0.11) + voice(C4, 0.13) |> out.left
```

## A loop over the notes

Name the chord as a list with a fourth note in it, `let chord = [(A2, 0.07), (E3, 0.11), (C4, 0.13), (G4, 0.17)]`, loop over it with `let voices = for (pitch, rate) in chord { voice(pitch, rate) }`, and replace the sum with `sum(voices) |> out.left`. Apply it. The loop calls the def once for each item, so a fourth voice plays without a fourth call, and another chord is only a different list.

```fbks
def voice(pitch, rate) = sine(freq: note(pitch), amp: sine(freq: rate, amp: 0.1, bias: 0.1))
let chord = [(A2, 0.07), (E3, 0.11), (C4, 0.13), (G4, 0.17)]
let voices = for (pitch, rate) in chord { voice(pitch, rate) }
sum(voices) |> out.left
```

## A ring for each voice

Add a second def that draws a ring breathing at a rate, `def halo(rate) = circle(radius: sine(freq: rate, amp: 0.4, bias: 0.4)) |> fill(distance: _).outline`, loop over the same list with `let rings = for (pitch, rate) in chord { halo(rate) }`, and add `sum(rings) |> out.color`. Apply it. Four rings grow and shrink, each in step with the voice that shares its rate, so you can watch the chord breathe.

```fbks
def voice(pitch, rate) = sine(freq: note(pitch), amp: sine(freq: rate, amp: 0.1, bias: 0.1))
def halo(rate) = circle(radius: sine(freq: rate, amp: 0.4, bias: 0.4)) |> fill(distance: _).outline
let chord = [(A2, 0.07), (E3, 0.11), (C4, 0.13), (G4, 0.17)]
let voices = for (pitch, rate) in chord { voice(pitch, rate) }
let rings = for (pitch, rate) in chord { halo(rate) }
sum(voices) |> out.left
sum(rings) |> out.color
```
