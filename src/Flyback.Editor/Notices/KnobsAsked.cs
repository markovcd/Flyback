namespace Flyback.Editor.Notices;

/// <summary>The knob panel was asked for, or asked away, from the toolbar or Ctrl+K.</summary>
internal sealed record KnobsAsked(bool Shown) : INotice;
