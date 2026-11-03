using Android.Content;
using Avalonia.Threading;

namespace Flyback.Editor.Android;

/// <summary>Starts the app again in a new process, which is what reads the settings that take effect only at a start.</summary>
internal static class DeviceRestart
{
    /// <summary>
    /// Asks for the launcher activity in a fresh task, then ends this process once the
    /// editor has finished what it was doing. The patch is not carried, since what
    /// Android opens has no path to name.
    /// </summary>
    public static void Launch(Reopen? reopen)
    {
        var context = global::Android.App.Application.Context;

        if (context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!)?.Component is not { } activity) return;

        Dispatcher.UIThread.Post(() =>
        {
            context.StartActivity(Intent.MakeRestartActivityTask(activity));
            Java.Lang.Runtime.GetRuntime()?.Exit(0);
        }, DispatcherPriority.Background);
    }
}
