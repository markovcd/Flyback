# Contributing to Flyback

Flyback is built strictly with AI. The code, the tests, the documents and the commit messages are written by an agent; a human directs the work, reviews it and decides what ships, but does not hand-write the source. Everything below follows from that, and none of it lowers the bar the codebase is held to.

## Contribute by directing an agent

Point one at the repository and let it read the ground rules first:

- [CLAUDE.md](CLAUDE.md) is the entry point and names everything else.
- `.claude/rules/` holds the standing rules — git workflow, prose style, ADRs, tests, the changelog, the website, Windows shell pitfalls.
- `.claude/skills/` holds task-specific know-how: authoring presets, the convenience modules, retaking screenshots, rebuilding the site's audio.

A hand-written patch is welcome as a report of what is wrong, but it is outside the premise, so say in the pull request that it was written by hand. It will be rewritten by an agent before it lands.

## Read the decisions before proposing a change of shape

`docs/adr/` holds the architecture decision records, in [Michael Nygard's](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions) format: context, decision, consequences. They are not background reading — several of them pre-emptively decline the refactor a first scan of the code would surface, and the rationale comments in the code are usually better than the reasoning that would replace them. [docs/adr/README.md](docs/adr/README.md) is the index.

## The gate

One command decides whether a change is acceptable:

```bash
docker build --target gate .
```

It restores, compiles and runs every test in the solution. The Build workflow runs exactly this on every push and pull request, and a release is built from the same image with the publishes stacked on top, so there is one description of what a change has to pass ([ADR-0120](docs/adr/0120-every-change-passes-the-gate-a-release-passes.md)). Run it before opening a pull request. Nothing merges red.

For a faster loop while working:

```bash
dotnet test --solution Flyback.slnx -c Release
```

The Docker gate is still the truth. It carries the libraries the headless UI tests rasterize with and the ffmpeg the recording tests look for; without them those tests skip rather than fail, and a local run can be green about code it never exercised.

Warnings are errors (`TreatWarningsAsErrors`), and nullable reference types are on across the solution. A change that builds locally with warnings does not build in the gate.

## What a change carries with it

- **A test.** A behavior change lands with a test that fails before it and passes after. The suite is about seven thousand tests across ten projects; a new one goes in the project that owns the behavior, not in whichever is convenient.
- **An ADR**, when the change is a decision rather than a fix — something a later reader would otherwise undo. Number it from `main` at commit time, because concurrent sessions have collided on a number, and add its row to the index.
- **A changelog bullet**, when a user of Flyback would notice. One terse bullet per feature, naming what it is and nothing more; settings, fallbacks, flags and caveats belong in the ADR and the code. No entry for tests, comments, ADRs or README edits.
- **The matching site edit, in the same commit**, if `site/` describes what changed — shortcuts, module and port names, presets, language syntax, CLI flags, recording formats, the plugin contract. The website is published from `main` and must not describe a version that no longer exists.
- **`PublicAPI.Unshipped.txt`**, when the public surface of `Flyback.Core` or `Flyback.Plugins` moves. Those two projects refuse to build until the change is written into that file, and it is what decides whether `PluginContractVersion` takes a major or a minor: the major with the first line marked `*REMOVED*`, the minor with the first line added since a release.

## Tests

```text
tests/
  Flyback.Core.Tests            core engine tests
  Flyback.Core.Specs            specification-style tests and examples
  Flyback.Core.Benchmarks       engine benchmarks
  Flyback.App.Tests             app and UI tests
  Flyback.Cli.Tests             command line tests
  Flyback.Plugins.Tests         plugin and runtime behavior tests
  Flyback.Plugins.OpenAi.Tests  chat-completions session tests
  Flyback.Plugins.Gemini.Tests  generateContent session tests
```

A passing run still has something to say. Rank it by duration and look at the top of the list:

```bash
./tests/Flyback.App.Tests/bin/Release/net10.0/Flyback.App.Tests.exe -xml results.xml
```

The median test takes under a millisecond, so anything past a second is an outlier that has to explain itself. Almost always it waits on a wall clock, waits out a deadline to prove a negative, leaves something running that every later test is charged for, or redoes work the class could do once. Treat an unexplained outlier as a defect and fix it in a commit of its own. Some tests are honestly slow — rendering frames takes as long as it takes — and those say so in a comment.

## Packages and the SDK

Before anything that will touch a lot of files, find out whether the packages are current:

```bash
for p in $(find src tests -name "*.csproj"); do dotnet list "$p" package --outdated; done
```

Per project, because the same command against the `.slnx` reports nothing. Never `--include-prerelease`. Take an upgrade that costs only a version number, in a commit of its own ahead of the work. Leave one that wants a decision — a license, a vendoring, a shipped behavior that would move — to the user, and say what it costs and what it buys.

## Prose and commits

American spelling, in code, comments, documents and commit messages; the identifiers already use it, and a British spelling in a comment makes it disagree with the code it describes.

Comments explain a thing only where it is not obvious, and never narrate history — no "three rather than six", no "unchanged by the consolidation". Write the comment as if the current shape were the only shape it ever had. Extended reasoning belongs in an ADR.

A commit subject is a declarative sentence stating what is now true, not a Conventional Commits prefix. The body is a paragraph, occasionally two, on what was wrong and what happens now.

## Pull requests

One change per pull request, with the gate green. History on `main` is linear, so rebase rather than merge. The Build workflow runs on every pull request and cancels an older run when a newer push lands on the same ref.

## License

Flyback is MIT ([LICENSE](LICENSE)). A contribution is offered under the same terms.
