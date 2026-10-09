namespace Flyback.Core.Graph;

/// <summary>
/// A module the compiler patches into a socket that nothing else is patched into
/// — the rack's normalled bus, where an unplugged jack already carries the signal
/// that socket is nearly always used with.
/// </summary>
/// <remarks>
/// Named by type id rather than held as a definition, so a plugin can normal one
/// of its sockets to <c>time</c> without the engine knowing that plugin exists. A
/// type id the running catalog does not hold falls back to the knob.
/// <para>
/// What is patched in is one hidden instance shared by every socket normalled to
/// it, carrying no knobs of its own: there is no node on the canvas for anybody
/// to have turned one on.
/// </para>
/// </remarks>
/// <param name="TypeId">The module to read, as a saved patch would name it.</param>
/// <param name="Port">Which of that module's outputs.</param>
public readonly record struct PortNormal(string TypeId, int Port = 0);
