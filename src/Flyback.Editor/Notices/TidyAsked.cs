namespace Flyback.Editor.Notices;

/// <summary>Laying the patch out was asked for; only the selected modules where <paramref name="OnlySelected"/>.</summary>
internal sealed record TidyAsked(bool OnlySelected) : INotice;
