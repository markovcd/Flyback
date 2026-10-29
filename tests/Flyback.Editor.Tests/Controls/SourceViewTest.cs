using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using Flyback.Core.Graph;
using Flyback.Editor.Composition;
using Flyback.Editor.Controls;
using Shouldly;

namespace Flyback.Editor.Tests.Controls;

/// <summary>
/// The patch as text beside the patch as a graph, and which of the two is the
/// document.
/// </summary>
/// <remarks>
/// ADR-0068 settles it on the file rather than the view: a patch opened as a graph
/// is a graph, and the text view of it is a printing. Applying one is how somebody
/// deliberately takes a patch into text, and from then on the canvas is a view of
/// it. The window opens on a preset, so everything here starts from the canvas
/// owning the patch.
/// </remarks>
public abstract class SourceViewTest : EditorTest
{

    internal static ToggleButton CodeButton(MainWindow window) =>
        All<ToggleButton>(window).Single(b => b.Name == "code");

    internal static TextEditor Text(MainWindow window) =>
        All<TextEditor>(window).Single(b => b.Name == "source");

    internal static Button Apply(MainWindow window) =>
        All<Button>(window).Single(b => b.Name == "apply");

    internal static StackPanel Inspector(MainWindow window) =>
        All<StackPanel>(window).Single(p => p.Name == "inspector");

    /// <summary>Shows the text view and lets the layout catch up.</summary>
    internal static TextEditor ShowCode(MainWindow window)
    {
        CodeButton(window).IsChecked = true;
        Settle(window);

        return Text(window);
    }

    /// <summary>Puts text in and asks for it, as Ctrl+Enter and the button both do.</summary>
    internal static void Evaluate(MainWindow window, string source)
    {
        ShowCode(window).Text = source;

        Press(Apply(window));
        Settle(window);
    }

    /// <summary>Presses Enter at the text box, with whatever is being held.</summary>
    internal static void Press(TextEditor text, KeyModifiers held) =>
        text.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
            KeyModifiers = held,
        });

    /// <summary>A tone: the clock, an oscillator and the Output it reaches.</summary>
    internal const string Hum = """
        # one steady tone
        let hum = t |> sine(freq: 220)
        hum |> out.left
        """;

    internal static Button Hand(MainWindow window) =>
        All<Button>(window).Single(b => b.Name == "hand");

    /// <summary>Pumps until the question about the text has arrived.</summary>
    internal static ModalOverlay Asking(MainWindow window)
    {
        for (var attempt = 0; attempt < 20 && !All<ModalOverlay>(window).Any(); attempt++)
            Dispatcher.UIThread.RunJobs();

        Settle(window);

        return All<ModalOverlay>(window).SingleOrDefault()
            ?? throw new InvalidOperationException("emptying unsaved text should have asked first");
    }

    /// <summary>Hands the patch back, answering the question that fronts it.</summary>
    internal static void HandBack(MainWindow window, string answer = "Discard changes")
    {
        Press(Hand(window));
        Press(All<Button>(Asking(window)).Single(b => b.Content as string == answer));

        Settle(window);
        Dispatcher.UIThread.RunJobs();
        Settle(window);
    }

    /// <summary>What the printing says for itself, or null when there is nothing shown.</summary>
    internal static string? Notice(MainWindow window) => All<TextBlock>(window)
        .Where(block => block.IsVisible && block.Text is { } said && said.Contains("Printed from"))
        .Select(block => block.Text)
        .FirstOrDefault();

    internal static Button Undo(MainWindow window) =>
        All<Button>(window).Single(b => b.Name == "undo");

    /// <summary>Everything the panel is saying, for the states where it says something.</summary>
    internal static string Panel(MainWindow window) =>
        string.Join(" ", All<TextBlock>(Inspector(window)).Select(block => block.Text));

    /// <summary>Puts the caret where a word is, as a click in the text would.</summary>
    internal static void Click(MainWindow window, string word)
    {
        var text = Text(window);

        text.CaretOffset = text.Text.IndexOf(word, StringComparison.Ordinal) + 1;
        Settle(window);
    }

    /// <summary>A patch whose first module has knobs to turn.</summary>
    internal static Patch Plasma() => Engine.Graph.Presets.All.Single(p => p.Name == "Plasma").Build(NodeCatalog.BuiltIn);

    /// <summary>Turns the first knob the panel is showing, and lets go of it.</summary>
    internal static void Turn(MainWindow window, double to)
    {
        var slider = Knobs(window).First();

        // The first knob's slider, which on a socket with a knee is travel rather than value.
        var node = Editor(window).Selection.Focused.ShouldNotBeNull();
        var spec = NodeCatalog.Require(node.TypeId).Inputs.First(p => NodeCatalog.Normalled(p) is null && !p.NeedsAWire);

        slider.Value = spec.Knee > 0f ? spec.Travel((float)to, spec.Min, spec.Max) : to;
        Settle(window);

        Release(slider);
        Settle(window);
    }

    /// <summary>Lets go of the pointer over a control, which is what ends a gesture.</summary>
    internal static void Release(Control over) =>
        over.RaiseEvent(new PointerReleasedEventArgs(
            over,
            new Pointer(0, PointerType.Mouse, true),
            over,
            default,
            0,
            default,
            KeyModifiers.None,
            MouseButton.Left)
        {
            RoutedEvent = InputElement.PointerReleasedEvent,
        });
}
