namespace Flyback.Editor.Android;

/// <summary>Where the editor keeps what it saves on a device: the app's private folder, which nothing else reads.</summary>
internal static class DeviceFolders
{
    public static EditorFolders Private()
    {
        var root = global::Android.App.Application.Context.FilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("The app has no private folder.");

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
