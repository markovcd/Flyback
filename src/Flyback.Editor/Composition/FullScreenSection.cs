using Avalonia.Controls;
using Avalonia.Layout;
using Flyback.Editor.Bars;
using Flyback.Editor.Controls;
using Flyback.Editor.Knobs;
using Flyback.Editor.Settings;
using Flyback.Editor.Shell;
using Flyback.Editor.Windows;
using Flyback.Host;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Composition;

/// <summary>The Full screen section of the settings window: which monitor, which edge the transport waits at, and live patching.</summary>
internal sealed class FullScreenSection : ISettingsSection
{
    private readonly OutputSettingRepository settings;
    private readonly IMonitors monitors;
    private readonly TransportControls transport;
    private readonly FullScreenPreview fullScreen;
    private readonly PanelKnobs knobs;
    private readonly LivePatchingRows live;

    public string Name => "Full screen";

    public string Group => SettingsGroups.Output;

    public Control View => rows;

    private readonly StackPanel rows = new() { Spacing = 8, Width = SettingsSession.SectionWidth };

    /// <summary>Which monitor full screen fills.</summary>
    private readonly ComboBox fullScreenOn = new Picker
    {
        Name = "fullScreenOn",
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>Which edge of a full-screen picture the transport waits at, and so which the knobs do.</summary>
    private readonly ComboBox transportEdge = new Picker
    {
        Name = "transportEdge",
        ItemsSource = new[] { "Top, knobs below", "Bottom, knobs on top" },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>The monitors <see cref="fullScreenOn"/> lists after its first two rows, in order.</summary>
    private List<MonitorSpot> fullScreenMonitors = [];

    public FullScreenSection(
        OutputSettingRepository settings,
        IMonitors monitors,
        TransportControls transport,
        FullScreenPreview fullScreen,
        PanelKnobs knobs,
        LivePatchingRows live,
        EditorHost host)
    {
        this.settings = settings;
        this.monitors = monitors;
        this.transport = transport;
        this.fullScreen = fullScreen;
        this.knobs = knobs;
        this.live = live;

        ToolTip.SetTip(fullScreenOn,
            "Where double-clicking the preview puts the picture. On another monitor the editor "
            + "stays where it is, and double-clicking the picture or pressing Esc brings it back.");

        ToolTip.SetTip(transportEdge,
            "Where the transport and the seek bar wait over a full-screen picture, and the viewer's; "
            + "the knobs take the other edge.");

        rows.Children.Add(SettingRows.Heading("Where it goes"));

        // A device has the one screen.
        if (!host.OnDevice) rows.Children.Add(InspectorRows.Field("Monitor", fullScreenOn));

        rows.Children.Add(InspectorRows.Field("Transport at", transportEdge));
        rows.Children.Add(SettingRows.Heading("Live patching"));
        rows.Children.Add(live.View);
    }

    /// <summary>
    /// Lists the monitors plugged in now, and the chosen one if it is not, and
    /// selects what the saved settings say. Asked as the settings open, since the
    /// monitors are the window's to name.
    /// </summary>
    public void Opening()
    {
        var current = settings.Current;

        var screens = monitors.All;

        fullScreenMonitors = [.. screens.Select(s => MonitorPlacement.Describe(s)!)];

        List<string> rows =
        [
            "Same monitor",
            "Another monitor",
            .. screens.Select(s => $"{s.DisplayName ?? "Monitor"} · {s.Bounds.Width}×{s.Bounds.Height}{(s.IsPrimary ? " · main" : "")}"),
        ];

        var chosen = current.FullScreenMonitor is { } wanted ? MonitorPlacement.Find(wanted, fullScreenMonitors) : null;

        // Kept on the list while unplugged, so saving anything else does not forget it.
        if (current.FullScreenMonitor is { } away && chosen is null)
        {
            fullScreenMonitors.Add(away);
            rows.Add($"{away.Name ?? "Monitor"} · {away.Width}×{away.Height} · not plugged in");
            chosen = fullScreenMonitors.Count - 1;
        }

        fullScreenOn.ItemsSource = rows;
        fullScreenOn.SelectedIndex = current.FullScreen switch
        {
            FullScreenOn.OtherMonitor => 1,
            FullScreenOn.ChosenMonitor when chosen is { } row => 2 + row,
            _ => 0,
        };
    }

    public void Start()
    {
        Show();
        Apply(settings.Current);
    }

    public void Show()
    {
        var current = settings.Current;

        transportEdge.SelectedIndex = current.Transport == TransportEdge.Bottom ? 1 : 0;
        live.Show(current);
    }

    public void Save()
    {
        settings.Change(Read);
        Apply(settings.Current);
    }

    /// <summary>Writes what the controls hold into <paramref name="into"/>.</summary>
    private void Read(OutputSettings into)
    {
        var (on, monitor) = ReadFullScreen(into);

        into.FullScreen = on;
        into.FullScreenMonitor = monitor;
        into.Transport = transportEdge.SelectedIndex == 1 ? TransportEdge.Bottom : TransportEdge.Top;
        live.Read(into);
    }

    private void Apply(OutputSettings now)
    {
        live.Apply(now);

        if (transport.Overlay is { } overlay) TransportOverlay.Lay(now.Transport, overlay, knobs.Stage);
        if (fullScreen.Away is { } picture)
            TransportOverlay.Lay(now.Transport, picture.Transport, picture.Knobs);
    }

    /// <summary>What <see cref="fullScreenOn"/> holds, as the settings keep it.</summary>
    private (FullScreenOn On, MonitorSpot? Monitor) ReadFullScreen(OutputSettings before) => fullScreenOn.SelectedIndex switch
    {
        1 => (FullScreenOn.OtherMonitor, before.FullScreenMonitor),
        >= 2 and var row when row - 2 < fullScreenMonitors.Count => (FullScreenOn.ChosenMonitor, fullScreenMonitors[row - 2]),
        _ => (FullScreenOn.SameMonitor, before.FullScreenMonitor),
    };
}
