# ADR-0191: The Sound tab picks the backend where more than one can play

**Status:** Accepted · 2026-10-09 · amends
[0085](0085-a-sound-backend-declares-its-own-settings.md)'s "only the preferred backend's
form is shown"

## Context

The backend that played was the supported one with the highest `Priority`, and nothing
else. That held while each platform had one backend, and JACK above ALSA
([0190](0190-linux-sound-goes-through-jackd-when-one-is-running.md)) could rank on a
condition somebody had to set up. ASIO on Windows cannot: Realtek's driver package installs
an ASIO driver on ordinary desktops, so "a driver is installed" says nothing about wanting
it, and ranking it above WASAPI would take those machines' sound card away from every
other program.

## Decision

**Where more than one backend can play, the Sound tab asks which**, as *Play through*,
above the backend's own form. The list is every supported backend, in priority order.
Picking one shows its form at once; Save plays through it.

**The pick is kept in `output.json` as `soundOutput`, the backend's id.** Empty, the
default, means the ranking decides. A Save that did not change the pick leaves it as it
was, so a machine that never picked keeps following the ranking.

**A picked backend that cannot play this launch gives way to the preferred one**, and
stays picked: a driver reinstalled or a server started brings it back.
`PluginCatalog.AudioOutput(id)` is the one place that resolves it, and the editor, the
viewer and the Sound tab all ask it.

## Consequences

A machine with one backend sees the tab as before. Linux with jackd running can now
choose ALSA without stopping the server.
