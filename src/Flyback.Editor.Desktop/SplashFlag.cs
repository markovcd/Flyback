namespace Flyback.Editor.Desktop;

/// <summary>The <c>--splash</c> a launch is given to show <see cref="SplashWindow"/> however quick it is.</summary>
internal static class SplashFlag
{
    public const string Name = "--splash";

    /// <summary>Whether <see cref="Name"/> was given, and the arguments without it, so it is never read as a patch to open.</summary>
    public static (bool Given, string[] Without) Taken(string[] args)
    {
        var without = args.Where(a => !string.Equals(a, Name, StringComparison.OrdinalIgnoreCase)).ToArray();

        return (without.Length < args.Length, without);
    }
}
