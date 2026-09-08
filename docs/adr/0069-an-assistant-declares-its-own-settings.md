# ADR-0069: An assistant declares its own settings

**Status:** Accepted · 2026-09-08 · *user-directed* · takes the route
[0055](0055-a-plugins-extra-declares-its-editor.md) opened for carried state and
applies it to configuration; finishes what
[0025](0025-platform-io-behind-loadable-plugins.md) and
[0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md) drew and
[0047](0047-the-agent-may-listen-where-the-model-can.md) tested; leaves
[0034](0034-settings-in-a-file-the-key-in-the-operating-system.md) exactly where
it stood on the key

## Context

The assistant's settings were the App's. `AssistantSettings` had a property per
question — provider, model, endpoint, vision, hearing, ear, effort — and
`AssistantPanel` had a control per property, laid out by hand, with the rules
between them written out in the shell: which switch a chosen model takes away,
when the ear is worth showing, when it may be changed, what sentence goes under
the model box. `AssistantConfig` carried the same seven fields across the
boundary.

It worked, and it was wrong in a way that only shows up when a third provider
arrives. Everything the panel did was written in terms of what *these two*
adapters happen to want. A provider that needs a deployment name, a project id,
a region, a safety threshold or a system-prompt override has nowhere to put it:
the settings file has no room, the form has no row, and the config record has no
field. The only way to add one is to change the shell — which is precisely the
thing [0025](0025-platform-io-behind-loadable-plugins.md) exists to prevent, and
which [0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md)
had already stopped short of by putting the model list in the plugin while
leaving the form in the App.

The pressure was visible in the code before it was visible in a feature.
`AssistantSchema` had grown `BaseUrlEditable` — a field whose only reader was a
line in the panel that greyed a box out — and `Ears`, and `Known`, each one a
fact about models that the shell was forbidden to understand but had to consult
anyway. The boundary was in the right place and the traffic across it was the
wrong shape.

## Decision

**A provider declares what it has; the App draws it.** `IPatchAssistant.Form`
returns a list of `AssistantField` and the panel renders that list, knowing the
vocabulary and nothing else. There is no model name, no endpoint, no capability
and no provider name anywhere in the App's settings code.

**The vocabulary is three shapes, and stays that way until a provider is
blocked.** `Text`, `Pick` and `Switch`. Each carries a label, an optional note
shown under the control, and whether it may be changed with a line saying why
not. That is the whole of it: no number, no path, no list of records. Every
shape here is public API that cannot be withdrawn, and the pressure will be to
guess at the next one.

**A value is a string, whatever the shape.** A switch is one or nought, a choice
is the id it chose — the spelling
[0055](0055-a-plugins-extra-declares-its-editor.md) settled on for a carried
field written in the language. The settings file stays readable and
hand-editable, and the App stays out of the business of interpreting what a
provider meant.

**The form is a function of what is already on it, and is asked for again after
every change.** Half of a form depends on the rest: the chosen model decides
whether looking is offered, whether there is a second model to choose at all,
and what the sentence under the box says. This is what moved the interlocks out
of the shell — the panel no longer works out that an ear should disappear, it
asks, and the provider leaves the ear out of the list it sends back.

**A credential is not a field and there is no shape one could be.** The key box
belongs to the host, exactly as
[0034](0034-settings-in-a-file-the-key-in-the-operating-system.md) said. What a
provider says about its key is `AssistantCredential` — the variable it is
conventionally read from, and one line about where one comes from. A test asks
every installed assistant for its form and fails if any field is named like a
secret, because the answers are written to disk in plain text and "just declare
an apiKey field" will look harmless one day.

**The host still asks one question about a filled-in form: `Senses`.** The
workbench is built by the App, so the App has to know whether a rendered frame
may be shown and who — if anybody — is played the sound. It asks in terms of
what happens rather than what was chosen, which is the last piece of model
knowledge that used to live in `AssistantRun`.

**`AssistantConfig` is a key and a bag.** `AssistantChoices` — the old seven
fields minus the key — is what a plugin reads its own bag back as, on its own
side of the boundary.

**`AssistantSchema` survives as a helper for the ordinary shape.** Both adapters
ask the same five questions, so the declaration of them is written once, on the
plugin side, and delegated to. It also holds the two directions together:
`Form` says what is offered and `Read` says what a filled-in form means, and
they have to agree — a switch shown for a model that refuses pictures would be a
switch that lies. A provider whose settings are some other shape declares its
own fields and never touches it.

**The settings file grew a level: a bag of strings per provider.** Nothing in
the App knows what a provider's settings are called, so they are filed under the
provider's id. Two providers may both have a "model" and mean different lists by
it, and somebody trying a second provider for an afternoon should not lose the
endpoint they configured on the first.

## Consequences

**An existing `assistant.json` is not read.** Its properties are not these ones,
so every form opens on its declared defaults and a model and an endpoint have to
be picked once more. Migrating would mean teaching the App the six key names it
has just been relieved of, for one release; 0034 already says this file is not
load-bearing, and the key — which is the part nobody wants to type again — was
never in it and is untouched.

**A provider can now have settings nobody here imagined.** That is the point,
and it is worth stating what it costs: a field a provider declares is a field
the App will draw, store and hand back without ever validating it. `Sane` on the
field is the only tidying there is, and a provider that declares nonsense gets
nonsense back.

**What a provider cannot have is a control of its own** — the same limit 0055
took, for the same reason, and the escape hatch is the same one: a host-owned
contract assembly and an Avalonia version lock, and the bar is a provider that
is actually blocked rather than an imagined one.

**Two model facts left the shell and are now only visible through the form.**
`Known` and the capability flags are a plugin's business, so the tests that pin
the name-matching rules moved to the adapter's own test project, where there is
a direct reference. What crosses the boundary is tested as what crosses it: a
declared form, and `Senses`.

**The panel keeps its controls rather than rebuilding them.** A declaration is
reconciled onto what is already on screen, and the children are only reordered
when the declared keys actually change — otherwise every keystroke in the model
box would take the caret out of it. A field that disappears and comes back
comes back holding what it held.

**A field shape a build has never heard of is skipped.** A provider written
against a later vocabulary loses a row rather than the form, which is the same
rule the inspector already follows for a plugin's carried state.
