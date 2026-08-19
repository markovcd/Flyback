using Avalonia;
using Avalonia.Controls;
using Flyback.App.Controls;

namespace Flyback.App;

/// <summary>
/// The module list, and the one gesture that opens it: a right-click on empty
/// canvas.
/// </summary>
/// <remarks>
/// <para>
/// The list itself is <see cref="ModulePalette"/> and knows nothing about how it
/// is shown. All that is here is where it appears and what happens to what is
/// picked from it — which is the whole of what changed when it stopped being a
/// column down the left of the window (ADR-0046).
/// </para>
/// <para>
/// One palette, built once and kept. It holds which plugins are ticked, and that
/// is a setting rather than something to be re-answered every time the list is
/// opened.
/// </para>
/// </remarks>
public sealed partial class MainWindow
{
    private void BuildPalette()
    {
        palette = new ModulePalette(plugins.Modules, Add);
        paletteFlyout.Content = palette;

        editor.MenuRequested += (_, at) =>
        {
            wiring = null;
            ShowPalette(at);
        };

        // A wire let go over bare canvas asks the same question with one more
        // thing known: what it is going to be plugged into.
        editor.WireDropped += (_, drop) =>
        {
            wiring = drop;
            ShowPalette(drop.At);
        };

        // Where the last one was asked for, so that what is picked lands where
        // the canvas was clicked rather than wherever the view is centred. Held
        // here rather than passed through the flyout, which has no room for it.
        void Add(string typeId)
        {
            paletteFlyout.Hide();

            if (wiring is { } drop) editor.AddNodeWired(typeId, drop);
            else editor.AddNode(typeId, addingAt);

            // Back to the canvas, or the next keypress would go to a filter box
            // that is no longer on screen.
            editor.Focus();
        }
    }

    /// <summary>Where the module about to be picked belongs, in graph space.</summary>
    private Point? addingAt;

    /// <summary>
    /// The wire the module about to be picked should arrive plugged into, or
    /// null where the list was opened without one — a right-click or the space
    /// bar. Cleared by those, so a module added afterwards is not wired to
    /// whatever the last dropped wire happened to be.
    /// </summary>
    private WireDrop? wiring;

    private void ShowPalette(Point at)
    {
        if (palette is null) return;

        addingAt = at;

        // Opened at the pointer, which is the point that was clicked — so the
        // list appears under the hand and what comes out of it lands where the
        // hand was.
        paletteFlyout.ShowAt(editor, showAtPointer: true);

        // After showing, because a control that is not yet in a visual tree
        // cannot take the keyboard.
        palette.Reset();
    }
}
