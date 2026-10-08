using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Editor.Bars;
using Flyback.Editor.Controls;
using Flyback.Editor.Inspect;
using Flyback.Editor.Knobs;
using Flyback.Plugins.Hosting;
using Flyback.Ui.Controls;
using Flyback.Ui.Midi;
using Flyback.Ui;

namespace Flyback.Editor.Settings;

/// <summary>
/// The MIDI section of the settings window: which plugin hears an instrument, what
/// a controller does to a knob that sits elsewhere, and how the panel knobs stand.
/// </summary>
internal sealed class MidiSection : ISettingsSection
{
    private readonly OutputSettingRepository settings;
    private readonly PanelKnobs knobs;
    private readonly ControlHub hub;
    private readonly InstrumentLibrary instruments;
    private readonly TransportControls transport;

    public string Name => "MIDI";

    public Control View => rows;

    private readonly StackPanel rows = new() { Spacing = 8, Width = SettingsSession.SectionWidth };

    /// <summary>How a knob meets a controller that disagrees with it.</summary>
    private readonly ComboBox takeover = new Picker
    {
        Name = "takeover",
        ItemsSource = new[] { "Jump to the controller", "Pick up the knob" },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>Whether an instrument's Start and Stop play and pause the patch.</summary>
    private readonly CheckBox followTransport = new()
    {
        Name = "followTransport",
        Content = "Play and pause with an instrument's Start and Stop",
    };

    /// <summary>How a new patch lays out the computer's keyboard.</summary>
    private readonly ComboBox keyboardLayout = new Picker
    {
        Name = "keyboardLayout",
        ItemsSource = new[] { "Piano", "Scale" },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>Whether the panel knobs stand in a fixed grid.</summary>
    private readonly CheckBox knobGridOn = new()
    {
        Name = "knobGrid",
        Content = "Keep the knobs in a fixed grid",
    };

    /// <summary>How many knobs across the grid is.</summary>
    private readonly NumericUpDown knobColumns = GridSide("knobColumns");

    /// <summary>How many rows the grid has before the next starts below it.</summary>
    private readonly NumericUpDown knobRows = GridSide("knobRows");

    /// <param name="instruments">The instruments the section lists.</param>
    public MidiSection(
        PluginCatalog plugins,
        PanelKnobs knobs,
        ControlHub hub,
        InstrumentLibrary instruments,
        OutputSettingRepository settings,
        TransportControls transport)
    {
        this.settings = settings;
        this.knobs = knobs;
        this.hub = hub;
        this.instruments = instruments;
        this.transport = transport;

        ToolTip.SetTip(takeover,
            "When a controller's knob is not where the knob on screen is: jump straight to the controller, "
            + "or leave the knob alone until the controller passes it. Flyback's own, whichever plugin "
            + "hears the controller.");

        ToolTip.SetTip(followTransport,
            "A drum machine or sequencer the patch listens to plays the patch from the top on Start, "
            + "pauses it on Stop and plays on from there on Continue.");

        ToolTip.SetTip(keyboardLayout,
            "How the computer keyboard is laid out on a patch when its first MIDI In is added: as a piano, "
            + "or as a scale, one note to a key. Patches that already have a MIDI In keep their own.");

        // Which backend hears a keyboard, and which plugin it came from.
        var midiNote = new TextBlock
        {
            Name = "midiNote",
            FontSize = Text.Small,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
            Text = plugins.PreferredMidiInput is { IsSupported: true } input
                ? SettingRows.Attributed($"Heard through {input.Name}", plugins.Provider(input))
                : "No MIDI plugin is installed, so the only instrument is the computer's own keyboard.",
        };

        rows.Children.Add(midiNote);
        rows.Children.Add(InspectorRows.Field("Knobs", takeover));
        rows.Children.Add(followTransport);
        rows.Children.Add(InspectorRows.Field("Computer keys", keyboardLayout));

        ToolTip.SetTip(knobGridOn,
            "Stand the panel knobs in fixed columns and rows, so each keeps the row and column of the knob "
            + "it follows on a controller however wide the window is. Past the last row the next grid starts "
            + "below. In the editor's panel, over the picture and in the desktop viewer.");

        void Follow() => knobColumns.IsEnabled = knobRows.IsEnabled = knobGridOn.IsChecked == true;

        knobGridOn.IsCheckedChanged += (_, _) => Follow();
        Follow();

        rows.Children.Add(knobGridOn);
        rows.Children.Add(InspectorRows.Field("Columns", knobColumns, indent: 20));
        rows.Children.Add(InspectorRows.Field("Rows", knobRows, indent: 20));

        var known = string.Join(", ", instruments.Profiles.Select(profile => profile.Name));
        var instrumentsNote = new TextBlock
        {
            Text = $"Known by name: {known}. A profile of your own, one .json per instrument, goes in {InstrumentLibrary.UserFolder}.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = Text.Small,
            Foreground = Text.Muted,
        };
        ToolTip.SetTip(instrumentsNote,
            "An instrument Flyback knows by name offers its tracks on a MIDI In's channel field, binds a knob "
            + "from the panel's menu without being touched, and names what a learned knob follows.");
        rows.Children.Add(instrumentsNote);
    }

    public void Start()
    {
        Show();
        Apply();
    }

    public void Show() => Show(settings.Current);

    public void Save()
    {
        settings.Change(Read);
        Apply();
    }

    private void Apply()
    {
        hub.Takeover = settings.Current.Takeover;
        transport.FollowsInstruments = settings.Current.FollowTransport;
        knobs.KnobGrid = settings.Current.KnobGrid;
    }

    private void Show(OutputSettings current)
    {
        takeover.SelectedIndex = current.Takeover == Takeover.PickUp ? 1 : 0;
        followTransport.IsChecked = current.FollowTransport;
        keyboardLayout.SelectedIndex = current.Keyboard == KeyboardLayout.Scale ? 1 : 0;
        knobGridOn.IsChecked = current.KnobGrid.On;
        knobColumns.Value = current.KnobGrid.Columns;
        knobRows.Value = current.KnobGrid.Rows;
    }

    /// <summary>Writes what the controls hold into <paramref name="into"/>, keeping a grid side whose box was emptied.</summary>
    private void Read(OutputSettings into)
    {
        into.Takeover = takeover.SelectedIndex == 1 ? Takeover.PickUp : Takeover.Jump;
        into.FollowTransport = followTransport.IsChecked == true;
        into.Keyboard = keyboardLayout.SelectedIndex == 1 ? KeyboardLayout.Scale : KeyboardLayout.Piano;

        // An emptied box keeps what was saved.
        into.KnobGrid = new KnobGrid
        {
            On = knobGridOn.IsChecked == true,
            Columns = knobColumns.Value is { } across ? (int)Math.Round(across) : into.KnobGrid.Columns,
            Rows = knobRows.Value is { } down ? (int)Math.Round(down) : into.KnobGrid.Rows,
        };

        into.KnobGrid.Clamp();
    }

    private static NumericUpDown GridSide(string name) => new()
    {
        Name = name,
        Minimum = KnobGrid.Fewest,
        Maximum = KnobGrid.Most,
        Increment = 1,
        FormatString = "0",
        FontSize = Text.Body,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };
}
