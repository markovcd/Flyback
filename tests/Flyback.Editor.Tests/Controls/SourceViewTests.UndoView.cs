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
    // --- which view a press of undo belongs to ------------------------------

    /// <summary>
    /// Undo follows the view where the canvas owns the patch. Typing into a printing
    /// is on the text's stack, and with the canvas showing it is typing nobody can
    /// see — a press there takes back the last thing done to the canvas.
    /// </summary>
    [AvaloniaFact]
    public void Undo_on_the_canvas_leaves_typing_in_a_hidden_printing_alone()
    {
        var window = Open();
        var text = ShowCode(window);

        // Through the document, which is what typing is.
        text.Document.Insert(0, "# a note to myself\n");
        Settle(window);

        CodeButton(window).IsChecked = false;
        Settle(window);

        var before = Editor(window).History.Patch.Nodes.Count;

        Editor(window).Edits.AddNode("value").ShouldNotBeNull();
        Settle(window);

        Press(Undo(window));
        Settle(window);

        Editor(window).History.Patch.Nodes.Count.ShouldBe(before, "the module just added is what came back");
        text.Text.ShouldStartWith("# a note to myself", customMessage: "and the typing was left alone");

        // With nothing left on the canvas's stack the button goes gray, rather
        // than offering a press that would land where nobody is looking.
        Undo(window).IsEnabled.ShouldBeFalse();

        // The typing is still there to take back from the view it was done in.
        ShowCode(window);

        Press(Undo(window));
        Settle(window);

        text.Text.ShouldNotStartWith("# a note to myself");
    }

    /// <summary>
    /// A knob turned on the canvas leaves typing in the hidden printing its
    /// undo.
    /// </summary>
    /// <remarks>
    /// Writing a knob back into a printing forgets the text's steps, since the
    /// reading is made afresh and one of them would put the old number back. A
    /// printing that has been typed into is not written to, and the steps on
    /// its stack are the typing, so those are kept.
    /// </remarks>
    [AvaloniaFact]
    public void A_knob_turned_on_the_canvas_leaves_hidden_typing_its_undo()
    {
        var window = Open();
        var text = ShowCode(window);

        // Through the document, which is what typing is.
        text.Document.Insert(0, "# a note to myself\n");
        Settle(window);

        CodeButton(window).IsChecked = false;
        Settle(window);

        var editor = Editor(window);
        var turned = editor.History.Patch.Nodes.First(n =>
            n.TypeId != NodeCatalog.OutputTypeId
            && NodeCatalog.BuiltIn.Require(n.TypeId).Inputs.Count > 0);

        editor.Selection.Select(turned.Id);
        Settle(window);

        var slider = Knobs(window).First();

        Turn(window, slider.Minimum + ((slider.Maximum - slider.Minimum) * 0.37));

        ShowCode(window);

        text.Text.ShouldStartWith("# a note to myself");

        Press(Undo(window));
        Settle(window);

        text.Text.ShouldNotStartWith("# a note to myself", customMessage: "the typing is what Ctrl+Z takes back in the view it was done in");
    }
}
