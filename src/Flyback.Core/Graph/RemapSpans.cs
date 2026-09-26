namespace Flyback.Core.Graph;

/// <summary>
/// What an Auto remap's two pairs of knobs mean: fractions of a range, or,
/// where a side is null, numbers typed by hand, with the reason it has no range.
/// </summary>
internal sealed record RemapSpans(RemapSpan? In, RemapSpan? Out, string? InWhy = null, string? OutWhy = null)
{
    public static RemapSpans Unwired { get; } = new(RemapSpan.Unit, RemapSpan.Unit);

    /// <summary>
    /// Whether everything it feeds takes a single number, so a color arriving is
    /// turned into its brightness before it is remapped rather than after.
    /// </summary>
    public bool Narrow { get; init; }
}