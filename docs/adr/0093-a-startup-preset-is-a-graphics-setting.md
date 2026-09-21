# ADR-0093: A startup preset is a Graphics setting

**Status:** Accepted · 2026-09-17 · *user-directed* · amends
[0082](0082-the-output-settings-move-to-the-settings-window.md)

## Context

The window always opened on Plasma: `MainWindow`'s constructor called
`Presets.Default()`, a method that hard-coded the choice, and set the toolbar's
own preset list to whatever `PluginCatalog.Presets[0]` happened to be. Neither
read a setting, so somebody who always starts from a different preset — their
own instrument's, or a plugin's — picked it again by hand every launch. That is
exactly the property-of-the-machine-forgotten-at-every-launch problem 0082
already fixed for Size, Render and the rest.

## Decision

**`DefaultPreset` is an `OutputSettings` string, in the Graphics section**,
kept in `output.json` beside `Width` and the rest. It is a name rather than a
row: the row a preset sits at depends on the plugin catalogue, which
`OutputSettings` does not know, so a name a build does not offer is read as
though nothing were chosen — the same courtesy `VideoFormat` gets from a
format list that is not fixed either.

**The Graphics section gets a fourth row, "Startup patch"**, a plain picker of
every preset's name in the same order the toolbar's own list already shows
them — ideas, then interplay, then the big ones, then the blank canvas
(`Presets.All`'s order, which `PatchPreset.Kind` sorts on). Built once and
kept, like every other Graphics control, and filled in once the window's
plugin catalogue exists rather than in its own field initializer.

**Choosing one here changes nothing on the canvas.** It is a draft, like the
size and the renderer above it, and unlike the toolbar's own list: pressing
Cancel or the cross leaves it exactly as unconsidered, and Save writes only
where the next launch starts, never what the current one is showing.

**The window opens on it directly, not through `Presets.Default()`.** That
method returned `Plasma(NodeCatalog.Current)`, a fixed patch built against a
catalogue the window's own plugins might not agree with; it is gone; the
constructor now looks the saved name up in the plugin catalogue's own preset
list — the ordered one the toolbar uses — and builds whichever it finds there,
falling back to the first for a name that is missing, empty, or was never
saved. The toolbar's own selection is set to match without rebuilding the
patch a second time: it is given the row already agreed on before it is told
to show it, so its own change handler — which exists to rebuild the canvas
when somebody actually picks something — finds nothing has moved and does
nothing.

## Consequences

**A settings file with nothing chosen, or naming a preset since removed,
still opens on the first preset** — Plasma on a plain install — so nothing
about a fresh launch or an existing settings file changes underneath anybody
who has not touched the new row.

**The window's title, on the first frame, is always the preset it actually
opened on.** It was already computed from what the toolbar's list showed;
now what the toolbar's list shows is what was asked for, rather than a name
picked independently of the patch built beside it.

## Amendment, 2026-09-21: the row opens the gallery

The toolbar's list became a gallery of tiles, and a plain picker of names beside
it was the one place a preset was still chosen blind. The "Startup patch" row is
a button that reads the chosen name and opens the same gallery, tiles, filter and
audition included, over the settings window. A tile picked there is the draft the
decision above describes: it names the row and changes nothing else.

**That gallery saves and deletes nothing.** The presets somebody saved are there
to be picked, with no card to keep the patch on the canvas and no Delete on a
tile, because a settings window that is cancelled has to leave nothing behind.
With none saved the run is not shown at all.

**A name this launch does not offer is still the name on the button**, and what
Save writes back, which is what listing it at the end of the picker was for. It
is built fresh at every click, so a preset saved or deleted since is there or
gone.
