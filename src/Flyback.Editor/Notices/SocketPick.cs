namespace Flyback.Editor.Notices;

/// <summary>A socket clicked while linking sockets to a knob.</summary>
public readonly record struct SocketPick(Guid Node, int Port);
