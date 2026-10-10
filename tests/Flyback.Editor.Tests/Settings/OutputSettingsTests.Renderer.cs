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
    // --- the render switch and the interpreter -----------------------------------

    /// <summary>One box, named for what would draw: the graphics card's APIs, then the CPU.</summary>
    [AvaloniaFact]
    public void The_render_box_names_what_would_draw()
    {
        var window = Open();
        var render = All<ComboBox>(OpenSettings(window)).Single(b => b.Name == "render");

        render.ItemsSource.ShouldBe(OperatingSystem.IsWindows()
            ? new[] { "OpenGL", "Direct3D", "CPU" }
            : new[] { "OpenGL", "CPU" });
    }

    /// <summary>
    /// Compiled and interpreted give the same bits, so which one runs is not a
    /// setting — only a flag a run is started with (ADR-0076).
    /// </summary>
    [AvaloniaFact]
    public void Whether_the_cpu_compiles_is_not_a_setting()
    {
        var window = Open();
        var dialog = OpenSettings(window);

        All<ToggleButton>(dialog).ShouldNotContain(b => b.Content as string == "Compiled" || b.Content as string == "Interpreted");
        All<TextBlock>(dialog).Select(t => t.Text).ShouldNotContain("CPU code");
    }

    /// <summary>
    /// Started interpreted, a run never puts IL under a picture the CPU draws, and
    /// says so once rather than leaving the status bar's word to be noticed.
    /// </summary>
    [AvaloniaFact]
    public void A_run_started_interpreted_stays_interpreted_and_says_so()
    {
        var window = NewMainWindow(new EditorSetup { Launch = new() { Interpreted = true } });

        window.Show();
        Settle(window);

        var preview = All<PreviewHost>(window).Single();

        preview.Use(PreviewBackend.Cpu);
        Settle(window);

        preview.Program.Il.ShouldBeNull();
        All<ReportLine>(window).Single().History.ShouldContain(line => line.Contains("interpreted"));
    }

    /// <summary>
    /// The one control whose draft could reach the picture on its own, so pinned
    /// by itself: turning the GPU off without saving leaves the shader asked for.
    /// </summary>
    [AvaloniaFact]
    public void The_gpu_switch_changes_nothing_until_save()
    {
        var window = Open();
        var preview = All<PreviewHost>(window).Single();
        var wanted = preview.Wanted;

        var dialog = OpenSettings(window);
        var gpu = All<ComboBox>(dialog).Single(b => b.Name == "render");

        gpu.SelectedItem = "CPU";
        Dispatcher.UIThread.RunJobs();

        preview.Wanted.ShouldBe(wanted);

        CloseSettings(window, dialog, "cancel");

        preview.Wanted.ShouldBe(wanted);
    }
}
