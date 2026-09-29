using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Flyback.App.Canvas;
using Flyback.App.Controls;
using Flyback.Core.Graph;

namespace Flyback.App.Inspect;

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
/// Pinned above the scroller rather than the first thing in it. What a block is
/// and the buttons that act on it are wanted at every scroll position; the reading
/// is what moves.
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
        new SolidColorBrush(Avalonia.Media.Colors.White, 0.7);

    /// <summary>
    /// The header band's contents: the name, and under it what kind of thing this
    /// is. Both on the band, because the band is what says which block this is.
    /// </summary>
    private readonly StackPanel name = new() { Margin = new Thickness(Inset, 6, Inset, 7) };

    private readonly StackPanel rest = new()
    {
        Spacing = 4,
        Margin = new Thickness(Inset, 6, Inset, 10),
    };

    private ModulePlate((IBrush Ink, IBrush Quiet) inks)
    {
        Ink = inks.Ink;
        Quiet = inks.Quiet;

        Child = new StackPanel { Children = { name, rest } };
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
    public double Band => name.Bounds.Bottom + name.Margin.Bottom;

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
