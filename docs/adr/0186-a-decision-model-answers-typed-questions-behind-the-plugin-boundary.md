# ADR-0186: A decision model answers typed questions behind the plugin boundary

**Status:** Accepted · 2026-10-08 · *user-directed* · planned in
[docs/handoff/decision-model.md](../handoff/decision-model.md); implemented in
`src/Flyback.Plugins/Decide` and `src/plugins/Flyback.Plugins.SystemOne`

## Context

Every judgment Flyback makes about words is a substring match, a count or the chat
assistant. Laya (Convai Innovations, open weights) answers typed questions about a
state in one forward pass, with a probability each, and its server speaks
`POST /v1/systemone`. A probability is something a feature can gate on; prose is not.

## Decision

**A decision model is an eighth kind of plugin contribution**,
`IPluginRegistry.AddDecisionModel(IDecisionModel)`, in `Flyback.Plugins.Decide`.
A request is a state and up to 64 questions, each a `Choice`, a `Score` or a `YesNo`
(the wire's `noul`); an answer carries its probabilities and a confidence.
`SystemOneWire` is the format, hand-written as the chat-completions one is.

**The host keeps the key and the downloads.** A model sends over the
`IAssistantTransport` it is handed (0158), its key filed as `decision.<id>` apart
from any assistant's. A model that needs files says so through `IPreparedModel`,
each pinned by size and SHA-256; the host asks, downloads over https into
`<data folder>/models/<id>/`, and moves a file in only once it hashes as pinned. The
pinned hash is the signature, so a file changed upstream is refused.

**Nothing is sent anywhere unless somebody chose it.** Until a model is chosen,
questions go to the likeliest installed model that runs here; a hosted one is asked
only once named in the settings. With none, `Decisions.None` answers null, and every
feature behaves as it did without one.

**`Decisions` is the one door.** It picks the model, configures it, holds it to a
ten-second deadline and never throws, so a feature asks and carries on.
`flyback-cli decide` is the same door from a terminal and throws its reasons instead.

**A feature asks as its use.** Each ask names a `DecisionUse` (`modules`, `issues`,
`turns`), and the settings may lay values over the chosen model's for one use, filed
under the use and then the model: a laya-serve's checkpoints are each better at a
different question, and which question is the host's to know, not the plugin's. The
contract does not move; `decide --for <use> --set key=value --save` writes them, and
Settings → Decisions shows and sets them a use at a time.

`PluginContractVersion` stays at 1.0.0: nothing has shipped at it, and `/release`
works the version out from the surface.

## Consequences

- The Decision server plugin is in the box and asks nothing until chosen.
- The Laya plugin, ONNX in-process, is to follow once its export is published.
- A feature that asks has to work as well with no answer as without the feature.
