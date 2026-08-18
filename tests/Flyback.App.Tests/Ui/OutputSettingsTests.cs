using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.App;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The Output's panel, which is where every audio and video setting lives since
/// ADR-0037 emptied the toolbar into it.
/// </summary>
/// <remarks>
/// These exist because of how that panel is built. The controls in it are the
/// state of the instrument rather than of a selection, so they are made once and
/// moved into the inspector each time the Output is selected — and a control may
/// have one parent at a time. Selecting away and back is the sequence that
/// throws if the inspector ever stops detaching them first, and it is a runtime
/// exception rather than anything a compiler would catch.
/// </remarks>
public class OutputSettingsTests : UiTest
{
    /// <summary>
    /// The real window, on the preset it opens with. Nothing is stubbed: with no
    /// plugins loaded the catalogue is empty and the audio device is silent,
    /// which is the same path a machine with no sound backend takes.
    /// </summary>
    private static MainWindow Open()
    {
        var window = new MainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        return window;
    }

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    private static void Select(MainWindow window, NodeInstance node)
    {
        var editor = Editor(window);

        var body = new Point(
            node.X + NodeGeometry.Width / 2,
            node.Y + NodeGeometry.HeaderHeight / 2);

        var at = editor.TranslatePoint(editor.GraphToScreen.Transform(body), window)
            ?? throw new InvalidOperationException("the editor is not in this window");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    /// <summary>The panel is present exactly when its export button is in the tree.</summary>
    private static bool ShowingSettings(MainWindow window) =>
        All<Button>(window).Any(b => b.Content as string == "Export video…");

    private static ComboBox Size(MainWindow window) =>
        All<ComboBox>(window).Single(c => c.ItemsSource is IEnumerable<string> items && items.Any(i => i.Contains(" x ")));

    /// <summary>
    /// The export length, told apart from the Output's own knob rows by the
    /// range it was declared with — with the Output selected there are seven
    /// number boxes on the panel and only one of them is this.
    /// </summary>
    private static NumericUpDown Length(MainWindow window) =>
        All<NumericUpDown>(window).Single(n => n.Maximum == 600m);

    [AvaloniaFact]
    public void The_toolbar_no_longer_carries_the_settings()
    {
        var window = Open();

        // Nothing is selected on open, so none of it should be anywhere.
        ShowingSettings(window).ShouldBeFalse();
        All<Button>(window).Select(b => b.Content as string)
            .ShouldNotContain("Save frame…", "the picture settings belong to the Output now");
    }

    [AvaloniaFact]
    public void Selecting_the_output_shows_its_settings()
    {
        var window = Open();

        Select(window, Editor(window).Patch.Output);

        ShowingSettings(window).ShouldBeTrue();
        All<Button>(window).Select(b => b.Content as string).ShouldContain("Save frame…");
        All<Button>(window).Select(b => b.Content as string).ShouldContain("Render audio…");
    }

    [AvaloniaFact]
    public void Selecting_another_module_puts_them_away()
    {
        var window = Open();
        var patch = Editor(window).Patch;

        Select(window, patch.Output);
        ShowingSettings(window).ShouldBeTrue();

        Select(window, patch.Nodes.First(n => n.TypeId != NodeCatalog.OutputTypeId));

        ShowingSettings(window).ShouldBeFalse();
    }

    /// <summary>
    /// The one this suite is really for. A panel moved out of the inspector and
    /// back has to be detached on the way out, or re-adding it throws — and the
    /// only way to find out is to do it.
    /// </summary>
    [AvaloniaFact]
    public void Selecting_the_output_again_brings_them_back()
    {
        var window = Open();
        var patch = Editor(window).Patch;
        var other = patch.Nodes.First(n => n.TypeId != NodeCatalog.OutputTypeId);

        for (var round = 0; round < 3; round++)
        {
            Select(window, patch.Output);
            ShowingSettings(window).ShouldBeTrue($"round {round + 1}: the settings should be back");

            Select(window, other);
            ShowingSettings(window).ShouldBeFalse($"round {round + 1}: and away again");
        }
    }

    /// <summary>
    /// They are the instrument's state, not the selection's, so a round trip
    /// through another module must not reset them to what they were on launch.
    /// </summary>
    [AvaloniaFact]
    public void The_settings_keep_their_values_across_a_round_trip()
    {
        var window = Open();
        var patch = Editor(window).Patch;
        var other = patch.Nodes.First(n => n.TypeId != NodeCatalog.OutputTypeId);

        Select(window, patch.Output);

        Size(window).SelectedIndex = 1;
        Length(window).Value = 25m;

        Select(window, other);
        Select(window, patch.Output);

        Size(window).SelectedIndex.ShouldBe(1, "the preview size should have survived");
        Length(window).Value.ShouldBe(25m, "and so should the export length");
    }

    /// <summary>
    /// Changing the size has to reach the preview, not just the box showing it —
    /// the control is wired once at startup and the panel is rebuilt many times.
    /// </summary>
    [AvaloniaFact]
    public void Changing_the_size_reaches_the_preview()
    {
        var window = Open();
        var patch = Editor(window).Patch;

        Select(window, patch.Output);

        var preview = All<PreviewHost>(window).Single();
        var before = preview.Resolution;

        Size(window).SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();

        preview.Resolution.ShouldNotBe(before);
        preview.Resolution.Width.ShouldBe(320);
    }

    /// <summary>A sequencer gets its own list, and the Output does not.</summary>
    [AvaloniaFact]
    public void Only_the_output_gets_the_output_settings()
    {
        var window = Open();
        var editor = Editor(window);

        var sequencer = editor.AddNode("seq.notes");
        sequencer.ShouldNotBeNull();

        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        ShowingSettings(window).ShouldBeFalse("a sequencer is not the Output");
        All<TextBlock>(window).Select(t => t.Text).ShouldContain("A3", "but it does get its notes");
    }
}
