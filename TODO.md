# To do

Work the user has asked for and nobody has started. Take an item off when it lands on `main`.

- **Seek bar in the editor.** Scrub the patch's clock to any point, over a length the user sets.
- **Optional stats overlay on fullscreen/viewer.** Text overlay, off by default, showing fps, ops time and similar.
- **Split `NodeEditor` the way `MainWindow` was split.** About 4,200 lines across 15 partial files of one control; find its hubs and pull the regions out into classes (ADR-0148, and ADR-0017 for why it is one control).
