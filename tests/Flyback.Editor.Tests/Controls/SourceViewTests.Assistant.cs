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
    // --- an assistant's patch, and the text ---------------------------------

    /// <summary>
    /// What the window handed the assistant panel to put a patch on the canvas with,
    /// which is the one thing the end of a turn does to the shell.
    /// </summary>
    /// <remarks>
    /// By reflection: the window takes its plugins from a static no test can put a
    /// provider into, so no turn can be run against a real window.
    /// </remarks>
    private static Action<Patch> AssistantApplies(MainWindow window)
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        return ((IAssistantEditor)typeof(AssistantPanel)
            .GetField("editor", flags)!
            .GetValue(All<AssistantPanel>(window).Single())!).Apply;
    }

    /// <summary>Two oscillators mixed into the left speaker: a patch no text here describes.</summary>
    private static Patch Drone()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 900, 40);
        var one = b.Add("osc.sine", 40, 40);
        var two = b.Add("osc.sine", 40, 240);
        var mix = b.Add("math.add", 400, 40);

        b.Wire(one, 0, mix, 0)
            .Wire(two, 0, mix, 1)
            .Wire(mix, 0, output, NodeCatalog.OutputLeftPort);

        return b.Patch;
    }

    private static int Oscillators(Patch patch) =>
        patch.Nodes.Count(n => n.TypeId == "osc.sine");

    /// <summary>
    /// A printing keeps up with an assistant's patch as it keeps up with an undo.
    /// The assistant column is beside the text view, so a turn ending over a
    /// printing is ordinary — and applying a stale one would put the old patch back.
    /// </summary>
    [AvaloniaFact]
    public void A_printing_keeps_up_with_an_assistants_patch()
    {
        var window = Open();
        var text = ShowCode(window);

        AssistantApplies(window)(Drone());
        Settle(window);

        Editor(window).History.Locked.ShouldBeFalse("a printing is a reading; the canvas still owns the patch");
        Notice(window).ShouldNotBeNull();

        Oscillators(PatchLanguage.Build(text.Text).Patch).ShouldBe(2);
    }

    /// <summary>
    /// Where the text is the document it goes on saying what the canvas holds, since
    /// it is what a save writes and what the next apply builds: the assistant's
    /// patch is written into it and built from there.
    /// </summary>
    [AvaloniaFact]
    public void An_assistants_patch_over_a_text_document_is_written_into_the_text()
    {
        var window = Open();

        Evaluate(window, Hum);

        AssistantApplies(window)(Drone());
        Settle(window);

        var editor = Editor(window);

        editor.History.Locked.ShouldBeTrue("the text is still the document");
        Oscillators(editor.History.Patch).ShouldBe(2, "the assistant's patch is on the canvas");
        Oscillators(PatchLanguage.Build(Text(window).Text).Patch).ShouldBe(2);
    }

    /// <summary>
    /// And one press takes both back: the patch, and the text as it was written —
    /// the comment in it included, which no printing could have kept.
    /// </summary>
    [AvaloniaFact]
    public void Undoing_an_assistants_patch_puts_back_the_text_as_it_was_written()
    {
        var window = Open();

        Evaluate(window, Hum);

        var text = Text(window);

        // Typed rather than loaded, so there is a stack under the text to go back
        // down — and a second apply, so the patch is this text's.
        text.Document.Insert(text.Document.TextLength, "\n# and a second note");
        Press(Apply(window));
        Settle(window);

        var written = text.Text;

        AssistantApplies(window)(Drone());
        Settle(window);

        text.Text.ShouldNotBe(written);

        Press(Undo(window));
        Settle(window);

        text.Text.ShouldBe(written);
        Oscillators(Editor(window).History.Patch).ShouldBe(1, "the patch the text describes is back with it");
        Editor(window).History.Locked.ShouldBeTrue();
    }
}
