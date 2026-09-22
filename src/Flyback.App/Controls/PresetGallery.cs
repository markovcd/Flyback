using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>A tile of the gallery: the preset it picks, and the picture it shows it by.</summary>
internal sealed record PointedTile(PatchPreset Preset, Image Picture);

/// <summary>
/// The gallery as a dialog shows it: the box that narrows it, which stays put, and
/// the tiles, which scroll beneath it.
/// </summary>
internal sealed record GalleryParts(TextBox Filter, Control Tiles);

/// <summary>
/// The presets somebody saved, and what the gallery may do about them: save the
/// patch on the canvas as another, and delete one.
/// </summary>
/// <param name="All">What is saved now, in the order to show it. Asked again after every change.</param>
/// <param name="Check">
/// Whether a name may be saved under, and a line saying so — why not, or that it
/// replaces one already saved.
/// </param>
/// <param name="Replaces">Whether saving under a name would replace a preset already saved, which is asked about first.</param>
/// <param name="Keep">Saves the patch on the canvas under a name <paramref name="Check"/> allowed. False where that failed.</param>
/// <param name="Remove">Deletes one.</param>
internal sealed record YourPresets(
    Func<IReadOnlyList<PatchPreset>> All,
    Func<string, (bool Allowed, string Hint)> Check,
    Func<string, bool> Replaces,
    Func<string, bool> Keep,
    Action<PatchPreset> Remove)
{
    /// <summary>Whether the run is only picked from: no card to save one, and no tile that deletes.</summary>
    public bool PickOnly { get; private init; }

    /// <summary>The same presets, to pick from and nothing else.</summary>
    public YourPresets ToPickFrom() => this with { PickOnly = true };
}

/// <summary>
/// Every preset as a tile — a picture, its name and what it is for — under a heading
/// for each kind, to be shown in a dialog and picked from with a click.
/// </summary>
/// <remarks>
/// A dropdown stopped being the right shape once there were thirty presets to read
/// past. A tile is a button, so the keyboard walks them and Enter picks one; what it
/// answers with is the preset, and the caller decides what picking it means.
/// </remarks>
internal static partial class PresetGallery
{
    /// <summary>The style class of a tile whose preset is being asked about deleting.</summary>
    private const string Asking = "asking";

    private const double TileWidth = 192;
    private const double PictureHeight = TileWidth * PresetThumbnails.Height / PresetThumbnails.Width;

    /// <summary>
    /// What a <see cref="PresetKind"/> is called where it heads its own run of
    /// presets — said in the words somebody choosing a patch would use, since
    /// nobody opening the list is looking for an Interplay. Shouted like the
    /// module palette's section headings, being the same thing in the same kind
    /// of list.
    /// </summary>
    public static string Heading(PresetKind kind) => kind switch
    {
        PresetKind.Idea => "ONE IDEA",
        PresetKind.Interplay => "SOUND AND PICTURE",
        PresetKind.Showcase => "SHOWCASE",
        _ => "BLANK",
    };

    /// <summary>What heads the presets somebody saved, last of all.</summary>
    public const string YoursHeading = "YOUR PRESETS";

    /// <summary>
    /// The gallery of <paramref name="ordered"/>, which must already be grouped by
    /// kind, with the tile of <paramref name="showing"/> outlined as the one on the
    /// canvas.
    /// </summary>
    /// <param name="pointedAt">
    /// Told the tile the pointer has come to rest on, and null when it leaves one —
    /// what the caller auditions.
    /// </param>
    /// <param name="yours">
    /// The presets somebody saved, headed after all the rest, or null for a gallery
    /// without that section.
    /// </param>
    /// <param name="site">
    /// Where shared presets are listed from, headed last, or null for a gallery without
    /// them. A tile of theirs answers with its <see cref="SitePreset"/>.
    /// </param>
    public static GalleryParts Build(
        IReadOnlyList<PatchPreset> ordered,
        PatchPreset? showing,
        PresetThumbnails thumbnails,
        Action<PointedTile?>? pointedAt = null,
        YourPresets? yours = null,
        PresetSite? site = null)
    {
        var gallery = new StackPanel { Name = "gallery", Spacing = 6 };
        var search = new Search { Elsewhere = site is not null };

        foreach (var run in ordered.GroupBy(preset => preset.Kind))
        {
            gallery.Children.Add(new TextBlock
            {
                Text = Heading(run.Key),
                FontSize = Text.Caption,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Colors.PresetAccent(run.Key)),
                Margin = new Thickness(0, 10, 0, 2),
            });

            var tiles = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };

            foreach (var preset in run)
                tiles.Children.Add(Tile(preset, preset == showing, Colors.PresetAccent(preset.Kind), thumbnails, pointedAt, search));

            gallery.Children.Add(tiles);
            search.Add((TextBlock)gallery.Children[^2], tiles);
        }

        if (yours is not null) Yours(gallery, yours, showing, thumbnails, pointedAt, search);

        search.Apply();

        if (site is not null) gallery.Children.Add(new SiteRun(site, search.Box).View);

        // The hint beside the runs rather than among them, so the gallery stays
        // what it has always been: a heading, then its tiles, and again.
        var tilesAndHint = new StackPanel
        {
            Margin = new Thickness(16, 0, 16, 16),
            Children = { search.Hint, gallery },
        };

        return new GalleryParts(search.Box, tilesAndHint);
    }

    /// <summary>
    /// The run of presets somebody saved: a card to save the patch on the canvas as
    /// one, and then a tile each. Shown with none saved, since the card is how the
    /// first one gets there — unless the run is only picked from, which has no card
    /// and so nothing to show.
    /// </summary>
    private static void Yours(
        StackPanel gallery,
        YourPresets yours,
        PatchPreset? showing,
        PresetThumbnails thumbnails,
        Action<PointedTile?>? pointedAt,
        Search search)
    {
        if (yours.PickOnly && yours.All().Count == 0) return;

        var accent = Colors.Feedback;

        var heading = new TextBlock
        {
            Text = YoursHeading,
            FontSize = Text.Caption,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(accent),
            Margin = new Thickness(0, 10, 0, 2),
        };

        var tiles = new WrapPanel { Name = "yours", ItemSpacing = 8, LineSpacing = 8 };

        gallery.Children.Add(heading);
        gallery.Children.Add(tiles);
        search.Add(heading, tiles);

        Fill();

        // Every change rebuilds the run from what is saved rather than editing
        // it, so a preset saved over another is one tile, in its place.
        void Fill()
        {
            tiles.Children.Clear();

            if (!yours.PickOnly) tiles.Children.Add(KeepCard(yours, Fill));

            foreach (var preset in yours.All())
            {
                var tile = Tile(preset, preset == showing, accent, thumbnails, pointedAt, search);

                if (!yours.PickOnly) Removable(tile, preset, () =>
                {
                    // Whatever is being tried may be the one going.
                    pointedAt?.Invoke(null);
                    yours.Remove(preset);
                    Fill();
                });

                tiles.Children.Add(tile);
            }

            // Whatever is typed goes on narrowing the run this rebuilt.
            search.Apply();
        }
    }

    /// <summary>
    /// The first tile of the saved run: a button that becomes a name box, and saves
    /// the patch on the canvas under what is typed there.
    /// </summary>
    private static Border KeepCard(YourPresets yours, Action saved)
    {
        var card = new Border
        {
            Name = "keep-card",
            Width = TileWidth + 16,
            MinHeight = PictureHeight + 16,
            CornerRadius = new CornerRadius(3),
            BorderBrush = new SolidColorBrush(Colors.Separator),
            BorderThickness = new Thickness(1),
        };

        var offer = new Button
        {
            Name = "keep-preset",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            Content = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = "+",
                        FontSize = 28,
                        Foreground = Text.Muted,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = "Save this patch as a preset",
                        FontSize = Text.Body,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
            },
        };

        ToolTip.SetTip(offer, "Keep the patch on the canvas here, with the sounds and pictures it uses.");

        var name = new TextBox { Name = "preset-name", PlaceholderText = "Name", MaxLength = 60, FontSize = Text.Body };
        var hint = new TextBlock { FontSize = Text.Caption, Foreground = Text.Muted, TextWrapping = TextWrapping.Wrap };
        var save = new Button { Name = "save-preset", Content = "Save", FontSize = Text.Body, IsEnabled = false };
        var cancel = new Button { Content = "Cancel", FontSize = Text.Body, Background = Brushes.Transparent };

        var form = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(8),
            Children =
            {
                new TextBlock { Text = "Save this patch as", FontSize = Text.Body, FontWeight = FontWeight.SemiBold },
                name,
                hint,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { save, cancel } },
            },
        };

        card.Child = offer;

        offer.Click += (_, _) =>
        {
            name.Text = string.Empty;
            Checked();
            card.Child = form;
            name.Focus();
        };

        name.TextChanged += (_, _) => Checked();

        // Escape here backs out of the name rather than out of the gallery, and
        // Enter saves, as it does in every other box that names something.
        name.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter when save.IsEnabled:
                    Save();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    card.Child = offer;
                    offer.Focus();
                    e.Handled = true;
                    break;
            }
        };

        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => card.Child = offer;

        return card;

        void Checked()
        {
            var (allowed, words) = yours.Check(name.Text ?? string.Empty);

            save.IsEnabled = allowed;
            hint.Text = words;
            hint.IsVisible = words.Length > 0;
        }

        void Save()
        {
            var wanted = name.Text ?? string.Empty;

            if (!yours.Replaces(wanted))
            {
                if (yours.Keep(wanted)) saved();
                return;
            }

            // A preset is a file and there is no undo for replacing one, so the
            // form gives way to the question and comes back, name intact, on no.
            card.Child = new Border
            {
                Padding = new Thickness(8),
                VerticalAlignment = VerticalAlignment.Center,
                Child = Question.Row(
                    $"Replace “{wanted.Trim()}”?",
                    default,
                    "Replace the saved preset with this patch.",
                    "Leave the saved one alone.",
                    replace =>
                    {
                        if (replace)
                        {
                            if (yours.Keep(wanted)) saved();
                            else card.Child = form;
                        }
                        else
                        {
                            card.Child = form;
                            name.Focus();
                        }
                    }),
            };
        }
    }

    /// <summary>
    /// Gives a saved preset's tile a way to be deleted: a right-click, and then a
    /// question in the place its description was, because the file goes for good.
    /// </summary>
    private static void Removable(Button tile, PatchPreset preset, Action remove)
    {
        var words = (StackPanel)tile.Content!;
        var item = new MenuItem { Header = "Delete…" };

        tile.ContextFlyout = new MenuFlyout { Items = { item } };

        // The answers are buttons inside the tile, and a click bubbles from them to
        // it: unstopped, answering would open the preset it was asked about.
        words.AddHandler(Button.ClickEvent, (_, e) => e.Handled = true);

        item.Click += (_, _) =>
        {
            var last = words.Children.Count - 1;
            var description = words.Children[last];

            tile.Classes.Add(Asking);

            words.Children[last] = Question.Row(
                $"Delete “{preset.Name}”?",
                default,
                "Delete this preset and the file it is saved in.",
                "Keep it.",
                gone =>
                {
                    tile.Classes.Remove(Asking);

                    if (gone) remove();
                    else words.Children[last] = description;
                });
        };
    }

    private static Button Tile(
        PatchPreset preset,
        bool showing,
        Color accent,
        PresetThumbnails thumbnails,
        Action<PointedTile?>? pointedAt,
        Search search)
    {
        var image = new Image { Stretch = Stretch.UniformToFill };

        // Said only of a preset that would not draw. One that is only heard is
        // drawn as a speaker instead, and one with nothing wired is left bare.
        var words = new TextBlock
        {
            FontSize = Text.Small,
            Foreground = Text.Muted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // In the muted color the words are, through a ContentControl because
        // that is where a glyph takes its color from.
        var speaker = new ContentControl
        {
            Name = "sound-only",
            Foreground = Text.Muted,
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Content = new Viewbox { Width = 40, Height = 40, Child = Glyphs.Speaker() },
        };

        // What the preset says until its patch is open, and then what the patch says.
        var description = new TextBlock
        {
            Name = "description",
            Text = preset.Description,
            FontSize = Text.Caption,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = preset.Description.Length > 0,
        };

        // Who made it, once the patch is open and says.
        var credit = new TextBlock
        {
            Name = "credit",
            FontSize = Text.Caption,
            FontStyle = FontStyle.Italic,
            Foreground = Text.Muted,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsVisible = false,
        };

        // What it is tagged, once the patch is open and says.
        var tags = new TextBlock
        {
            Name = "tags",
            FontSize = Text.Caption,
            Foreground = new SolidColorBrush(accent),
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsVisible = false,
        };

        var picture = new Border
        {
            Name = "thumbnail",
            Width = TileWidth,
            Height = PictureHeight,
            Background = new SolidColorBrush(Colors.Canvas),
            ClipToBounds = true,
            CornerRadius = new CornerRadius(3),
            Child = new Grid { Children = { image, words, speaker } },
        };

        var tile = new Button
        {
            Name = "tile",
            Tag = preset,
            Width = TileWidth + 16,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Colors.Node),
            BorderBrush = showing ? new SolidColorBrush(accent) : Brushes.Transparent,
            BorderThickness = new Thickness(2),
            Content = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    picture,
                    new TextBlock { Text = preset.Name, FontSize = Text.Body, FontWeight = FontWeight.SemiBold },
                    description,
                    credit,
                    tags,
                },
            },
        };

        // A tile being asked about is not one to open: a click anywhere on it
        // while the question is up is a miss for the answers.
        tile.Click += (_, _) =>
        {
            if (!tile.Classes.Contains(Asking)) Dialog.Close<PatchPreset?>(tile, preset);
        };
        tile.PointerEntered += (_, _) => pointedAt?.Invoke(new PointedTile(preset, image));
        tile.PointerExited += (_, _) => pointedAt?.Invoke(null);

        _ = Fill(image, words, speaker, description, credit, tags, thumbnails.Of(preset), said => search.Credit(tile, said));

        return tile;
    }

    /// <summary>
    /// Puts the thumbnail on its tile when it is drawn. Awaited from the UI thread,
    /// so what follows the wait is on it too.
    /// </summary>
    private static async Task Fill(
        Image image,
        TextBlock words,
        Control speaker,
        TextBlock description,
        TextBlock credit,
        TextBlock tags,
        Task<Thumbnail> drawing,
        Action<Thumbnail> drawn)
    {
        var thumbnail = await drawing;

        drawn(thumbnail);

        if (thumbnail.Description is { } said)
        {
            description.Text = said;
            description.IsVisible = true;
        }

        if (thumbnail.Author is { } author)
        {
            credit.Text = "by " + author;
            credit.IsVisible = true;
        }

        if (thumbnail.Tags is { Count: > 0 } tagged)
        {
            tags.Text = string.Join(" · ", tagged);
            tags.IsVisible = true;
        }

        if (thumbnail.Pixels is null && thumbnail.Words == Thumbnail.SoundOnly.Words)
        {
            speaker.IsVisible = true;
            ToolTip.SetTip(speaker, thumbnail.Words);
        }
        else
        {
            words.Text = thumbnail.Words;
        }

        if (thumbnail.Pixels is { } pixels) image.Source = Bitmap(pixels);
    }

    /// <summary>
    /// The box that narrows the gallery, and the keys it answers — the module
    /// palette's, so that finding a preset is the same few keystrokes as finding
    /// a module.
    /// </summary>
    /// <remarks>
    /// Hides tiles rather than rebuilding them as the palette does, because a tile
    /// carries a picture drawn off the UI thread and may be playing one. A run is
    /// hidden with its heading, so no heading stands over nothing. The focus stays
    /// in the box the whole time and the arrows walk the tiles without taking it,
    /// so typing goes on narrowing while they move.
    /// </remarks>
    private sealed class Search
    {
        private readonly List<(TextBlock Heading, Panel Tiles)> runs = [];

        /// <summary>The preset tiles showing, in the order they are shown, which is what the arrows walk.</summary>
        private readonly List<Button> listed = [];

        /// <summary>Which of <see cref="listed"/> the arrows have reached, and what Enter would pick.</summary>
        private int highlighted = -1;

        /// <summary>What the highlighted tile was painted before it was highlighted, to be put back.</summary>
        private IBrush? unhighlighted;

        /// <summary>Who made each tile's patch and what it is tagged, known once the patch is open.</summary>
        private readonly Dictionary<Button, string[]> credits = [];

        public TextBox Box { get; } = new()
        {
            Name = "preset-filter",
            PlaceholderText = "Filter presets",
            FontSize = Text.Body,
            Margin = new Thickness(16, 8, 16, 2),
        };

        /// <summary>Whether presets from elsewhere follow, so the hint says it means the ones here.</summary>
        public bool Elsewhere { get; init; }

        public TextBlock Hint { get; } = new()
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = Text.Small,
            Foreground = Text.Muted,
            Margin = new Thickness(0, 10, 0, 0),
            IsVisible = false,
        };

        public Search()
        {
            Box.TextChanged += (_, _) => Apply();

            Box.KeyDown += (_, e) =>
            {
                switch (e.Key)
                {
                    case Key.Down:
                        Highlight(highlighted + 1);
                        break;

                    case Key.Up:
                        Highlight(highlighted - 1);
                        break;

                    case Key.Enter:
                        if (highlighted >= 0 && highlighted < listed.Count)
                            listed[highlighted].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;

                    // Empties the box; an empty one lets the key through, so a
                    // second press closes the gallery — one key, always a step back.
                    case Key.Escape when Box.Text is { Length: > 0 }:
                        Box.Text = string.Empty;
                        break;

                    default:
                        return;
                }

                e.Handled = true;
            };

            // Posted, because the dialog gives its sheet the focus as it goes up,
            // which is after this is put in it.
            Box.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => Box.Focus());
        }

        public void Add(TextBlock heading, Panel tiles) => runs.Add((heading, tiles));

        /// <summary>
        /// Makes <paramref name="tile"/> findable by its author and its tags, and
        /// narrows again if something is typed, since it may match now.
        /// </summary>
        public void Credit(Button tile, Thumbnail said)
        {
            string[] words = [.. said.Tags ?? [], .. said.Author is { } author ? [author] : Array.Empty<string>()];

            if (words.Length == 0) return;

            credits[tile] = words;

            if (Box.Text is not { Length: > 0 }) return;

            // The highlight stays on the tile the arrows left it on.
            var reached = highlighted >= 0 && highlighted < listed.Count ? listed[highlighted] : null;

            Apply();

            if (reached is not null && listed.IndexOf(reached) is > 0 and var index) Highlight(index);
        }

        /// <summary>
        /// Shows what matches the box. A preset matches on its name, the heading it
        /// is under, its author or a tag, but not on its description: every preset
        /// has a sentence of prose, and matching it turns a search for a common
        /// word into most of the gallery.
        /// </summary>
        public void Apply()
        {
            var text = Box.Text?.Trim() ?? string.Empty;

            Unhighlight();
            listed.Clear();

            var anything = false;

            foreach (var (heading, tiles) in runs)
            {
                var headed = Has(heading.Text, text);
                var any = false;

                foreach (var child in tiles.Children)
                {
                    // Anything that is not a preset is the card that saves one,
                    // which nobody is looking for by name.
                    var shown = child is Button { Tag: PatchPreset preset } button
                        ? headed || Has(preset.Name, text) || Credited(button, text)
                        : text.Length == 0 || headed;

                    child.IsVisible = shown;
                    any |= shown;

                    if (shown && child is Button { Tag: PatchPreset } tile) listed.Add(tile);
                }

                heading.IsVisible = tiles.IsVisible = any;
                anything |= any;
            }

            Hint.Text = Elsewhere ? $"No preset on this machine matches “{text}”." : $"Nothing matches “{text}”.";
            Hint.IsVisible = !anything;

            // The first match, so a few letters and Enter picks what you were
            // after without an arrow key.
            Highlight(0);
        }

        private bool Credited(Button tile, string text) =>
            text.Length > 0 && credits.TryGetValue(tile, out var words) && words.Any(word => Has(word, text));

        private static bool Has(string? said, string text) =>
            text.Length == 0 || (said?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false);

        /// <summary>
        /// Moves the highlight, clamped to the ends rather than wrapping, as the
        /// module palette's is.
        /// </summary>
        private void Highlight(int index)
        {
            if (listed.Count == 0) return;

            Unhighlight();

            highlighted = Math.Clamp(index, 0, listed.Count - 1);

            var tile = listed[highlighted];

            unhighlighted = tile.Background;
            tile.Background = new SolidColorBrush(Colors.Attention, 0.28);
            tile.BringIntoView();
        }

        private void Unhighlight()
        {
            if (highlighted >= 0 && highlighted < listed.Count) listed[highlighted].Background = unhighlighted;

            highlighted = -1;
        }
    }

    private static WriteableBitmap Bitmap(byte[] pixels)
    {
        var size = new PixelSize(PresetThumbnails.Width, PresetThumbnails.Height);
        var bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

        using var locked = bitmap.Lock();

        var stride = PresetThumbnails.Width * 4;

        for (var y = 0; y < PresetThumbnails.Height; y++)
            Marshal.Copy(pixels, y * stride, locked.Address + y * locked.RowBytes, stride);

        return bitmap;
    }
}
