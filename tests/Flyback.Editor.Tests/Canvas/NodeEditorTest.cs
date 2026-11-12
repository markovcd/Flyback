using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;

namespace Flyback.Editor.Tests.Canvas;

/// <summary>A canvas showing a patch, and the patches and clicks its tests share.</summary>
public abstract class NodeEditorTest : EditorTest
{
    private protected static NodeDef Sink => NodeCatalog.BuiltIn.Require(OutputModule.TypeId);

    /// <summary>
    /// An editor showing a patch, laid out large enough that framing it does not
    /// shrink the nodes to nothing.
    /// </summary>
    private protected (NodeEditor Editor, Window Window) Editing(Patch patch) => Editing(patch, 900, 700);

    /// <summary>A source with one output, and the Output block to wire it into.</summary>
    private protected static Patch Pair(out NodeInstance source, out NodeInstance sink)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        source = builder.Add("value", 0, 0);
        sink = builder.Add(OutputModule.TypeId, 420, 0);

        return builder.Patch;
    }

    private protected static void ClickAt(NodeEditor editor, Window window, Point graph)
    {
        var at = Screen(editor, window, graph);

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }
}
