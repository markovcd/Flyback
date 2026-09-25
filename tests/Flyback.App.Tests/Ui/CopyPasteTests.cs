using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
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
    private static Patch Chain(out NodeInstance time, out NodeInstance osc, out NodeInstance sink)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        time = b.Add("time", 0, 0);
        osc = b.Add("osc.sine", 0, 260, (1, 220f));
        sink = b.Add(NodeCatalog.OutputTypeId, 520, 120);

        b.Wire(time, 0, osc, 0).Wire(osc, 0, sink, NodeCatalog.OutputLeftPort);

        return b.Patch;
    }

    private static IClipboard Clipboard(Window window) =>
        TopLevel.GetTopLevel(window)?.Clipboard
        ?? throw new InvalidOperationException("no clipboard under this window");

    private static int Count(Patch patch, string typeId) => patch.Nodes.Count(n => n.TypeId == typeId);

    // --- duplicating --------------------------------------------------------

    /// <summary>
    /// Ctrl+D leaves a second copy beside the original, selected, and does not go
    /// near the clipboard — so something copied earlier is still there to paste.
    /// </summary>
    [AvaloniaFact]
    public async Task Duplicating_copies_beside_the_original_and_spares_the_clipboard()
    {
        var patch = Chain(out var time, out var osc, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, time);
        (await editor.Clipboard.CopyAsync(Clipboard(window))).ShouldBeNull();

        var copied = (await Clipboard(window).TryGetTextAsync()).ShouldNotBeNull();

        Click(editor, window, osc);
        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Control);
        Settle(window);

        Count(editor.History.Patch, "osc.sine").ShouldBe(2);

        var made = editor.Selection.Nodes.ShouldHaveSingleItem();
        made.Id.ShouldNotBe(osc.Id, "what is selected is the copy, ready to be dragged off");
        (made.X, made.Y).ShouldBe((osc.X + 28, osc.Y + 28));

        (await Clipboard(window).TryGetTextAsync()).ShouldBe(copied);
    }

    /// <summary>
    /// The Output cannot be duplicated any more than it can be copied, and a
    /// gesture that silently did nothing would read as a broken one.
    /// </summary>
    [AvaloniaFact]
    public void Duplicating_only_the_Output_says_so()
    {
        var patch = Chain(out _, out _, out var sink);
        var (editor, window) = Editing(patch);

        var said = new List<string>();
        editor.Report.Said += (_, line) => said.Add(line);

        Click(editor, window, sink);
        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Control);
        Settle(window);

        Count(editor.History.Patch, NodeCatalog.OutputTypeId).ShouldBe(1);
        said.ShouldHaveSingleItem().ShouldContain("cannot be duplicated");
    }

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

        (await editor.Clipboard.CopyAsync(Clipboard(window))).ShouldBeNull();

        var text = await Clipboard(window).TryGetTextAsync();
        var loaded = PatchIO.Read(text.ShouldNotBeNull(), NodeCatalog.BuiltIn);

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

        (await editor.Clipboard.CopyAsync(Clipboard(window))).ShouldBeNull("an empty selection has nothing to say");
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

        (await editor.Clipboard.CopyAsync(Clipboard(window))).ShouldNotBeNullOrWhiteSpace();
    }

    // --- pasting ------------------------------------------------------------

    [AvaloniaFact]
    public async Task Pasting_adds_the_modules_and_the_wire_between_them()
    {
        var patch = Chain(out var time, out var osc, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, time);
        Click(editor, window, osc, RawInputModifiers.Control);

        await editor.Clipboard.CopyAsync(Clipboard(window));
        (await editor.Clipboard.PasteAsync(Clipboard(window))).ShouldBeNull();

        Count(patch, "time").ShouldBe(2);
        Count(patch, "osc.sine").ShouldBe(2);

        var pasted = editor.Selection.Nodes.Single(n => n.TypeId == "osc.sine");
        patch.IncomingTo(pasted.Id, 0).ShouldNotBeNull("the wire came with them");
    }

    [AvaloniaFact]
    public async Task What_was_pasted_is_what_is_selected_afterwards()
    {
        var patch = Chain(out var time, out var osc, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, time);
        Click(editor, window, osc, RawInputModifiers.Control);

        await editor.Clipboard.CopyAsync(Clipboard(window));
        await editor.Clipboard.PasteAsync(Clipboard(window));

        editor.Selection.Nodes.Count.ShouldBe(2);
        editor.Selection.Nodes.ShouldNotContain(n => n.Id == time.Id || n.Id == osc.Id);
        editor.Selection.Focused.ShouldNotBeNull();
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
        await editor.Clipboard.CopyAsync(Clipboard(window));

        await editor.Clipboard.PasteAsync(Clipboard(window));
        var first = editor.Selection.Nodes.Single();

        await editor.Clipboard.PasteAsync(Clipboard(window));
        var second = editor.Selection.Nodes.Single();

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

        await editor.Clipboard.CopyAsync(Clipboard(window));
        await editor.Clipboard.PasteAsync(Clipboard(window));

        editor.History.Undo().ShouldBeTrue();

        Count(editor.History.Patch, "time").ShouldBe(1);
        Count(editor.History.Patch, "osc.sine").ShouldBe(1);
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

        (await editor.Clipboard.PasteAsync(Clipboard(window))).ShouldNotBeNullOrWhiteSpace();
        patch.Nodes.Count.ShouldBe(before);
    }

    [AvaloniaFact]
    public async Task Pasting_an_empty_clipboard_does_nothing_and_says_nothing()
    {
        var patch = Chain(out _, out _, out _);
        var (editor, window) = Editing(patch);

        await Clipboard(window).ClearAsync();

        (await editor.Clipboard.PasteAsync(Clipboard(window))).ShouldBeNull();
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
        await Clipboard(window).SetTextAsync(PatchIO.ToJson(drone, NodeCatalog.BuiltIn));

        (await editor.Clipboard.PasteAsync(Clipboard(window))).ShouldBeNull();

        patch.Nodes.Count.ShouldBe(3 + drone.Nodes.Count - 1);
        patch.Nodes.Count(n => NodeCatalog.IsSink(n.TypeId)).ShouldBe(1);
    }

    // --- the boxes ----------------------------------------------------------

    /// <summary>
    /// A box copied is a box pasted. Pressing one selects the modules inside it,
    /// so without this the gesture reads as copying a group and getting a
    /// handful of loose modules back.
    /// </summary>
    [AvaloniaFact]
    public async Task A_box_is_pasted_as_a_box()
    {
        var patch = Chain(out var time, out var osc, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, time);
        Click(editor, window, osc, RawInputModifiers.Control);

        editor.Edits.GroupSelected();
        Settle(window);

        var box = editor.Selection.Group.ShouldNotBeNull();
        box.Rename("Voice");

        (await editor.Clipboard.CopyAsync(Clipboard(window))).ShouldBeNull();
        (await editor.Clipboard.PasteAsync(Clipboard(window))).ShouldBeNull();

        patch.Groups.ShouldNotBeNull().Count.ShouldBe(2);

        // What arrived is what is selected, and it is exactly a box — so the
        // very next Ctrl+Shift+G has something to put back.
        var pasted = editor.Selection.Group.ShouldNotBeNull();

        pasted.Id.ShouldNotBe(box.Id);
        pasted.Title().ShouldBe("Voice");
        pasted.Members.Count.ShouldBe(2);
        pasted.Members.ShouldNotContain(id => id == time.Id || id == osc.Id);
    }

    // --- cutting ------------------------------------------------------------

    [AvaloniaFact]
    public async Task Cutting_copies_and_then_removes()
    {
        var patch = Chain(out var time, out var osc, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, time);
        Click(editor, window, osc, RawInputModifiers.Control);

        (await editor.Clipboard.CutAsync(Clipboard(window))).ShouldBeNull();

        Count(patch, "time").ShouldBe(0);
        Count(patch, "osc.sine").ShouldBe(0);

        await editor.Clipboard.PasteAsync(Clipboard(window));

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

        (await editor.Clipboard.CutAsync(Clipboard(window))).ShouldNotBeNullOrWhiteSpace();
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
