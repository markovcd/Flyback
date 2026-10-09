namespace Flyback.Core.Graph;

/// <summary>
/// One port of one module inside a group, drawn on the edge of the box. The
/// module and the port rather than a number of its own: nothing renumbers, and a
/// wire drawn to one is a wire drawn to the module it names.
/// </summary>
/// <param name="IsOutput">
/// Which side it is, which is a fact about the port and not the box. Part of what
/// a socket is, because a module's inputs and outputs are numbered separately —
/// port 0 is very often both.
/// </param>
public readonly record struct GroupSocket(Guid Node, int Port, bool IsOutput);
