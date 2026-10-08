using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Inspect;

/// <summary>
/// The plate folded to one line pinned at the top of the inspector: the block's mark and name,
/// switching it and deleting it, and the menu with everything else in it. Shown on a panel too
/// short for the plate, and on any panel once the plate has scrolled away.
/// </summary>
/// <remarks>
/// Painted nothing of its own: the band behind it is the wash's, drawn this deep while it shows.
/// Its buttons are 44 across because this is where a finger meets the panel on a phone.
/// </remarks>
internal sealed class InspectorHeader : Border
{
    /// <summary>How deep the header is, and so how deep the wash draws its band while it shows.</summary>
    public const double Depth = 48;

    private const double ButtonSize = 44;

    private readonly Grid line = new()
    {
        ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"),
        ColumnSpacing = 2,
        Margin = new Thickness(10, 0, 2, 0),
    };

    private ModulePlate? shown;
    private PlateMenu? menu;

    public InspectorHeader()
    {
        Name = "inspector-header";
        Height = Depth;
        VerticalAlignment = VerticalAlignment.Top;
        Background = Brushes.Transparent;
        BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 1, Color = Color.FromArgb(0x73, 0, 0, 0) });
        IsVisible = false;
        Child = line;
    }

    /// <summary>What renames the block, in place under a mouse and in a dialog under a finger.</summary>
    public Renamer? Renamer { get; set; }

    /// <summary>Whether the picture has stepped aside, which the header's own button asks for.</summary>
    public PictureAside? Aside { get; set; }

    /// <summary>The plate this header stands for, or null while it stands for none.</summary>
    public ModulePlate? Plate => shown;

    /// <summary>Folds <paramref name="plate"/> into the header, or empties it for null.</summary>
    public void Show(ModulePlate? plate)
    {
        if (ReferenceEquals(plate, shown)) return;

        shown = plate;
        line.Children.Clear();
        menu = null;

        if (plate?.Face is not { } face) return;

        menu = new PlateMenu(plate, Rename);

        var title = Title(face, plate);
        Grid.SetColumn(title, 0);
        line.Children.Add(title);

        var column = 1;

        // The two a slipped finger is most often after, straight on the line; the rest in the menu.
        foreach (var mirrored in new[]
                 {
                     PlateActions.Find(plate, "switch-modules", "open-group", "close-group"),
                     PlateActions.Find(plate, "delete-modules", "delete-group"),
                 })
        {
            if (mirrored is null || PlateActions.Of(mirrored.Name) is not { } said) continue;

            var button = Square("header-" + mirrored.Name, said.Glyph(), ToolTip.GetTip(mirrored) as string ?? said.Label);
            button.IsEnabled = mirrored.IsEnabled;
            button.Click += (_, _) => PlateActions.Press(mirrored);
            Grid.SetColumn(button, column++);
            line.Children.Add(button);
        }

        if (Aside is { } aside)
        {
            var spread = Square("header-picture", Glyphs.Spread(), "");
            spread.Click += (_, _) => aside.Toggle();
            Grid.SetColumn(spread, 3);
            line.Children.Add(spread);
        }

        var more = Square("header-more", Glyphs.Dots(), "Everything that can be done to it");
        more.Click += (_, _) => menu.Open(more);
        Grid.SetColumn(more, 4);
        line.Children.Add(more);

        ShowAside();
    }

    /// <summary>Turns the picture button to what pressing it would do now.</summary>
    public void ShowAside()
    {
        if (Aside is not { } aside || line.Children.OfType<Button>().FirstOrDefault(b => b.Name == "header-picture") is not { } button) return;

        button.Content = aside.Hidden ? Glyphs.Gather() : Glyphs.Spread();
        ToolTip.SetTip(button, PictureAsideWords.Tip(aside.Hidden));
    }

    /// <summary>The mark, the name and what kind of thing it is. The menu is ⋯'s; a double-tap on the name renames.</summary>
    private DockPanel Title(PlateFace face, ModulePlate plate)
    {
        // Docked rather than stacked, so what kind of thing it is gets the width that is left and trims to it.
        var row = new DockPanel { Name = "header-title", HorizontalSpacing = 8, VerticalAlignment = VerticalAlignment.Center };

        if (face.Glyph is { } glyph && face.MarkInk is { } ink)
            row.Children.Add(new Avalonia.Controls.Shapes.Path
            {
                Data = glyph,
                Stroke = ink,
                StrokeThickness = 2.2,
                StrokeLineCap = PenLineCap.Round,
                Stretch = Stretch.Uniform,
                Width = 22,
                Height = 22,
                VerticalAlignment = VerticalAlignment.Center,
                [DockPanel.DockProperty] = Dock.Left,
            });

        var named = Named(face, plate);
        DockPanel.SetDock(named, Dock.Left);
        row.Children.Add(named);

        row.Children.Add(new TextBlock
        {
            Text = face.Kind,
            FontSize = Text.Small,
            Foreground = plate.Quiet,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        return row;
    }

    /// <summary>The name, in a panel of its own so renaming can put a box where it stands.</summary>
    private Panel Named(PlateFace face, ModulePlate plate) => new StackPanel
    {
        VerticalAlignment = VerticalAlignment.Center,
        Children = { NameText(face.Title, plate.Ink, face.Naming is not null) },
    };

    /// <summary>The name, renamed by a double-tap where it can be, as the plate's is.</summary>
    private TextBlock NameText(string text, IBrush ink, bool renames)
    {
        var name = new TextBlock
        {
            Name = "header-name",
            Text = text,
            FontSize = Text.Title,
            FontWeight = FontWeight.SemiBold,
            Foreground = ink,
            Background = Brushes.Transparent,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        if (!renames) return name;

        name.Cursor = NameBox.Renaming;
        name.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            Rename();
        };

        return name;
    }

    /// <summary>Puts a box where the header's name is, as a double-click does on the plate.</summary>
    private void Rename()
    {
        if (shown is not { Face.Naming: { } naming } plate) return;

        if (line.Children.OfType<DockPanel>().FirstOrDefault(p => p.Name == "header-title") is not { } row
            || row.Children.OfType<Panel>().FirstOrDefault() is not { } named
            || named.Children.OfType<TextBlock>().FirstOrDefault() is not { } name)
            return;

        if (Renamer is null || shown.Face is not { } face) return;

        Renamer.Open(
            name,
            plate.Ink,
            $"{face.Title} · {face.Kind}",
            naming.Held(),
            naming.Fallback,
            naming.Limit,
            naming.Rename,
            naming.Held,
            () => NameText(naming.Held() ?? naming.Fallback, plate.Ink, renames: true),
            naming.Changed);
    }

    private static Button Square(string name, Control glyph, string tip)
    {
        var button = new Button
        {
            Name = name,
            Content = glyph,
            Width = ButtonSize,
            Height = ButtonSize,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTip.SetTip(button, tip);

        return button;
    }
}
