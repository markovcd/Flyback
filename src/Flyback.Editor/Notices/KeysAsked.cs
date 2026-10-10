namespace Flyback.Editor.Notices;

/// <summary>The toolbar's keys button was pressed in or let out.</summary>
internal sealed record KeysAsked(bool Shown) : INotice;
