# ADR-0098: The briefing has a budget, and a list that outranks it

**Status:** Accepted · 2026-09-18 · *user-directed* · amends
[0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md), follows
[0097](0097-a-wrapping-module-carries-a-setting-where-what-it-wraps-differed.md)

## Context

[0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md) briefs the
assistant with every module's description, and past a fixed 40,000 characters
dropped every description at once and handed the model `describe_module` instead.
The figure was a guess made before there were any plugins to measure.
[0097](0097-a-wrapping-module-carries-a-setting-where-what-it-wraps-differed.md)
found the engine's own catalogue at the edge of it. With the three plugins that
ship, the briefing is 61,285 characters, so every installation was running on the
fallback: no module had a description, including the ones the conventions lean on.

How many modules there are is not Flyback's to decide. Anybody can install
plugins, so no budget holds for good, and an all-or-nothing cut at that size means
one small plugin takes every description with it.

## Decision

**The budget is 80,000 characters, and a setting.** It is under Settings → Agent,
between 10,000 and 1,000,000, and applies to the next conversation. The hosted
models Flyback talks to hold many times that, prefix caching has no upper
limit that matters here, and what it costs is the uncached first request of a
conversation. What ships comes to about three quarters of it, and a test fails
if it goes past four fifths.

**Past the budget, descriptions are cut one module at a time, and a list
decides whose go last.** `priority-modules.txt` names the modules whose
descriptions are always kept, however much they cost. The rest keep theirs in
catalogue order for as long as they fit in the room left, so the next one along
still gets in if it is short enough. Only the ones that fit nowhere are left out,
and the briefing says that some were and that `describe_module` has them.

**The list is a file beside the settings.** It is compiled into the plugin
assembly, written to the data folder on the first start, and never written
again. That makes it somebody's to edit, and the settings name the path so it
can be found. Deleting it puts the shipped list back. It is read when a
conversation starts and when settings are saved. An id no loaded module has is
kept and never matches, because a plugin that is not installed today may be
tomorrow.

**What is left out is decided by the catalogue and the settings alone.** It is
measured against the longest of the three things the briefing can say about
hearing. So the canvas can mark the same modules the briefing leaves out without
a conversation to ask, and the briefing stays the same bytes from one request to
the next.

**A module left out says so.** It has a dotted tag at the right of its header, a
tooltip on the tag, and the same sentence at the foot of the inspector. With no
provider chosen nothing is marked, because nobody is being told anything.

## Consequences

Every module that ships is described to the assistant again, which it has not
been since the plugins outgrew 40,000 characters.

The shipped list is every built-in but the five measurement modules, and from the
plugins the filter, the envelopes, the drum voices, the three delays everybody
reaches for and the shapes most pictures start from. Together that is about
15,000 characters of descriptions, so a briefing can go well past the budget before
the list has to be cut.

A list longer than its budget is honored and the briefing runs over. That is
somebody asking for exactly that, and the budget is theirs to raise.

A small local model is not protected by any of this. The briefing without a
single description is over 30,000 characters with what ships, which is past the
default context of the runtimes that serve those models. Fitting one means
asking the endpoint how big its context is, and that is a separate question.
