using Flyback.Core.Graph;
using Flyback.Engine.Compile;

namespace Flyback.Editor.Sequence;

/// <summary>
/// How many sections the Song has played at a second of the patch's clock, read off a
/// program of the Song and whatever steps it, so a Tempo or a slowed clock is followed.
/// </summary>
internal sealed class SongClock
{
    /// <summary>The Song's last output: sections stepped through, never coming round.</summary>
    private const int PlayedPort = NodeCatalog.MaxParts + 2;

    private readonly CompiledPatch program;
    private readonly double[] registers;
    private readonly DelayState memory;
    private readonly int offset;

    private SongClock(CompiledPatch program, int offset)
    {
        this.program = program;
        this.offset = offset;
        registers = program.AllocateRegisters();
        memory = new DelayState(program, 48000);
    }

    /// <summary>The clock for <paramref name="song"/> in <paramref name="patch"/>, or null where it does not compile.</summary>
    public static SongClock? Of(Patch patch, Guid song)
    {
        try
        {
            var measured = patch.CompileForMeasure([song], sound: true);
            var socket = measured.Sockets.FirstOrDefault(s => s.Node == song && s.Port == PlayedPort);

            return socket is null ? null : new SongClock(measured.Program, socket.Offset);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Sections played by <paramref name="seconds"/>.</summary>
    public double Played(double seconds)
    {
        program.Evaluate(0d, 0d, seconds, registers, default, memory);
        var played = registers[program.OutputBase + offset];

        return double.IsFinite(played) ? Math.Max(0d, played) : 0d;
    }

    /// <summary>The first second at which <paramref name="sections"/> have played, within <paramref name="length"/> seconds, or null where they never do.</summary>
    public double? SecondsAt(double sections, double length)
    {
        if (sections <= Played(0d)) return 0d;
        if (Played(length) < sections) return null;

        var (low, high) = (0d, length);

        for (var i = 0; i < 40; i++)
        {
            var middle = (low + high) / 2;
            if (Played(middle) >= sections) high = middle;
            else low = middle;
        }

        return high;
    }
}
