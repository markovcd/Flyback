# Stale site screenshots

Shots in `site/static/assets/shots` that no longer match the app and need the real window to retake, waiting for a retake session. One line per shot: the file, where the site uses it, what no longer matches, the date it went stale. A shot comes off the list in the commit that retakes it.


- presets-list (index.html): drawn before the editor's own look. `shot --gallery` draws the gallery headless but leaves every preset's picture empty, so it still needs the real window. 2026-11-05.
