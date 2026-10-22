namespace Flyback.Editor.iOS;

/// <summary>
/// What the launch asked for, so a script can drive the app:
/// <c>xcrun simctl launch booted app.flybackmodular.editor --preset Sidebands --interpreted</c>.
/// </summary>
internal sealed record DeviceRequest(string? Preset, bool Interpreted)
{
    /// <summary>The arguments the app was launched with.</summary>
    public static DeviceRequest Current { get; private set; } = new(null, false);

    public static void Take(string[] args)
    {
        var at = Array.IndexOf(args, "--preset");

        Current = new(at >= 0 && at + 1 < args.Length ? args[at + 1] : null, args.Contains("--interpreted"));
    }
}
