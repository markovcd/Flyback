# Security basics

## Secrets stay out of everything a person or a machine reads

An API key, a token or a signing key never goes in source, a committed config, a log, an error message, a test fixture, a snapshot, a commit message or a reply. A user's key lives in the operating system's store through a secret-store plugin, never in a settings file (ADR-0034), and an assistant never holds it: the host puts it on each request for one origin (ADR-0158).

- A test that needs a key uses a fake one made for the test.
- A local build that needs `RELEASE_SIGNING_KEY` makes a throwaway local key (the `release-key` skill). Never ask for the real one.
- Anything that prints a request, a setting or an environment leaves the key out.
- A secret found committed is reported to the user at once. Deleting it from the tree does not delete it from history; rotating it is the user's call.

## Input from outside is checked where it enters

A patch file, a bundle, a sample, a model's reply, a submitted plugin, a form on the preset site and a command-line flag are hostile until checked. Check once, at the door, then trust the checked value inside:

- **Sizes.** A length read off the wire is not an allocation. Every hand-rolled reader caps it against what the input could hold.
- **Numbers.** A flag a user types that the program multiplies (`--size`, `--seconds`, `--fps`) has a ceiling, and overflow is checked.
- **Paths.** A name from a bundle, an upload or a request never becomes a path written to without being checked to stay in its folder. `PatchBundle` reads into memory and writes no path at all.
- **Text.** Parsers of untrusted text never throw; they report (`LanguageIssue`, `PatchLoad`). Text reaching HTML, SQL or a shell is escaped or passed as a parameter, never concatenated.
- **Code.** A plugin loads only once somebody said yes to it, and only while it hashes as it did (ADR-0158). An update installs only if its signature verifies against the committed public key (ADR-0088). A submitted `.fbkp` is decompiled and read, never run (`/review-plugin`).

## Never loosen a check to make something work

Loading a plugin nobody allowed, skipping a hash or signature check, trusting a local key in a release build, disabling certificate validation or widening what the preset site accepts is never the fix for "it doesn't work". If a check is in the way, say which and why, and let the user decide.

## Collect the least

A run says what it played and nothing about who played it (ADR-0094). `Statistics/Usage.cs` is the whole policy of what is counted; anything new that is counted is added there, where a reader can audit it. User paths, machine names and patch contents stay out of logs and statistics.

## Dependencies are code nobody here read

The engine takes none (ADR-0019), and a package elsewhere is a question to raise, not a line to add. Restores are locked to the committed `packages.lock.json` files, so a version that moved fails the gate rather than building.
