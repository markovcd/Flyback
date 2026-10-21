---
name: outside-input
description: Use when writing code that reads a patch, bundle, sample, upload, model reply, submitted plugin, preset-site form or command-line flag - sizes, numbers, paths, text and code are checked once, at the door.
---

# Outside input

## Input from outside is checked where it enters

A patch file, a bundle, a sample, a model's reply, a submitted plugin, a form on the preset site and a command-line flag are hostile until checked. Check once, at the door, then trust the checked value inside:

- **Sizes.** A length read off the wire is not an allocation. Every hand-rolled reader caps it against what the input could hold.
- **Numbers.** A flag a user types that the program multiplies (`--size`, `--seconds`, `--fps`) has a ceiling, and overflow is checked.
- **Paths.** A name from a bundle, an upload or a request never becomes a path written to without being checked to stay in its folder. `PatchBundle` reads into memory and writes no path at all.
- **Text.** Parsers of untrusted text never throw; they report (`LanguageIssue`, `PatchLoad`). Text reaching HTML, SQL or a shell is escaped or passed as a parameter, never concatenated.
- **Code.** A plugin loads only once somebody said yes to it, and only while it hashes as it did (ADR-0158). An update installs only if its signature verifies against the committed public key (ADR-0088). A submitted `.fbkp` is decompiled and read, never run (`/review-plugin`).
