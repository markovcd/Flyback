namespace Flyback.Core.Compile;

/// <summary>
/// One Scope, as the two programs of a patch see it between them: the speakers'
/// program writes into it and the screen's program reads out of it.
/// </summary>
/// <param name="Node">
/// Which module this belongs to. The two programs eliminate different dead code,
/// so position in one says nothing about position in the other — the id pairs
/// them up.
/// </param>
/// <param name="Window">
/// How much of the past the chart is asking for, in seconds. Read off the knob at
/// compile time, because what fills the buffer runs once a frame and outside the
/// program.
/// </param>
/// <param name="Trace">
/// The buffer itself — written by whoever is refilling it, read by the screen's
/// program as an ordinary <see cref="OpCode.Table"/>. Empty in the speakers'
/// program, which writes the ring rather than the buffer.
/// </param>
/// <param name="Spectrum">
/// Whether the buffer is filled with the window's frequency content rather than
/// the window itself — see <see cref="Graph.NodeDef.ChartsSpectrum"/>.
/// </param>
public sealed record TapSpec(Guid Node, float Window, LoadedSample Trace, bool Spectrum = false);