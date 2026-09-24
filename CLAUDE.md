# Flyback

Flyback is a patchable synthesiser for .NET 10: one module graph generates both a picture and a sound. See [README.md](README.md) for the build and run commands, [docs/engineering-guide.md](docs/engineering-guide.md) for the architecture, the code style and how tests are written, [docs/glossary.md](docs/glossary.md) for the one word each thing is called by, and `docs/adr/` for the decisions behind the design.

Standing rules for working in this repo are in `.claude/rules/`, and Claude Code loads them every session:

- `git-workflow.md`: commit straight to `main`, and isolate from other sessions' uncommitted work.
- `packages.md`: check the packages and the SDK before starting anything big; take cheap upgrades, hand off expensive ones.
- `prose-style.md`: American spelling; succinct comments that never narrate history.
- `adrs.md`: check `docs/adr/` before proposing a refactor; rewrite a day-old ADR in place; number a new ADR from `main` at commit time.
- `changelog.md`: what CHANGELOG.md may contain.
- `tests.md`: rank a run by duration, treat an unexplained slow test as a defect, and ship every feature with a Gherkin scenario written as a requirement.
- `website.md`: a change to anything `site/` describes updates the site in the same commit.
- `preset-site-defaults.md`: the preset site's default presets are files, migrated by the change that breaks them.
- `windows-shell.md`: PowerShell and Bash-heredoc pitfalls that corrupt files.
- `looking-at-a-patch.md`: if only the picture or the sound needs looking at, use `flyback-viewer`, not the editor and not a render.
- `running-the-app.md`: before launching the real window, wait for any other Flyback to close, and drive only the process you started.
- `release-key.md`: every key is `RELEASE_SIGNING_KEY`, a local test key here, made anew whenever it is missing; `./release.sh` tries a release.

Task-specific know-how is in `.claude/skills/`, and Claude Code loads each skill when its description matches the task:

- `authoring-presets`: building and measuring a showcase or teaching preset.
- `played-presets`: presets with MIDI voices and panel knobs.
- `convenience-modules`: the wrapper modules (Stroke, Fade, Desk, Echo, Hiss and the rest) and porting presets onto them exactly.
- `site-screenshots`: retaking `site/assets/shots` from the real app.
- `site-audio-tracks`: rebuilding the website's listening-row MP3s.
- `build-artifacts`: running `release.sh` after a feature lands on `main`, only ever on `main` in the main worktree, so the build is under `dist/`.
- `performance`: where a heavy patch's time goes, the open leads for making it run or export faster, what has been ruled out, and how to measure it.

`vibe-check`, which finds what the catalog is missing by reading what an unchecked agent reaches for, lives in the [vibe-mode kit](https://github.com/markovcd/vibe-mode) and is installed under `~/.claude/skills/` on this machine.

Commands in `.claude/commands/` are run by name rather than matched:

- `/bughunt`: hunt for bugs, confirm each as a failing test, fix it, and file the test where it belongs.
- `/release`: check the changelog and the plugin contract, land the release commit on main, and fire the Release workflow.

`.claude/settings.json` turns on the `Flyback Vibe` output style (`.claude/output-styles/vibe.md`) for every session in this repo. It governs wording only: what gets checked, weighed and recommended is unchanged.
