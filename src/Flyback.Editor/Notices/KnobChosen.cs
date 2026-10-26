namespace Flyback.Editor.Notices;

/// <summary>
/// A panel knob was picked for a socket on the canvas: <paramref name="Control"/> to
/// follow, null for a new one, or with <paramref name="Unlink"/> the one to let go of.
/// </summary>
internal sealed record KnobChosen(SocketPick Pick, Guid? Control, bool Unlink = false) : INotice;
