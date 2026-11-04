using Flyback.Ui;

namespace Flyback.Editor.Android;

/// <summary>
/// The stall trace a launch can ask for, in the app's folder on shared storage, where
/// <c>adb pull</c> reaches it without the app being debuggable.
/// </summary>
internal static class DeviceStalls
{
    private static StallWatch? watch;

    /// <summary>Where the trace is being written, or null while none is.</summary>
    public static string? Path { get; private set; }

    /// <summary>Starts the trace, on the UI thread, or leaves it as it is when one is already running.</summary>
    public static void Start()
    {
        if (watch is not null) return;

        var folder = global::Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath
            ?? global::Android.App.Application.Context.FilesDir?.AbsolutePath;

        if (folder is null) return;

        var path = System.IO.Path.Combine(folder, "stalls.txt");

        try
        {
            StallTrace.Open(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        Path = path;
        watch = new StallWatch();
    }
}
