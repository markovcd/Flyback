using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flyback.Host;
using Flyback.Ui.Controls;
using Shouldly;

namespace Flyback.Editor.Tests.Settings;

public partial class OutputSettingsTests
{
    // --- the preview's own frame rate ------------------------------------

    private static ComboBox PreviewFrameRate(Visual within) =>
        All<ComboBox>(within).Single(c => c.Name == "previewFrameRate");

    /// <summary>Nothing has ever asked for a cap, so the preview starts uncapped.</summary>
    [AvaloniaFact]
    public void The_preview_rate_starts_unlimited()
    {
        var window = Open();
        var preview = All<PreviewHost>(window).Single();

        preview.FrameRate.ShouldBe(0);
        (PreviewFrameRate(OpenSettings(window)).SelectedItem as string).ShouldBe("Unlimited");
    }

    /// <summary>
    /// A cap picked is a draft, like every other Picture row: the preview
    /// keeps running uncapped until Save, and takes the new rate then.
    /// </summary>
    [AvaloniaFact]
    public void Changing_the_preview_rate_reaches_the_preview_only_on_save()
    {
        var window = Open();
        var preview = All<PreviewHost>(window).Single();

        var dialog = OpenSettings(window);

        PreviewFrameRate(dialog).SelectedIndex = 1; // 24 fps
        Dispatcher.UIThread.RunJobs();

        preview.FrameRate.ShouldBe(0, "a draft until Save");

        CloseSettings(window, dialog);

        preview.FrameRate.ShouldBe(24);
    }

    /// <summary>The cap is kept, the same way the recording rate is.</summary>
    [AvaloniaFact]
    public void The_preview_rate_is_kept_for_the_next_launch()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window);

        PreviewFrameRate(dialog).SelectedIndex = 3; // 30 fps
        CloseSettings(window, dialog);

        var kept = OutputSettings.Load(settingsPath);
        kept.PreviewFrameRate.ShouldBe(30);

        var again = OpenSettings(Open(settingsPath));
        (PreviewFrameRate(again).SelectedItem as string).ShouldBe("30 fps");
    }
}
