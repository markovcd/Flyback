namespace Flyback.Core.Language;

/// <summary>
/// <c>keyboard scale [ C D E G A ]</c> or <c>keyboard piano</c>: how the
/// computer keyboard is laid out, which belongs to the patch rather than to any
/// module in it.
/// </summary>
/// <param name="Scale">The block as it was written, and null for a piano.</param>
/// <param name="BlockLine">Where the block's '[' is, for a complaint about what is inside it.</param>
/// <param name="BlockColumn">Where the block's '[' is, for a complaint about what is inside it.</param>
public sealed record KeyboardStatement(string? Scale, int Line, int Column, int BlockLine = 0, int BlockColumn = 0) : Statement(Line, Column);