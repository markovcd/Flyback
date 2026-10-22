using Flyback.Editor.Shell;
using Foundation;

namespace Flyback.Editor.iOS;

/// <summary>Where the editor keeps what it saves on a device: the app's Application Support folder, which nothing else reads.</summary>
internal static class DeviceFolders
{
    /// <summary>The app's own folder, made on first use.</summary>
    public static string Root()
    {
        var support = NSFileManager.DefaultManager.GetUrls(NSSearchPathDirectory.ApplicationSupportDirectory, NSSearchPathDomain.User)
            .FirstOrDefault()?.Path
            ?? throw new InvalidOperationException("The app has no Application Support folder.");

        Directory.CreateDirectory(support);

        return support;
    }

    public static EditorFolders Private()
    {
        var root = Root();

        return new()
        {
            GroupFolder = Path.Combine(root, "Groups"),
            PresetFolder = Path.Combine(root, "Presets"),
            ThumbnailFolder = Path.Combine(root, "Thumbnails"),
            SettingsPath = Path.Combine(root, "settings.json"),
            RecoveryFolder = Path.Combine(root, "Recovery"),
            SharedPresetFolder = Path.Combine(root, "Shared"),
        };
    }
}
