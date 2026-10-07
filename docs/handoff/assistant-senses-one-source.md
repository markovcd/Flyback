# What an assistant can see and hear is worked out twice

Found on 2026-10-07, on `main` at `84e82fdc`. It is on TODO.md; take it off there, and delete this file, in the commit that lands it.

- **Severity:** Low
- **Status:** Open

## What is wrong

Two answers to the same question come from two places that are only in step by convention:

- **The settings form** (`IPatchAssistant.Form`) decides whether "Let it look at the picture" and "Let it listen to the sound" are offered and enabled. `AssistantSchema.Form` enables the hearing switch when `Ears` is not empty; `ProgramAssistants.Form` (Claude Code, Codex) removes it outright.
- **The senses** (`IPatchAssistant.Senses`, returning `AssistantSenses`) decide what the workbench offers and what a turn tells the person. `AssistantSchema.Senses` derives `Hearing` and `EarOffered` from the same `Ears` and the same switch.

Gemini and OpenAI build both from `Schema.Surveyed(values)`, so they agree. The programs agree because their schemas happen to list no model that hears. Nothing ties the two: a provider whose form is built differently, as the programs' already is, can offer a switch the senses deny, or the reverse. The CLI asks the form a third way (`AskConversation.Senses` looks for the switch's key).

## Evidence

Before `84e82fdc`, every turn asked to listen told the person "'Let it listen to the sound' in its settings turns that on", including on Claude Code, whose settings have no such switch. The fix added `AssistantSenses.EarOffered`, computed beside `Hearing` in `AssistantSchema.Senses`; the form still computes its own answer.

## Impact

Wrong advice in the assistant's column and in `flyback-cli ask`, the kind that reaches a tutorial: a person told to tick a switch that is not there, or never told about one that is.

## Suggested fix

One source. Let the provider declare what it can do once, per model, and have both the form and the senses read it:

1. A provider's capabilities as data: which models see, which hear, and whether a switch for each is offered at all (a program has none for hearing). `AssistantSchema` already holds most of this in `AssistantModel.Vision`/`Hearing` and `Ears`.
2. `Form` builds the two switches from it (offered, enabled, why not) and `Senses` derives `Vision`, `Hearing` and `EarOffered` from it and the values; `ProgramAssistants` says "no hearing switch" in that declaration instead of filtering the form.
3. `AskConversation.Senses` reads `AssistantSenses` rather than searching the form for a key.
4. A test across the shipped assistants (Claude Code, Codex, Gemini, OpenAI, with listening on and off and with a survey written down): `EarOffered` is true exactly when the form shows the hearing switch enabled and unticked, and `Vision` exactly when the look switch is on and enabled.

The plugin contract may change in place (the user's call: not concerned with backwards compatibility, the contract stays 1.0.0).
