using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Flyback.Core.Graph;
using Flyback.Editor.Controls;
using Flyback.Editor.Site;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Gallery;

/// <summary>
/// The run of presets the preset site offers: asked of the site with whatever the
/// filter box says, a quarter of a second after typing stops, a page at a time.
/// </summary>
/// <remarks>
/// Its tiles answer the dialog with the <see cref="SitePreset"/> itself, so nothing
/// is downloaded until somebody has picked one and said the patch on the canvas may go.
/// While the site does not answer, the presets opened from it before are listed
/// instead, and answer with their <see cref="KeptPreset"/>. In a page, a preset
/// needing a plugin the page lacks is listed with why, and cannot be picked.
/// </remarks>
internal sealed class SiteRun : IDisposable
{
    private readonly PresetSite site;
    private readonly KeptSharedPresets kept;
    private readonly TextBox box;
    private readonly IDialog dialog;
    private readonly bool inPage;
    private readonly WrapPanel tiles = new() { Name = "site-presets", ItemSpacing = 8, LineSpacing = 8 };
    private readonly TextBlock status = new() { Name = "site-status", FontSize = Text.Body, Foreground = Text.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly Button more = new() { Name = "more-presets", Content = "More", FontSize = Text.Body, IsVisible = false, Margin = new Thickness(0, 8, 0, 0) };
    private readonly SiteSearch<SitePreset> search;

    public SiteRun(PresetSite site, KeptSharedPresets kept, TextBox box, IDialog dialog, Action<IPreset> open, bool inPage)
    {
        this.site = site;
        this.kept = kept;
        this.box = box;
        this.dialog = dialog;
        this.inPage = inPage;

        search = new SiteSearch<SitePreset>((page, cancel) => site.SearchAsync(box.Text, page, cancel), status, more, $"The preset site at {site.Root} did not answer.")
        {
            Show = (found, fresh, cancel) => Show(found, fresh, open, cancel),
            Empty = () => tiles.Children.Count == 0,
            Nothing = () => box.Text is { Length: > 0 } typed ? $"Nothing on the preset site matches “{typed.Trim()}”." : "Nothing is shared on the preset site yet.",
            Failed = (fresh, cancel) => { if (fresh) ShowKept(open, cancel); },
        };

        View = new StackPanel
        {
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Margin = new Thickness(0, 10, 0, 2),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = PresetGallery.SiteHeading,
                            FontSize = Text.Caption,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = new SolidColorBrush(Colors.Note),
                        },
                        SiteLink.To("Open", site.Root),
                    },
                },
                status,
                tiles,
                more,
            },
        };

        box.TextChanged += (_, _) => _ = search.AskAsync(fresh: true, after: SiteSearch<SitePreset>.Typing);
        more.Click += (_, _) => _ = search.AskAsync(fresh: false);

        // Asked while the gallery is up and not a moment after.
        View.AttachedToVisualTree += (_, _) => _ = search.AskAsync(fresh: true);
        View.DetachedFromVisualTree += (_, _) => Dispose();
    }

    public StackPanel View { get; }

    /// <summary>Stops asking the site, which is the end of the gallery.</summary>
    public void Dispose() => search.Dispose();

    private void Show(IReadOnlyList<SitePreset> found, bool fresh, Action<IPreset> open, CancellationToken cancel)
    {
        if (fresh) tiles.Children.Clear();

        foreach (var preset in found)
        {
            kept.Refresh(site.Root, preset);
            tiles.Children.Add(SiteTile(preset, () => open(preset), token => StillAsync(preset, token), reportable: true, cancel));
        }
    }

    /// <summary>A listed preset's still: the one kept with it where it was opened before, and otherwise the site's.</summary>
    private async Task<byte[]?> StillAsync(SitePreset preset, CancellationToken cancel) =>
        await Task.Run(() => kept.Find(site.Root, preset.Id) is { } one ? kept.Still(one) : null, cancel)
        ?? await site.StillAsync(preset, cancel);

    /// <summary>The presets opened from the site before that match the filter box, as the site would match them.</summary>
    private void ShowKept(Action<IPreset> open, CancellationToken cancel)
    {
        tiles.Children.Clear();
        more.IsVisible = false;

        var words = (box.Text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matching = kept.All(site.Root).Where(k => words.All(word =>
            $"{k.Preset.Name} {k.Preset.Author} {k.Preset.Description} {string.Join(' ', k.Preset.Tags)}".Contains(word, StringComparison.OrdinalIgnoreCase)));

        foreach (var one in matching)
            tiles.Children.Add(SiteTile(one.Preset, () => open(one), _ => Task.FromResult(kept.Still(one)), reportable: false, cancel));

        if (tiles.Children.Count > 0) status.Text += " These were kept when they were last opened from it.";
    }

    /// <param name="reportable">Offers a report to the site's admin, which a site that does not answer cannot take.</param>
    private Button SiteTile(SitePreset preset, Action pick, Func<CancellationToken, Task<byte[]?>> still, bool reportable, CancellationToken cancel)
    {
        var picture = new Border
        {
            Width = PresetCard.TileWidth,
            Height = PresetCard.PictureHeight,
            Background = new SolidColorBrush(Colors.Canvas),
            ClipToBounds = true,
            CornerRadius = new CornerRadius(3),
        };

        var words = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                picture,
                new TextBlock { Text = preset.Name, FontSize = Text.Body, FontWeight = FontWeight.SemiBold },
                RatingLine.Of(preset.Rating),
            },
        };

        if (preset.Description.Length > 0)
            words.Children.Add(new TextBlock { Text = preset.Description, FontSize = Text.Caption, Foreground = Text.Muted, TextWrapping = TextWrapping.Wrap });

        if (preset.Author.Length > 0)
            words.Children.Add(new TextBlock
            {
                Text = "by " + preset.Author,
                FontSize = Text.Caption,
                FontStyle = FontStyle.Italic,
                Foreground = Text.Muted,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

        if (preset.Tags.Count > 0)
            words.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", preset.Tags),
                FontSize = Text.Caption,
                Foreground = new SolidColorBrush(Colors.Note),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

        var tile = new Button
        {
            Name = "site-tile",
            Tag = preset,
            Width = PresetCard.TileWidth + 16,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Colors.Node),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(2),
            Content = words,
        };

        tile.Click += (_, _) => pick();

        _ = ShowStillAsync(still, picture, cancel);

        if (inPage && preset.PageLacks is { } lacks)
        {
            words.Children.Add(new TextBlock
            {
                Name = "site-lacks",
                Text = lacks + ", which Flyback in a browser does not have.",
                FontSize = Text.Caption,
                Foreground = new SolidColorBrush(Colors.Attention),
                TextWrapping = TextWrapping.Wrap,
            });

            tile.IsEnabled = false;
            ToolTip.SetShowOnDisabled(tile, true);
            ToolTip.SetTip(tile, $"{lacks}: download “{preset.Name}” from the preset site to open it in Flyback.");

            return tile;
        }

        if (!reportable)
        {
            ToolTip.SetTip(tile, $"Open “{preset.Name}” as it was kept when it was last opened from the preset site.");
            return tile;
        }

        ToolTip.SetTip(tile, $"Download “{preset.Name}” from the preset site and open it. Right-click to report it.");

        var report = new MenuItem { Name = "report-preset", Header = "Report…" };

        report.Click += async (_, _) =>
        {
            if (await ReportView.AskAsync(dialog, preset.Name, (reason, details, cancel) => site.ReportAsync(preset, reason, details, cancel)) is not { } said) return;

            status.Text = said;
            status.IsVisible = true;
        };

        tile.ContextFlyout = new MenuFlyout { Items = { report } };

        return tile;
    }

    private static async Task ShowStillAsync(Func<CancellationToken, Task<byte[]?>> still, Border into, CancellationToken cancel)
    {
        try
        {
            if (await still(cancel) is not { } bytes) return;

            using var stream = new MemoryStream(bytes, writable: false);

            into.Child = new Image { Source = new Bitmap(stream), Stretch = Stretch.UniformToFill };
        }
        catch (Exception ex) when (ex is OperationCanceledException or ArgumentException or InvalidOperationException or IOException or NotSupportedException)
        {
        }
    }
}
