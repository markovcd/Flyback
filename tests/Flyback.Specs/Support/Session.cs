using Flyback.Core.Graph;
using Flyback.Core.Language;

namespace Flyback.Specs.Support;

/// <summary>
/// What happened to the patch around its edits in one scenario: the file it went
/// through, the text it was read from, its undo history and what was pasted.
/// </summary>
public sealed class Session
{
    /// <summary>The picture before the patch went through a file or a text, to compare against after.</summary>
    public Frame? Before { get; set; }

    /// <summary>The sound before the patch went through a file, to compare against after.</summary>
    public IReadOnlyList<double>? BeforeSound { get; set; }

    public string? File { get; set; }

    public PatchLoad? Opened { get; set; }

    public LanguageLoad? Text { get; set; }

    public IReadOnlyList<NodeInstance> Pasted { get; set; } = [];

    public IReadOnlyList<PatchPreset> Presets { get; set; } = [];
}
