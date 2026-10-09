using Reqnroll;

namespace Flyback.Specs.Support;

/// <summary>
/// One scenario's hold on the headless platform, from its first window to its last.
/// </summary>
/// <remarks>
/// The platform has one keyboard, one clipboard and one UI thread for the whole run, so
/// scenarios that open windows take turns: between two of one scenario's steps, another's
/// could take the keyboard focus, copy over what it put on the clipboard, or hold back the
/// timers its clock runs on.
/// </remarks>
public sealed class HeadlessTurn(ScenarioContext scenario)
{
    /// <summary>How long a scenario waits for the platform; the whole run of specs takes less.</summary>
    private static readonly TimeSpan Cap = TimeSpan.FromMinutes(5);

    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>The scenario holding the platform, and what took it.</summary>
    private static volatile string? holding;

    private readonly HashSet<object> holders = [];

    /// <summary>Waits for the platform, unless this scenario holds it already.</summary>
    /// <exception cref="TimeoutException">Another scenario held it past <see cref="Cap"/>.</exception>
    public void Take(object holder)
    {
        if (holders.Count == 0)
        {
            if (!Gate.Wait(Cap))
                throw new TimeoutException($"the headless platform is still held after {Cap.TotalMinutes:0} minutes, by {holding}");

            holding = $"\"{scenario.ScenarioInfo.Title}\" ({holder.GetType().Name})";
        }

        holders.Add(holder);
    }

    /// <summary>Lets the platform go once <paramref name="holder"/> was the last window left open.</summary>
    public void Leave(object holder)
    {
        if (holders.Remove(holder) && holders.Count == 0) Gate.Release();
    }
}
