using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Flyback.Editor.Controls;
using Flyback.Editor.Hubs;
using Flyback.Editor.Notices;
using Flyback.Editor.Settings;
using Flyback.Editor.Shell;
using Flyback.Engine.Render;
using Flyback.Host;
using Flyback.Ui.Audio;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Composition;

/// <summary>The Picture section of the settings window: how the picture is drawn.</summary>
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
        + "OpenGL will not start, and either takes over from the next time Flyback starts. On a "
        + "Mac, Metal builds a patch's shader without holding the window, and OpenGL is there to "
        + "compare it with; either takes over at once.";

    /// <summary>What the Renderer box offers: the drivers this machine has, its own first, then the CPU.</summary>
    private static readonly string[] Renderers = [.. OutputSettings.DriversHere.Select(Said), GraphicsApi.Processor];

    private static int ProcessorRow => Renderers.Length - 1;

    private readonly OutputSettingRepository settings;
    private readonly PreviewHost preview;
    private readonly ReportLine report;
    private readonly IAudioEngine audio;
    private readonly RecordingState recording;

    public string Name => "Picture";

    public string Group => SettingsGroups.Output;

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

    /// <summary>Whether the preview smooths the picture's edges.</summary>
    private readonly ComboBox smoothing = new Picker
    {
        Name = "smoothing",
        ItemsSource = new[] { "Off", "Fast" },
        SelectedIndex = 1,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private readonly ComboBox previewFrameRate = new Picker
    {
        Name = "previewFrameRate",
        ItemsSource = PreviewFrameRates.Select(r => r <= 0 ? "Unlimited" : $"{r:0.##} fps").ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <param name="preview">The picture, whose renderer the Renderer box follows when it falls back.</param>
    public PictureSection(
        OutputSettingRepository settings,
        PreviewHost preview,
        ReportLine report,
        EditorHost host,
        IAudioEngine audio,
        RecordingState recording)
    {
        this.settings = settings;
        this.preview = preview;
        this.report = report;
        this.audio = audio;
        this.recording = recording;

        ToolTip.SetTip(previewFrameRate,
            "How often the preview redraws itself. Lower to see it near what a recording will "
            + "show, or to ease off a slow machine — the Recording section picks a take's own "
            + "rate, and reads whatever the preview last drew whatever this says.");

        rows.Children.Add(SettingRows.Heading("Drawing"));
        rows.Children.Add(InspectorRows.Field("Size", resolution));

        ToolTip.SetTip(smoothing,
            "Smooths the jagged edges of the picture, 3D above all, with a quick pass over each frame. "
            + "A take records it too. flyback-cli render draws four samples a pixel unless told --smooth fast or off.");

        // A page draws on WebGL alone, so it has no renderer to pick.
        if (!host.InPage)
        {
            ToolTip.SetTip(renderer, RendererTip);
            rows.Children.Add(InspectorRows.Field("Renderer", renderer));
        }

        rows.Children.Add(InspectorRows.Field("Smoothing", smoothing));
        rows.Children.Add(InspectorRows.Field("Preview rate", previewFrameRate));

        preview.BackendChanged += message =>
        {
            // Keeps showing what was asked for when the renderer falls back, and stays
            // enabled: the other driver is the way out, at once on a Mac and from the
            // next start on Windows.
            renderer.SelectedIndex = RendererRow(preview.Wanted == PreviewBackend.Gpu, settings.Current);
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
    /// A driver picked and not yet saved, other than the one drawing now, waits for a
    /// restart on Windows, the one system that picks its driver only as Flyback starts.
    /// </summary>
    public string? Restarts
    {
        get
        {
            var row = renderer.SelectedIndex;

            if (!OperatingSystem.IsWindows() || row < 0 || row == ProcessorRow) return null;

            var picked = OutputSettings.DriversHere[row];

            return picked != startedOn && row != RendererRow(settings.Current.Gpu, settings.Current)
                ? $"draw with {Said(picked)}"
                : null;
        }
    }

    /// <summary>The driver this run draws with, as the settings said when it started.</summary>
    private GraphicsDriver startedOn;

    public void Start()
    {
        startedOn = settings.Current.DriverHere;
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
        renderer.SelectedIndex = RendererRow(current.Gpu, current);
        previewFrameRate.SelectedIndex = SettingRows.Nearest(PreviewFrameRates, current.PreviewFrameRate);
        smoothing.SelectedIndex = current.Smoothing == Smoothing.Off ? 0 : 1;
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
        var row = Math.Max(renderer.SelectedIndex, 0);

        into.Width = size.Width;
        into.Height = size.Height;

        // The CPU says nothing about which driver draws the window, so that is kept.
        into.Gpu = row != ProcessorRow;

        if (row != ProcessorRow) into.Graphics = OutputSettings.DriversHere[row];


        into.PreviewFrameRate = PreviewFrameRates[Math.Max(previewFrameRate.SelectedIndex, 0)];
        into.Smoothing = smoothing.SelectedIndex == 0 ? Smoothing.Off : Smoothing.Fast;
    }

    /// <param name="before">What was in force until now, or null as the editor starts.</param>
    private void Apply(OutputSettings now, OutputSettings? before)
    {
        var size = SizeOf(now);

        preview.Resolution = size;
        preview.Draw(now.DriverHere);
        preview.Use(now.Gpu ? PreviewBackend.Gpu : PreviewBackend.Cpu);
        preview.FrameRate = now.PreviewFrameRate;
        preview.Smoothing = now.Smoothing;

        // A live Scan uses Coordinates' aspect (ADR-0077), so keep it in step
        // with the preview now that sizes are not all the same shape.
        audio.Aspect = SynthRenderer.AspectOf(size.Width, size.Height);

        // One box picks both. On Windows only the CPU takes effect at once; a Mac switches either.
        if (before is not null && now.DriverHere != before.DriverHere)
            report.Say(OperatingSystem.IsWindows()
                ? $"{Said(now.DriverHere)} draws from the next time Flyback starts."
                : $"{Said(now.DriverHere)} draws the picture.");
    }

    /// <summary>The row of <see cref="renderer"/> that says <paramref name="gpu"/> and the driver <paramref name="current"/> names.</summary>
    private static int RendererRow(bool gpu, OutputSettings current) =>
        gpu ? Math.Max(0, OutputSettings.DriversHere.ToList().IndexOf(current.DriverHere)) : ProcessorRow;

    /// <summary>A driver as the Renderer box and the status line say it.</summary>
    private static string Said(GraphicsDriver driver) => driver switch
    {
        GraphicsDriver.Direct3D => "Direct3D",
        GraphicsDriver.Metal => GraphicsApi.Metal,
        _ => "OpenGL",
    };

    /// <summary>The row of the size list a saved size is, or the default for one the list no longer offers.</summary>
    private static int SizeRow(OutputSettings settings)
    {
        var row = Array.FindIndex(Resolutions.All,
            r => r.Size.Width == settings.Width && r.Size.Height == settings.Height);

        return row < 0 ? Resolutions.Default : row;
    }
}
