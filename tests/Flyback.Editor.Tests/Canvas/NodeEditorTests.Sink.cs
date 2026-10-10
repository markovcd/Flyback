using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Flyback.Editor.Canvas;
using Flyback.Editor.Notices;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Shouldly;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Tests.Canvas;

public partial class NodeEditorTests
{
    // --- the sink is not deletable ------------------------------------------

    /// <summary>
    /// Delete on the Output does nothing at all — including not clearing the
    /// selection, which would take its settings panel away with it (ADR-0037).
    /// </summary>
    [AvaloniaFact]
    public void Delete_does_nothing_to_the_output_and_leaves_it_selected()
    {
        var patch = Pair(out _, out var sink);
        var (editor, window) = Editing(patch);

        ClickAt(editor, window, Body(sink));
        editor.Selection.Focused.ShouldBe(sink);

        editor.Edits.DeleteSelected();
        Settle(window);

        patch.Nodes.ShouldContain(sink, "the Output cannot be removed");
        editor.Selection.Focused.ShouldBe(sink, "and stays selected, or its panel would vanish");
    }

    [AvaloniaFact]
    public void Delete_removes_anything_else()
    {
        var patch = Pair(out var source, out _);
        var (editor, window) = Editing(patch);

        ClickAt(editor, window, Body(source));
        editor.Selection.Focused.ShouldBe(source);

        editor.Edits.DeleteSelected();
        Settle(window);

        patch.Nodes.ShouldNotContain(source);
    }
}
