using Avalonia.Media;

namespace Flyback.Editor.Inspect;

/// <summary>What a plate says about its block, for the header and the menu that stand in for it on a short panel.</summary>
/// <param name="Title">The name as the canvas heads the block with it.</param>
/// <param name="Kind">What kind of thing it is: its category, or a box's count.</param>
/// <param name="Glyph">The block's mark, or null for one with none.</param>
/// <param name="MarkInk">What the mark is drawn in where it stands beside the name.</param>
/// <param name="Description">What it is, or null.</param>
/// <param name="Naming">How it is renamed, or null where it cannot be.</param>
internal sealed record PlateFace(string Title, string Kind, Geometry? Glyph, IBrush? MarkInk, string? Description, PlateName? Naming);
