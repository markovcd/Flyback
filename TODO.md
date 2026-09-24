# To do

Work the user has asked for and nobody has started. Take an item off when it lands on `main`.

- **Seek bar in the editor.** Scrub the patch's clock to any point, over a length the user sets.
- **One object for what the window's regions share.** Bundle the canvas, the document, the files, the plugins, the report line and the usage counter, so `PresetSlot` (15 constructor arguments) and `Playback` (12) take it instead (ADR-0148).
- **The picture on another monitor as a class of its own.** `ShowPictureOn` and `BringPictureBack` in `MainWindow.FullScreen.cs` build and tear down that window by hand (ADR-0129).
