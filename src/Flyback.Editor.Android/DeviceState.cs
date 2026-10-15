using Android;
using Android.App;
using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using Flyback.Editor.Composition;
using Flyback.Editor.Hubs;
using Flyback.Editor.Notices;

namespace Flyback.Editor.Android;

/// <summary>
/// Answers <c>adb shell am broadcast -a app.flybackmodular.editor.STATE</c> with the editor's
/// <see cref="EditorReadout"/> as JSON, in the broadcast's data. <c>…editor.PLAY</c> presses
/// Play first, since a patch opens paused, and <c>…editor.SIDE</c> presses the side button,
/// showing the preview and the inspector, or the canvas again with <c>--ez shown false</c>.
/// </summary>
internal sealed class DeviceState : BroadcastReceiver
{
    private const string Asked = "app.flybackmodular.editor.STATE";

    private const string Played = "app.flybackmodular.editor.PLAY";

    private const string Sided = "app.flybackmodular.editor.SIDE";

    /// <summary>The running editor's container, or null before it has started.</summary>
    public static IServiceProvider? Provider { get; set; }

    /// <summary>On the main thread, which is Avalonia's UI thread on Android.</summary>
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (Provider is null)
        {
            ResultData = """{"started":false}""";
            return;
        }

        if (intent?.Action == Played) Provider.GetRequiredService<Playback>().Resume();
        if (intent?.Action == Sided) Provider.GetRequiredService<Reactions>().Raise(new SideAsked(intent.GetBooleanExtra("shown", true)));

        ResultData = Provider.GetRequiredService<EditorReadout>().Read().ToJsonString();
    }

    /// <summary>Listens while <paramref name="activity"/> lives, to senders holding DUMP: adb's shell, and no other app.</summary>
    public static DeviceState Listen(Activity activity)
    {
        var receiver = new DeviceState();
        var filter = new IntentFilter(Asked);
        filter.AddAction(Played);
        filter.AddAction(Sided);

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            activity.RegisterReceiver(receiver, filter, Manifest.Permission.Dump, null, ReceiverFlags.Exported);
        else
            activity.RegisterReceiver(receiver, filter, Manifest.Permission.Dump, null);

        return receiver;
    }
}
