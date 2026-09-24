# Keep the website in step

The website in `site/` (index.html, tutorials.html, plugins.html; published to https://markovcd.github.io/Flyback/ by `.github/workflows/pages.yml` on push to main) must stay accurate. Any change to functionality it describes gets the matching site edit in the same commit: shortcuts, toolbar buttons, module/port names, presets, text-language syntax, CLI commands and flags, settings tabs, recording formats, the plugin contract (plugins.html), supported platforms.

After a user-visible change, grep `site/*.html` for the affected names and fix the prose.

- The tutorials' `.fbks` snippets and `data-patch` diagrams must still build. Check them with `flyback-cli check` and by opening them in the app, since the app loads plugins (`hsv` is ambiguous there and needs `color.hsv`) and the CLI does not.
- If the UI changes visibly, a full-window screenshot in `site/assets/shots` it stales goes on `.claude/skills/site-screenshots/stale.md` for a retake session of its own; a canvas-only shot is retaken in the same commit (see the `site-screenshots` skill).
- **Never draw a module, a socket or a wire in HTML, CSS or SVG.** The canvas is the only thing that knows what one looks like, and a second drawing of it goes stale the moment a module gains a glyph, a skin or a state (ADR-0119). A patch on the site is a photograph of a real one: `SHOT_DIR=<somewhere> ./tests/Flyback.App.Tests/bin/Release/net10.0/Flyback.App.Tests.exe -method "*PatchShotTests*"`, then webp at quality 88. Add a patch to `PatchShotTests` rather than drawing it.
- plugins.html is the plugin guide. Edit it for any change to `src/Flyback.Plugins/IFlybackPlugin.cs` (registry methods), the audio/MIDI/secret/assistant interfaces, `NodeDef`/`PortSpec`/`ExtraField`, `Emitter` state calls (cells, planes, delay lines), `PluginHost`/`ModuleCatalog` refusal rules, `PluginProject` in Flyback.App.csproj, or the shipped plugin names it cites.
- The platforms the site claims for downloads must match what `.github/workflows/release.yml` and the Dockerfile's default RIDS actually publish (currently win-x64, osx-arm64, linux-x64).
- Site colors mirror `src/Flyback.Ui/Controls/Colors.cs` and `Flyback.xshd`, so a theme change there means updating `site/assets/site.css`.
- A preset's track on the site changes with the preset (see the `site-audio-tracks` skill).

Site changes are not changelog entries. American spelling applies.
