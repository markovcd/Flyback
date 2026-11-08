namespace Flyback.Engine.Render;

/// <summary>
/// How many cores a sound may be cut across (ADR-0230), for every program the process
/// builds from now on: Settings → Sound.
/// </summary>
public static class SoundCores
{
    /// <summary>Left to the machine: <see cref="Here"/>.</summary>
    public const int Automatic = 0;

    /// <summary>The most that can be asked for.</summary>
    public const int Highest = 8;

    private static int wanted = Automatic;

    /// <summary>Raised when <see cref="Wanted"/> changes, on the thread that changed it.</summary>
    public static event Action? Changed;

    /// <summary>
    /// What this machine gets when nobody asks: two cores on a phone or tablet, which a Pixel
    /// played no warmer than one, and half the cores up to four elsewhere.
    /// </summary>
    public static int Here { get; } =
        OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()
            ? Math.Clamp(Environment.ProcessorCount, 1, 2)
            : Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    /// <summary>What was asked for, <see cref="Automatic"/> or a count from 1 to <see cref="Highest"/>.</summary>
    public static int Wanted
    {
        get => Volatile.Read(ref wanted);
        set
        {
            var sane = Sane(value);
            if (Interlocked.Exchange(ref wanted, sane) != sane) Changed?.Invoke();
        }
    }

    /// <summary>The most legs a sound built now is cut into.</summary>
    public static int Most => Wanted is Automatic ? Here : Wanted;

    /// <summary>What can be picked here: automatic, then every count up to this machine's cores or <see cref="Highest"/>.</summary>
    public static IReadOnlyList<int> Choices { get; } =
        [Automatic, .. Enumerable.Range(1, Math.Clamp(Environment.ProcessorCount, 1, Highest))];

    /// <summary><paramref name="cores"/> if it can be asked for, otherwise <see cref="Automatic"/>.</summary>
    public static int Sane(int cores) => cores is >= 1 and <= Highest ? cores : Automatic;
}
