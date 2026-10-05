using Flyback.Engine.Compile;

namespace Flyback.Engine.Slopes;

/// <summary>
/// The slopes of everything a <see cref="DelayState"/> remembers, one lane per
/// knob, carried from one evaluation to the next.
/// </summary>
internal sealed class SlopeMemory
{
    public SlopeMemory(CompiledPatch program, DelayState delays, int lanes)
    {
        Lanes = lanes;

        var lines = program.DelayLengths.Count;

        Lines = new double[lines][];
        Heads = new int[lines];
        for (var i = 0; i < lines; i++) Lines[i] = new double[delays.LineLength(i) * lanes];

        Phases = new double[program.PhaseCount * lanes];
        PreviousSlopes = new double[program.PhaseCount * lanes];
        PreviousInputs = new double[program.PhaseCount];
        Running = new bool[program.PhaseCount];

        Units = new double[program.UnitCount * lanes];
        Planes = new double[program.PlaneCount * lanes];
    }

    public int Lanes { get; }

    public double[][] Lines { get; }

    public int[] Heads { get; }

    public double[] Phases { get; }

    public double[] PreviousSlopes { get; }

    public double[] PreviousInputs { get; }

    public bool[] Running { get; }

    public double[] Units { get; }

    public double[] Planes { get; }

    /// <summary>Line <paramref name="slot"/>'s slopes from <paramref name="seconds"/> ago, interpolated as <see cref="DelayState.Read"/> is.</summary>
    public void Read(int slot, double seconds, float maximum, int sampleRate, Span<double> into)
    {
        var line = Lines[slot];
        var length = line.Length / Lanes;

        if (!double.IsFinite(seconds)) seconds = 0d;

        var samples = Math.Min(Math.Clamp(seconds, 0d, maximum) * sampleRate, length - 2);
        var whole = (int)samples;
        var fraction = samples - whole;

        var first = Wrap(Heads[slot] - whole, length) * Lanes;
        var second = Wrap(Heads[slot] - whole - 1, length) * Lanes;

        for (var j = 0; j < Lanes; j++)
            into[j] = line[first + j] + (line[second + j] - line[first + j]) * fraction;
    }

    /// <summary>Writes slopes at the head of line <paramref name="slot"/> and advances it.</summary>
    public void Write(int slot, ReadOnlySpan<double> slopes)
    {
        var line = Lines[slot];
        var length = line.Length / Lanes;
        var next = Wrap(Heads[slot] + 1, length);

        slopes.CopyTo(line.AsSpan(next * Lanes, Lanes));
        Heads[slot] = next;
    }

    private static int Wrap(int index, int length)
    {
        index %= length;
        return index < 0 ? index + length : index;
    }
}
