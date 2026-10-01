using Avalonia.Controls;
using Flyback.App.Settings;

namespace Flyback.App.Assist;

/// <summary>The Assistant section of the settings window, whose controls the assistant's panel keeps.</summary>
internal sealed class AssistantSection(AssistantPanel panel) : ISettingsSection
{
    public string Name => "Assistant";

    /// <summary>The panel's form, taken back from the window that last borrowed it as the settings open.</summary>
    public Control View { get; private set; } = new Panel();

    public void Opening() => View = panel.SettingsSection();

    public void Show() => panel.DiscardSettings();

    public void Save() => panel.SaveSettings();
}
