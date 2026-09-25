using Avalonia;
using Avalonia.Controls;
using Flyback.App.Inspect;
using Flyback.App.Knobs;
using Flyback.App.Midi;
using Flyback.App.Settings;
using Flyback.App.Statistics;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;

namespace Flyback.App.Canvas;

/// <summary>
/// The palette, and the one gesture that opens it: a right-click on empty canvas
/// (ADR-0046, ADR-0148).
/// </summary>
/// <remarks>
/// The list itself is <see cref="ModulePalette"/> and knows nothing about how it is
/// shown; all that is here is where it appears and what happens to what is picked.
/// One palette, built once and kept, because which plugins are ticked is a setting
/// rather than something to re-answer every time.
/// </remarks>
internal sealed class Palette
{
    private readonly NodeEditor editor;
    private readonly Document document;
    private readonly Func<KeyboardLayout> keyboard;
    private readonly ModulePalette list;
    private readonly PluginCatalog plugins;
    private readonly Usage usage;
    private readonly ReportLine report;

    /// <summary>Where the palette is shown, at the pointer.</summary>
    public Flyout Flyout { get; } = new()
    {
        Placement = PlacementMode.Pointer,
        ShowMode = FlyoutShowMode.Standard,
    };

    /// <summary>The groups somebody kept, listed above the catalog.</summary>
    public GroupLibrary Groups { get; }

    /// <summary>
    /// Lays the computer keyboard out by the MIDI section's default when the first
    /// MIDI In is about to join a patch, so the layout lands in the same edit as the module.
    /// </summary>
    private void LayFirstKeyboard(string typeId)
    {
        if (typeId != NodeCatalog.MidiTypeId
            || keyboard() != KeyboardLayout.Scale
            || editor.History.Patch.KeyboardScale is not null
            || editor.History.Patch.FirstOf(NodeCatalog.MidiTypeId) is not null)
            return;

        editor.History.Patch.KeyboardScale = [.. Inspector.Major];
        document.Relaid();
    }

    /// <param name="knobs">The instruments the list offers.</param>
    /// <param name="sections">How the MIDI section lays out a first keyboard.</param>
    /// <param name="setup">Where kept groups are read from and written to, or the usual place.</param>
    public Palette(NodeEditor editor, Document document, PluginCatalog plugins, ReportLine report, Usage usage, PanelKnobs knobs, OutputSections sections, EditorSetup setup)
    {
        var instruments = knobs.View.Instruments;
        var groupFolder = setup.GroupFolder;

        this.editor = editor;
        this.document = document;
        keyboard = () => sections.Saved.Keyboard;
        this.plugins = plugins;
        this.usage = usage;
        this.report = report;

        Groups = new GroupLibrary(plugins.Modules, groupFolder);
        list = new ModulePalette(plugins.Modules, Add, Groups, AddGroup, instruments, AddInstrument);

        Flyout.Content = list;
        Flyout.FlyoutPresenterClasses.Add(ModulePalette.PresenterClass);

        editor.Gestures.MenuRequested += (_, at) =>
        {
            wiring = null;
            Show(at);
        };

        // A wire let go over bare canvas asks the same question with one more
        // thing known: what it is going to be plugged into.
        editor.Gestures.WireDropped += (_, drop) =>
        {
            wiring = drop;
            Show(drop.At);
        };

        // Where the last one was asked for, so that what is picked lands where
        // the canvas was clicked rather than wherever the view is centered. Held
        // here rather than passed through the flyout, which has no room for it.
        void Add(string typeId)
        {
            Flyout.Hide();

            LayFirstKeyboard(typeId);

            if (wiring is { } drop) editor.Edits.AddNodeWired(typeId, drop);
            else editor.Edits.AddNode(typeId, addingAt);

            usage.Count(Used.Added);

            // Back to the canvas, or the next keypress would go to a filter box
            // that is no longer on screen.
            editor.Focus();
        }

        // An instrument arrives whole: its clock and a module per track, built
        // for the port it is on right now. See InstrumentScaffold.
        void AddInstrument(PanelInstrument instrument)
        {
            Flyout.Hide();

            var added = editor.Edits.AddFragment(InstrumentScaffold.Build(instrument.Id, instrument.Profile, plugins.Modules, editor.Geometry), addingAt);

            usage.Count(Used.Added);
            report.Say(wiring is null
                ? $"Added {instrument.Profile.Name} — {added.Count} modules, one per track. Delete the tracks you will not use."
                : $"Added {instrument.Profile.Name}. The wire was left loose: there is more than one module to choose from.");

            editor.Focus();
        }

        // A kept group arrives as what it is — a fragment, box and all — rather
        // than as a module, because that is what one is. See GroupLibrary.
        void AddGroup(SavedGroup entry)
        {
            Flyout.Hide();

            // Refused by name rather than added with holes in it, which is the
            // same answer pasting such a fragment gives and the same sentence.
            if (!entry.IsComplete)
            {
                report.Say($"“{entry.Name}” was not added. {entry.Load.Summary}", entry.Load.Detail);
                editor.Focus();
                return;
            }

            var added = editor.Edits.AddFragment(entry.Fragment, addingAt);

            // A wire dropped on bare canvas asked what to plug into, and a box
            // has more than one answer to that — so it is left where it was and
            // said so, rather than guessed at. Which socket a module gets is
            // Fitting's decision; a group has no such thing to consult.
            report.Say(wiring is null
                ? $"Added “{entry.Name}” — {added.Count} modules."
                : $"Added “{entry.Name}”. The wire was left loose: a box has more than one socket to choose from.");

            editor.Focus();
        }
    }



    /// <summary>
    /// Keeps a group, so it can be added again from the module list.
    /// </summary>
    /// <remarks>
    /// The name is what the list calls it, which is why the button offering this
    /// is refused to a group that has none — see the group inspector. Saving one
    /// under a name already kept replaces it, the way saving anything under a
    /// name it already has does, and says which of the two happened.
    /// </remarks>
    public void SaveGroup(NodeGroup group)
    {
        if (string.IsNullOrWhiteSpace(group.Name)) return;

        var replacing = Groups.All.Any(entry =>
            string.Equals(entry.Name, group.Name, StringComparison.CurrentCultureIgnoreCase));

        try
        {
            var kept = Groups.Save(group, editor.History.Patch);

            report.Say(
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
            report.Say($"Could not keep “{group.Name}”: {ex.Message}", GroupLibrary.DefaultFolder);
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

    public void Show(Point at)
    {
        addingAt = at;

        // Opened at the pointer, which is the point that was clicked — so the
        // list appears under the hand and what comes out of it lands where the
        // hand was.
        Flyout.ShowAt(editor, showAtPointer: true);

        // After showing, because a control that is not yet in a visual tree
        // cannot take the keyboard.
        list.Reset();
    }
}
