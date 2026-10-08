using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Flyback.Editor.Canvas;
using Flyback.Ui.Controls;
using Flyback.Core.Graph;

namespace Flyback.Editor.Inspect;

/// <summary>
/// What stands at the head of the inspector: the block's name and what kind of
/// thing it is, and under them what can be done to it.
/// </summary>
/// <remarks>
/// Nothing here is painted. The band behind the name, the background under it and
/// the mark are all <see cref="ModuleWash"/>'s, which is behind the whole panel:
/// one surface fading out once, where a band drawn here would end on a line of its
/// own. What this holds is the layout and the inks that go with that surface.
/// <para>
/// The first thing in the scroller, so the reading can always be reached; once it has
/// scrolled away, or where there is no room for it, <see cref="InspectorHeader"/> stands
/// in for it (ADR-0187).
/// </para>
/// <para>
/// Given the unselected colors although what it shows is always selected. The head
/// of the panel is here to say which block this is, and a ring that was never off
/// would say nothing.
/// </para>
/// </remarks>
internal sealed class ModulePlate : Decorator
{
    /// <summary>
    /// How far what stands on the plate keeps off its sides, which is the panel's
    /// own inset: the name and the description below it are read down one edge.
    /// </summary>
    private const double Inset = 12;

    /// <summary>
    /// How far a quiet line is pulled back into what is behind it, where a skin asks
    /// for text colored from its background.
    /// </summary>
    private const double QuietFade = 0.35;

    /// <summary>
    /// The class the box that a name is typed into is given, so <see cref="Naming"/>
    /// can find it.
    /// </summary>
    public const string NameBoxClass = "naming";

    /// <summary>
    /// Undresses the box a name is renamed in: no fill, no border, no focus ring.
    /// </summary>
    /// <remarks>
    /// Local values do not reach it. The theme's own styles set the fill and the
    /// ring on the border inside the box's template, so the only thing that can say
    /// otherwise is a style reaching the same part. What is wanted is a name with a
    /// caret in it — a box appearing where a name was is a second thing to look at
    /// on the one line that says which block this is.
    /// </remarks>
    public static Style Naming()
    {
        var style = new Style(x => x
            .OfType<TextBox>()
            .Class(NameBoxClass)
            .Template()
            .OfType<Border>()
            .Name("PART_BorderElement"));

        style.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(0)));

        return style;
    }

    /// <summary>
    /// What the line under the name is written in. White pulled back rather than the
    /// canvas's label gray, because it stands on the band beside the name rather than
    /// on the body: one surface, one ink, one of them quieter.
    /// </summary>
    private static readonly IBrush Label =
        new ImmutableSolidColorBrush(Avalonia.Media.Colors.White, 0.7);

    /// <summary>
    /// The header band's contents: the name, and under it what kind of thing this
    /// is. Both on the band, because the band is what says which block this is.
    /// </summary>
    private readonly StackPanel name = new() { VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The band: the mark, the name, and on a wide panel under a mouse the buttons too.</summary>
    private readonly Grid band = new()
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        ColumnSpacing = 10,
        Margin = new Thickness(Inset, 6, Inset, 7),
    };

    private readonly ContentControl mark = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly StackPanel rest = new()
    {
        Spacing = 4,
        Margin = new Thickness(Inset, 6, Inset, 10),
    };

    /// <summary>A finger's strip of worded buttons in place of the rows of glyphs.</summary>
    private readonly UniformGrid strip = new()
    {
        Name = "plate-strip",
        Columns = 5,
        ColumnSpacing = 6,
        Margin = new Thickness(10, 0, 10, 8),
        IsVisible = false,
    };

    private ModulePlate((IBrush Ink, IBrush Quiet) inks)
    {
        Ink = inks.Ink;
        Quiet = inks.Quiet;

        Grid.SetColumn(mark, 0);
        Grid.SetColumn(name, 1);
        band.Children.Add(mark);
        band.Children.Add(name);

        Child = new StackPanel { Children = { band, strip, rest } };
    }

    /// <summary>How the plate is laid out at the moment, once <see cref="Lay"/> has been asked.</summary>
    public PlateLayout? Layout { get; private set; }

    /// <summary>Puts a box where the name is, as a double-click on it does. Null where the block cannot be renamed.</summary>
    public Action? BeginRename { get; set; }

    /// <summary>
    /// Lays the plate out for how it is being worked: the rows beside the name under a mouse on
    /// a wide panel, under it on a narrow one, and a strip of worded buttons under a finger.
    /// </summary>
    /// <param name="openMenu">Opens the menu with everything in it, from a finger's More.</param>
    public void Lay(PlateLayout layout, PictureAside aside, Action<Control> openMenu)
    {
        if (Layout == layout) return;

        Layout = layout;

        if (Face is { Glyph: { } glyph, MarkInk: { } ink })
            mark.Content = new Avalonia.Controls.Shapes.Path
            {
                Data = glyph,
                Stroke = ink,
                StrokeThickness = 2.2,
                StrokeLineCap = PenLineCap.Round,
                Stretch = Stretch.Uniform,
                Width = layout == PlateLayout.Wide ? 26 : 24,
                Height = layout == PlateLayout.Wide ? 26 : 24,
            };

        (rest.Parent as Panel)?.Children.Remove(rest);

        foreach (var row in rest.Children.OfType<StackPanel>()) row.HorizontalAlignment = HorizontalAlignment.Left;

        switch (layout)
        {
            case PlateLayout.Wide:
                // One row of glyphs on the band, beside the name, in groups.
                rest.IsVisible = true;
                rest.Orientation = Orientation.Horizontal;
                rest.Spacing = 10;
                rest.Margin = new Thickness(0);
                rest.VerticalAlignment = VerticalAlignment.Center;
                foreach (var row in rest.Children.OfType<StackPanel>()) row.Margin = new Thickness(0);
                Grid.SetColumn(rest, 2);
                band.Children.Add(rest);
                strip.IsVisible = false;
                break;

            case PlateLayout.Narrow:
                rest.IsVisible = true;
                rest.Orientation = Orientation.Vertical;
                rest.Spacing = 4;
                rest.Margin = new Thickness(Inset, 6, Inset, 10);
                rest.VerticalAlignment = VerticalAlignment.Top;
                foreach (var row in rest.Children.OfType<StackPanel>()) row.Margin = new Thickness(0, 0, 0, 6);
                ((StackPanel)Child!).Children.Add(rest);
                strip.IsVisible = false;
                break;

            case PlateLayout.Touch:
                // Kept in the tree, hidden: the strip and the menu press these.
                rest.IsVisible = false;
                ((StackPanel)Child!).Children.Add(rest);
                Strip(aside, openMenu);
                break;
        }
    }

    /// <summary>Turns the strip's picture button to what pressing it would do now.</summary>
    public void ShowAside(PictureAside aside)
    {
        if (strip.Children.OfType<Button>().FirstOrDefault(b => b.Name == "strip-picture") is not { } button) return;

        var hidden = aside.Hidden;

        button.Content = PlateActions.Words(hidden ? Glyphs.Gather() : Glyphs.Spread(), PictureAsideWords.Label(hidden));
        ToolTip.SetTip(button, PictureAsideWords.Tip(hidden));
    }

    /// <summary>The strip a finger gets: switching, the second most wanted, deleting, the picture, and More.</summary>
    private void Strip(PictureAside aside, Action<Control> openMenu)
    {
        strip.Children.Clear();

        foreach (var names in new[]
                 {
                     new[] { "switch-modules", "switch-group" },
                     ["duplicate-modules", "open-group", "close-group"],
                     ["delete-modules", "delete-group"],
                 })
        {
            if (PlateActions.Find(this, names) is not { } mirrored || PlateActions.Of(mirrored.Name) is not { } said) continue;

            var button = PlateActions.Worded("strip-" + mirrored.Name, said.Glyph(), said.Label, ToolTip.GetTip(mirrored) as string, 52);
            button.IsEnabled = mirrored.IsEnabled;
            button.Click += (_, _) => PlateActions.Press(mirrored);
            strip.Children.Add(button);
        }

        var picture = PlateActions.Worded("strip-picture", Glyphs.Spread(), PictureAsideWords.Label(false), PictureAsideWords.Tip(false), 52);
        picture.Click += (_, _) => aside.Toggle();
        strip.Children.Add(picture);

        var more = PlateActions.Worded("strip-more", Glyphs.Dots(), "More", "Everything else that can be done to it", 52);
        more.Click += (_, _) => openMenu(more);
        strip.Children.Add(more);

        strip.IsVisible = true;
    }

    /// <summary>
    /// Where the name goes. A panel rather than the control itself, because renaming
    /// swaps a box in for the name where it stands.
    /// </summary>
    public Panel Named => name;

    /// <summary>Where everything under the name goes, which is the body.</summary>
    public Panel Under => rest;

    /// <summary>
    /// How deep the band behind the name runs: what stands in it and the room round
    /// it. The wash draws that band and reads this — the panel sets the name at the
    /// size it wants, and nothing else has a number that has to agree.
    /// </summary>
    public double Band => band.Bounds.Bottom + band.Margin.Bottom;

    /// <summary>What the plate says about its block, for what stands in for it on a short panel.</summary>
    public PlateFace? Face { get; set; }

    /// <summary>What the name is written in.</summary>
    public IBrush Ink { get; }

    /// <summary>What a quieter line under it is written in.</summary>
    public IBrush Quiet { get; }

    /// <summary>A module, from its category's accent or from the palette its author gave it.</summary>
    public static ModulePlate Of(NodeDef def) => new(Inks(def, ModuleSkins.Of(def)));

    /// <summary>
    /// A box, which belongs to no category and wears the grays the canvas draws one
    /// in.
    /// </summary>
    public static ModulePlate Box() => new((Brushes.White, Label));

    /// <summary>
    /// White over the label gray, which is what the canvas writes on a block — or,
    /// where the skin asks for it, each line colored from the background it
    /// covers. Read off <see cref="ModuleBackdrop"/> rather than worked out again
    /// here, and from the panel's own picture (ADR-0118), so the plate can never
    /// pick a direction the canvas's own title would not.
    /// </summary>
    private static (IBrush Ink, IBrush Quiet) Inks(NodeDef def, ModuleSkin? skin)
    {
        if (skin is not { ContrastText: true }) return (Brushes.White, Label);

        var backdrop = ModuleBackdrop.Of(def, selected: false, panel: true);
        var (top, floor) = backdrop.HeaderGradient;
        var lift = backdrop.HeaderLift;

        return (
            NodeSkin.Ink(top, floor, lift, 0),
            NodeSkin.Ink(top, floor, lift, QuietFade));
    }

    /// <summary>
    /// The ink given to a line standing on the body, just under the header —
    /// the description and the normalled note — where the skin asks for
    /// contrast text. Plain <see cref="Text.Muted"/> otherwise, which is what
    /// those rows have always been written in.
    /// </summary>
    /// <remarks>
    /// Taken from the body's own top color rather than a point down the wash,
    /// which fades out by about the middle of the panel (<see cref="NodeSkin.Fade"/>)
    /// — exact enough for a couple of rows of muted prose near the top of it.
    /// </remarks>
    public static IBrush BodyQuiet(NodeDef def)
    {
        if (ModuleSkins.Of(def) is not { ContrastText: true }) return Text.Muted;

        var backdrop = ModuleBackdrop.Of(def, selected: false, panel: true);

        return NodeSkin.Ink(backdrop.BodyInk, backdrop.BodyInk, backdrop.BodyLift, QuietFade);
    }
}
