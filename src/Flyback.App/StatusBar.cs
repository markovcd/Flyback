using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.App.Statistics;
using Flyback.Plugins.Hosting;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App;

/// <summary>
/// The bar along the bottom: whatever there is to say, and what the patch costs
/// as it plays (ADR-0148).
/// </summary>
/// <remarks>
/// A grid rather than a row of controls, because a row hands every child the width
/// it asks for and the report would push the count off the edge of a narrow window.
/// </remarks>
internal sealed class StatusBar
{
    private readonly IDialogs dialogs;
    private readonly NodeEditor editor;
    private readonly PreviewHost preview;
    private readonly Usage usage;
    private readonly IWindowFocus focus;
    private readonly PluginCatalog plugins;
    private readonly ReportLine report;

    private readonly TextBlock status = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = Text.Body,

        // Every line on this bar shares one row of a narrow window, so this one
        // gives way the same way the report beside it does rather than being
        // sheared off at whatever character the edge fell on.
        TextTrimming = TextTrimming.CharacterEllipsis,

        // On the right of the bar, against the edge the report is not on, and
        // the gap a StackPanel would give, added by hand since this is a grid.
        TextAlignment = TextAlignment.Right,
        Margin = new Thickness(8, 0, 0, 0),
    };

    /// <summary>Said in the report's place while a patch just opened is compiled, before it starts.</summary>
    public Shimmer Compiling { get; } = new("Compiling…");

    /// <summary>The bar itself.</summary>
    public Control View { get; }

    /// <param name="site">Where the letter at the end of the bar is sent.</param>
    public StatusBar(NodeEditor editor, PluginCatalog plugins, ReportLine report, Usage usage, PreviewHost preview, SiteAccess site, Playback playback, IDialogs dialogs, IWindowFocus focus)
    {
        this.dialogs = dialogs;
        this.focus = focus;
        this.plugins = plugins;
        this.report = report;
        this.editor = editor;
        this.preview = preview;
        this.usage = usage;

        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            Margin = new Thickness(12, 5),
        };

        var letter = Glyph("letter", Glyphs.Letter(), "Write to Flyback's author. Anything you like, good or bad.");

        letter.Click += async (_, _) => await WriteToTheAuthorAsync(site, playback);

        // The same bar the count divides itself with, at the same size and color:
        // a drawn rule here would be a second kind of separator on one line.
        var rule = new TextBlock
        {
            Name = "statusRule",
            Text = "|",
            FontSize = Text.Body,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };

        // In the report's place while it shows: what is said meanwhile is about a
        // patch that has not started, and is read once it has.
        Compiling.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty) report.IsVisible = !Compiling.IsVisible;
        };

        Grid.SetColumn(report, 0);
        Grid.SetColumn(Compiling, 0);
        Grid.SetColumn(status, 1);
        Grid.SetColumn(rule, 2);
        Grid.SetColumn(letter, 3);

        bar.Children.Add(report);
        bar.Children.Add(Compiling);
        bar.Children.Add(status);
        bar.Children.Add(rule);
        bar.Children.Add(letter);

        View = new Border
        {
            Background = new SolidColorBrush(Colors.Panel),
            BorderBrush = new SolidColorBrush(Colors.Edge),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = bar,
        };

        var ticker = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        ticker.Tick += (_, _) => Update();
        ticker.Start();
    }

    /// <summary>Says what the patch costs and where its clock is.</summary>
    private void Update()
    {
        var nodes = editor.History.Patch.Nodes.Count;
        var wires = editor.History.Patch.Connections.Count;
        var ops = preview.Program.Ops.Length;

        // Which renderer produced the rate is part of what it means, so it is
        // said alongside — what is actually drawing, not what was asked for.
        var backend = preview.Backend == PreviewBackend.Gpu ? "GPU" : "CPU";

        // Only while the window is somebody's: a window behind others is drawn
        // at whatever rate the system leaves it, which says nothing about Flyback.
        if (focus.IsActive) usage.Drew(preview.FramesPerSecond, preview.Backend == PreviewBackend.Gpu);

        status.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{nodes} modules · {wires} wires · {ops} ops   |   t = {StatusClock.Text(preview.Time)}   |   {preview.FramesPerSecond:0} fps   |   {backend}");
    }

    private static Button Glyph(string name, Control glyph, string tip)
    {
        var button = new Button
        {
            Name = name,
            Content = glyph,
            Width = 22,
            Height = 18,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Text.Muted,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTip.SetTip(button, tip);

        return button;
    }

    /// <summary>Writing to the author, which the bar's last glyph opens (ADR-0136).</summary>
    private async Task WriteToTheAuthorAsync(SiteAccess site, Playback playback)
    {
        if (site.Root is not { } root)
        {
            report.Say("There is nowhere to send a letter: this copy has no site.");
            return;
        }

        // Built once and both shown and sent, so what was read is what goes.
        var about = SiteLetters.About(plugins, playback.Sound);

        var said = await dialogs.Show<string?>(
            LetterView.Title,
            LetterView.View(about, (mood, message, contact, cancel) => SiteLetters.SendAsync(site.Http, root, mood, message, contact, about, cancel)));

        if (said is not null) report.Say(said);
    }
}
