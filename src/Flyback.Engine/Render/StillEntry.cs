using Flyback.Core.Graph;

namespace Flyback.Core.Render;

/// <summary>One preset in a <see cref="StillIndex"/>: how it is offered, what it says of itself, and its still.</summary>
/// <param name="File">The still's file beside the index, or null where <paramref name="Still"/> says there is none.</param>
public sealed record StillEntry(
    string Name,
    PresetKind Kind,
    StillKind Still,
    string? File,
    string? Description = null,
    string? Author = null,
    IReadOnlyList<string>? Tags = null);
