# To do

Work the user has asked for and nobody has started. Take an item off when it lands on `main`.

- **Seek bar in the editor.** Scrub the patch's clock to any point, over a length the user sets.
- **Optional stats overlay on fullscreen/viewer.** Text overlay, off by default, showing fps, ops time and similar.
- **A DI container for the editor and the viewer.** Microsoft.Extensions.DependencyInjection, as the preset server already has through ASP.NET: the regions' constructors and statics such as `Startup.Plugins` resolved from one composition root. Needs an ADR, since ADR-0148 wires by hand.
