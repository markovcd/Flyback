# ADR-0047: The agent gets an ear, which is a second model

**Status:** Accepted · 2026-08-19 · *user-directed* · closes the open loop
[0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md) left,
over the sink [0022](0022-audio-and-video-are-two-sinks-over-one-patch.md) built

## Context

[0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md) gave the
agent a closed loop — build, compile, look, revise — and said plainly what it did
not close: *"The agent cannot hear. A patch built for the speakers is the one
case where the loop is open."*

That gap is not small. This instrument has two sinks and one patch
([0022](0022-audio-and-video-are-two-sinks-over-one-patch.md)), and a patch wired
only to `left` draws nothing at all — so for half of what the program makes, the
agent had the compiler and nothing else. The compiler tells you a patch is legal.
It cannot tell you it is *anything*, which is the whole reason `render` exists,
and there was no equivalent for the ear.

Worse, the failure the compiler cannot see is the one this instrument produces
most. An oscillator accumulates how far its `in` has moved
([0030](0030-oscillators-accumulate-their-phase.md)), so one driven by anything
that holds still is silent — legal, clean, and doing nothing. The agent was
guessing at exactly the class of mistake it was most likely to make.

Two things had changed since 0033 was written. Chat completions grew an
`input_audio` content part, so a sound can travel the same way a picture does.
And the pieces to make one were already here for entirely unrelated reasons:
`AudioRenderer` runs offline and single-threaded because it has to survive an
audio callback, and `WavWriter` exists because headless export must not depend on
a sound device. The same accident 0033 found in the video path — everything
needed, built for other reasons — turned out to hold for the audio path too.

What has *not* changed is that models differ here. Every endpoint this adapter
reaches can be shown a picture. Only a handful accept a sound, and one sent
elsewhere is a 400 naming the parameter rather than a shrug.

The first attempt at this sent the sound into the conversation, as a user turn
after the tool result, exactly as a rendered frame goes. It does not work, and
the reason is worth recording because it is not obvious from the wire format:

> `400: This model requires that either input content or output modality contain
> audio.`

The models that take a sound require *every* request to carry one. A
conversation driven by one is refused on its first turn — before a patch exists,
let alone a sound to listen to — and would stay refused on every turn until the
first `listen`. Asking for audio *back* satisfies the rule and is worse: the
answer this loop needs is a function call, not speech.

And they do not take a picture. Driving with one would trade the patch's eyes
for its ears, on an instrument whose whole premise is that one patch makes both.

## Decision

**`listen` is `render`'s counterpart, and is built exactly like it.** A tool on
the workbench, provider-neutral, `Task.Run` inside the workbench rather than in
each adapter — so no plugin can put a render on the UI thread
([0018](0018-never-render-frames-on-the-ui-thread.md)) by getting it wrong. It
refuses when nothing reaches `left` or `right`, the way `render` refuses when
nothing reaches `color`: a patch for the screen is silent *on purpose*, and
playing an agent two seconds of nothing is the audio spelling of showing it a
black rectangle.

**It is warmed from zero, never sought to.** The audio path is the one with a
memory — delay lines and `feedback.unit`
([0027](0027-delay-lines-give-the-audio-path-a-memory.md)) — so a stretch started
halfway along would arrive with empty lines and sound like something nobody would
ever hear. This is the same reasoning `render` warms frames for, and it is far
cheaper here: a second of audio is about 100k evaluations against a frame's 31M.

**Silence comes back as a sentence, not as a recording.** Below −66 dBFS nothing
is sent at all — the reply says so and says where to look, naming the causes the
compiler *cannot* see: gain at zero, an `in` wired to something that never moves,
a constant into `left` that the DC blocker removes. The compiler has already said
whatever it can see, and a model handed a payload of nothing tends to conclude
the tool is broken rather than the patch.

**The ear is a second model, asked on its own.** `listen` renders the sound and
`OpenAiSession` puts it to `AssistantConfig.EarModel` in a separate request — a
system prompt about listening, one user turn carrying the WAV, and no tools at
all, so nothing can come back but words. Those words are appended to the tool
result, and the model driving the conversation reads a description of a sound it
never receives.

This is forced by the 400 above rather than chosen, but it is better than what it
replaces in three ways. The driver can be any model, so sight is kept. The WAV is
sent once instead of sitting in a history that is resent every turn. And what the
ear is asked is the `note` the driver already writes when it calls `listen` — a
field that was a memo to itself becomes the question somebody answers.

**Hearing is off by default, where sight is on.** `AssistantConfig.Hearing`
defaults false and is a checkbox somebody ticks, beside the model that will do
the listening. It spends a second model per call, which is a thing to opt into
rather than to discover on a bill.

**What a model accepts is the provider's to know, and the schema is where it
says so.** `AssistantSchema.SuggestedModels` was a list of strings nothing read;
it is now a list of `AssistantModel`, each carrying whether it takes a picture
and whether it takes a sound. The shell reads those two bools and never a name —
which is the boundary [0025](0025-platform-io-behind-loadable-plugins.md) drew
and [0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md)
kept, and the reason this is not a list of model names in the panel.

The form follows it: the model box offers the suggestions and still takes a name
typed over them, a switch the chosen model would refuse is disabled with the
reason on it, and `Configured` sends neither a picture nor a sound the model is
recorded as refusing — whatever the box still shows. The tick itself is left
alone, because a preference destroyed by passing through one model on the way to
another is a preference lost rather than respected.

**A model nobody wrote down is a stranger, not a refusal.** A name matches the
one it is, or that name with a date after it — `gpt-4o-2024-11-20` is a `gpt-4o`,
and the suffix beginning with a digit is the whole of the test. A bare prefix
match is not good enough: `gpt-4o-transcribe` begins with `gpt-4o` and is a
different model, and reading it as one answers a question this list cannot
answer, in the direction that takes a switch away from somebody who knows better
than it does. A word after the name is another model; a number after it is the
same one pinned to a day. Everything else leaves both switches alone — the
endpoint is a field, so most of what this reaches was never written down here,
and not knowing is not the same as knowing it cannot.

**Nothing asks for audio back.** That would want `modalities` on the request and
would answer in speech. This is a tool loop; the reply it needs is a function
call.

**The briefing changes with the configuration.** One paragraph, and it has to
change: it is the only place the model is told what it can check. A model told
nothing assumes it can hear — every other tool it has answers when called — and
would describe a sound it never heard.

## Consequences

**What the agent knows about the sound is second-hand.** It reads one listener's
account rather than hearing anything, and the handbook says so and tells it to
attribute what it repeats. The peak and rms in the same reply are the corrective:
those are measured from the samples, so where the prose and the numbers disagree
the numbers are the sound and the prose is an opinion of it.

**A listen is two requests and two models.** The second is small — one sound,
four seconds at most, at 24 kHz rather than the speakers' 48 — and it is sent
once rather than resent with every turn that follows, which is the arrangement
paying for itself. What it costs instead is a round trip in the middle of a tool
call, and a second model's rates on top of the first's.

**The `input_audio` spelling is not the `image_url` spelling.** Bare base64 and a
separate `format` against a data URL — two shapes in one method, and the kind of
thing that fails as a 400 rather than as anything visible from here, which is why
`Wire` is tested without a network.

**The handbook now has two versions.** Both are deterministic, so the prefix
cache still holds within a run
([0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md)), but a
person who turns hearing on has changed the cached prefix and pays to write it
once more.

**The panel shows a caption, not a player.** What was rendered went to the ear,
and a transcript that started playing sound over a patch already playing would be
two instruments at once. The line carries the levels and what the ear said; the
speakers stay the patch's.

**An ear that will not answer costs the description, not the turn.** A model that
is not available at this endpoint, or a request that fails outright, comes back
as a sentence in the tool result saying so — beside levels that are still true,
because they were measured here.

**A list of model capabilities is a thing that goes stale.** It is written down
rather than probed because there is no way to ask an arbitrary chat-completions
endpoint what it accepts without sending it something, and this is the cost of
that: a model added to a provider next month is a stranger here until somebody
adds a line. The failure is in the safe direction — a stranger is offered
everything and refused nothing, exactly as before any of this was recorded — and
it is the same trade `NodeDef.Description` already makes, where a field that
looks like documentation is load-bearing.

**Four of the suggestions lost sight, and that is a bug fix rather than a
restriction.** `llama3.1` and `qwen2.5` name the text-only weights, and the three
audio models do not take a picture either. The form had been offering to send
every one of them one, ever since it offered them at all. Nothing had recorded
which models could see, so nothing could have known.

What this does not do: nothing is ever sent *back* as audio; there is no
spectrogram, so what the model gets is what a listener gets rather than what an
analyser would; and `listen` renders the sound alone, so a patch whose picture
and sound are one thing is still examined through two tools.
