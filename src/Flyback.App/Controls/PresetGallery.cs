using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>A tile of the gallery: the preset it picks, and the picture it shows it by.</summary>
internal sealed record PointedTile(PatchPreset Preset, Image Picture);

/// <summary>
/// The presets somebody saved, and what the gallery may do about them: save the
/// patch on the canvas as another, and delete one.
/// </summary>
/// <param name="All">What is saved now, in the order to show it. Asked again after every change.</param>
/// <param name="Check">
/// Whether a name may be saved under, and a line saying so — why not, or that it
/// replaces one already saved.
/// </param>
/// <param name="Keep">Saves the patch on the canvas under a name <paramref name="Check"/> allowed. False where that failed.</param>
/// <param name="Remove">Deletes one.</param>
internal sealed record YourPresets(
    Func<IReadOnlyList<PatchPreset>> All,
    Func<string, (bool Allowed, string Hint)> Check,
    Func<string, bool> Keep,
    Action<PatchPreset> Remove);

/// <summary>
/// Every preset as a tile — a picture, its name and what it is for — under a heading
/// for each kind, to be shown in a dialog and picked from with a click.
/// </summary>
/// <remarks>
/// A dropdown stopped being the right shape once there were thirty presets to read
/// past. A tile is a button, so the keyboard walks them and Enter picks one; what it
/// answers with is the preset, and the caller decides what picking it means.
/// </remarks>
internal static class PresetGallery
{
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
    public static Control Build(
        IReadOnlyList<PatchPreset> ordered,
        PatchPreset? showing,
        PresetThumbnails thumbnails,
        Action<PointedTile?>? pointedAt = null,
        YourPresets? yours = null)
    {
        var gallery = new StackPanel { Name = "gallery", Spacing = 6, Margin = new Thickness(16, 8, 16, 16) };

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
                tiles.Children.Add(Tile(preset, preset == showing, Colors.PresetAccent(preset.Kind), thumbnails, pointedAt));

            gallery.Children.Add(tiles);
        }

        if (yours is not null) Yours(gallery, yours, showing, thumbnails, pointedAt);

        return gallery;
    }

    /// <summary>
    /// The run of presets somebody saved: a card to save the patch on the canvas as
    /// one, and then a tile each. Shown with none saved, since the card is how the
    /// first one gets there.
    /// </summary>
    private static void Yours(
        StackPanel gallery,
        YourPresets yours,
        PatchPreset? showing,
        PresetThumbnails thumbnails,
        Action<PointedTile?>? pointedAt)
    {
        var accent = Colors.Feedback;

        gallery.Children.Add(new TextBlock
        {
            Text = YoursHeading,
            FontSize = Text.Caption,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(accent),
            Margin = new Thickness(0, 10, 0, 2),
        });

        var tiles = new WrapPanel { Name = "yours", ItemSpacing = 8, LineSpacing = 8 };

        gallery.Children.Add(tiles);

        Fill();

        // Every change rebuilds the run from what is saved rather than editing
        // it, so a preset saved over another is one tile, in its place.
        void Fill()
        {
            tiles.Children.Clear();
            tiles.Children.Add(KeepCard(yours, Fill));

            foreach (var preset in yours.All())
            {
                var tile = Tile(preset, preset == showing, accent, thumbnails, pointedAt);

                Removable(tile, preset, () =>
                {
                    // Whatever is being tried may be the one going.
                    pointedAt?.Invoke(null);
                    yours.Remove(preset);
                    Fill();
                });

                tiles.Children.Add(tile);
            }
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
            if (yours.Keep(name.Text ?? string.Empty)) saved();
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

        item.Click += (_, _) =>
        {
            var last = words.Children.Count - 1;
            var description = words.Children[last];

            words.Children[last] = Question.Row(
                $"Delete “{preset.Name}”?",
                default,
                "Delete this preset and the file it is saved in.",
                "Keep it.",
                gone =>
                {
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
        Action<PointedTile?>? pointedAt)
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
                    new TextBlock
                    {
                        Text = preset.Description,
                        FontSize = Text.Caption,
                        Foreground = Text.Muted,
                        TextWrapping = TextWrapping.Wrap,
                        IsVisible = preset.Description.Length > 0,
                    },
                },
            },
        };

        tile.Click += (_, _) => Dialog.Close<PatchPreset?>(tile, preset);
        tile.PointerEntered += (_, _) => pointedAt?.Invoke(new PointedTile(preset, image));
        tile.PointerExited += (_, _) => pointedAt?.Invoke(null);

        _ = Fill(image, words, speaker, thumbnails.Of(preset));

        return tile;
    }

    /// <summary>
    /// Puts the thumbnail on its tile when it is drawn. Awaited from the UI thread,
    /// so what follows the wait is on it too.
    /// </summary>
    private static async Task Fill(Image image, TextBlock words, Control speaker, Task<Thumbnail> drawing)
    {
        var thumbnail = await drawing;

        if (thumbnail == Thumbnail.SoundOnly)
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
