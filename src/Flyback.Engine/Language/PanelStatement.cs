namespace Flyback.Core.Language;

/// <summary>
/// <c>panel name = 0.5, label: "…", cc: 21, channel: 2, device: "…"</c>: a knob
/// on the patch's panel, which sockets follow by naming it where a number goes.
/// </summary>
/// <param name="Settings">Everything after the resting value, as named arguments.</param>
public sealed record PanelStatement(string Name, Expr Value, IReadOnlyList<Argument> Settings, int Line, int Column)
    : Statement(Line, Column);