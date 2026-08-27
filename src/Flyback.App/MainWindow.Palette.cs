using Avalonia;
using Flyback.App.Controls;
using Flyback.Core.Graph;

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
        groups = new GroupLibrary(plugins.Modules, groupFolder);
        palette = new ModulePalette(plugins.Modules, Add, groups, AddGroup);

        paletteFlyout.Content = palette;
        paletteFlyout.FlyoutPresenterClasses.Add(ModulePalette.PresenterClass);

        Styles.Add(ModulePalette.Trim());

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

        // A kept group arrives as what it is — a fragment, box and all — rather
        // than as a module, because that is what one is. See GroupLibrary.
        void AddGroup(SavedGroup entry)
        {
            paletteFlyout.Hide();

            // Refused by name rather than added with holes in it, which is the
            // same answer pasting such a fragment gives and the same sentence.
            if (!entry.IsComplete)
            {
                Report($"“{entry.Name}” was not added. {entry.Load.Summary}", entry.Load.Detail);
                editor.Focus();
                return;
            }

            var added = editor.AddFragment(entry.Fragment, addingAt);

            // A wire dropped on bare canvas asked what to plug into, and a box
            // has more than one answer to that — so it is left where it was and
            // said so, rather than guessed at. Which socket a module gets is
            // Fitting's decision; a group has no such thing to consult.
            Report(wiring is null
                ? $"Added “{entry.Name}” — {added.Count} modules."
                : $"Added “{entry.Name}”. The wire was left loose: a box has more than one socket to choose from.");

            editor.Focus();
        }
    }


    /// <summary>
    /// The groups somebody kept, listed above the catalogue in the module list.
    /// Built once beside the palette, because the palette is what shows it.
    /// </summary>
    private GroupLibrary? groups;

    /// <summary>
    /// Keeps a group, so it can be added again from the module list.
    /// </summary>
    /// <remarks>
    /// The name is what the list calls it, which is why the button offering this
    /// is refused to a group that has none — see the group inspector. Saving one
    /// under a name already kept replaces it, the way saving anything under a
    /// name it already has does, and says which of the two happened.
    /// </remarks>
    private void SaveGroup(NodeGroup group)
    {
        if (groups is null || string.IsNullOrWhiteSpace(group.Name)) return;

        var replacing = groups.All.Any(entry =>
            string.Equals(entry.Name, group.Name, StringComparison.CurrentCultureIgnoreCase));

        try
        {
            var kept = groups.Save(group, editor.Patch);

            Report(
                replacing
                    ? $"Replaced “{kept.Name}” in the module list."
                    : $"Kept “{kept.Name}”. It is under Groups in the module list.",
                $"Saved as {kept.Path}");
        }
        catch (Exception ex)
        {
            // Said rather than swallowed: silently failing to keep what somebody
            // just asked to keep is the one outcome they cannot see for
            // themselves until the day they go looking for it.
            Report($"Could not keep “{group.Name}”: {ex.Message}", GroupLibrary.DefaultFolder);
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
