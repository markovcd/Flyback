# Stale site screenshots

Shots in `site/assets/shots` that no longer match the app and need the real window to retake, waiting for a retake session. One line per shot: the file, where the site uses it, what no longer matches, the date it went stale. A shot comes off the list in the commit that retakes it.


- Full-window editor shots that show the transport row (assistant, knob-panel, euclid-kit, whole-band, flyback-theme, as each has it): the row lacks the Live button at its right end. 2026-10-26.
- flyback-theme, knob-panel (index.html), assistant, presets-list (index.html): toolbar buttons have no fill at rest and toggles, sliders and focus are amber, not blue. flyback-theme and knob-panel are `flyback-cli shot` but need the plugin presets, which a plain build's `shot --presets` does not list. 2026-11-02.
- flyback-theme, assistant, presets-list (index.html): the toolbar's symbols are panel marks with their names under them, and the bar is 10 px taller, which moves knob-panel's crop down too. 2026-11-02.
