# ADR-0232: Decision models are built in, and the keyed client leaves the contract

**Status:** Accepted · 2026-11-09 · *user-directed* · supersedes the plugin half of
[0186](0186-a-decision-model-answers-typed-questions-behind-the-plugin-boundary.md),
follows [0231](0231-assistants-are-built-in-not-plugins.md)

## Context

[0231](0231-assistants-are-built-in-not-plugins.md) took the assistants out of the plugin
contract but kept the keyed client there (`IKeyedClient`, `KeyedClient`, `KeyCredential`),
because a decision model still sent over it. The only decision model was the Decision
server, a plugin of a hundred lines Flyback ships. The rest of the `Decide` namespace,
the questions, answers and wire, was contract only so that plugin could be one.

## Decision

**A decision model is code in `Flyback.Assist`, not a plugin.**

- `IPluginRegistry.AddDecisionModel` is gone, with the catalog's decision models. The
  `Decide` kit moves to `Flyback.Assist.Decide`, the Decision server with it as
  `SystemOneModel`. `DecisionModels.Shipped` is what a host offers; the editor gets it
  through `EditorSetup.DecisionModels` and flyback-cli through its `Plugins`.
- The keyed client moves to `Flyback.Assist.Keys`. Only `ISecretStore` stays in
  `Flyback.Plugins.Secrets`, since a secret store is still a plugin.
- **The contract stays 1.0.0**, as in 0231, and the removals go straight into
  `PublicAPI.Shipped.txt`.

## Consequences

The contract is what a plugin adds to a patch or to the machine: modules, presets,
sound and MIDI backends, and secret stores. Laya, the decision model that runs here
(TODO.md), lands as a model in `Flyback.Assist`, so the ONNX runtime it needs becomes a
dependency of the app rather than of an optional plugin. The tests' Fake Decider
plugin becomes `Bare`, a plugin that registers nothing.
