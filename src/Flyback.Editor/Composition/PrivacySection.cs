using Avalonia.Controls;
using Flyback.Editor.Settings;
using Flyback.Editor.Shell;

namespace Flyback.Editor.Composition;

/// <summary>The Privacy section of the settings window: what Flyback sends out, updates (where it updates itself) and usage.</summary>
internal sealed class PrivacySection : ISettingsSection
{
    private readonly UpdatesSection? updates;
    private readonly UsageSection usage;
    private readonly StackPanel panel = new() { Spacing = 8, Width = SettingsSession.SectionWidth };

    public PrivacySection(EditorHost host, UpdatesSection updates, UsageSection usage)
    {
        this.updates = host.UpdatesItself ? updates : null;
        this.usage = usage;

        if (this.updates is not null)
        {
            panel.Children.Add(SettingRows.Heading("Updates"));
            panel.Children.Add(this.updates.View);
        }

        panel.Children.Add(SettingRows.Heading("Usage"));
        panel.Children.Add(usage.View);
    }

    public string Name => "Privacy";

    public string Group => SettingsGroups.None;

    public Control View => panel;

    public void Show()
    {
        updates?.Show();
        usage.Show();
    }

    public void Save()
    {
        updates?.Save();
        usage.Save();
    }
}
