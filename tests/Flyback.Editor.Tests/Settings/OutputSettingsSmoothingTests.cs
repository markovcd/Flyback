using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flyback.Engine.Render;
using Flyback.Host;
using Flyback.Core;
using Flyback.Ui.Controls;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Settings;

/// <summary>The Picture section's Smoothing row: a quick pass over each frame of the preview, on until turned off (ADR-0215).</summary>
public class OutputSettingsSmoothingTests : OutputSettingsTest
{
    [AvaloniaFact]
    public void The_preview_starts_smoothed()
    {
        var window = Open();

        All<PreviewHost>(window).Single().Smoothing.ShouldBe(Smoothing.Fast);
        (Smoothed(OpenSettings(window)).SelectedItem as string).ShouldBe("Fast");
    }

    [AvaloniaFact]
    public void Turning_smoothing_off_reaches_the_preview_on_save_and_is_kept()
    {
        var window = Open(settingsPath);
        var preview = All<PreviewHost>(window).Single();
        var dialog = OpenSettings(window);

        Smoothed(dialog).SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();

        preview.Smoothing.ShouldBe(Smoothing.Fast, "a draft until Save");

        CloseSettings(window, dialog);

        preview.Smoothing.ShouldBe(Smoothing.Off);
        OutputSettings.Load(settingsPath).Smoothing.ShouldBe(Smoothing.Off);
    }

    /// <summary>Four samples a pixel is a render's, so a file asking the preview for it, or for something unknown, reads as Fast.</summary>
    [AvaloniaTheory]
    [InlineData("""{ "smoothing": 2 }""")]
    [InlineData("""{ "smoothing": 41 }""")]
    [InlineData("""{ }""")]
    public void A_preview_smoothing_it_cannot_draw_reads_as_fast(string section)
    {
        SettingsFile.Write(settingsPath, OutputSettings.Section, section);

        OutputSettings.Load(settingsPath).Smoothing.ShouldBe(Smoothing.Fast);
    }

    private static ComboBox Smoothed(Avalonia.Visual within) => All<ComboBox>(within).Single(c => c.Name == "smoothing");
}
