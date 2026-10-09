namespace Flyback.Editor.Notices;

/// <summary>The code button was pressed: the text view is wanted, or the canvas back.</summary>
internal sealed record CodeAsked(bool Shown) : INotice;
