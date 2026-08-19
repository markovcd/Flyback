# ADR-0033: Patches may be authored by an agent, behind the plugin boundary

**Status:** Accepted · 2026-08-16 · *user-directed* · extends
[0025](0025-platform-io-behind-loadable-plugins.md), amends
[0019](0019-no-third-party-dependencies-in-the-engine.md)

## Context

The instrument could make a picture and a sound, but authoring a patch meant
knowing the catalogue and wiring it by hand. Asked for an assistant that builds
patches to instruction, the interesting question turned out not to be which
model to call.

It was that **the engine already emits everything an agent needs to converge**,
by accident of decisions taken for entirely different reasons:

- `PatchCompiler` returns structured issues — no video sink, unknown module,
  a cycle and which node closed it ([0011](0011-compile-backwards-from-output.md)).
- `PatchIo.Read` returns what a file is short of, by plugin and by module id
  ([0020](0020-json-patch-files-keyed-by-string-type-ids.md),
  [0026](0026-modules-from-plugins-with-provenance-in-the-file.md)).
- `SynthRenderer` and `PngWriter` will draw a frame to memory with no window and
  no file, because rendering was never allowed near the UI thread
  ([0018](0018-never-render-frames-on-the-ui-thread.md)).

That is a closed loop — build, compile, look, revise — and a compiler can only
tell you a patch is *legal*. Only a rendered frame tells you it is anything.

What was missing is a network client. Nothing in `src/` had ever made an
outbound request, or held a credential, or taken a dependency the engine did not
already forbid itself.

## Decision

**The assistant is a plugin.** A third-party network SDK is exactly the edge
[0025](0025-platform-io-behind-loadable-plugins.md) drew its boundary for: the
shell stays free of backend packages, and for someone who does not want a
network dependency it is *absent* rather than merely unused. `IPluginRegistry`
gains a fourth method, which is what its own doc comment has promised since 0025
— existing plugins only ever call that interface, so none of them noticed.

**The tool layer is host-owned; a plugin is a transport adapter.** Naming a
module, wiring a port, reading the compiler's complaints and drawing a frame is
knowledge of the graph, the catalogue and the compiler. Duplicating it per
provider would be duplicating the engine. So it lives once, provider-neutral, as
`PatchWorkbench` in `Flyback.Plugins`, and an adapter's whole job is to turn a
list of tools into that vendor's function-calling shape and back again.

**The agent edits a working copy, through handles and port names.** Not
`.fbk` JSON. A model asked to emit a document must invent twenty consistent
guids, count a Sequencer's twenty-one inputs to find an index, place nodes in
pixels, and re-emit all of it to change one knob — with all-or-nothing
rejection at the end. Handles (`osc.sine` becomes `sine1`) and port names are
what the model is good at; guids, indices and coordinates never cross the
boundary at all.

**Every edit answers with the compiler's current complaints.** Compiling thirty
nodes takes microseconds, and the alternative is paying a round trip for
information that was already free.

**The agent looks at what it made** — a strip of frames, not one, because a
still cannot show motion and motion is most of what this instrument is. Frames
are stepped from zero rather than jumped to, because the renderer owns the
history `feedback` reads and a patch shown without its warm-up is a patch shown
black.

**The handbook is a cached prefix, and `NodeDef.Description` is part of it
verbatim.** Those descriptions were written for a reader who does not know the
synth, which is the same thing a model needs.

**Nothing is applied without a click, and one assignment undoes it.** Because
the workbench holds a copy, the patch that was open is pristine throughout, so
`editor.Patch = before` is an exact one-level undo in an application that has
none.

**`propose` is the only way anything reaches the editor, so an adapter must
never end a turn quietly without one.** The copy is what makes the feature safe
and it is also what makes stopping short invisible: the edits are listed in the
transcript, the prose reads like success, and the only sign is an Apply button
that never lights up. An adapter that sees the model stop without proposing owes
the person either a proposal or a reason — it says so to the model once, and
fails the turn with a sentence if that changes nothing. What a provider must not
do is return an empty-handed success.

> **Reversed.** *(2026-08-20, user-directed.)* The reason was already there: it
> is whatever the model said, and it reaches the transcript on its own. A turn
> ending without a proposal is most often a question — a choice only the person
> can make, an instruction that could be read two ways — and answering one with
> "call propose now" argues with a fair answer and spends a turn doing it. An
> adapter now lets a turn end where the model ended it. The conversation is
> multi-turn and keeps the workbench, so the next thing to happen is the person
> typing, which is what a question is for. What must still never happen is an
> *empty* turn: the prose is the reason, so it has to reach the panel.

**What may be proposed is a patch that reaches somebody**, which is not the same
as a patch with no complaints. `propose` gates on `IssueSeverity.Error` across
*both* programs ([0011](0011-compile-backwards-from-output.md),
[0022](0022-audio-and-video-are-two-sinks-over-one-patch.md)) — the video pass
never reaches a node only the ear does, so a patch offered for its sound has to
have compiled its sound. A sink missing while the other is present is not
remarked on at all and does not block; a patch with neither is refused outright,
because nothing is watching and nothing is listening. `render` asks after the
video sink itself rather than reading it off the issues, since the issue it used
to lean on is gone — a black rectangle is the one thing an assistant must never
be handed for a patch that works.

## Consequences

**Nondeterminism enters a program that was deterministic end to end.** Every
other test here pins an exact number or an approved image. The new part cannot
be tested that way at all, which is why the workbench is tested exhaustively and
offline — forty-odd cases against `NodeCatalog.BuiltIn`, no network, no plugin,
no Avalonia — and the adapters thinly. About ninety per cent of the feature never
touches a provider.

**`NodeDef.Description` stops being only a tooltip.** It is now the interface a
model reasons through, so editing one changes behaviour. That is a real
constraint on a field that used to be free text, and it is the price of not
maintaining a second description that would drift from the first.

**`PatchWorkbench` is the first thing in `Flyback.Plugins` that is a capability
rather than a contract.** Everything else there is an interface and a loader.
This is a working class the host owns and hands *to* plugins — which is the shape
that keeps every SDK on the far side of the boundary, and it is why adding a
second provider is a few hundred lines rather than a second implementation of
the same understanding.

**The feature costs money and needs a network**, so it is absent offline and
says so rather than failing at the first click. `Unavailable` returns a sentence
instead of `IAudioOutput`'s bool, because "no key set" is a *different kind of
no* from "wrong operating system" — one the person can act on, and only if they
are told.

**A patch and rendered pictures of it go to a third party.** Named in the panel's
standing footer, in the status bar, and in the plugin summary. Nothing is sent
without a click on Ask; nothing applied without a click on Apply.

**A warmed render costs about a hundred frames.** At 320×180 that is tens of
milliseconds and it happens at most once per model turn — but it is real work on
a pool thread, and putting the `Task.Run` inside the workbench rather than in
each adapter is what stops a plugin ever doing it on the UI thread, which
[0018](0018-never-render-frames-on-the-ui-thread.md) forbids and which deadlocks
besides.

**The agent cannot hear.** It can see the picture and reason about the sound from
the modules, and the handbook tells it to say so plainly rather than pretend. A
patch built for the speakers is the one case where the loop is open.
[0047](0047-the-agent-may-listen-where-the-model-can.md) closes it without
disturbing a word of this: the agent still cannot hear, and is given an ear
instead — a second model that can, asked on its own and answering in prose.

What this does not do: there is no way to steer a run once it has started beyond
stopping it; a proposal is all-or-nothing rather than a diff; and the handbook is
rebuilt per run rather than shared between them, so two windows pay for it twice.
