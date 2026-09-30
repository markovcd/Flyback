namespace Flyback.App;

/// <summary>
/// Paces a page's recompiles to what it keeps up with: a knob dragged asks for one each
/// step, and a page has one thread for the canvas, the preview and the compile.
/// </summary>
/// <remarks>
/// The first ask after a quiet spell compiles at once. After that, one compile at most
/// every <see cref="Least"/> or twice as long as the last one took, whichever is longer,
/// and the last ask always compiles, so the patch lands where the drag stopped.
/// </remarks>
/// <param name="compile">Compiles the patch as it is now, and reports with <see cref="Ran"/>.</param>
/// <param name="clock">Time since some fixed moment.</param>
/// <param name="after">Runs an action once, after a delay, on the thread asks come from.</param>
public sealed class RecompilePacing(Action compile, Func<TimeSpan> clock, Action<TimeSpan, Action> after)
{
    /// <summary>The shortest gap between two compiles.</summary>
    public static readonly TimeSpan Least = TimeSpan.FromMilliseconds(100);

    private TimeSpan ended = TimeSpan.MinValue;
    private TimeSpan gap = Least;
    private bool pending;
    private bool waiting;

    /// <summary>Asks for a compile: now, or at the end of the gap, with every ask in between folded into it.</summary>
    public void Ask()
    {
        if (waiting)
        {
            pending = true;
            return;
        }

        var due = ended == TimeSpan.MinValue ? TimeSpan.Zero : ended + gap - clock();

        if (due <= TimeSpan.Zero)
        {
            compile();
            return;
        }

        pending = true;
        waiting = true;
        after(due, Fire);
    }

    /// <summary>Says a compile ran, from an ask or anywhere else, and how long it took.</summary>
    public void Ran(TimeSpan took)
    {
        ended = clock();
        gap = took * 2 > Least ? took * 2 : Least;
        pending = false;
    }

    private void Fire()
    {
        waiting = false;

        if (pending) compile();
    }
}
