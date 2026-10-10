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

/// <summary>
/// The canvas. ADR-0017 chose one custom-drawn control over composed ones and
/// rests that choice on a single claim: painting and hit-testing cannot drift
/// apart, because both go through NodeGeometry. Nothing checked it until these —
/// and a drift there is the worst kind of bug this program can have, because the
/// socket is drawn where you see it and answers somewhere else.
/// </summary>
public partial class NodeEditorTests : EditorTest
{
    private static NodeDef Sink => NodeCatalog.BuiltIn.Require(NodeCatalog.OutputTypeId);

    /// <summary>
    /// An editor showing a patch, laid out large enough that framing it does not
    /// shrink the nodes to nothing.
    /// </summary>
    private (NodeEditor Editor, Window Window) Editing(Patch patch) => Editing(patch, 900, 700);

    /// <summary>A source with one output, and the Output block to wire it into.</summary>
    private static Patch Pair(out NodeInstance source, out NodeInstance sink)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        source = builder.Add("value", 0, 0);
        sink = builder.Add(NodeCatalog.OutputTypeId, 420, 0);

        return builder.Patch;
    }

    private static void ClickAt(NodeEditor editor, Window window, Point graph)
    {
        var at = Screen(editor, window, graph);

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }
}
