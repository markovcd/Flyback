using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

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

    /// <summary>
    /// A toolbar control by the name it was given. The buttons up there are
    /// glyphs now, and a glyph is a poor thing to write an assertion against.
    /// </summary>
    private static T Named<T>(MainWindow window, string name)
        where T : Control =>
        All<T>(window).Single(c => c.Name == name);

    /// <summary>What every button in the window is labelled, in tree order.</summary>
    private static IEnumerable<string?> Buttons(MainWindow window) =>
        All<Button>(window).Select(b => b.Content as string);

    /// <summary>The panel is present exactly when its export button is in the tree.</summary>
    private static bool ShowingSettings(MainWindow window) =>
        All<Button>(window).Any(b => b.Content as string == "Export…");

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

        Buttons(window).ShouldNotContain("Rewind", "the timeline belongs to the Output now");
    }

    /// <summary>
    /// Everything that writes a file is the one button. There was a second for
    /// stills until a PNG became one of the kinds the first offers, and the
    /// difference between them was never anything a person would have said out
    /// loud — both write what the patch is doing to a file.
    /// </summary>
    [AvaloniaFact]
    public void There_is_one_button_for_writing_a_file()
    {
        var window = Open();

        Select(window, Editor(window).Patch.Output);

        Buttons(window).ShouldContain("Export…");
        Buttons(window).ShouldNotContain("Save frame…");
    }

    /// <summary>
    /// The assistant opens under the canvas rather than across the window, so
    /// the palette and the inspector keep their height while a conversation is
    /// going on. Both are the same width because they are the same column.
    /// </summary>
    [AvaloniaFact]
    public void The_assistant_shares_the_canvas_column()
    {
        var window = Open();

        var assistant = All<AssistantPanel>(window).Single();
        var editor = Editor(window);

        assistant.GetVisualParent().ShouldBeSameAs(
            editor.GetVisualParent(),
            "the two are stacked in one column, not one above the whole window");

        var toggle = Named<ToggleButton>(window, "assistant");

        toggle.IsChecked = true;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        assistant.IsVisible.ShouldBeTrue();
        assistant.Bounds.Width.ShouldBe(editor.Bounds.Width, 1);
        assistant.Bounds.Width.ShouldBeLessThan(window.Bounds.Width - 200, "the side panels are still beside it");
    }

    /// <summary>
    /// About is on the toolbar rather than in the Output's panel: it is about
    /// the program, and nothing there is about a patch at all.
    /// </summary>
    [AvaloniaFact]
    public void About_is_on_the_toolbar_whatever_is_selected()
    {
        var window = Open();

        Named<Button>(window, "about").ShouldNotBeNull();

        Select(window, Editor(window).Patch.Output);

        Named<Button>(window, "about").ShouldNotBeNull();
    }

    /// <summary>
    /// Three of the buttons are drawn rather than typed. A folder and a floppy
    /// disk are what open and save look like everywhere, and neither is a
    /// character any font here can be relied on to have — the code points exist,
    /// and on Windows they resolve to the color emoji font, which would put
    /// full-color pictures in a bar of thin grey strokes. Tidy is drawn for the
    /// opposite reason: no character means what it does, so it is a patch in
    /// miniature instead.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("open")]
    [InlineData("save")]
    [InlineData("tidy")]
    public void The_drawn_icons_are_drawn_rather_than_typed(string name)
    {
        var window = Open();
        var icon = Named<Button>(window, name).Content.ShouldBeOfType<Avalonia.Controls.Shapes.Path>();

        icon.Data.ShouldNotBeNull();

        // Taken from the button rather than set here, so that hovering, pressing
        // and grey-out all reach it. A binding that failed to resolve leaves this
        // null and draws nothing at all.
        icon.Stroke.ShouldNotBeNull("the stroke follows the button's own foreground");
    }

    /// <summary>
    /// Every toolbar button is a symbol now, so the tip is the only place it
    /// says what it does. One without is a button nobody can identify.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("open")]
    [InlineData("save")]
    [InlineData("undo")]
    [InlineData("redo")]
    [InlineData("assistant")]
    [InlineData("settings")]
    [InlineData("about")]
    [InlineData("tidy")]
    public void Every_toolbar_icon_says_what_it_is(string name)
    {
        var window = Open();
        var button = Named<ContentControl>(window, name);

        var tip = ToolTip.GetTip(button) as string;

        tip.ShouldNotBeNullOrWhiteSpace();
        tip.ShouldNotBe(button.Content as string, "the tip is a sentence, not the glyph again");
        tip.Length.ShouldBeGreaterThan(8);
    }

    /// <summary>
    /// Rewind moves the picture and the sound together, so it lives with the
    /// rest of the instrument rather than on the toolbar it used to sit on.
    /// </summary>
    [AvaloniaFact]
    public void Rewind_is_on_the_output_panel()
    {
        var window = Open();

        Buttons(window).ShouldNotContain("Rewind");

        Select(window, Editor(window).Patch.Output);

        Buttons(window).ShouldContain("Rewind");
    }

    [AvaloniaFact]
    public void Selecting_the_output_shows_its_settings()
    {
        var window = Open();

        Select(window, Editor(window).Patch.Output);

        ShowingSettings(window).ShouldBeTrue();

        Buttons(window).ShouldContain("Export…");
        Buttons(window).ShouldContain("Rewind");
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

    // --- the export button --------------------------------------------------

    private static Button Export(MainWindow window) =>
        All<Button>(window).Single(b => b.Content as string is "Export…" or "Stop");

    /// <summary>The preset it opens on draws something, so there is a file to write.</summary>
    [AvaloniaFact]
    public void The_export_is_offered_when_the_patch_reaches_something()
    {
        var window = Open();
        Select(window, Editor(window).Patch.Output);

        Export(window).IsEnabled.ShouldBeTrue();
    }

    /// <summary>
    /// Nothing wired into either half of the Output means nothing to write, and
    /// the button says so by being greyed rather than by opening a dialog with
    /// an empty list of file types.
    /// </summary>
    [AvaloniaFact]
    public void The_export_is_greyed_out_when_the_patch_reaches_nothing()
    {
        var window = Open();
        var editor = Editor(window);

        editor.Patch = Presets.Empty(NodeCatalog.BuiltIn);
        Select(window, editor.Patch.Output);

        Export(window).IsEnabled.ShouldBeFalse();
    }

    /// <summary>
    /// And it comes back the moment something reaches it — the state follows the
    /// patch rather than being decided once when the panel was built.
    /// </summary>
    [AvaloniaFact]
    public void Wiring_something_up_brings_the_export_back()
    {
        var window = Open();
        var editor = Editor(window);

        editor.Patch = Presets.Empty(NodeCatalog.BuiltIn);
        Select(window, editor.Patch.Output);
        Export(window).IsEnabled.ShouldBeFalse();

        var knob = editor.AddNode("value");
        knob.ShouldNotBeNull();
        editor.Patch.Connect(knob.Id, 0, editor.Patch.Output.Id, NodeCatalog.OutputColorPort);
        editor.NotifyPatchChanged();

        Select(window, editor.Patch.Output);

        Export(window).IsEnabled.ShouldBeTrue();
    }

    /// <summary>A greyed control that will not say why is worse than no control.</summary>
    [AvaloniaFact]
    public void The_greyed_export_says_why()
    {
        var window = Open();
        var editor = Editor(window);

        editor.Patch = Presets.Empty(NodeCatalog.BuiltIn);
        Select(window, editor.Patch.Output);

        var button = Export(window);

        ToolTip.GetShowOnDisabled(button).ShouldBeTrue("or the reason is never read");
        (ToolTip.GetTip(button) as string).ShouldNotBeNull().ShouldContain("nothing to write");
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
