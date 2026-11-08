using Android.Content;

namespace Flyback.Editor.Android;

/// <summary>
/// What the intent that started the activity asked for, so a script can drive the app:
/// <c>adb shell am start -n app.flybackmodular.editor/... --es preset Sidebands --ez interpreted true</c>,
/// <c>--ez stalls true</c> to write every stall over 100 ms to <see cref="DeviceStalls.Path"/>,
/// and <c>--ei cores 4</c> for the sound to use that many cores this run, whatever Settings says.
/// </summary>
internal sealed record DeviceRequest(string? Preset, bool Interpreted, bool Stalls, int? Cores)
{
    /// <summary>The intent the activity was last started with.</summary>
    public static DeviceRequest Current { get; private set; } = new(null, false, false, null);

    public static void Take(Intent? intent) =>
        Current = new(
            intent?.GetStringExtra("preset"),
            intent?.GetBooleanExtra("interpreted", false) ?? false,
            intent?.GetBooleanExtra("stalls", false) ?? false,
            intent?.GetIntExtra("cores", 0) is > 0 and var cores ? cores : null);
}
