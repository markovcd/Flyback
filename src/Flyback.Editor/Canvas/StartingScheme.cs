using Flyback.Editor.Shell;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Canvas;

/// <summary>The color scheme the editor starts in, as the Canvas settings chose it.</summary>
internal static class StartingScheme
{
    /// <summary>
    /// The scheme the settings in <paramref name="folders"/> name, from the shipped ones
    /// and those in its schemes folder; the first there is where the name finds none.
    /// </summary>
    public static Scheme For(EditorFolders folders)
    {
        var chosen = folders.SettingsPath is { } path ? CanvasSettings.Load(path).ColorScheme : null;

        return SchemeLibrary.Load(folders.SchemeFolder).Find(chosen);
    }
}
