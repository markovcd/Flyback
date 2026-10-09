namespace Flyback.Editor.Notices;

/// <summary>The text view was shown or hidden.</summary>
internal sealed record ViewChanged(bool ShowingCode) : INotice;
