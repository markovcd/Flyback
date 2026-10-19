# Draw with shapes

From: Empty

A circle and a star, moved, melted into one, set turning, and outlined.

## A circle

Add a Circle and a Fill. Wire the Circle's distance into the Fill's distance, and the Fill's fill into the Output's color. A shape is a distance to its edge, negative inside, and the Fill turns it into ink.

```fbks
circle() |> fill(distance: _) |> out.color
```

## Move it

Add a Translate. Wire its x and y into the Circle's x and y, turn its dx to -0.4, and turn the Circle's radius to 0.3. A Translate moves whatever reads its coordinates, so the circle slides left.

```fbks
translate(dx: -0.4) |> circle(radius: 0.3) |> fill(distance: _) |> out.color
```

## A star beside it

Add a Star with a Translate of its own, its dx at 0.4, and a Combine. Wire the Circle's distance into the Combine's a, the Star's into its b, and the Combine's union into the Fill's distance in place of the Circle's. A Combine's outputs are distances too, so the Fill draws both shapes.

```fbks
let round = translate(dx: -0.4) |> circle(radius: 0.3)
let pointed = translate(dx: 0.4) |> star()
combine(a: round, b: pointed) |> fill(distance: _) |> out.color
```

## Melt them

Turn the Combine's smoothness to 0.4. Where the two shapes come near, they melt into one, like two drops of water.

```fbks
let round = translate(dx: -0.4) |> circle(radius: 0.3)
let pointed = translate(dx: 0.4) |> star()
combine(a: round, b: pointed, smoothness: 0.4) |> fill(distance: _) |> out.color
```

## Spin the star

Add a Rotate between the Star's Translate and the Star: wire the Translate's x and y into the Rotate's, and the Rotate's into the Star's. Add a Time and wire its t into the Rotate's angle. The star turns on its own center, about once every six seconds, and the melted seam follows its points round.

```fbks
let pointed = translate(dx: 0.4) |> rotate(angle: t) |> star()
```

## Breathe

Add a Sine with its freq at 0.5, its amp at 0.2 and its bias at 0.3, and wire it into the Combine's smoothness. The seam swells from a narrow neck to a thick melt and back, once every two seconds.

```fbks
let round = translate(dx: -0.4) |> circle(radius: 0.3)
let pointed = translate(dx: 0.4) |> rotate(angle: t) |> star()
combine(a: round, b: pointed, smoothness: sine(freq: 0.5, amp: 0.2, bias: 0.3)) |> fill(distance: _)
  |> out.color
```

## Just the edge

Wire the Fill's outline into the Output's color in place of its fill. The same shapes, drawn as a line round their edge, melting and turning.

```fbks
let round = translate(dx: -0.4) |> circle(radius: 0.3)
let pointed = translate(dx: 0.4) |> rotate(angle: t) |> star()
let ink = combine(a: round, b: pointed, smoothness: sine(freq: 0.5, amp: 0.2, bias: 0.3)) |> fill(distance: _)
ink.outline |> out.color
```
