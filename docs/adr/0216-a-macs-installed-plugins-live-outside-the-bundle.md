# ADR-0216: A Mac's installed plugins live outside the bundle

**Status:** Accepted · 2026-10-27 · extends
[0199](0199-the-microsoft-store-gets-an-msix-the-store-signs-and-updates.md)'s data-folder
plugins to a macOS bundle

## Context

The editor installed a plugin from the preset site into `Contents/MacOS/plugins/` inside
`Flyback.app`, under its assembly name, `Flyback.Plugins.Speech`. The next update's
`codesign --force --deep` refused the bundle: a folder in `Contents/MacOS` with a dot in
its name is taken for a nested bundle, and this one is not a valid bundle. The shipped
plugins' folders (`MacIO`, `Keychain`) have no dot, so a fresh copy signed fine. Any
file written into a signed bundle breaks its seal anyway.

## Decision

**A copy running from a `.app` installs plugins under the data folder**, as a Store copy
does: `PluginHost.DefaultDirectory` is `~/.config/Flyback/plugins` there. The shipped
plugins still load from the bundle, and an installed one of the same name replaces a
shipped one.

**What an older version put in the bundle is moved out** by `BundlePlugins.MoveOut`:
at each start, by the updater before it signs, and by `install.sh` before it signs.
A folder the bundle's `plugins.sha256` does not list moves, and its allowance moves with
it, the files it names still checked where it went.

**An update whose signing fails is still installed.** Its files are already in place,
so the note says it is and why it is unsigned, rather than counting a failed install.

## Consequences

- Removing `Flyback.app` no longer removes plugins the user installed.
- A bundle is changed only by an update or `install.sh`, both of which sign it after.
