namespace Flyback.Cli.Models;

/// <summary>What one of a patch's two programs costs.</summary>
/// <param name="Delays">Delay lines, which only the sound path ever has memory for.</param>
/// <param name="Phases">Phase accumulators — oscillators that carry their phase.</param>
/// <param name="Cells">One-evaluation cells, one per cycle in the graph.</param>
internal sealed record Cost(int Ops, int Registers, int Delays, int Phases, int Cells);