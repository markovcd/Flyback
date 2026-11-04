using Android.Content;

namespace Flyback.Editor.Android;

/// <summary>
/// What the intent that started the activity asked for, so a script can drive the app:
/// <c>adb shell am start -n app.flybackmodular.editor/... --es preset Sidebands --ez interpreted true</c>,
/// and <c>--ez stalls true</c> to write every stall over 100 ms to <see cref="DeviceStalls.Path"/>.
/// </summary>
internal sealed record DeviceRequest(string? Preset, bool Interpreted, bool Stalls)
{
    /// <summary>The intent the activity was last started with.</summary>
    public static DeviceRequest Current { get; private set; } = new(null, false, false);

    public static void Take(Intent? intent) =>
        Current = new(
            intent?.GetStringExtra("preset"),
            intent?.GetBooleanExtra("interpreted", false) ?? false,
            intent?.GetBooleanExtra("stalls", false) ?? false);
}
