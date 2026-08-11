# ADR-0002: Split the engine from the UI shell

**Status:** Accepted · 2026-08-11

## Context

A video synth has two very different halves. One is a numerical pipeline —
graph, compiler, per-pixel evaluation — that is deterministic, testable and has
nothing to say about windows. The other is an interactive editor, which is all
event handling, layout and platform APIs.

Bundling them is the default for a single-window app, and it is what most
hobby graphics projects do. It also makes the numerical half impossible to
exercise without standing up a UI.

## Decision

Two projects:

- **`VideoSynth.Core`** — graph model, compiler, renderer, PNG writer. Its only
  references are the base class libraries. No UI framework, no imaging library.
- **`VideoSynth.App`** — Avalonia shell. References Core.

The dependency runs one way and there is no abstraction layer between them; the
App calls Core's concrete types directly.

## Consequences

The engine was verified before the UI existed. A ~40-line console harness
rendered every preset to PNG and benchmarked throughput, which is how the maths
was confirmed correct — the feedback spiral was visibly there — while the window
was still an empty stub. That harness needed no mocking because Core has nothing
to mock.

Core is 1,221 lines and App is 1,229. Replacing the shell with WPF, a MAUI
front end, a CLI renderer or a headless render farm means rewriting the second
number and none of the first.

The cost is a project boundary to maintain: things the UI wants must be public
on Core. In practice this pushed a few decisions in a good direction — port
metadata like `Min`/`Max` and module `Description` live on `NodeDef` as data
rather than being hardcoded in the inspector.

No interface or DI container sits between the two. For a two-project solution
with one implementation of everything, that indirection would buy nothing.
