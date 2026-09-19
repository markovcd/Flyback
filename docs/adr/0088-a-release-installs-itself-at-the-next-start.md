# ADR-0088: A release installs itself at the next start, if its signature says it is ours

**Status:** Accepted · 2026-09-17 · *user-directed* · extends
[0028](0028-publish-one-platform-at-a-time.md) and
[0034](0034-settings-in-a-file-the-key-in-the-operating-system.md)

## Context

A release is a zip of one platform's folder on the GitHub releases page
([0028](0028-publish-one-platform-at-a-time.md)), and getting a new one has meant
noticing it exists, downloading it and unpacking it over the old one by hand. The
instruction was for Flyback to do that itself: look for a new version at startup,
download it in the background, install it at the next start — on unless switched
off in the settings — and say on the status bar that it happened. And the update
was to be checked for authenticity, not only for arriving intact.

Three facts shape how.

**A running program cannot replace itself.** On Windows the executable and every
loaded assembly are locked; everywhere, the process would go on running the old
code. And the shell starts `flyback-cli` from the same folder
([0078](0078-export-leaves-the-shell-for-the-cli-that-already-writes-it.md)), so
another process may hold those files too.

**A hash published beside a package vouches for nothing.** Whoever could replace
the package could replace the hash. Authenticity needs a key that is not published
with the release — which means a signature, which means cryptography, where
[0034](0034-settings-in-a-file-the-key-in-the-operating-system.md) said there is
none in Flyback.

**The macOS bundle is unsigned** ([0028](0028-publish-one-platform-at-a-time.md)),
and Apple silicon will not start an unsigned program.

## Decision

**Releases are found through GitHub's API**, `releases/latest`, once a launch,
after the window exists. Drafts and prereleases are ignored. A build that is not a
release — anything whose informational version carries a commit hash — never looks.

**Each release carries `SHA256SUMS` and `SHA256SUMS.sig`**: `sha256sum` over the
packages, and an ECDSA P-256 signature over that list made by `openssl dgst
-sha256 -sign`. The private key is the repository secret `RELEASE_SIGNING_KEY`; the
public key is `src/Flyback.App/Updates/release-key.pem`, compiled in. The workflow
refuses to start without the secret or with one that does not pair with the
committed key, and verifies its own signature before publishing.

**One signature over the list, not one per package**, because the list names every
package with its version: an older release's genuine list cannot be passed off as a
newer release.

**Verification only, through the platform's ECDSA.** Nothing is invented, no cipher
is chosen here, and no key but a public one is held — the line 0034 drew was
against making up a scheme, and this reads it that way.

**Nothing unverified is written anywhere but a `.partial`.** The list's signature is
checked before the package is downloaded, the package against the list before it is
unpacked, and the unpacked folder is renamed to its version only once complete — so
a folder named for a version is one that is ready.

**The new version installs itself.** At the next start, before plugins are loaded,
the old version starts the new one from where it was unpacked with
`--apply-update <copy> <pid> -- <arguments>` and exits. The new one waits for it,
copies its own files over the copy, and starts the copy with the original arguments.
Nothing extra ships, nothing needs building per platform on a machine of that
platform, and every release carries the installer it is installed with. Those
arguments are a contract between versions, and are only ever added to.

**What a release ships is replaced; nothing else is touched** — except a plugin
folder the release ships, which is replaced whole, since a plugin is every assembly
in its folder. A plugin somebody added is left alone. Every replaced file is moved
aside first and moved back if the install fails; one still aside at the next
install is put back before it begins.

**It is not attempted when it cannot finish**: when the copy is not writable (a
system folder, a translocated Mac bundle), or when another shell or
`flyback-cli` is running out of it. The download waits for a start when neither is
so.

**On a Mac, the unpacked bundle is signed ad hoc before it is started, and the
installed one after it is replaced**, with `codesign --sign -`, which is part of
macOS.

**What happened is said once**, by the window that opens after. An install that
worked opens a dialog with the changelog's sections for every release after the one
it replaced, up to its own — the build carries the changelog inside itself, and
reads which release it replaced from that copy's `Flyback.dll` before overwriting
it, so a jump over a release shows both. One that failed says why on the status
bar, as does a build whose changelog has no section for its own version. A version that fails three times
is left for the next release, and a failure is never retried in the same start.

**It is on by default**, in `update.json` beside the other settings files, and the
settings window has an Updates tab to switch it off. Off also discards a download
that is waiting.

## Consequences

**A repository without a key installs nothing.** The committed placeholder has no
key in it, so a build carries none and says so on the terminal. The first release
after the key is committed is the first that can be installed as an update, and the
first release containing this code has to be installed by hand.

**Flyback now goes online without being asked**, once a launch, to GitHub. What it
sends is what any download sends; the Updates tab says so and switches it off.

**Losing the private key ends automatic updates** for every installed copy: a
release signed with a new key is refused by copies that trust the old one, and has
to be installed by hand once. Rotating the key is the same by-hand step, done
deliberately.

**A start with an update waiting is two process starts**, and on a Mac the second
is through Launch Services, so it is the application the Dock knows. A launch from
a terminal returns the prompt when the first exits; the updated window is no longer
attached to that terminal.

**The tests cover every refusal** — wrong key, altered list, altered package,
wrong layout — against a fake GitHub, and the install and its rollback against a
real folder. The hand-off between two processes is not unit-tested; it was tried by
publishing two versions and letting one install the other.
