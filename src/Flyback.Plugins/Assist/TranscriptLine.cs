namespace Flyback.Plugins.Assist;

/// <summary>One line of the transcript, as the panel showed it.</summary>
internal sealed record TranscriptLine(Voice Voice, string Text)
{
    /// <summary>
    /// How one thing a turn did reads in a transcript, and the word and text a log
    /// or a <c>--json</c> stream files it under.
    /// </summary>
    public static (TranscriptLine Line, string Kind, string Text) Of(PatchEvent happened) => happened switch
    {
        PatchEvent.Said said => (new(Voice.Said, said.Text), "said", said.Text),
        PatchEvent.Did did => (new(Voice.Note, did.Summary), "did", did.Summary),
        PatchEvent.Read read => (new(Voice.Handbook, read.Text), "read", read.Text),
        PatchEvent.Saw saw => (new(Voice.Note, saw.Caption), "saw", saw.Caption),
        PatchEvent.Heard heard => (new(Voice.Note, heard.Caption), "heard", heard.Caption),
        PatchEvent.Cost cost => Spent(cost),
        PatchEvent.Proposed proposed => (new(Voice.Proposed, $"Proposed: {proposed.Summary}"), "proposed", proposed.Summary),
        PatchEvent.Failed failed => (new(Voice.Failed, failed.Message), "failed", failed.Message),
        _ => throw new ArgumentOutOfRangeException(nameof(happened), happened, "Not one of PatchEvent's cases."),
    };

    private static (TranscriptLine, string, string) Spent(PatchEvent.Cost cost)
    {
        var spent = $"{cost.Input} in ({cost.CacheRead} cached), {cost.Output} out.";

        return (new(Voice.Aside, spent), "cost", spent);
    }
}
