# ADR-0072: A conversation is saved with the patch it is about

**Status:** Accepted · 2026-09-11 · *user-directed* · extends
[0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md), amends
[0060](0060-a-bundle-is-a-patch-and-what-it-names.md)

## Context

A conversation with the assistant lived as long as the window did. Closing the
program lost the history, the patch the workbench was building, and whatever
prompt cache the provider held — and the second instruction, "more blue, and
slower", is the common one. The conversation log could not stand in for it: it
records what the panel showed, not what was sent, so it has no tool arguments in
the provider's shape, no thought signatures (which a Gemini function call must be
handed back with), and no pictures or clips.

What a conversation is made of is held in three places, and only one of them is
the host's to understand:

- the provider's history, in that provider's wire format;
- the workbench — the patch it began on, the one it is building, the handles its
  modules answer to, and what the run has spent;
- the run and the panel — how many turns, and the transcript.

## Decision

**A conversation belongs to a saved document.** One per patch file. A patch that
has never been saved has nowhere to keep one and loses it on close — unless it
is saved, at which point the conversation goes with it. Opening another document
ends the conversation that was going, and the panel shows the one the new
document arrived with, or none.

**Where it is kept follows the kind of file.** A bundle carries it inside itself,
as `conversation.json` beside `patch.fbk`. A `.fbk`, and a `.fbks` the text owns,
keep theirs in `%AppData%\Flyback\sessions\`, one file each, found by the patch
file's path — beside the document rather than in it, because a `.fbk` is the patch
and nothing else ([0020](0020-json-patch-files-keyed-by-string-type-ids.md)). A
printing saved as `.fbks` is a copy and takes nothing with it.

**Kept beside the file, it is believed only while the file is what it was saved
as.** The kept conversation holds a hash of the exact text written. A patch changed
anywhere else — another program, a checkout, a copy dropped over it — is not the
patch the conversation was about, and one carried on over a patch it never saw
would describe modules that are not there. A file saved over with a patch that
has no conversation forgets the one it had.

**It is written when the patch is saved, and at no other time.** Never behind
somebody's back into a bundle they may be about to send. So a finished turn is
unsaved work: the title takes its dot, and closing or replacing the document asks
the question it asks about an edit, in words that say it is the conversation at
stake. What is written is the conversation as it stood when its last turn ended,
because a save can land in the middle of a turn and a session is not something to
read while it runs.

**Each owner saves its own part.** `IPatchSession.Save` hands back the provider's
history as a string nobody else reads, and `IPatchAssistant.Resume` takes it back
over a workbench the host has already restored. Both are defaulted — a provider
that has never heard of this saves nothing and resumes nothing, and a conversation
with it carries on as the patch it had built with no memory of the words. The
workbench saves its handles rather than working them out again, because a handle
the model chose is not the one the type id would give, and the history is written
in those names. The panel saves its transcript line by line.

**Nothing goes in that is not the conversation.** No key — it is a request header
in both adapters and never in the history. No settings: what the conversation was
set up with is a fingerprint, enough to tell whether the settings in force now are
the same and nothing more, since an endpoint can be a private address. No pictures
or clips: they are nearly all of the size, the model can render or listen again,
and each is replaced by a sentence saying one was there. The Gemini history keeps
everything else exactly, thought signatures included; the chat-completions one
drops the briefing, which is rebuilt for every run, so a conversation carried on is
told the current handbook.

**Carried on at the first message, under the rules a message already follows.** A
conversation that had its turns, one had with other settings, and one whose patch
was edited after it was opened start a new conversation — the three reasons a run
already gives. The first message rather than the moment of opening, because that
is when there is a key and a configuration to carry it on with.

## Consequences

**A bundle sent to somebody carries the conversation.** Everything asked and
everything said about the patch goes where the bundle goes. That is the point of
putting it there — a bundle is the whole document wherever it is taken — and it is
also the one real exposure here. There is no way yet to save a bundle without it
short of starting another conversation; if one is wanted, it is a checkbox on the
save, not a change to this record.

**The workbench keeps the paths it had.** A bundle rewrites every path in the patch
to name its own copy; the saved workbench is not rewritten with it. On the machine
that saved it the old paths still resolve, since a bundle's library falls through to
the disk. On another machine a proposal from a carried-on workbench names files
that are not there — which is what a patch naming a missing file already does
everywhere else.

**A kept conversation for a file that has moved or gone is never found again, and
never tidied.** Moving a patch file loses its conversation; renaming it by saving
it again does not. The folder grows by one small file per patch that has one.

**A carried-on conversation is a new log file.** The log belongs to a run, and a
carried-on conversation is a new run of the same conversation.

**A turn that asked a question now puts a dot in the title.** It is the price of
the conversation being part of the document: the alternative was a conversation
that quietly vanished whenever somebody chatted about a saved patch and closed it
without changing anything.

## Amendments

**2026-09-11 (later) — a conversation can be set aside.** With a conversation
coming back every time its patch opens, the only ways to be rid of one were to
change a setting or to use up its turns. **New conversation** sits at the left of
the strip the send button is in, and empties the panel for a conversation about
the same patch. The patch is untouched, and so is the disk: the conversation set
aside stays with the patch file until the patch is saved again, which then writes
the new one, or none. Setting one aside is not unsaved work — nothing new has been
said — so it puts no dot in the title, and a patch closed straight afterwards opens
next time with the old conversation still there. The button is dead while a turn
runs, because stopping one is the other button's job.
