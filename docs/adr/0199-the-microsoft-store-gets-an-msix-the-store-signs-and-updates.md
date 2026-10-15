# ADR-0199: The Microsoft Store gets an MSIX the Store signs and updates

**Status:** Accepted · 2026-10-15 · *user-directed* · extends
[0088](0088-a-release-installs-itself-at-the-next-start.md)

## Context

The user asked for Flyback on the Microsoft Store. The Store takes a Windows desktop
program in one of two ways.

- **An `.exe` or `.msi` installer at a URL we host** (Store policy 10.2.9). Every PE
  file in it, plugins and native libraries included, must be signed with a certificate
  from a CA in Microsoft's Trusted Root Program, and the install must be silent. The
  cheap certificate service, Azure Artifact Signing, takes individuals only in the US
  and Canada.
- **An MSIX package.** The Store signs it on ingestion, so no certificate is bought,
  and the Store installs and updates it. Its install folder (`WindowsApps`) is
  read-only.

The developer account is free. The name "Flyback" was already reserved by someone
else, so the listing is **Flyback Modular**, publisher "Potato Smasher".

Three things a release does today assume a folder Flyback can write to: the updater
replaces the copy's own files (0088), plugins are installed into `plugins/` beside the
executable, and file types are registered by writing `HKCU\Software\Classes` at runtime.

## Decision

**The Store gets an MSIX of the release's own `win-x64` folder.** `scripts/pack-msix.ps1`
lays `src/Flyback.Editor.Desktop/Store/AppxManifest.xml` and its logos beside the
folder's files and packs them with `makeappx`. The package is unsigned; the Store signs it.
The zip and its updater stay exactly as they are for everyone who doesn't use the Store.

**A Store copy is told apart by its package identity** (`StoreCopy.Running`,
`GetCurrentPackageFullName`), and then:

- **The Store updates it.** `Installation.Current()` is null for a Store copy, so the
  updater never looks and never hands off.
- **Installed plugins go under the data folder.** `PluginHost.DefaultDirectory` is
  `%AppData%\Flyback\plugins`. The shipped plugins still load from the package's
  `plugins/`, checked against its `plugins.sha256`, and an installed plugin of the same
  name replaces the shipped one rather than loading beside it.
- **Windows registers the file types from the manifest**, which names the same four
  kinds and icons as `FileTypes.Kinds` (`StoreManifestTests`). With no `Installation`,
  `FileTypes.ForThisCopy()` writes nothing to the registry.

**File-system write virtualization is off** (`desktop6:FileSystemWriteVirtualization`,
the restricted capability `unvirtualizedResources`). `%AppData%\Flyback` is then the
real folder, the one a zip copy, a terminal and the assistant's own program (0172) all
read. The capability needs a justification in the submission, which this paragraph is.

**The command line keeps its name through aliases.** `flyback.exe`, `flyback-cli.exe`
and `flyback-viewer.exe` are execution aliases with the console subsystem, so a
terminal reaches them as it reaches a zip copy's.

**Release packs, installs and attaches it.** `store.yml` runs after the release is up:
it downloads the `win-x64` zip, packs the MSIX, then `scripts/store-smoke.ps1` signs a
copy with a throwaway certificate, installs it, and checks that `flyback-cli --version`
answers through its alias and that `plugin list` names the data folder. The package is
then attached to the release. Uploading it in Partner Center is the user's step.

## Consequences

- A Store copy's Settings → Files "Open with" choice changes nothing, because the
  manifest decides. Windows' own "Open with" still lists Flyback.
- `flyback-cli plugin list` in a Store copy lists only the installed folder, not the
  shipped plugins.
- Store certification re-runs on every release, so a release reaches Store users only
  after the user submits it and Microsoft passes it.
- The listing needs a privacy policy URL, since Flyback is a Win32 program and counts
  usage (0094).
