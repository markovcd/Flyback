namespace Flyback.Plugins.Assist;

/// <summary>
/// How many tokens a conversation's next request will carry: what the provider said
/// the last one sent, or, where it says nothing, an estimate from what was sent.
/// </summary>
internal sealed class ContextGauge
{
    /// <summary>Characters to a token, roughly, across English and JSON.</summary>
    internal const int CharactersPerToken = 4;

    /// <summary>What one picture or clip is counted as. A strip of four 320x180 frames is a few hundred tokens.</summary>
    internal const int MediaTokens = 500;

    private long characters;
    private int media;

    private ContextGauge(long characters) => this.characters = characters;

    /// <summary>A gauge for a conversation over <paramref name="workbench"/>, which every request carries the briefing and tools of.</summary>
    public static ContextGauge Of(PatchWorkbench workbench, int reported = 0, IEnumerable<string>? said = null)
    {
        ArgumentNullException.ThrowIfNull(workbench);

        long characters = workbench.Briefing.Length;

        foreach (var tool in workbench.Tools) characters += tool.Name.Length + tool.Description.Length + tool.Schema.Length;
        foreach (var text in said ?? []) characters += text.Length;

        return new ContextGauge(characters) { Reported = reported };
    }

    /// <summary>What the provider said the last request sent, or 0 where it has said nothing.</summary>
    public int Reported { get; private set; }

    public int Tokens => Reported > 0
        ? Reported
        : (int)Math.Min(int.MaxValue, characters / CharactersPerToken + (long)media * MediaTokens);

    public void Report(int input)
    {
        if (input > 0) Reported = input;
    }

    public void Add(string? text) => characters += text?.Length ?? 0;

    public void AddMedia() => media++;

    /// <summary>Pictures and clips from earlier turns go as a line each.</summary>
    public void ForgetMedia() => media = 0;
}
