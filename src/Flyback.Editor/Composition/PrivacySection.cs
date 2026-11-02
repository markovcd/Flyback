using Avalonia.Controls;
using Flyback.Editor.Settings;

namespace Flyback.Editor.Composition;

/// <summary>The Privacy section of the settings window: what Flyback sends out, updates and usage both.</summary>
internal sealed class PrivacySection(UpdatesSection updates, UsageSection usage) : ISettingsSection
{
    public string Name => "Privacy";

    public string Group => SettingsGroups.None;

    public Control View { get; } = new StackPanel
    {
        Spacing = 8,
        Width = SettingsSession.SectionWidth,
        Children = { SettingRows.Heading("Updates"), updates.View, SettingRows.Heading("Usage"), usage.View },
    };

    public void Show()
    {
        updates.Show();
        usage.Show();
    }

    public void Save()
    {
        updates.Save();
        usage.Save();
    }
}
