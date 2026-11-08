namespace Flyback.Engine.Compile;

/// <summary>The cells of a <see cref="DelayState"/> one leg of a sound reads and writes (<see cref="SoundLegs"/>).</summary>
/// <param name="Lines">The leg's delay lines.</param>
/// <param name="Phases">Its accumulators.</param>
/// <param name="Units">The one-evaluation cells it reads or writes.</param>
/// <param name="Planes">The plane cells it reads or writes.</param>
/// <param name="Traces">The traces it taps.</param>
internal sealed record LegCells(int[] Lines, int[] Phases, int[] Units, int[] Planes, int[] Traces);
