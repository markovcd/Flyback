using Avalonia.Controls;
using Flyback.App.Statistics;
using Flyback.App.Updates;

namespace Flyback.App.Settings;

/// <summary>The Privacy section of the settings window: what Flyback sends out, updates and usage both.</summary>
internal sealed class PrivacySection(UpdatesSection updates, UsageSection usage) : ISettingsSection
{
    public string Name => "Privacy";

    public Control View { get; } = new StackPanel { Spacing = 24, Width = 280, Children = { updates.View, usage.View } };

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
