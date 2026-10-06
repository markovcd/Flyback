# ADR-0181: A Beam draws two taps against each other on phosphor

**Status:** Accepted · 2026-10-06 · *user-directed* · a third kind of chart beside
[0053](0053-a-scope-records-what-the-speakers-played.md)'s Scope and
[0073](0073-an-analyzer-is-a-scope-filled-with-a-spectrum.md)'s Analyzer · answers
the general path [0043](0043-a-scan-is-a-probe-read-backwards.md) left undrawn

## Context

Oscilloscope music (Jerobeam Fenderson, Hansi Raber) is stereo sound made to be
watched on an oscilloscope in X-Y mode: left across, right up. What makes it look
like a tube rather than a plot is the beam. It lays down the same energy every
moment, so a stroke it crosses slowly is bright and a jump between strokes all but
vanishes; the spot is a Gaussian with a halo; and the phosphor fades behind it.

None of that is a function of one pixel. The brightness at a pixel is a sum over
every segment the beam crossed lately, and 0043 already measured that marching
samples per pixel costs far more than a frame has.

## Decision

**A Beam is a Scope whose buffer holds a picture.** It taps two inputs, `x` and
`y` (`NodeDef.TappedInputs`), and its chart is a beam (`NodeDef.ChartsBeam`,
`ChartKind.Beam`). Once a frame, where a Scope's buffer is resampled, `Beams.Draw`
rasterizes the rings into a 512-texel square: each evaluation's energy is spread
along the segment the beam crossed as Gaussian spots a texel apart, so brightness
is one over speed. Spots closer than that are merged into one, so a beam that
dwells costs no more than one that moves. A 128-texel copy, shrunk and blurred, is
the halo.

**The fade is a weight, not a history.** Each evaluation is weighted by
`exp(-age / persistence)` and the last five persistences are drawn. The picture
depends on the ring and the moment alone, never on the frame rate or on what the
last frame drew, so a still, an export and the preview of the same moment agree.

**The picture reads it as a table.** The buffer's rate is one, so a position is a
texel's index; a bilinear read is two `Table` reads a row apart. Nothing new
crosses to the shader: 0167's float texture already carries it, and the web
editor's worker hands it to the page as it hands a Scope's chart, with a kind code
of 2.

**Intensity, glow and hue are sockets; persistence is a knob.** What is applied
after the read may be swept. What decides the rasterizing is read at compile time,
as a Scope's window is. Each channel saturates on its own, so a slow stroke burns
green and then white.

**A still plays the sound up to its moment.** `flyback-cli render -o x.png --at s`
plays from nought to `s` before drawing whenever the picture reads a chart or a
Meter, so a Beam, a Scope and a Meter can be checked from the command line.

## Consequences

- Drawing costs a few milliseconds a frame on the drawing thread, in proportion to
  how far the beam travels. Noise, which crosses the screen every evaluation,
  would cost far more, so past a budget of travel every *n*th evaluation is drawn
  with the weight of those skipped.
- Full scale is fixed: -1 to 1 is the picture's height, and the beam's width is
  fixed in texels. Zooming is a patch's business.
- A Beam's buffer is about a megabyte, uploaded each frame; in a page it crosses
  from the worker each frame too. Its two rings are a Scope's, 32 seconds each,
  though it reads at most two thirds of a second of them.
- It inherits the Scope's three cliffs: nothing while sound is off, nothing the
  speakers do not reach, and only the past.
