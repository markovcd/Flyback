using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

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

    /// <summary>
    /// The gallery of <paramref name="ordered"/>, which must already be grouped by
    /// kind, with the tile of <paramref name="showing"/> outlined as the one on the
    /// canvas.
    /// </summary>
    /// <param name="pointedAt">
    /// Told the preset whose tile the pointer has come to rest on, and null when it
    /// leaves one — what the caller auditions.
    /// </param>
    public static Control Build(
        IReadOnlyList<PatchPreset> ordered,
        PatchPreset? showing,
        PresetThumbnails thumbnails,
        Action<PatchPreset?>? pointedAt = null)
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
                tiles.Children.Add(Tile(preset, preset == showing, thumbnails, pointedAt));

            gallery.Children.Add(tiles);
        }

        return gallery;
    }

    private static Button Tile(
        PatchPreset preset,
        bool showing,
        PresetThumbnails thumbnails,
        Action<PatchPreset?>? pointedAt)
    {
        var image = new Image { Stretch = Stretch.UniformToFill };

        // Said while the frame is still being drawn as well as when there is none
        // to draw, so a tile is never a bare dark rectangle that might be a fault.
        var words = new TextBlock
        {
            FontSize = Text.Small,
            Foreground = Text.Muted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var picture = new Border
        {
            Name = "thumbnail",
            Width = TileWidth,
            Height = PictureHeight,
            Background = new SolidColorBrush(Colors.Canvas),
            ClipToBounds = true,
            CornerRadius = new CornerRadius(3),
            Child = new Grid { Children = { image, words } },
        };

        var accent = Colors.PresetAccent(preset.Kind);

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
        tile.PointerEntered += (_, _) => pointedAt?.Invoke(preset);
        tile.PointerExited += (_, _) => pointedAt?.Invoke(null);

        _ = Fill(image, words, thumbnails.Of(preset));

        return tile;
    }

    /// <summary>
    /// Puts the thumbnail on its tile when it is drawn. Awaited from the UI thread,
    /// so what follows the wait is on it too.
    /// </summary>
    private static async Task Fill(Image image, TextBlock words, Task<Thumbnail> drawing)
    {
        var thumbnail = await drawing;

        words.Text = thumbnail.Words;

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
