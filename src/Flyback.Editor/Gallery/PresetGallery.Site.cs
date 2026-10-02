using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Flyback.Editor.Controls;
using Flyback.Ui.Controls;
using Flyback.Editor.Site;
using Flyback.Core.Graph;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Gallery;

internal sealed partial class PresetGallery
{
    /// <summary>What heads the presets the preset site offers, after everything on this machine.</summary>
    public const string SiteHeading = "ON THE PRESET SITE";

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
    private sealed class SiteRun : IDisposable
    {
        private static readonly TimeSpan Typing = TimeSpan.FromMilliseconds(250);

        private readonly PresetSite site;
        private readonly KeptSharedPresets kept;
        private readonly TextBox box;
        private readonly IDialog dialog;
        private readonly bool inPage;
        private readonly WrapPanel tiles = new() { Name = "site-presets", ItemSpacing = 8, LineSpacing = 8 };
        private readonly TextBlock status = new() { Name = "site-status", FontSize = Text.Body, Foreground = Text.Muted, TextWrapping = TextWrapping.Wrap };
        private readonly Button more = new() { Name = "more-presets", Content = "More", FontSize = Text.Body, IsVisible = false, Margin = new Thickness(0, 8, 0, 0) };

        private int page;
        private CancellationTokenSource? asking;

        public SiteRun(PresetSite site, KeptSharedPresets kept, TextBox box, IDialog dialog, Action<IPreset> open, bool inPage)
        {
            this.site = site;
            this.kept = kept;
            this.box = box;
            this.dialog = dialog;
            this.inPage = inPage;

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
                                Text = SiteHeading,
                                FontSize = Text.Caption,
                                FontWeight = FontWeight.SemiBold,
                                Foreground = new SolidColorBrush(Colors.Note),
                            },
                            Link("Open", site.Root),
                        },
                    },
                    status,
                    tiles,
                    more,
                },
            };

            box.TextChanged += (_, _) => _ = AskAsync(fresh: true, open, after: Typing);
            more.Click += (_, _) => _ = AskAsync(fresh: false, open);

            // Asked while the gallery is up and not a moment after.
            View.AttachedToVisualTree += (_, _) => _ = AskAsync(fresh: true, open);
            View.DetachedFromVisualTree += (_, _) => Dispose();
        }

        public StackPanel View { get; }

        /// <summary>Stops asking the site, which is the end of the gallery.</summary>
        public void Dispose() => Stop();

        private void Stop()
        {
            asking?.Cancel();
            asking?.Dispose();
            asking = null;
        }

        private async Task AskAsync(bool fresh, Action<IPreset> open, TimeSpan after = default)
        {
            Stop();

            var cancel = (asking = new CancellationTokenSource()).Token;
            var wanted = fresh ? 1 : page + 1;

            if (fresh) status.Text = "Looking…";

            status.IsVisible = fresh;
            more.IsEnabled = false;

            SitePresetPage found;

            try
            {
                if (after > TimeSpan.Zero) await Task.Delay(after, cancel);

                found = await site.SearchAsync(box.Text, wanted, cancel);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
            {
                status.Text = $"The preset site at {site.Root} did not answer.";
                status.IsVisible = true;
                more.IsEnabled = true;

                if (fresh) ShowKept(open, cancel);

                return;
            }

            if (cancel.IsCancellationRequested) return;

            page = found.Page;

            if (fresh) tiles.Children.Clear();

            foreach (var preset in found.Items)
            {
                kept.Refresh(site.Root, preset);
                tiles.Children.Add(SiteTile(preset, () => open(preset), token => StillAsync(preset, token), reportable: true, cancel));
            }

            status.Text = box.Text is { Length: > 0 } typed ? $"Nothing on the preset site matches “{typed.Trim()}”." : "Nothing is shared on the preset site yet.";
            status.IsVisible = tiles.Children.Count == 0;
            more.IsVisible = found.More;
            more.IsEnabled = true;
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
                Width = TileWidth,
                Height = PictureHeight,
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
                Width = TileWidth + 16,
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

        /// <summary>A line of text that opens <paramref name="uri"/> in the system browser.</summary>
        private static TextBlock Link(string text, Uri uri)
        {
            var link = new TextBlock
            {
                Text = text,
                FontSize = Text.Caption,
                Foreground = new SolidColorBrush(Colors.Attention),
                TextDecorations = TextDecorations.Underline,
                Cursor = new Cursor(StandardCursorType.Hand),
            };

            link.PointerPressed += async (_, _) =>
            {
                if (TopLevel.GetTopLevel(link)?.Launcher is { } launcher)
                    await launcher.LaunchUriAsync(uri);
            };

            return link;
        }
    }
}
