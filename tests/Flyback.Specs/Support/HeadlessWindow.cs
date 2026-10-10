using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Flyback.Ui.Testing;

namespace Flyback.Specs.Support;

/// <summary>
/// A window a scenario opens on the headless platform: its <see cref="HeadlessTurn"/>,
/// taken the first time it runs anything on the UI thread, and its close with the scenario.
/// </summary>
public abstract class HeadlessWindow(HeadlessTurn turn) : IDisposable
{
    /// <summary>The window while it is open, and null before and after.</summary>
    protected Window? Shown { get; set; }

    /// <summary>Runs <paramref name="act"/> on the UI thread, holding the platform for this scenario.</summary>
    protected void Run(Action act)
    {
        turn.Take(this);
        Headless.Run(act);
    }

    protected T Run<T>(Func<T> act)
    {
        turn.Take(this);
        return Headless.Run(act);
    }

    protected T Run<T>(Func<Task<T>> act)
    {
        turn.Take(this);
        return Headless.Run(act);
    }

    /// <summary>Runs layout to completion, on the UI thread.</summary>
    protected void Settle() => UiTest.Settle(Shown!);

    /// <summary>
    /// Presses and releases a key on <paramref name="open"/>, which takes the keyboard back
    /// first: it is the whole platform's, and another scenario's window may hold it.
    /// </summary>
    protected static void PressKey(Window open, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        open.Activate();
        open.KeyPressQwerty(key, modifiers);
        open.KeyReleaseQwerty(key, modifiers);
    }

    /// <summary>Closes the window, for a kind that would ask first.</summary>
    protected virtual void Close(Window open) => open.Close();

    public virtual void Dispose()
    {
        try
        {
            if (Shown is not { } open) return;

            Shown = null;

            Run(() =>
            {
                Close(open);
                Dispatcher.UIThread.RunJobs();
            });
        }
        finally
        {
            turn.Leave(this);
        }

        GC.SuppressFinalize(this);
    }
}
