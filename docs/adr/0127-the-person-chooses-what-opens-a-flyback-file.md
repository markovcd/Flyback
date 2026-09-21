# ADR-0127: The person chooses what opens a Flyback file

**Status:** Accepted · 2026-09-21 · *user-directed* · follows
[0088](0088-a-release-installs-itself-at-the-next-start.md) and
[0123](0123-a-third-program-plays-a-patch-and-writes-nothing.md)

## Context

A release is a folder in a zip, with no installer, so nothing tells Windows or
a Linux desktop about `.fbk`, `.fbkb` and `.fbks` files. macOS knows about them
from the bundle's `Info.plist`, which names the editor. Two programs could open
one of these files: the editor, which edits it, and the viewer, which plays it.

## Decision

**A setting, not something done on its own.** Settings → Files has one choice,
"Open with": Nothing, The editor or The viewer. Nothing is the default. The
choice is sent to the operating system only on Save.

**For the signed-in user only, for the copy that is running.**
- Windows: a ProgID per kind under `HKCU\Software\Classes`, pointing at
  `Flyback.exe` or `flyback-viewer.exe` beside it, then `SHChangeNotify`.
  Nothing takes back only Flyback's own keys and values, so another program's
  claim on an extension stays. If the person picked a default in "Open with",
  Windows keeps it over this, and Flyback does not try to change it.
- Linux: a desktop entry and a shared-mime-info package under
  `$XDG_DATA_HOME` (`~/.local/share`), with the program's full path in `Exec`,
  then `update-mime-database` and `update-desktop-database` if they exist. The
  viewer's entry is `NoDisplay`, since from a menu it would have no file.
- macOS: the plist cannot change at run time without breaking the bundle's
  signature, so Finder always hands the file to the editor. The editor reads
  the setting and starts the viewer with the file. If the editor was started
  for that file (it has been up under five seconds and holds no work), it
  closes. Nothing on macOS still opens the editor.

**Chosen program registered again on every Save.** A copy that was moved
points the files at itself the next time settings are saved. Nothing is
applied only when it changes, so a person who never chose leaves the system
untouched.

## Consequences

- The choice lives in `file-types.json` as well as in the system, because macOS
  needs it at the moment a file arrives.
- A development build can register itself too, if somebody asks it to.
- The viewer reports a patch that will not open to a terminal. Started from a
  file manager, it has none, so such a file simply does not play.
- Neither the specs project nor any test reaches a real registry or desktop
  database: the Windows tests write under a scratch key and the Linux tests
  into a scratch data folder.
