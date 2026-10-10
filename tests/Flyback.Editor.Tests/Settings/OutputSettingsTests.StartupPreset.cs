using Flyback.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.Editor.Assist;
using Flyback.Editor.Canvas;
using Flyback.Editor.Capture;
using Flyback.Editor.Controls;
using Flyback.Engine.Graph;
using Flyback.Ui.Controls;
using Flyback.Editor.Knobs;
using Flyback.Editor.Notices;
using Flyback.Editor.Settings;
using Flyback.Editor.Windows;
using Flyback.Core.Graph;
using Flyback.Engine.Render;
using Flyback.Plugins;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;
using Flyback.Ui;
using Flyback.Host;

namespace Flyback.Editor.Tests.Settings;

public partial class OutputSettingsTests
{
    // --- which preset the window opens on next (ADR-0093) --------------------

    private static Button StartupPreset(Visual within) => All<Button>(within).Single(c => c.Name == "defaultPreset");

    /// <summary>The name the Startup patch row shows.</summary>
    private static string? StartupName(Visual within) =>
        All<TextBlock>(within).Single(t => t.Name == "defaultPresetName").Text;

    /// <summary>The patches among the engine's presets, by name, in the order the gallery shows them.</summary>
    private static List<string> Patches =>
        [.. PresetOrder.Of(Presets.All).Where(p => p.Kind is not PresetKind.Blank).Select(p => p.Name)];

    /// <summary>Picks the startup patch the way a person would: the row's button, then a tile of the gallery it opens, and its button.</summary>
    private static void PickStartupPreset(MainWindow window, ModalOverlay dialog, string name)
    {
        Press(StartupPreset(dialog));

        for (var attempt = 0; attempt < 20 && All<ModalOverlay>(window).Count() < 2; attempt++)
            Dispatcher.UIThread.RunJobs();

        Settle(window);

        Press(All<Button>(window).Single(b => b.Name == "tile" && ((PatchPreset)b.Tag!).Name == name));
        Press(All<Button>(window).Single(b => b.Name == "use-preset"));

        for (var attempt = 0; attempt < 20 && All<ModalOverlay>(window).Count() > 1; attempt++)
            Dispatcher.UIThread.RunJobs();

        Settle(window);
    }

    /// <summary>A machine with no settings file names Plasma.</summary>
    [AvaloniaFact]
    public void The_startup_preset_starts_on_Plasma()
    {
        var window = Open();

        StartupName(OpenSettings(window, FilesTab)).ShouldBe(PresetLibrary.Fallback);
    }

    [AvaloniaFact]
    public void Giving_the_knobs_the_top_moves_the_transport_to_the_bottom_and_is_kept()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window);

        All<ComboBox>(dialog).Single(c => c.Name == "transportEdge").SelectedIndex = 1;
        CloseSettings(window, dialog);

        All<TransportOverlay>(window).Single().VerticalAlignment.ShouldBe(Avalonia.Layout.VerticalAlignment.Bottom);
        All<StageKnobs>(window).Single().VerticalAlignment.ShouldBe(Avalonia.Layout.VerticalAlignment.Top);
        OutputSettings.Load(settingsPath).Transport.ShouldBe(TransportEdge.Bottom);
    }

    [AvaloniaFact]
    public void A_knob_grid_set_in_the_settings_holds_the_panel_and_is_kept()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window, MidiTab);

        All<CheckBox>(dialog).Single(c => c.Name == "knobGrid").IsChecked = true;
        All<NumericUpDown>(dialog).Single(c => c.Name == "knobColumns").Value = 3;
        All<NumericUpDown>(dialog).Single(c => c.Name == "knobRows").Value = 2;
        CloseSettings(window, dialog);

        All<ControlsPanel>(window).Single().KnobGrid.ShouldNotBeNull().ToString().ShouldBe("3x2");
        All<StageKnobs>(window).Single().KnobGrid.ShouldNotBeNull().ToString().ShouldBe("3x2");
        OutputSettings.Load(settingsPath).KnobGrid.ToString().ShouldBe("3x2");
    }

    [AvaloniaFact]
    public void Save_is_drawn_apart_from_cancel()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window);

        IBrush? Fill(string name) => All<Avalonia.Controls.Presenters.ContentPresenter>(Named<Button>(dialog, name)).First().Background;

        Fill("save").ShouldNotBe(Fill("cancel"));
    }

    [AvaloniaFact]
    public void Columns_and_rows_sit_indented_under_the_knob_grid_switch()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window, MidiTab);

        var grid = All<CheckBox>(dialog).Single(c => c.Name == "knobGrid");

        foreach (var side in All<NumericUpDown>(dialog).Where(c => c.Name is "knobColumns" or "knobRows"))
        {
            var label = side.FindAncestorOfType<Grid>()!.Children.OfType<TextBlock>().Single();

            label.TranslatePoint(default, grid)!.Value.X.ShouldBeGreaterThan(8, $"{label.Text} is not indented under the switch");
        }
    }

    /// <summary>
    /// No caption, value or option in any tab is cut short. Every option of a list is
    /// measured, not only the one showing.
    /// </summary>
    [AvaloniaFact]
    public void Nothing_in_the_settings_window_is_clipped()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window);
        var clipped = new List<string>();

        foreach (var tab in Tabs(dialog).Items.OfType<TabItem>().ToList())
        {
            var name = ((TextBlock)tab.Header!).Text;

            ShowSettingsTab(dialog, name!);
            Settle(window);

            var page = (Visual)tab.Content!;

            foreach (var text in All<TextBlock>(page).Where(t => t.IsEffectivelyVisible && t.TextWrapping == TextWrapping.NoWrap))
            {
                if (string.IsNullOrEmpty(text.Text) || text.FindAncestorOfType<ComboBox>() is not null) continue;

                if (Natural(text.FontFamily, text.FontSize, text.FontWeight, text.Text) > text.Bounds.Width + 0.5) clipped.Add($"{name}: \"{text.Text}\"");
            }

            foreach (var list in All<ComboBox>(page).Where(c => c.IsEffectivelyVisible))
            {
                var room = All<ContentControl>(list).Single(c => c.Name == "ContentPresenter").Bounds.Width;

                foreach (var item in list.Items)
                {
                    var said = item is SettingOption option ? option.Name : item?.ToString() ?? "";

                    if (Natural(list.FontFamily, list.FontSize, list.FontWeight, said) > room + 0.5) clipped.Add($"{name}: {list.Name} option \"{said}\"");
                }
            }
        }

        clipped.ShouldBeEmpty();

        static double Natural(FontFamily family, double size, FontWeight weight, string said)
        {
            var probe = new TextBlock { Text = said, FontFamily = family, FontSize = size, FontWeight = weight };
            probe.Measure(Avalonia.Size.Infinity);

            return probe.DesiredSize.Width;
        }
    }

    [AvaloniaFact]
    public void A_hand_edited_knob_grid_is_brought_into_range()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        SettingsFile.Write(settingsPath, OutputSettings.Section, """{ "knobGrid": { "on": true, "columns": 0, "rows": 500 } }""");

        OutputSettings.Load(settingsPath).KnobGrid.ToString().ShouldBe("1x32");
    }

    [AvaloniaFact]
    public void A_file_with_no_knob_grid_wraps_the_knobs() =>
        OutputSettings.Load(settingsPath).KnobGrid.On.ShouldBeFalse();

    [AvaloniaFact]
    public void Picking_Direct3D_is_kept_for_the_next_launch_and_said()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows has a second driver");

        var window = Open(settingsPath);
        var dialog = OpenSettings(window);
        var render = All<ComboBox>(dialog).Single(c => c.Name == "render");

        render.SelectedItem.ShouldBe("OpenGL");

        render.SelectedItem = "Direct3D";
        CloseSettings(window, dialog);

        OutputSettings.Load(settingsPath).Driver.ShouldBe(GraphicsDriver.Direct3D);
        OutputSettings.Load(settingsPath).Gpu.ShouldBeTrue();
        All<ReportLine>(window).Single().History.ShouldContain("Direct3D draws from the next time Flyback starts.");
        All<ComboBox>(OpenSettings(Open(settingsPath))).Single(c => c.Name == "render").SelectedItem.ShouldBe("Direct3D");
    }

    /// <summary>The CPU draws the picture whatever draws the window, so picking it keeps the driver.</summary>
    [AvaloniaFact]
    public void Picking_the_cpu_keeps_the_driver()
    {
        new OutputSettings { Driver = GraphicsDriver.Direct3D }.Save(settingsPath);

        var window = Open(settingsPath);
        var dialog = OpenSettings(window);

        All<ComboBox>(dialog).Single(c => c.Name == "render").SelectedItem = "CPU";
        CloseSettings(window, dialog);

        OutputSettings.Load(settingsPath).Gpu.ShouldBeFalse();
        OutputSettings.Load(settingsPath).Driver.ShouldBe(GraphicsDriver.Direct3D);
    }

    [AvaloniaFact]
    public void Only_Windows_offers_Direct3D()
    {
        var dialog = OpenSettings(Open());

        All<ComboBox>(dialog).Any(c => c.Name == "driver").ShouldBeFalse();
        ((IEnumerable<string>)All<ComboBox>(dialog).Single(c => c.Name == "render").ItemsSource!)
            .Contains("Direct3D").ShouldBe(OperatingSystem.IsWindows());
    }

    /// <summary>What is picked here is a launch's business, not this one's.</summary>
    [AvaloniaFact]
    public void Picking_a_startup_preset_does_not_change_the_canvas()
    {
        var window = Open();
        var editor = Editor(window);
        var before = editor.History.Patch;

        var dialog = OpenSettings(window, FilesTab);

        PickStartupPreset(window, dialog, Patches[2]);

        StartupName(dialog).ShouldBe(Patches[2]);
        editor.History.Patch.ShouldBeSameAs(before);

        CloseSettings(window, dialog);

        editor.History.Patch.ShouldBeSameAs(before);
    }

    [AvaloniaFact]
    public void The_startup_preset_is_kept_and_opens_the_next_launch_on_it()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window, FilesTab);

        var chosen = Patches[3];

        PickStartupPreset(window, dialog, chosen);
        CloseSettings(window, dialog);

        OutputSettings.Load(settingsPath).DefaultPreset.ShouldBe(chosen);

        var next = Open(settingsPath);

        next.Title.ShouldBe($"{chosen} — {GlobalConstants.ApplicationName}");
        StartupName(OpenSettings(next, FilesTab)).ShouldBe(chosen);
    }

    /// <summary>Changed and not saved is dropped, like every other Picture row.</summary>
    [AvaloniaFact]
    public void A_startup_preset_changed_and_not_saved_is_dropped()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window, FilesTab);

        var before = StartupName(dialog);

        PickStartupPreset(window, dialog, Patches[4]);
        CloseSettings(window, dialog, "cancel");

        File.Exists(settingsPath).ShouldBeFalse();

        StartupName(OpenSettings(window, FilesTab)).ShouldBe(before);
    }

    /// <summary>
    /// Saving some other setting leaves a startup patch this launch cannot
    /// offer as it was.
    /// </summary>
    /// <remarks>
    /// A plugin's preset chosen as the startup patch, and one launch without
    /// the plugin. The choice is listed all the same, so it is the row shown
    /// and what Save writes back, rather than the patch the window fell back
    /// to. The sound settings beside it are kept for a backend that is not
    /// installed this launch, for the same reason.
    /// </remarks>
    [AvaloniaFact]
    public void A_startup_patch_this_launch_does_not_offer_is_kept()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        SettingsFile.Write(settingsPath, OutputSettings.Section, "{\"defaultPreset\":\"A Plugin's Preset\"}");

        OutputSettings.Load(settingsPath).DefaultPreset
            .ShouldBe("A Plugin's Preset", "the file is read the way this test expects");

        var window = Open(settingsPath);
        var dialog = OpenSettings(window);

        var rate = PreviewFrameRate(dialog);

        rate.SelectedIndex = rate.SelectedIndex == 1 ? 2 : 1;
        Settle(window);

        CloseSettings(window, dialog);

        OutputSettings.Load(settingsPath).DefaultPreset.ShouldBe("A Plugin's Preset");
    }
}
