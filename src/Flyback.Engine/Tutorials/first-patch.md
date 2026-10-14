# Your first patch

From: Empty

A picture from one Sine: a pulse, then stripes, then rings that move.

## Add a Sine

Press Space on the canvas, type sine and press Enter. On a touch screen, hold a finger on the canvas instead.

```fbks
let wave = sine()
```

## Wire it into the color

Drag from the Sine's out socket into the Output's color socket. The whole picture brightens and darkens once a second: with nothing wired into its in, a Sine runs on Time.

```fbks
sine() |> out.color
```

## Lay it across the screen

Add a Coordinates and wire its x into the Sine's in. Now the Sine runs across the picture instead of through time, and the pulse becomes one soft band.

```fbks
x |> sine() |> out.color
```

## More stripes

Select the Sine and turn its freq to 4, on the module or in the inspector. Four cycles across the screen are four stripes.

```fbks
x |> sine(freq: 4) |> out.color
```

## Rings

Add a Rings and wire its out into the Output's color, in place of the Sine. Rings needs no coordinates: with nothing wired into x and y, it reads the pixel's own.

```fbks
rings() |> out.color
```

## Make them move

Add a Time and wire its t into the Rings' offset. The rings travel inward, one ring a second.

```fbks
rings(offset: t) |> out.color
```

## Keep it

Press Ctrl+S to save the patch as a file of your own. The Sine and the Coordinates are still on the canvas; delete them, or wire them into something next.
