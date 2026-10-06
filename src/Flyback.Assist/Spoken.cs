using Flyback.Plugins.Assist;

namespace Flyback.Assist;

/// <summary>
/// One thing said in a conversation as a transcript is handed it: the line it
/// shows, and the word and text a log or a <c>--json</c> stream files it under.
/// </summary>
/// <param name="Keep">Whether it is saved with the patch, rather than only about this showing of it.</param>
/// <param name="Event">What the assistant did, where this is one of its turn's events.</param>
internal sealed record Spoken(TranscriptLine Line, string Kind, string Text, bool Keep = true, PatchEvent? Event = null)
{
    /// <summary>How one thing a turn did reads.</summary>
    public static Spoken Of(PatchEvent happened) => happened switch
    {
        PatchEvent.Said said => new(new(Voice.Said, said.Text), "said", said.Text, Event: happened),
        PatchEvent.Did did => new(new(Voice.Note, did.Summary), "did", did.Summary, Event: happened),
        PatchEvent.Read read => new(new(Voice.Handbook, read.Text), "read", read.Text, Event: happened),
        PatchEvent.Saw saw => new(new(Voice.Note, saw.Caption), "saw", saw.Caption, Event: happened),
        PatchEvent.Heard heard => new(new(Voice.Note, heard.Caption), "heard", heard.Caption, Event: happened),
        PatchEvent.Cost cost => Spent(cost),
        PatchEvent.Proposed proposed => new(new(Voice.Proposed, $"Proposed: {proposed.Summary}"), "proposed", proposed.Summary, Event: happened),
        PatchEvent.Failed failed => new(new(Voice.Failed, failed.Message), "failed", failed.Message, Event: happened),
        _ => throw new ArgumentOutOfRangeException(nameof(happened), happened, "Not one of PatchEvent's cases."),
    };

    private static Spoken Spent(PatchEvent.Cost cost)
    {
        var spent = $"{cost.Input} in ({cost.CacheRead} cached), {cost.Output} out"
            + (cost.Model is null ? "." : $", from {cost.Model}.");

        return new(new(Voice.Aside, spent), "cost", spent, Event: cost);
    }
}
