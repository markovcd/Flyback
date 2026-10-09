namespace Flyback.Editor.Notices;

/// <summary>The swap button was pressed: the picture wants the wide column, or the canvas wants it back.</summary>
internal sealed record SwapAsked(bool Swapped) : INotice;
