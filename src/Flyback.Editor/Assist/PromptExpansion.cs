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
        + "for a new patch, below. Write it out as the detailed brief they would have given had they known what to ask "
        + "for, in their voice, as plain text with no preamble and nothing after it. Decide what the idea leaves open, "
        + "such as tempo, key and length, rather than asking. Cover, in this order: the feel in two sentences; the length "
        + "and the sections, each with its bars or seconds; the sound, one line per instrument or layer; the picture; how "
        + "the sound drives the picture; the constraints (one clock for the tempo, every melodic voice in the key, a master "
        + "limiter, groups labeled, a few panel knobs for what is worth playing with); and a last line asking for a short "
        + "report of what it did differently and why. Leave out any part the idea does not call for.]"
        + Environment.NewLine + Environment.NewLine + idea;
}
