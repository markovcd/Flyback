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
    // --- the caret points the panel -----------------------------------------

    /// <summary>Puts the caret where a word is, as a click in the text would.</summary>
    private static void Click(MainWindow window, string word)
    {
        var text = Text(window);

        text.CaretOffset = text.Text.IndexOf(word, StringComparison.Ordinal) + 1;
        Settle(window);
    }

    /// <summary>
    /// On a group's header, or anywhere in its block that is about no module, the
    /// caret points the panel at the group, as a click on the box does; on a
    /// module inside it, at the module.
    /// </summary>
    [AvaloniaFact]
    public void The_caret_on_a_group_points_the_panel_at_the_group()
    {
        var window = Open();

        Evaluate(window,
            """
            group "Voice" {
              let a = sine(freq: 2)

              let b = a |> drive()
            }
            b |> out.left
            """);

        Click(window, "group \"Voice\"");

        Editor(window).Selection.Group.ShouldNotBeNull().Name.ShouldBe("Voice");
        Panel(window).ShouldContain("drawn as one");

        Click(window, "sine");
        Editor(window).Selection.Focused.ShouldNotBeNull().TypeId.ShouldBe("osc.sine");

        // The blank line between the two bindings, inside the block.
        var text = Text(window);
        text.CaretOffset = text.Text.IndexOf("let b", StringComparison.Ordinal) - 3;
        Settle(window);

        Editor(window).Selection.Group.ShouldNotBeNull().Name.ShouldBe("Voice");
    }

    /// <summary>
    /// A group named in the text since it was applied is not on the canvas yet,
    /// and the panel says so in a group's words rather than falling silent.
    /// </summary>
    [AvaloniaFact]
    public void A_group_the_canvas_has_not_got_yet_says_so()
    {
        var window = Open();

        Evaluate(window,
            """
            group "Voice" {
              let a = sine(freq: 2)
              let b = a |> drive()
            }
            b |> filter(cutoff: 400) |> out.left
            """);

        var text = Text(window);
        text.Document.Replace(text.Text.IndexOf("Voice", StringComparison.Ordinal), 5, "Chorus");
        Settle(window);

        Click(window, "group \"Chorus\"");

        Editor(window).Selection.Focused.ShouldBeNull();
        Panel(window).ShouldContain("this group is not there to show yet");

        // Renaming a group moves the modules in it too, but not one outside it.
        Click(window, "filter");
        Editor(window).Selection.Focused.ShouldNotBeNull().TypeId.ShouldBe("audio.filter");
    }

    /// <summary>
    /// The caret is the text view's pointer, and the panel follows it exactly as
    /// it follows a click on the canvas.
    /// </summary>
    [AvaloniaFact]
    public void The_caret_points_the_panel()
    {
        var window = Open();

        Evaluate(window, Hum);
        Click(window, "sine");

        Editor(window).Selection.Focused.ShouldNotBeNull().TypeId.ShouldBe("osc.sine");
    }

    /// <summary>
    /// A module written with no name of its own is still a module, and the whole
    /// point of pointing by position rather than by name is that it can be
    /// clicked without the text being rewritten to name it.
    /// </summary>
    [AvaloniaFact]
    public void A_module_with_no_name_is_pointed_at_too()
    {
        var window = Open();

        Evaluate(window, "math.mix(a: 1.5524476) |> out.left");
        Click(window, "math.mix");

        Editor(window).Selection.Focused.ShouldNotBeNull().TypeId.ShouldBe("math.mix");
    }

    /// <summary>
    /// A printing is what the text view shows for a patch built on the canvas,
    /// and it has to be as clickable as text somebody wrote.
    /// </summary>
    [AvaloniaFact]
    public void The_caret_points_the_panel_in_a_printing_too()
    {
        var window = Open();

        ShowCode(window);

        var text = Text(window);
        var call = text.Text.IndexOf('(');

        call.ShouldBeGreaterThan(0, "the preset prints as calls");

        text.CaretOffset = call;
        Settle(window);

        Editor(window).Selection.Focused.ShouldNotBeNull();
    }
}
