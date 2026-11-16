namespace Flyback.Host.Ableton;

/// <summary>
/// What a platform has to hold for multicast to reach this app at all. Android
/// drops multicast packets unless an app holds a Wi-Fi multicast lock, so its
/// shell installs one; everywhere else there is nothing to hold.
/// </summary>
internal static class MulticastPermit
{
    private static Func<IDisposable> take = () => Nothing.Instance;

    /// <summary>Points this at the platform's lock. Called once during startup.</summary>
    internal static void Install(Func<IDisposable> permit) => take = permit;

    /// <summary>Holds the lock until the result is disposed.</summary>
    public static IDisposable Take() => take();

    private sealed class Nothing : IDisposable
    {
        public static Nothing Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
