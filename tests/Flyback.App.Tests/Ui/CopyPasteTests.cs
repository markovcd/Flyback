using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Copy and paste on the canvas, through the system clipboard the real gesture
/// uses.
/// </summary>
/// <remarks>
/// Nothing here stubs the clipboard. What goes on it is the JSON a patch is
/// saved as, so these are also the check that the two ends agree about the
/// format — and that pasting something which is not a patch at all is answered
/// with a sentence rather than an exception.
/// </remarks>
public class CopyPasteTests : UiTest
{
    private const double Wide = 1200;
    private const double Tall = 800;

    private static Patch Chain(out NodeInstance time, out NodeInstance osc, out NodeInstance sink)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        time = b.Add("time", 0, 0);
        osc = b.Add("osc.sine", 0, 260, (1, 220f));
        sink = b.Add(NodeCatalog.OutputTypeId, 520, 120);

        b.Wire(time, 0, osc, 0).Wire(osc, 0, sink, NodeCatalog.OutputLeftPort);

        return b.Patch;
    }

    private static (NodeEditor Editor, Window Window) Editing(Patch patch)
    {
        var editor = new NodeEditor { Width = Wide, Height = Tall };
        var window = Show(editor, Wide);

        editor.Patch = patch;
        Settle(window);

        return (editor, window);
    }

    private static Point Body(NodeInstance node) =>
        new(node.X + NodeGeometry.Width / 2, node.Y + NodeGeometry.HeaderHeight / 2);

    private static void Click(
        NodeEditor editor, Window window, NodeInstance node, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var at = editor.TranslatePoint(editor.GraphToScreen.Transform(Body(node)), window)
            ?? throw new InvalidOperationException("the editor is not in this window");

        window.MouseDown(at, MouseButton.Left, modifiers);
        window.MouseUp(at, MouseButton.Left, modifiers);
        Settle(window);
    }

    private static IClipboard Clipboard(Window window) =>
        TopLevel.GetTopLevel(window)?.Clipboard
        ?? throw new InvalidOperationException("no clipboard under this window");

    private static int Count(Patch patch, string typeId) => patch.Nodes.Count(n => n.TypeId == typeId);

    // --- copying ------------------------------------------------------------

    /// <summary>
    /// What goes on the clipboard is a patch file. Which is the whole reason for
    /// using the system clipboard at all: it pastes into another window of this
    /// program, and into a text editor as something a person can read.
    /// </summary>
    [AvaloniaFact]
    public async Task What_is_copied_is_a_patch()
    {
        var patch = Chain(out var time, out var osc, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, time);
        Click(editor, window, osc, RawInputModifiers.Control);

        (await editor.CopySelectionAsync()).ShouldBeNull();

        var text = await Clipboard(window).TryGetTextAsync();
        var loaded = PatchIo.Read(text.ShouldNotBeNull(), NodeCatalog.BuiltIn);

        loaded.IsComplete.ShouldBeTrue();
        loaded.Patch.Nodes.ShouldContain(n => n.TypeId == "osc.sine");
        loaded.Patch.Nodes.ShouldContain(n => n.TypeId == "time");
    }

    [AvaloniaFact]
    public async Task Copying_nothing_leaves_the_clipboard_alone()
    {
        var patch = Chain(out _, out _, out _);
        var (editor, window) = Editing(patch);

        await Clipboard(window).SetTextAsync("something else");

        (await editor.CopySelectionAsync()).ShouldBeNull("an empty selection has nothing to say");
        (await Clipboard(window).TryGetTextAsync()).ShouldBe("something else");
    }

    /// <summary>
    /// The Output cannot be copied, and a gesture that silently did nothing
    /// would read as a broken one.
    /// </summary>
    [AvaloniaFact]
    public async Task Copying_the_output_alone_says_why_it_did_not()
    {
        var patch = Chain(out _, out _, out var sink);
        var (editor, window) = Editing(patch);

        Click(editor, window, sink);

        (await editor.CopySelectionAsync()).ShouldNotBeNullOrWhiteSpace();
    }

    // --- pasting ------------------------------------------------------------

    [AvaloniaFact]
    public async Task Pasting_adds_the_modules_and_the_wire_between_them()
    {
        var patch = Chain(out var time, out var osc, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, time);
        Click(editor, window, osc, RawInputModifiers.Control);

        await editor.CopySelectionAsync();
        (await editor.PasteAsync()).ShouldBeNull();

        Count(patch, "time").ShouldBe(2);
        Count(patch, "osc.sine").ShouldBe(2);

        var pasted = editor.SelectedNodes.Single(n => n.TypeId == "osc.sine");
        patch.IncomingTo(pasted.Id, 0).ShouldNotBeNull("the wire came with them");
    }

    [AvaloniaFact]
    public async Task What_was_pasted_is_what_is_selected_afterwards()
    {
        var patch = Chain(out var time, out var osc, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, time);
        Click(editor, window, osc, RawInputModifiers.Control);

        await editor.CopySelectionAsync();
        await editor.PasteAsync();

        editor.SelectedNodes.Count.ShouldBe(2);
        editor.SelectedNodes.ShouldNotContain(n => n.Id == time.Id || n.Id == osc.Id);
        editor.SelectedNode.ShouldNotBeNull();
    }

    /// <summary>
    /// A paste landing exactly on what it was copied from reads as nothing
    /// having happened, and so does a second paste landing on the first.
    /// </summary>
    [AvaloniaFact]
    public async Task Pasting_twice_gives_two_groups_in_two_places()
    {
        var patch = Chain(out var time, out _, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, time);
        await editor.CopySelectionAsync();

        await editor.PasteAsync();
        var first = editor.SelectedNodes.Single();

        await editor.PasteAsync();
        var second = editor.SelectedNodes.Single();

        (second.X, second.Y).ShouldNotBe((first.X, first.Y));
        (first.X, first.Y).ShouldNotBe((time.X, time.Y));
    }

    [AvaloniaFact]
    public async Task Pasting_is_one_undo()
    {
        var patch = Chain(out var time, out var osc, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, time);
        Click(editor, window, osc, RawInputModifiers.Control);

        await editor.CopySelectionAsync();
        await editor.PasteAsync();

        editor.Undo().ShouldBeTrue();

        Count(editor.Patch, "time").ShouldBe(1);
        Count(editor.Patch, "osc.sine").ShouldBe(1);
    }

    /// <summary>
    /// The ordinary way to reach this is having copied something else entirely,
    /// so it is answered with a sentence rather than with the parser's wording
    /// or with an exception.
    /// </summary>
    [AvaloniaFact]
    public async Task Pasting_something_that_is_not_a_patch_says_so_and_changes_nothing()
    {
        var patch = Chain(out _, out _, out _);
        var (editor, window) = Editing(patch);

        var before = patch.Nodes.Count;
        await Clipboard(window).SetTextAsync("this is not a patch");

        (await editor.PasteAsync()).ShouldNotBeNullOrWhiteSpace();
        patch.Nodes.Count.ShouldBe(before);
    }

    [AvaloniaFact]
    public async Task Pasting_an_empty_clipboard_does_nothing_and_says_nothing()
    {
        var patch = Chain(out _, out _, out _);
        var (editor, window) = Editing(patch);

        await Clipboard(window).ClearAsync();

        (await editor.PasteAsync()).ShouldBeNull();
        patch.Nodes.Count.ShouldBe(3);
    }

    /// <summary>
    /// A whole saved patch on the clipboard pastes as everything in it but the
    /// sink — which is a thing worth being able to do, and the reason the format
    /// is the file's rather than one of this feature's own.
    /// </summary>
    [AvaloniaFact]
    public async Task A_whole_saved_patch_pastes_as_everything_but_its_output()
    {
        var patch = Chain(out _, out _, out _);
        var (editor, window) = Editing(patch);

        var drone = Presets.Drone(NodeCatalog.BuiltIn);
        await Clipboard(window).SetTextAsync(PatchIo.ToJson(drone, NodeCatalog.BuiltIn));

        (await editor.PasteAsync()).ShouldBeNull();

        patch.Nodes.Count.ShouldBe(3 + drone.Nodes.Count - 1);
        patch.Nodes.Count(n => NodeCatalog.IsSink(n.TypeId)).ShouldBe(1);
    }

    // --- cutting ------------------------------------------------------------

    [AvaloniaFact]
    public async Task Cutting_copies_and_then_removes()
    {
        var patch = Chain(out var time, out var osc, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, time);
        Click(editor, window, osc, RawInputModifiers.Control);

        (await editor.CutSelectionAsync()).ShouldBeNull();

        Count(patch, "time").ShouldBe(0);
        Count(patch, "osc.sine").ShouldBe(0);

        await editor.PasteAsync();

        Count(patch, "time").ShouldBe(1);
        Count(patch, "osc.sine").ShouldBe(1);
    }

    /// <summary>A cut that could not copy must not delete: it would be a delete wearing a cut's name.</summary>
    [AvaloniaFact]
    public async Task Cutting_the_output_removes_nothing()
    {
        var patch = Chain(out _, out _, out var sink);
        var (editor, window) = Editing(patch);

        Click(editor, window, sink);

        (await editor.CutSelectionAsync()).ShouldNotBeNullOrWhiteSpace();
        patch.FirstOf(NodeCatalog.OutputTypeId).ShouldNotBeNull();
    }

    // --- the keys -----------------------------------------------------------

    /// <summary>
    /// The gestures are on the canvas rather than on the window, so that Ctrl+C
    /// in a text box still means the text in it.
    /// </summary>
    [AvaloniaFact]
    public void The_canvas_takes_the_clipboard_keys_and_leaves_undo_alone()
    {
        var patch = Chain(out var time, out _, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, time);

        foreach (var key in new[] { Key.C, Key.X, Key.V })
        {
            var taken = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = key,
                KeyModifiers = KeyModifiers.Control,
                Source = editor,
            };

            editor.RaiseEvent(taken);
            taken.Handled.ShouldBeTrue($"Ctrl+{key} belongs to the canvas");
        }

        // Undo is the window's, and the canvas marking it handled would take it
        // off the window entirely.
        var undo = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Z,
            KeyModifiers = KeyModifiers.Control,
            Source = editor,
        };

        editor.RaiseEvent(undo);
        undo.Handled.ShouldBeFalse("Ctrl+Z is handled on the window");
    }
}
