# Rain on a Window on ASIO4ALL crashes inside AMD's OpenGL driver

Found on 2026-10-12, on `main` at `af78a331`. It is on TODO.md; take it off there, and
delete this file, in the commit that lands it.

- **Severity:** Medium
- **Status:** Open: needs a session on the Windows machine

## What is wrong

On Windows with ASIO4ALL as the sound output and the picture drawn through OpenGL, the
editor dies while Rain on a Window plays, and once when Plasma was played. Through WASAPI
it has not crashed. The freeze that came first (the window waiting on a driver that
stopped answering) is fixed in `af78a331`; this crash is a separate fault.

## Evidence

- Every crash is an access violation (`c0000005`) in `atio6axx.dll` 32.0.21045.5002,
  AMD's OpenGL driver, at the same offset `0x95e5bc`. The .NET Runtime entry (1026)
  is the same unhandled native exception; nothing reaches the console.
- The freeze before it was logged as Application Hang 1002, hang type cross-thread.
- Rain on a Window's sound is 3,656 ops: 0.6-0.8x real time interpreted and about 2x as
  IL on a cloud machine, measured with a throwaway test compiling it against
  `ShippedPlugins.Loaded.Modules`. Its picture is 2,072 ops with four feedback cells.
- After the ASIO4ALL routing was changed and Flyback killed, a launch took a long time
  and Plasma crashed on play; later the same build played Plasma and heavy patches on
  ASIO with the GPU, then Rain on a Window crashed again.

## Not yet known

Whether ASIO matters to the crash or only to its timing. These runs of Rain on a Window
split it:

1. ASIO, GPU, Settings → Picture → driver Direct3D (ANGLE, no `atio6axx.dll`).
2. WASAPI, GPU, driver OpenGL.
3. ASIO, GPU off.

Run with `--trace stalls.txt` to see how long each sound callback and frame took before it
died. A debugger or a dump (`procdump -e -ma Flyback.exe`) on the machine would name the
GL call and the thread it came from.

## Suspects

- The preview's clock is the sound's (`Transport.Start` hands it `audio.Listen` and
  `audio.Time`), so the picture's pacing follows ASIO4ALL's block size.
- `AsioAudioDevice` unloads and reloads the driver on every pause and play; DAWs load
  it once. Keeping it loaded and only calling `start` and `stop` would rule out the
  churn.
- A shader or texture this patch uploads that AMD's GL compiler mishandles, which run 2
  would show without ASIO.

## Workaround

Settings → Picture → driver Direct3D.
