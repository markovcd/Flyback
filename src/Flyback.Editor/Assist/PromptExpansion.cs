using Flyback.Assist;
using Flyback.Plugins.Assist;

namespace Flyback.Editor.Assist;

/// <summary>A short idea for a patch, written out by the assistant as the brief to build it from.</summary>
internal static class PromptExpansion
{
    /// <summary>
    /// The brief <paramref name="run"/> writes for <paramref name="idea"/>, or why it wrote none.
    /// Nothing it proposes is kept: the run is a throwaway.
    /// </summary>
    public static async Task<(string? Brief, string? Failure)> BriefAsync(AssistantRun run, string idea, CancellationToken cancel)
    {
        string? brief = null;

        await foreach (var happened in run.Ask(Request(idea), cancel))
        {
            switch (happened)
            {
                case PatchEvent.Said said when !string.IsNullOrWhiteSpace(said.Text):
                    brief = said.Text.Trim();
                    break;

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
}
