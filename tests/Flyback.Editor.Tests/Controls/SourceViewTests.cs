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
public partial class SourceViewTests : EditorTest
{

    private static ToggleButton CodeButton(MainWindow window) =>
        All<ToggleButton>(window).Single(b => b.Name == "code");

    private static TextEditor Text(MainWindow window) =>
        All<TextEditor>(window).Single(b => b.Name == "source");

    private static Button Apply(MainWindow window) =>
        All<Button>(window).Single(b => b.Name == "apply");

    private static StackPanel Inspector(MainWindow window) =>
        All<StackPanel>(window).Single(p => p.Name == "inspector");

    /// <summary>Shows the text view and lets the layout catch up.</summary>
    private static TextEditor ShowCode(MainWindow window)
    {
        CodeButton(window).IsChecked = true;
        Settle(window);

        return Text(window);
    }

    /// <summary>Puts text in and asks for it, as Ctrl+Enter and the button both do.</summary>
    private static void Evaluate(MainWindow window, string source)
    {
        ShowCode(window).Text = source;

        Press(Apply(window));
        Settle(window);
    }

    /// <summary>Presses Enter at the text box, with whatever is being held.</summary>
    private static void Press(TextEditor text, KeyModifiers held) =>
        text.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
            KeyModifiers = held,
        });

    /// <summary>A tone: the clock, an oscillator and the Output it reaches.</summary>
    private const string Hum = """
        # one steady tone
        let hum = t |> sine(freq: 220)
        hum |> out.left
        """;
}
