using Flyback.App.Canvas;

namespace Flyback.App.Notices;

/// <summary>The hand came off a socket it was turning on the canvas.</summary>
internal sealed record InputLetGo(SocketPick Pick);
