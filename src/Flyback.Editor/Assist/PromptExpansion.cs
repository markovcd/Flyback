using Flyback.Assist;
using Flyback.Plugins.Assist;

namespace Flyback.Editor.Assist;

/// <summary>A short message to the assistant, written out by it as the brief to build from.</summary>
internal static class PromptExpansion
{
    /// <summary>
    /// The brief <paramref name="run"/> writes for <paramref name="idea"/>, or why it wrote none.
    /// Nothing it proposes is kept: the run is a throwaway.
    /// </summary>
    public static Task<(string? Brief, string? Failure)> BriefAsync(AssistantRun run, string idea, CancellationToken cancel) =>
        WriteOutAsync(run, Request(idea), cancel);

    /// <summary>
    /// The brief <paramref name="run"/> writes for <paramref name="change"/> to the patch it runs over,
    /// or why it wrote none.
    /// </summary>
    public static Task<(string? Brief, string? Failure)> ChangeBriefAsync(AssistantRun run, string change, CancellationToken cancel) =>
        WriteOutAsync(run, Change(change), cancel);

    private static async Task<(string? Brief, string? Failure)> WriteOutAsync(AssistantRun run, string asked, CancellationToken cancel)
    {
        string? brief = null;
        var replied = false;

        await foreach (var happened in run.Ask(asked, cancel))
        {
            switch (happened)
            {
                case PatchEvent.Said said when !string.IsNullOrWhiteSpace(said.Text):
                    brief = said.Text.Trim();
                    replied = true;
                    break;

                case PatchEvent.Said or PatchEvent.Cost:
                    replied = true;
                    break;

                // A message that already reads as a full request can make the model build it,
                // and what it says after that is a report, not the message.
                case PatchEvent.Did or PatchEvent.Read or PatchEvent.Saw or PatchEvent.Heard or PatchEvent.Proposed when replied:
                    return (null, "it began building instead of writing the message out");

                case PatchEvent.Failed failed:
                    return (null, failed.Message);
            }
        }

        cancel.ThrowIfCancellationRequested();

        return brief is null ? (null, "The assistant sent nothing back.") : (brief, null);
    }

    private static string Request(string idea) =>
        "[From Flyback, not the person: build nothing, edit nothing and call no tools. The person typed a short idea "
        + "for a new patch, below. Write it out as the brief they would have given had they thought it through, in their "
        + "voice, as plain text with no preamble and nothing after it. "
        + "Open with the one idea that makes this patch itself and no other: a single mechanism the whole piece is built "
        + "around, shared by the sound and the picture. A break chopped by bending the clock it reads is such an idea; so "
        + "is a tune played by where a bouncing shape strikes the frame, or one chord whose overtones are the picture's "
        + "colors. Find the one this idea asks for, not one of those. "
        + "Then say how it feels, and how it develops over its length: what changes from one part to the next and which "
        + "layers come in and drop out, so something is always moving, even in a still piece. Give it enough layers to "
        + "fill the space, each there for a reason, and say what is on screen. Describe what is heard and seen, never how "
        + "to build it: name no module and no socket. Nothing is a default: a beat, a key, a tempo, a limiter, panel knobs "
        + "each appear only if this idea needs them. Decide what it leaves open rather than asking. Keep it under 300 "
        + "words, and end with one line asking for a short report of what was done differently and why.]"
        + Environment.NewLine + Environment.NewLine + idea;

    private static string Change(string change) =>
        "[From Flyback, not the person: build nothing, edit nothing and call no tools. The person has the patch above "
        + "open and typed a short request to change it, below. Write it out as the request they would have made had they "
        + "thought it through, in their voice, as plain text with no preamble and nothing after it. "
        + "First, in a sentence or two, name what makes this patch itself: the mechanism the sound and the picture share, "
        + "and the shape of the piece over its length. That is what the change keeps unless the request is about it. "
        + "Then say what changes, in terms of this patch's own parts: name its sections, layers and moments by what they "
        + "do in the piece, never by module or socket. Follow the change through everything it touches: a sound that "
        + "changes may take the picture tied to it along, and the other way round; a new length says which sections grow, "
        + "shrink, repeat or go, still landing on the piece's own phrases; a new tempo or key moves every part that keeps "
        + "time or pitch, not one of them; a new style names which layers carry it and which give way. "
        + "Then say what stays, by name, so it is plain where the change ends. Keep it in proportion: a request about the "
        + "bass stays about the bass and does not become a remix. Decide what it leaves open rather than asking, unless it "
        + "has two readings that lead to different patches. "
        + "End with how to tell it worked: two or three things heard or seen at given times. Then one line asking for a "
        + "short report of what changed, what was kept that might have been expected to change, and what to tune next. "
        + "Keep it under 250 words, and shorter for a small request.]"
        + Environment.NewLine + Environment.NewLine + change;
}
