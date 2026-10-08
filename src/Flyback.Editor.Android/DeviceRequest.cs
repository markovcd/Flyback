using Android.Content;

namespace Flyback.Editor.Android;

/// <summary>
/// What the intent that started the activity asked for, so a script can drive the app:
/// <c>adb shell am start -n app.flybackmodular.editor/... --es preset Sidebands --ez interpreted true</c>.
/// </summary>
internal sealed record DeviceRequest(string? Preset, bool Interpreted)
{
    /// <summary>The intent the activity was last started with.</summary>
    public static DeviceRequest Current { get; private set; } = new(null, false);

    public static void Take(Intent? intent) =>
        Current = new(intent?.GetStringExtra("preset"), intent?.GetBooleanExtra("interpreted", false) ?? false);
}
