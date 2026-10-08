using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Flyback.Ui.Audio;
using Flyback.Editor.Bars;
using Flyback.Editor.Capture;
using Flyback.Editor.Controls;
using Flyback.Ui.Controls;
using Flyback.Editor.Inspect;
using Flyback.Editor.Knobs;
using Flyback.Editor.Notices;
using Flyback.Engine.Render;
using Flyback.Ui;
using Flyback.Host;

namespace Flyback.Editor.Settings;

/// <summary>The Picture section of the settings window: size, preview rate, renderer and full screen.</summary>
internal sealed class PictureSection : ISettingsSection, IReactTo<TakeMarked>
{
    /// <summary>
    /// The frame rates the preview itself can be capped to, 0 standing for
    /// uncapped — the first row, since <see cref="SettingRows.Nearest"/> reads the
    /// list as ascending and a saved 0 should not land on 24 for being the
    /// closest positive number.
    /// </summary>
    private static readonly double[] PreviewFrameRates = [0, 24, 25, 30, 50, 60];

    public const string RendererTip =
        "What draws the picture: a shader on the graphics card, or the CPU. Switch to the CPU to "
        + "compare the two, or if a long session starts to look stepped. On Windows, OpenGL builds "
        + "a large patch's shader in a second where Direct3D can take several and hold the window "
        + "while it does; Direct3D is for a machine whose OpenGL misbehaves, is used anyway where "
        + "OpenGL will not start, and either takes over from the next time Flyback starts.";

    /// <summary>What <see cref="Renderer"/> offers: the graphics card's APIs in the order of <see cref="GraphicsDriver"/>, then the CPU.</summary>
    private static readonly string[] Renderers = OperatingSystem.IsWindows()
        ? ["OpenGL", "Direct3D", GraphicsApi.Processor]
        : ["OpenGL", GraphicsApi.Processor];

    private static int ProcessorRow => Renderers.Length - 1;

    private readonly OutputSettingRepository settings;
    private readonly IMonitors monitors;
    private readonly PreviewHost preview;
    private readonly ReportLine report;
    private readonly IAudioEngine audio;
    private readonly TransportControls transport;
    private readonly PanelKnobs knobs;
    private readonly RecordingState recording;

    public string Name => "Picture";

    public Control View => rows;

    private readonly StackPanel rows = new() { Spacing = 8, Width = SettingsSession.SectionWidth };

    /// <summary>What size the picture is drawn at. A take grays it out while it runs.</summary>
    private readonly ComboBox resolution = new Picker
    {
        ItemsSource = Resolutions.All.Select(r => r.Label).ToList(),
        SelectedIndex = Resolutions.Default,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// What draws the picture: OpenGL by default, because the graphics card is what
    /// keeps up with a large patch, Direct3D on Windows, or the CPU.
    /// </summary>
    private readonly ComboBox renderer = new Picker
    {
        Name = "render",
        ItemsSource = Renderers,
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private readonly ComboBox previewFrameRate = new Picker
    {
        Name = "previewFrameRate",
        ItemsSource = PreviewFrameRates.Select(r => r <= 0 ? "Unlimited" : $"{r:0.##} fps").ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

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

    /// <param name="preview">The picture, whose renderer the Renderer box follows when it falls back.</param>
    public PictureSection(
        OutputSettingRepository settings,
        IMonitors monitors,
        PreviewHost preview,
        ReportLine report,
        EditorHost host,
        IAudioEngine audio,
        TransportControls transport,
        PanelKnobs knobs,
        RecordingState recording)
    {
        this.settings = settings;
        this.monitors = monitors;
        this.preview = preview;
        this.report = report;
        this.audio = audio;
        this.transport = transport;
        this.knobs = knobs;
        this.recording = recording;

        ToolTip.SetTip(previewFrameRate,
            "How often the preview redraws itself. Lower to see it near what a recording will "
            + "show, or to ease off a slow machine — the Recording section picks a take's own "
            + "rate, and reads whatever the preview last drew whatever this says.");

        ToolTip.SetTip(fullScreenOn,
            "Where double-clicking the preview puts the picture. On another monitor the editor "
            + "stays where it is, and double-clicking the picture or pressing Esc brings it back.");

        ToolTip.SetTip(transportEdge,
            "Where the transport and the seek bar wait over a full-screen picture, and the viewer's; "
            + "the knobs take the other edge.");

        rows.Children.Add(InspectorRows.Field("Size", resolution));
        rows.Children.Add(InspectorRows.Field("Preview rate", previewFrameRate));

        // A page draws on WebGL alone, so it has no renderer to pick.
        if (!host.InPage)
        {
            ToolTip.SetTip(renderer, RendererTip);
            rows.Children.Add(InspectorRows.Field("Renderer", renderer));
        }

        rows.Children.Add(InspectorRows.Field("Full screen", fullScreenOn));
        rows.Children.Add(InspectorRows.Field("Transport", transportEdge));

        preview.BackendChanged += message =>
        {
            // Keeps showing what was asked for when the renderer falls back, and stays
            // enabled: the other driver, from the next start, is the way out.
            renderer.SelectedIndex = RendererRow(preview.Wanted == PreviewBackend.Gpu, settings.Current.Driver);
            ToolTip.SetTip(renderer, preview.GpuAvailable ? RendererTip : message);
            report.Say(message);
        };
    }

    /// <summary>What size <paramref name="settings"/> draws the picture at, or the default for one the list no longer offers.</summary>
    public static PixelSize SizeOf(OutputSettings settings)
    {
        var (width, height) = Resolutions.All[SizeRow(settings)].Size;

        return new PixelSize(width, height);
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
        Apply(settings.Current, before: null);
    }

    public void Show() => Show(settings.Current);

    public void Save()
    {
        var before = settings.Change(Read);

        Apply(settings.Current, before);
    }

    /// <summary>The header of a take has committed to a frame size, so the size cannot change while one runs.</summary>
    public Task On(TakeMarked notice)
    {
        resolution.IsEnabled = !recording.Running;

        return Task.CompletedTask;
    }

    private void Show(OutputSettings current)
    {
        resolution.SelectedIndex = SizeRow(current);
        renderer.SelectedIndex = RendererRow(current.Gpu, current.Driver);
        transportEdge.SelectedIndex = current.Transport == TransportEdge.Bottom ? 1 : 0;
        previewFrameRate.SelectedIndex = SettingRows.Nearest(PreviewFrameRates, current.PreviewFrameRate);
    }

    /// <summary>Writes what the controls hold into <paramref name="into"/>, leaving what a control cannot say as it was.</summary>
    private void Read(OutputSettings into)
    {
        // Grayed out for the length of a take, whose file has committed to a size
        // and drops every frame that arrives at another. Graying a box does not
        // take back a row already picked in it — during the count-in, say — so
        // what it holds is not read while it is gray, and the row is put back.
        if (!resolution.IsEnabled) resolution.SelectedIndex = SizeRow(into);

        var size = Resolutions.All[Math.Max(resolution.SelectedIndex, 0)].Size;
        var fullScreen = ReadFullScreen(into);
        var row = Math.Max(renderer.SelectedIndex, 0);

        into.Width = size.Width;
        into.Height = size.Height;

        // The CPU says nothing about which driver draws the window, so that is kept.
        into.Gpu = row != ProcessorRow;

        if (row != ProcessorRow && OperatingSystem.IsWindows())
            into.Driver = row == 1 ? GraphicsDriver.Direct3D : GraphicsDriver.OpenGl;

        into.FullScreen = fullScreen.On;
        into.FullScreenMonitor = fullScreen.Monitor;
        into.Transport = transportEdge.SelectedIndex == 1 ? TransportEdge.Bottom : TransportEdge.Top;

        into.PreviewFrameRate = PreviewFrameRates[Math.Max(previewFrameRate.SelectedIndex, 0)];
    }

    /// <param name="before">What was in force until now, or null as the editor starts.</param>
    private void Apply(OutputSettings now, OutputSettings? before)
    {
        var size = SizeOf(now);

        preview.Resolution = size;
        preview.Use(now.Gpu ? PreviewBackend.Gpu : PreviewBackend.Cpu);
        preview.FrameRate = now.PreviewFrameRate;

        // A live Scan uses Coordinates' aspect (ADR-0077), so keep it in step
        // with the preview now that sizes are not all the same shape.
        audio.Aspect = SynthRenderer.AspectOf(size.Width, size.Height);

        if (transport.Overlay is { } overlay) TransportOverlay.Lay(now.Transport, overlay, knobs.Stage);
        if (transport.PictureWindow is { } picture)
            TransportOverlay.Lay(now.Transport, picture.Transport, picture.Knobs);

        // One box picks both, and only the CPU takes effect at once.
        if (before is not null && now.Driver != before.Driver)
            report.Say($"{(now.Driver == GraphicsDriver.Direct3D ? "Direct3D" : "OpenGL")} draws from the next time Flyback starts.");
    }

    /// <summary>The row of <see cref="renderer"/> that says <paramref name="gpu"/> and <paramref name="driver"/>.</summary>
    private static int RendererRow(bool gpu, GraphicsDriver driver) =>
        !gpu ? ProcessorRow : driver == GraphicsDriver.Direct3D && OperatingSystem.IsWindows() ? 1 : 0;

    /// <summary>The row of the size list a saved size is, or the default for one the list no longer offers.</summary>
    private static int SizeRow(OutputSettings settings)
    {
        var row = Array.FindIndex(Resolutions.All,
            r => r.Size.Width == settings.Width && r.Size.Height == settings.Height);

        return row < 0 ? Resolutions.Default : row;
    }

    /// <summary>What <see cref="fullScreenOn"/> holds, as the settings keep it.</summary>
    private (FullScreenOn On, MonitorSpot? Monitor) ReadFullScreen(OutputSettings before) => fullScreenOn.SelectedIndex switch
    {
        1 => (FullScreenOn.OtherMonitor, before.FullScreenMonitor),
        >= 2 and var row when row - 2 < fullScreenMonitors.Count => (FullScreenOn.ChosenMonitor, fullScreenMonitors[row - 2]),
        _ => (FullScreenOn.SameMonitor, before.FullScreenMonitor),
    };
}
