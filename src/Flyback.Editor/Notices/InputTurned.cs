using Flyback.Editor.Canvas;

namespace Flyback.Editor.Notices;

/// <summary>An input's value was turned on the canvas.</summary>
internal sealed record InputTurned(SocketPick Pick) : INotice;
