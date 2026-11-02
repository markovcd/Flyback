using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaEdit;
using Flyback.Editor.Composition;
using Shouldly;

namespace Flyback.Editor.Tests.Controls;

/// <summary>Ctrl+D duplicates in the text as on the canvas, rather than deleting the line as the editor would.</summary>
public class SourceViewDuplicateTests : SourceViewTest
{
    private static TextEditor Owned(MainWindow window)
    {
        Evaluate(window, Hum);

        var text = Text(window);
        text.TextArea.Focus();
        Settle(window);

        return text;
    }

    private static void ControlD(MainWindow window)
    {
        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Control);
        Settle(window);
    }

    [AvaloniaFact]
    public void Ctrl_D_with_nothing_selected_copies_the_caret_s_line_below_it()
    {
        var window = Open();
        var text = Owned(window);
        var line = "let hum = t |> sine(freq: 220)";
        text.CaretOffset = text.Text.IndexOf("sine", StringComparison.Ordinal);

        ControlD(window);

        text.Text.Split('\n').Count(l => l.TrimEnd('\r') == line).ShouldBe(2);
        text.Text.ShouldContain("hum |> out.left", Case.Sensitive, "no line is deleted");
        text.TextArea.Caret.Line.ShouldBe(3, "the caret goes down with the copy");
    }

    [AvaloniaFact]
    public void Ctrl_D_copies_the_selection_after_itself()
    {
        var window = Open();
        var text = Owned(window);
        var at = text.Text.IndexOf("220", StringComparison.Ordinal);
        text.Select(at, 3);

        ControlD(window);

        text.Text.ShouldContain("freq: 220220)");
        text.SelectedText.ShouldBe("220");
        text.SelectionStart.ShouldBe(at + 3);
    }
}
