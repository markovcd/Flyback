using Android;
using Android.App;
using Android.Content;
using Microsoft.Extensions.DependencyInjection;

namespace Flyback.Editor.Android;

/// <summary>
/// Answers <c>adb shell am broadcast -a app.flybackmodular.editor.STATE</c> with the editor's
/// <see cref="EditorReadout"/> as JSON, in the broadcast's data.
/// </summary>
internal sealed class DeviceState : BroadcastReceiver
{
    private const string Asked = "app.flybackmodular.editor.STATE";

    /// <summary>The running editor's container, or null before it has started.</summary>
    public static IServiceProvider? Provider { get; set; }

    /// <summary>On the main thread, which is Avalonia's UI thread on Android.</summary>
    public override void OnReceive(Context? context, Intent? intent) =>
        ResultData = Provider?.GetRequiredService<EditorReadout>().Read().ToJsonString() ?? """{"started":false}""";

    /// <summary>Listens while <paramref name="activity"/> lives, to senders holding DUMP: adb's shell, and no other app.</summary>
    public static DeviceState Listen(Activity activity)
    {
        var receiver = new DeviceState();
        var filter = new IntentFilter(Asked);

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            activity.RegisterReceiver(receiver, filter, Manifest.Permission.Dump, null, ReceiverFlags.Exported);
        else
            activity.RegisterReceiver(receiver, filter, Manifest.Permission.Dump, null);

        return receiver;
    }
}
