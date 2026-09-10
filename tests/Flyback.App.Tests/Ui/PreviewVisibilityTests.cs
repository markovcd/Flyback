using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The preview put away when a patch has nothing wired into the Output's 'color',
/// so the inspector takes the row rather than sitting under a box that could only
/// show black.
/// </summary>
/// <remarks>
/// A patch built only for the ear is as deliberate as one built only for the eye, so
/// this is about the row the preview stands in giving its share back — the way
/// <c>ShowAssistant</c> already does for the assistant.
/// </remarks>
public class PreviewVisibilityTests : UiTest
{
    private static MainWindow Open(Patch patch)
    {
        var window = new MainWindow();

        window.Show();
        Settle(window);

        Editor(window).Patch = patch;

        Settle(window);

        return window;
    }

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    private static PreviewHost Preview(MainWindow window) => All<PreviewHost>(window).Single();

    /// <summary>
    /// The grid the preview and the inspector share, found by walking up from
    /// the preview's own host rather than by name — it has none.
    /// </summary>
    private static Grid PreviewRow(MainWindow window)
    {
        var host = Preview(window);
        var border = host.FindAncestorOfType<Border>() ?? throw new InvalidOperationException("no Border above the preview");

        return border.GetVisualParent() as Grid ?? throw new InvalidOperationException("the preview's Border is not in a Grid");
    }

    private static Patch WiredTo(int port)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var knob = b.Add("value", 0, 0);
        var output = b.Add(NodeCatalog.OutputTypeId, 200, 0);

        b.Wire(knob, 0, output, port);

        return b.Patch;
    }

    [AvaloniaFact]
    public void The_preview_is_hidden_when_only_sound_is_wired()
    {
        var window = Open(WiredTo(NodeCatalog.OutputLeftPort));

        Preview(window).IsEffectivelyVisible.ShouldBeFalse();
        PreviewRow(window).RowDefinitions[0].ActualHeight.ShouldBe(0d, 0.5, "the row gives its share to the inspector");
    }

    [AvaloniaFact]
    public void The_preview_is_shown_when_color_is_wired()
    {
        var window = Open(WiredTo(NodeCatalog.OutputColorPort));

        Preview(window).IsEffectivelyVisible.ShouldBeTrue();
        PreviewRow(window).RowDefinitions[0].ActualHeight.ShouldBeGreaterThan(0d);
    }

    /// <summary>
    /// Loading a patch that has stopped reaching the screen puts the preview
    /// away, and the reverse brings it back — this is not merely the first
    /// patch a window happens to open on.
    /// </summary>
    [AvaloniaFact]
    public void The_preview_follows_the_patch_that_is_opened_next()
    {
        var window = Open(WiredTo(NodeCatalog.OutputColorPort));
        Preview(window).IsEffectivelyVisible.ShouldBeTrue();

        Editor(window).Patch = WiredTo(NodeCatalog.OutputLeftPort);
        Settle(window);
        Preview(window).IsEffectivelyVisible.ShouldBeFalse();

        Editor(window).Patch = WiredTo(NodeCatalog.OutputColorPort);
        Settle(window);
        Preview(window).IsEffectivelyVisible.ShouldBeTrue();
    }

    /// <summary>Patching and unpatching 'color' is an edit, not a new document, and is heard the same way.</summary>
    [AvaloniaFact]
    public void Unpatching_color_hides_the_preview_without_opening_a_new_patch()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var knob = b.Add("value", 0, 0);
        var output = b.Add(NodeCatalog.OutputTypeId, 200, 0);
        b.Wire(knob, 0, output, NodeCatalog.OutputColorPort);

        var window = Open(b.Patch);
        Preview(window).IsEffectivelyVisible.ShouldBeTrue();

        var editor = Editor(window);
        editor.Patch.Connections.Clear();
        editor.NotifyPatchChanged();
        Dispatcher.UIThread.RunJobs();
        Settle(window);

        Preview(window).IsEffectivelyVisible.ShouldBeFalse();
    }
}
