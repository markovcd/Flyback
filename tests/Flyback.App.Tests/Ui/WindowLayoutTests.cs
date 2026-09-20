using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The window leaves as it was left: size, maximized state, the panels and the
/// views, and never a position (ADR-0121).
/// </summary>
public sealed class WindowLayoutTests : UiTest
{
    private readonly string layoutPath = Path.Combine(
        Path.GetTempPath(),
        "flyback-layout-" + Guid.NewGuid().ToString("N"),
        "layout.json");

    public override void Dispose()
    {
        base.Dispose();

        var folder = Path.GetDirectoryName(layoutPath);

        if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private MainWindow Open()
    {
        var window = Owned(new MainWindow(layoutPath: layoutPath));

        window.Show();
        Settle(window);
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    private static ToggleButton Button(MainWindow window, string name) =>
        All<ToggleButton>(window).Single(b => b.Name == name);

    private static WindowLayout Left(MainWindow window, string path)
    {
        window.CloseWithoutAsking();

        return WindowLayout.Load(path).ShouldNotBeNull();
    }

    [AvaloniaFact]
    public void A_missing_or_unreadable_file_is_no_layout()
    {
        WindowLayout.Load(layoutPath).ShouldBeNull();

        Directory.CreateDirectory(Path.GetDirectoryName(layoutPath)!);
        File.WriteAllText(layoutPath, "{ not json");

        WindowLayout.Load(layoutPath).ShouldBeNull();
    }

    [AvaloniaFact]
    public void Numbers_edited_out_of_range_are_brought_back()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(layoutPath)!);
        File.WriteAllText(layoutPath, """{ "width": -5, "canvasWeight": 0, "assistantWidth": -1, "sideWeight": 1000 }""");

        var layout = WindowLayout.Load(layoutPath).ShouldNotBeNull();

        layout.Width.ShouldBe(0);
        layout.CanvasWeight.ShouldBe(WindowLayout.DefaultCanvasWeight);
        layout.AssistantWidth.ShouldBe(WindowLayout.DefaultAssistantWidth);
        layout.SideWeight.ShouldBe(100);
    }

    [AvaloniaFact]
    public void The_views_and_panels_come_back_as_they_were_left()
    {
        var window = Open();

        Button(window, "code").IsChecked = true;
        Button(window, "swap").IsChecked = true;
        Button(window, "controls").IsChecked = true;
        Settle(window);

        var left = Left(window, layoutPath);

        left.Code.ShouldBeTrue();
        left.Swapped.ShouldBeTrue();
        left.ControlsOpen.ShouldBeTrue();

        var again = Open();

        Button(again, "code").IsChecked.ShouldBe(true);
        Button(again, "swap").IsChecked.ShouldBe(true);
        Button(again, "controls").IsChecked.ShouldBe(true);
    }

    [AvaloniaFact]
    public void A_panel_left_closed_stays_closed()
    {
        var window = Open();

        Button(window, "controls").IsChecked = false;
        Settle(window);

        Left(window, layoutPath).ControlsOpen.ShouldBeFalse();

        Button(Open(), "controls").IsChecked.ShouldBe(false);
    }

    [AvaloniaFact]
    public void The_dragged_column_weights_come_back()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(layoutPath)!);
        File.WriteAllText(layoutPath, """{ "canvasWeight": 1, "sideWeight": 1, "previewWeight": 1, "inspectorWeight": 3 }""");

        var window = Open();
        var grid = All<Grid>(window).Single(g => g.Name == "columns");

        grid.ColumnDefinitions[0].Width.Value.ShouldBe(1);
        grid.ColumnDefinitions[2].Width.Value.ShouldBe(1);
        grid.RowDefinitions[2].Height.Value.ShouldBe(3);
    }

    [AvaloniaFact]
    public void The_size_is_kept_and_the_position_is_not()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(layoutPath)!);
        File.WriteAllText(layoutPath, """{ "width": 1000, "height": 700 }""");

        var window = Open();

        window.ClientSize.Width.ShouldBe(1000, 1);
        window.ClientSize.Height.ShouldBe(700, 1);

        window.Width = 1100;
        window.Height = 720;
        Settle(window);

        var left = Left(window, layoutPath);

        left.Maximized.ShouldBeFalse();
        left.Width.ShouldBe(1100, 1);
        left.Height.ShouldBe(720, 1);

        using var json = JsonDocument.Parse(File.ReadAllText(layoutPath));

        json.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .ShouldNotContain(name => name.Contains("position", StringComparison.OrdinalIgnoreCase)
                || name == "x" || name == "y" || name == "left" || name == "top");
    }

    [AvaloniaFact]
    public void A_maximized_window_keeps_the_size_it_had_before()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(layoutPath)!);
        File.WriteAllText(layoutPath, """{ "maximized": true, "width": 1000, "height": 700 }""");

        var window = Open();

        window.WindowState.ShouldBe(WindowState.Maximized);

        var left = Left(window, layoutPath);

        left.Maximized.ShouldBeTrue();
        left.Width.ShouldBe(1000, 1);
        left.Height.ShouldBe(700, 1);
    }

    [AvaloniaFact]
    public void A_window_with_no_layout_path_writes_nothing()
    {
        var window = Owned(new MainWindow());

        window.Show();
        window.CloseWithoutAsking();

        File.Exists(layoutPath).ShouldBeFalse();
    }
}
