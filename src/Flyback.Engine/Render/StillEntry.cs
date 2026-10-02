using Flyback.Core.Graph;

namespace Flyback.Engine.Render;

/// <summary>One preset in a <see cref="StillIndex"/>: how it is offered, what it says of itself, and its still.</summary>
/// <param name="File">The still's file beside the index, or null where <paramref name="Still"/> says there is none.</param>
public sealed record StillEntry(
    string Name,
    PresetKind Kind,
    StillKind Still,
    string? File,
    string? Description = null,
    string? Author = null,
    IReadOnlyList<string>? Tags = null)
{
    /// <summary>The heading the editor lists the preset under.</summary>
    public string Heading => PresetKinds.Heading(Kind);
}
