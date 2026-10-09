using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using Flyback.Core.Graph;
using Flyback.Editor.Controls;
using Flyback.Ui.Controls;
using Flyback.Editor.Site;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Gallery;

/// <summary>
/// Every preset as a card — a picture, its name and what it is for — under a heading
/// for each kind, narrowed from a column beside them and read about in one after.
/// </summary>
/// <remarks>
/// A click chooses a card and the column after says what it is; a double-click,
/// Enter or the column's button opens it. What it answers with is the preset, and
/// the caller decides what picking it means.
/// </remarks>
internal sealed class PresetGallery(PresetThumbnails thumbnails, IDialog dialog, LastPress lastPress, EditorHost host)
{
    /// <summary>The style class of a tile whose preset is being asked about deleting.</summary>
    public const string Asking = "asking";

    public const double TileWidth = 176;
    public const double PictureHeight = TileWidth * PresetThumbnails.Height / PresetThumbnails.Width;

    /// <summary>The color of what is chosen and of the button that opens it.</summary>
    public static Color Accent => Colors.Feedback;

    /// <summary>
    /// What a <see cref="PresetKind"/> is called where it heads its own run of
    /// presets, shouted like the module palette's section headings, being the same
    /// thing in the same kind of list.
    /// </summary>
    public static string Heading(PresetKind kind) => PresetKinds.Heading(kind);

    /// <summary>What heads the presets somebody saved, last of all.</summary>
    public const string YoursHeading = "YOUR PRESETS";

    /// <summary>What heads the presets the preset site offers, after everything on this machine.</summary>
    public const string SiteHeading = "ON THE PRESET SITE";

    /// <summary>
    /// The gallery of <paramref name="ordered"/>, which must already be grouped by
    /// kind, with the card of <paramref name="showing"/> chosen as the one on the canvas.
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
    /// <param name="kept">
    /// The shared presets opened before, listed in their place while the site does not
    /// answer. A tile of theirs answers with its <see cref="KeptPreset"/>.
    /// </param>
    /// <param name="prompting">
    /// Whether to show a card to start from a prompt, first. It answers with a
    /// <see cref="PromptedStart"/>.
    /// </param>
    /// <param name="use">What the button that opens the chosen card says.</param>
    public GalleryParts Build(
        IReadOnlyList<PatchPreset> ordered,
        PatchPreset? showing,
        Action<PointedTile?>? pointedAt = null,
        YourPresets? yours = null,
        PresetSite? site = null,
        KeptSharedPresets? kept = null,
        bool prompting = false,
        string use = "Use this preset")
    {
        var choice = new GalleryChoice { Elsewhere = site is not null, Typing = !lastPress.ByFinger };

        return new GalleryParts(choice.Box, BuildTiles);

        Control BuildTiles(Action<IPreset?> open)
        {
            choice.Open = open;

            var gallery = new StackPanel { Name = "gallery", Spacing = 8 };

            // Thumbnails still waiting to be drawn when the gallery closes are not drawn.
            var closing = new CancellationTokenSource();

            foreach (var run in ordered.GroupBy(preset => preset.Kind))
            {
                var section = Heading(run.Key);
                var (heading, count) = RunHeading(section);
                var tiles = new WrapPanel { ItemSpacing = 10, LineSpacing = 10 };

                foreach (var preset in run)
                    tiles.Children.Add(Tile(preset, section, thumbnails, pointedAt, choice, closing.Token).Button);

                gallery.Children.Add(heading);
                gallery.Children.Add(tiles);
                choice.Add(section, heading, count, tiles);
            }

            if (yours is not null) Yours(gallery, yours, thumbnails, pointedAt, choice, closing.Token);

            choice.Apply();
            choice.Choose(showing);

            var main = new StackPanel { Margin = new Thickness(20, 16, 20, 24), Spacing = 16 };

            if (prompting) main.Children.Add(PromptCard.Of(open));

            main.Children.Add(choice.Hint);
            main.Children.Add(gallery);

            if (site is not null)
            {
                var shared = new SiteRun(site, kept ?? KeptSharedPresets.None, choice.Box, dialog, open, host.InPage).View;

                main.Children.Add(shared);
                choice.AddElsewhere(SiteHeading, shared);
            }

            var layout = new GalleryLayout(choice, main, showing, use);

            layout.View.DetachedFromVisualTree += (_, _) => closing.Cancel();

            return layout.View;
        }
    }

    /// <summary>The line over a run: its heading, how many of it are shown, and a rule to the edge.</summary>
    private static (Grid Heading, TextBlock Count) RunHeading(string section)
    {
        var count = new TextBlock
        {
            FontSize = Text.Caption,
            Foreground = Text.Muted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0),
        };

        var rule = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Colors.Separator),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var heading = new Grid
        {
            Name = "run-heading",
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            Margin = new Thickness(0, 8, 0, 2),
        };

        var title = new TextBlock
        {
            Name = "run-title",
            Text = section,
            FontSize = Text.Caption,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Colors.Label),
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(count, 1);
        Grid.SetColumn(rule, 2);
        heading.Children.Add(title);
        heading.Children.Add(count);
        heading.Children.Add(rule);

        return (heading, count);
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
        PresetThumbnails thumbnails,
        Action<PointedTile?>? pointedAt,
        GalleryChoice choice,
        CancellationToken closing)
    {
        if (yours.PickOnly && yours.All().Count == 0) return;

        var (heading, count) = RunHeading(YoursHeading);
        var tiles = new WrapPanel { Name = "yours", ItemSpacing = 10, LineSpacing = 10 };

        gallery.Children.Add(heading);
        gallery.Children.Add(tiles);
        choice.Add(YoursHeading, heading, count, tiles);

        Fill();

        // Every change rebuilds the run from what is saved rather than editing
        // it, so a preset saved over another is one tile, in its place.
        void Fill()
        {
            choice.Drop(tiles);
            tiles.Children.Clear();

            if (!yours.PickOnly) tiles.Children.Add(KeepCard(yours, Fill));

            foreach (var preset in yours.All())
            {
                var card = Tile(preset, YoursHeading, thumbnails, pointedAt, choice, closing);

                if (!yours.PickOnly) Removable(card, preset, () =>
                {
                    // Whatever is being tried may be the one going.
                    pointedAt?.Invoke(null);
                    yours.Remove(preset);
                    Fill();
                });

                tiles.Children.Add(card.Button);
            }

            // Whatever is typed goes on narrowing the run this rebuilt.
            choice.Apply();
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
            CornerRadius = new CornerRadius(12),
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
            CornerRadius = new CornerRadius(12),
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
    private static void Removable(PresetCard card, PatchPreset preset, Action remove)
    {
        var tile = card.Button;
        var words = (Panel)card.Description.Parent!;
        var item = new MenuItem { Header = "Delete…" };

        tile.ContextFlyout = new MenuFlyout { Items = { item } };

        // The answers are buttons inside the tile, and a click bubbles from them to
        // it: unstopped, answering would choose the preset it was asked about.
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

    private static PresetCard Tile(
        PatchPreset preset,
        string section,
        PresetThumbnails thumbnails,
        Action<PointedTile?>? pointedAt,
        GalleryChoice choice,
        CancellationToken closing)
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
            Content = new Viewbox { Width = 36, Height = 36, Child = Glyphs.Speaker() },
        };

        var badges = new StackPanel
        {
            Name = "badges",
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        // What the preset says until its patch is open, and then what the patch says.
        var description = new TextBlock
        {
            Name = "description",
            Text = preset.Description,
            FontSize = Text.Caption,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            IsVisible = preset.Description.Length > 0,
        };

        var picture = new Border
        {
            Name = "thumbnail",
            Width = TileWidth,
            Height = PictureHeight,
            Background = new SolidColorBrush(Colors.Canvas),
            ClipToBounds = true,
            CornerRadius = new CornerRadius(7),
            Child = new Grid { Children = { image, words, speaker, badges } },
        };

        var tile = new Button
        {
            Name = "tile",
            Tag = preset,
            Width = TileWidth + 16,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(8, 8, 8, 10),
            CornerRadius = new CornerRadius(12),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1.5),
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    picture,
                    new StackPanel
                    {
                        Spacing = 2,
                        Margin = new Thickness(2, 0),
                        Children =
                        {
                            new TextBlock { Text = preset.Name, FontSize = Text.Body, FontWeight = FontWeight.SemiBold },
                            description,
                        },
                    },
                },
            },
        };

        var card = new PresetCard(preset, tile, image, words, speaker, description, badges, section);

        tile.PointerEntered += (_, _) => pointedAt?.Invoke(new PointedTile(preset, image));
        tile.PointerExited += (_, _) => pointedAt?.Invoke(null);

        choice.Track(card);

        _ = Say(card, thumbnails.Said(preset), choice);

        // Drawn once it is scrolled into sight: a gallery of hundreds draws the
        // dozen on screen, and never the ones nobody scrolls to.
        tile.EffectiveViewportChanged += Seen;

        return card;

        void Seen(object? sender, EffectiveViewportChangedEventArgs e)
        {
            if (tile.Bounds.Width <= 0 || !e.EffectiveViewport.Intersects(new Rect(tile.Bounds.Size))) return;

            tile.EffectiveViewportChanged -= Seen;
            _ = Fill(card, () => thumbnails.Of(preset, closing), choice, closing);
        }
    }

    /// <summary>
    /// Puts what the patch says of itself on its card, and makes the card findable
    /// by it. Awaited from the UI thread, so what follows the wait is on it too.
    /// </summary>
    private static async Task Say(PresetCard card, Task<Thumbnail> saying, GalleryChoice choice)
    {
        var thumbnail = await saying;

        if (thumbnail.Description is { } described)
        {
            card.Description.Text = described;
            card.Description.IsVisible = true;
        }

        Badges(card.Badges, thumbnail.Reaches);

        choice.Said(card, thumbnail);
    }

    /// <summary>A pill each for the halves of the Output a preset works with.</summary>
    public static void Badges(Panel into, (bool Picture, bool Sound)? reaches)
    {
        into.Children.Clear();

        if (reaches is not { } wired) return;

        // A patch with nothing wired yet is an Output waiting for both.
        var neither = !wired.Picture && !wired.Sound;

        if (wired.Sound || neither) into.Children.Add(Badge("Sound", PresetCard.Heard));
        if (wired.Picture || neither) into.Children.Add(Badge("Picture", PresetCard.Seen));
    }

    private static Border Badge(string label, Color dot) => new()
    {
        Name = "badge",
        CornerRadius = new CornerRadius(9),
        Background = new SolidColorBrush(Colors.Edge, 0.82),
        Padding = new Thickness(7, 2),
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Children =
            {
                new Avalonia.Controls.Shapes.Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = new SolidColorBrush(dot),
                    VerticalAlignment = VerticalAlignment.Center,
                },
                new TextBlock { Text = label, FontSize = Text.Micro, Foreground = new SolidColorBrush(Colors.Label) },
            },
        },
    };

    /// <summary>
    /// Puts the thumbnail on its card when it is drawn. Awaited from the UI thread,
    /// so what follows the wait is on it too.
    /// </summary>
    private static async Task Fill(PresetCard card, Func<Task<Thumbnail>> drawing, GalleryChoice choice, CancellationToken closing)
    {
        Thumbnail thumbnail;

        while (true)
        {
            try
            {
                thumbnail = await drawing();
                break;
            }
            catch (OperationCanceledException) when (!closing.IsCancellationRequested)
            {
                // Given up by another gallery that closed while it waited: asked again for this one.
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (thumbnail.Pixels is null && thumbnail.Still is null && thumbnail.Words == Thumbnail.SoundOnly.Words)
        {
            card.Speaker.IsVisible = true;
            ToolTip.SetTip(card.Speaker, thumbnail.Words);
        }
        else
        {
            card.Words.Text = thumbnail.Words;
        }

        if (thumbnail.Still is { } still) card.Picture.Source = new Bitmap(new MemoryStream(still));
        else if (thumbnail.Pixels is { } pixels) card.Picture.Source = Bitmap(pixels);

        choice.Drawn(card);
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
