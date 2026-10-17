namespace Flyback.Cli.Models;

/// <summary>
/// The edits <c>flyback-cli tool</c> has made to one patch file, and how many of them stand.
/// </summary>
/// <param name="Patch">The patch file's full path.</param>
/// <param name="Start">The patch the history began on, as patch JSON: what <c>reset</c> goes back to.</param>
/// <param name="Original">The file before the first step, or null where there was none.</param>
/// <param name="Steps">Every edit, oldest first, including any undone.</param>
/// <param name="At">How many of <paramref name="Steps"/> stand; the rest are there to redo.</param>
internal sealed record ToolLog(string Patch, string Start, byte[]? Original, List<ToolStep> Steps, int At)
{
    /// <summary>The file after the first <paramref name="steps"/> steps, or null for no file.</summary>
    public byte[]? File(int steps) => steps == 0 ? Original : Steps[steps - 1].After;

    /// <summary>The patch being worked on after the first <paramref name="steps"/> steps, as patch JSON.</summary>
    public string Working(int steps) => steps == 0 ? Start : Steps[steps - 1].Working;

    /// <summary>What each module answers to after the first <paramref name="steps"/> steps, or null where the type ids name them.</summary>
    public Dictionary<string, Guid>? Handles(int steps) => steps == 0 ? null : Steps[steps - 1].Handles;

    /// <summary>Whether <paramref name="file"/> is what the history last left, so its steps still apply.</summary>
    public bool Matches(byte[]? file) =>
        File(At) is { } expected ? file is not null && expected.AsSpan().SequenceEqual(file) : file is null;

    /// <summary>This history with <paramref name="step"/> made after the steps that stand, dropping any undone ones.</summary>
    public ToolLog Then(ToolStep step) => this with { Steps = [.. Steps.Take(At), step], At = At + 1 };
}
