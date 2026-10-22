using Avalonia.Threading;
using CoreFoundation;
using Flyback.Editor.Composition;
using Flyback.Editor.Hubs;
using Flyback.Editor.Notices;
using Microsoft.Extensions.DependencyInjection;

namespace Flyback.Editor.iOS;

/// <summary>
/// Answers the Darwin notification <c>app.flybackmodular.editor.STATE</c> by writing the
/// editor's <see cref="EditorReadout"/> as JSON to <c>readout.json</c> in the app's
/// Application Support folder. <c>…editor.PLAY</c> presses Play first, since a patch opens
/// paused, and <c>…editor.SIDE</c> presses the side button. On the simulator:
/// <c>xcrun simctl spawn booted notifyutil -p app.flybackmodular.editor.STATE</c>.
/// </summary>
internal static class DeviceState
{
    private const string Asked = "app.flybackmodular.editor.STATE";

    private const string Played = "app.flybackmodular.editor.PLAY";

    private const string Sided = "app.flybackmodular.editor.SIDE";

    /// <summary>The file a notification's answer is written to.</summary>
    public static string ReadoutPath => Path.Combine(DeviceFolders.Root(), "readout.json");

    /// <summary>Listens for as long as the app runs, which is as long as the editor does.</summary>
    public static void Listen(IServiceProvider provider)
    {
        // The Darwin center ignores the object observed, though the binding's signature asks for one.
        foreach (var name in new[] { Asked, Played, Sided })
            CFNotificationCenter.Darwin.AddObserver(name, null!, (received, _) => Dispatcher.UIThread.Post(() => Answer(provider, received)));
    }

    private static void Answer(IServiceProvider provider, string received)
    {
        if (received == Played) provider.GetRequiredService<Playback>().Resume();
        if (received == Sided) provider.GetRequiredService<Reactions>().Raise(new SideAsked(true));

        var written = ReadoutPath + ".part";
        File.WriteAllText(written, provider.GetRequiredService<EditorReadout>().Read().ToJsonString());
        File.Move(written, ReadoutPath, overwrite: true);
    }
}
