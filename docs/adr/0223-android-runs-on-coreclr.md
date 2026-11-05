# ADR-0223: Android runs on CoreCLR

**Status:** Accepted · 2026-11-05 · *user-directed* · amends
[0184](0184-the-editor-runs-on-android-with-its-plugins-linked-in.md)

## Context

The sound's speed is IL generated at run time (0076). On Android that IL ran on Mono's JIT,
and a Pixel 9 Pro played Mycelium at 1.2× real time: about an eleventh of a desktop, where
the hardware accounts for perhaps 1.5× of the gap. Heavier presets crackled, and Mycelium
did too once the phone warmed and slowed. .NET 10 ships CoreCLR for Android as an
experimental option, `UseMonoRuntime=false`, the same runtime and JIT the desktop uses.
Built that way, the same phone, cold, played Mycelium at a steady 1.54×.

## Decision

**The Android app runs on CoreCLR.** The project sets `UseMonoRuntime=false`; nothing else
in the code changes. Arek chose to take it while it is still marked experimental, ahead of
.NET 11 making it the default.

## Consequences

- The sound is worked out about 30% faster on the phone; the rest of the gap to the desktop
  stays open.
- The arm64 APK grows from about 24 MB to 45 MB.
- A cold start took 7.9 s on the Pixel, with logcat twice timing out waiting for
  `libandroid.so`. It is not yet timed against Mono, so whether CoreCLR costs startup is open.
- An experimental runtime can break on an SDK update; the daily emulator run (0212) and the
  Android build on every push are what would catch it.
