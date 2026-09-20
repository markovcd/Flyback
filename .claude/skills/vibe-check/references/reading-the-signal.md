# Reading the Signal

A miss is a name the agent reached for that your project does not have. It does
not automatically mean the project is missing a feature. Sort each one before
reporting it.

---

## Four kinds of miss

**A hole.** The thing does not exist, nothing in the architecture forbids it, and
its absence is the odd part. This is the finding you are mining for. Flyback's
`blur` was one: a video synth with a whole Feedback category and nothing that
softens.

**A foreign shape.** The name belongs to a different architecture and your project
declines it on purpose. Still worth reporting, but as a different thing: it means
your model diverges from the field's default at a point where newcomers will
expect otherwise. The answer is usually a paragraph of documentation, not a
feature.

**A renaming.** The thing exists under a name the agent did not reach for. This is
not a gap at all — it is a discoverability finding, and often the most immediately
actionable one, because it says your name is off the path everyone else walks. Map
every miss against the real vocabulary by *function* before you call it a hole.

**Noise.** One agent, once, in a list it was padding. Report it only if something
else corroborates it.

---

## Confidence ladder

How hard the agent committed, weakest to strongest:

| Level | What it did | Weight |
|---|---|---|
| 1 | Named it once, in a list | Noise |
| 2 | Used it in one example | Weak |
| 3 | Returned to it as *the* canonical example whenever it needed a thing of that kind | Strong |
| 4 | Wrote it into user-facing documentation | Strong |
| 5 | Wrote it into instructions for another model or agent | Loudest |

Level 5 is the top of the ladder because the agent had to believe it enough to
teach it. Flyback's `blur` reached level 5: it went into the assistant's own
prompt, where it would have taught the shipped product to hallucinate it too.

Independent agents matter more than any single level. Four agents at level 2 beat
one agent at level 4.

---

## The name is the signal, the signature is contamination

Treat these as two separate readings of the same specimen.

The **name** comes from the shape of the domain — what a thing of this kind is
called across everything the model has seen. That is the part worth trusting.

The **signature** — the arguments, the calling convention, where it sits in a
pipeline — comes from whichever specific system the model learned it in, and it
arrives with that system's execution model attached. It is frequently the one
form yours cannot take.

`blur(radius: 3)` as an inline pipe stage was borrowed from synths that render in
passes over a framebuffer. Flyback evaluates one pure function per pixel, so that
signature would have meant re-evaluating the whole upstream chain once per tap.
The name was a real request; the form was somebody else's architecture.

So never report a hallucinated signature as a proposal. Report the hole, then
work out the shape separately, against your own constraints.

---

## Before calling anything a hole

Check affordance. Can the project express this at all, with the primitives it
already has? Three outcomes:

- **Yes, cheaply** — a real hole, and a small one. Highest value finding there is.
- **Yes, but the cost is wrong** — a real hole in a form you cannot take. Report
  both halves; the shape question is now the interesting one.
- **No, by design** — a foreign shape. Go back and re-sort it.

This check is yours to do and cannot be delegated back to a vibe agent, which
will cheerfully tell you anything is easy.

---

## Do not let the confidence transfer

The specimen wrote with total assurance. That assurance is an artifact of the
mode, not evidence, and it is contagious in a summary.

In the report, every invented name stays marked as invented, every count stays
attached to its claim, and the reader is told plainly that these came from agents
that were instructed not to check. A vibe-check report that reads like a feature
list has failed, however good the findings are.
