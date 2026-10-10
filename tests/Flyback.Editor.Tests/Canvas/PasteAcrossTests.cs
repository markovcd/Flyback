using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using AvaloniaEdit;
using Flyback.Editor.Windows;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Editor.Tests.Canvas;

/// <summary>
/// Pasting from one kind of document into the other: patch text onto the canvas,
/// and a canvas copy into the text.
/// </summary>
/// <remarks>
/// Either way only the modules, their wires and their groups cross. What a patch
/// says about itself — its panel, its credits, its length — stays behind.
/// </remarks>
public class PasteAcrossTests : EditorTest
{
    /// <summary>Two modules in a group, and everything a patch says about itself.</summary>
    private const string Grouped = """
        description "somebody else's patch"
        author "somebody"
        length 0:30
        panel level = 0.5

        group "Bass" {
          let tone = sine(freq: 110)
          let quiet = tone * 0.5
        }

        quiet |> out.left
        out.volume = level
        """;

    private static IClipboard Clipboard(Window window) =>
        TopLevel.GetTopLevel(window)?.Clipboard
        ?? throw new InvalidOperationException("no clipboard under this window");

    private static TextEditor ShowCode(MainWindow window)
    {
        All<ToggleButton>(window).Single(b => b.Name == "code").IsChecked = true;
        Settle(window);

        return All<TextEditor>(window).Single(b => b.Name == "source");
    }

    private static void Apply(MainWindow window, string source)
    {
        ShowCode(window).Text = source;

        Press(All<Button>(window).Single(b => b.Name == "apply"));
        Settle(window);
    }

    /// <summary>Ctrl+V at the text, from the keyboard.</summary>
    private static void PasteInto(MainWindow window, TextEditor text)
    {
        text.TextArea.Focus();
        window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);

        Settle(window);
    }

    // --- text onto the canvas -----------------------------------------------

    [AvaloniaFact]
    public async Task Patch_text_pastes_onto_the_canvas_as_its_modules_and_groups()
    {
        var window = Open();
        var editor = Editor(window);
        var patch = editor.History.Patch;

        var modules = patch.Nodes.Count;
        var groups = patch.Groups?.Count ?? 0;
        var controls = patch.Controls?.Count ?? 0;
        var description = patch.Description;

        await Clipboard(window).SetTextAsync(Grouped);
        (await editor.Clipboard.PasteAsync(Clipboard(window))).ShouldBeNull();

        patch = editor.History.Patch;

        patch.Nodes.Count.ShouldBe(modules + 2);
        patch.Nodes.Count(n => NodeCatalog.IsSink(n.TypeId)).ShouldBe(1);
        (patch.Groups?.Count ?? 0).ShouldBe(groups + 1);
        patch.Groups!.ShouldContain(g => g.Name == "Bass");

        (patch.Controls?.Count ?? 0).ShouldBe(controls, "the text's panel stays behind");
        patch.Description.ShouldBe(description, "and so does what it says about itself");

        var pasted = editor.Selection.Nodes;
        pasted.Count.ShouldBe(2);
        pasted.ShouldAllBe(n => ControlMap.All(n).Count() == 0);
    }

    [AvaloniaFact]
    public async Task Patch_text_that_does_not_build_pastes_nothing_and_says_so()
    {
        var window = Open();
        var editor = Editor(window);
        var before = editor.History.Patch.Nodes.Count;

        await Clipboard(window).SetTextAsync("let tone = sine(freq: 110");

        (await editor.Clipboard.PasteAsync(Clipboard(window))).ShouldNotBeNullOrWhiteSpace();
        editor.History.Patch.Nodes.Count.ShouldBe(before);
    }

    // --- a canvas copy into the text ----------------------------------------

    /// <summary>
    /// A module copied off a canvas arrives in the text as the statements that
    /// build it, under names the text has not used, so applying builds both.
    /// </summary>
    [AvaloniaFact]
    public async Task A_canvas_copy_pastes_into_the_text_as_text()
    {
        var window = Open();

        Apply(window, """
            let hum = t |> sine(freq: 220)
            hum |> out.left
            """);

        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var copied = b.Add("osc.sine", 0, 0, (1, 330f));
        copied.Name = "hum";

        await Clipboard(window).SetTextAsync(PatchIO.ToJson(b.Patch, NodeCatalog.BuiltIn));

        var text = ShowCode(window);
        text.CaretOffset = text.Document.TextLength;

        PasteInto(window, text);

        text.Text.ShouldNotContain("osc.sine", Case.Sensitive, "a type id is how the file says it, not the text");

        var built = PatchLanguage.Build(text.Text, NodeCatalog.BuiltIn);

        built.Ok.ShouldBeTrue(built.Report);
        built.Patch.Nodes.Count(n => n.TypeId == "osc.sine").ShouldBe(2);
        built.Patch.Nodes.ShouldContain(n => n.TypeId == "osc.sine" && n.InputValues[1] == 330f);
    }

    [AvaloniaFact]
    public async Task A_canvas_copy_pasted_into_the_text_is_one_thing_to_take_back()
    {
        var window = Open();

        Apply(window, "let hum = t |> sine(freq: 220)\nhum |> out.left\n");

        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        b.Add("osc.sine", 0, 0);

        await Clipboard(window).SetTextAsync(PatchIO.ToJson(b.Patch, NodeCatalog.BuiltIn));

        var text = ShowCode(window);
        var was = text.Text;

        text.CaretOffset = 0;
        PasteInto(window, text);

        text.Text.ShouldNotBe(was);

        text.Undo();

        text.Text.ShouldBe(was);
    }

    [AvaloniaFact]
    public async Task Ordinary_text_pastes_into_the_text_as_it_is()
    {
        var window = Open();

        Apply(window, "let hum = t |> sine(freq: 220)\nhum |> out.left\n");

        await Clipboard(window).SetTextAsync("# pasted as it is");

        var text = ShowCode(window);
        text.CaretOffset = 0;

        PasteInto(window, text);

        text.Text.ShouldStartWith("# pasted as it is");
    }
}
