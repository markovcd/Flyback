using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using Flyback.Editor.Assist;
using Flyback.Editor.Controls;
using Flyback.Ui.Controls;
using Flyback.Editor.Knobs;
using Flyback.Editor.Windows;
using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Editor.Tests.Controls;

public partial class SourceViewTests
{
    // --- the canvas as a view -----------------------------------------------

    /// <summary>
    /// A locked canvas keeps everything that looks and loses everything that
    /// changes. Deleting is the sharpest of those: the module would come
    /// straight back on the next evaluation, so the key is off rather than
    /// undone a moment later.
    /// </summary>
    [AvaloniaFact]
    public void A_locked_canvas_will_not_delete_a_module()
    {
        var window = Open();

        Evaluate(window, Hum);

        var editor = Editor(window);

        editor.Selection.SelectAll();
        editor.Focus();

        var before = editor.History.Patch.Nodes.Count;

        window.KeyPress(
            Key.Delete,
            RawInputModifiers.None,
            PhysicalKey.Delete,
            null);
        Settle(window);

        editor.History.Patch.Nodes.Count.ShouldBe(before);
    }

    /// <summary>
    /// And the panel that lists what the canvas can do lists what it can
    /// actually do. Naming gestures that are switched off would have somebody
    /// following them and concluding the program was broken.
    /// </summary>
    [AvaloniaFact]
    public void A_locked_canvas_does_not_offer_gestures_it_has_taken_away()
    {
        var window = Open();

        Evaluate(window, Hum);

        var said = string.Join(
            "\n",
            All<TextBlock>(window).Select(block => block.Text ?? string.Empty));

        said.ShouldNotContain("Right-click the canvas");
        said.ShouldNotContain("Delete removes what is selected");
        said.ShouldContain("Press F2 to go back to it");
    }

    /// <summary>
    /// But it still selects, because that is how somebody reads a patch and
    /// picks the module the inspector should be about.
    /// </summary>
    [AvaloniaFact]
    public void A_locked_canvas_still_selects()
    {
        var window = Open();

        Evaluate(window, Hum);

        var editor = Editor(window);

        editor.Selection.SelectAll();
        editor.Selection.Nodes.Count.ShouldBe(editor.History.Patch.Nodes.Count);
    }
}
