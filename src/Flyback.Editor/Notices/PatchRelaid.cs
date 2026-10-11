namespace Flyback.Editor.Notices;

/// <summary>Something about the whole patch rather than a module has changed, the keyboard's layout or the length, for the next write-back.</summary>
internal sealed record PatchRelaid : INotice;
