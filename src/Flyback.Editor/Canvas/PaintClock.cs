namespace Flyback.Editor.Canvas;

/// <summary>
/// How long the UI thread spends drawing the canvas, for a script to read: the last
/// draw, and the count and slowest since it last asked.
/// </summary>
/// <remarks>Only the drawing on the UI thread; turning it into pixels happens on the render thread.</remarks>
internal sealed class PaintClock
{
    private readonly Lock gate = new();
    private TimeSpan last, slowest;
    private int paints;

    public void Record(TimeSpan took)
    {
        lock (gate)
        {
            last = took;
            slowest = took > slowest ? took : slowest;
            paints++;
        }
    }

    /// <summary>The last draw, and the draws and the slowest of them since the last call, which starts the count again.</summary>
    public (TimeSpan Last, TimeSpan Slowest, int Paints) Take()
    {
        lock (gate)
        {
            var taken = (last, slowest, paints);
            (slowest, paints) = (TimeSpan.Zero, 0);

            return taken;
        }
    }
}
