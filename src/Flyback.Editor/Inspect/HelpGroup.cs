namespace Flyback.Editor.Inspect;

/// <summary>A run of related <see cref="HelpRow"/>s under one heading.</summary>
internal sealed record HelpGroup(string Title, IReadOnlyList<HelpRow> Rows);
