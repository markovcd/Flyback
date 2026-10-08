using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Flyback.Editor.Inspect;

/// <summary>
/// Whether the inspector shows its plate whole or folded into the pinned header: whole at the
/// top of the scrolling rows, which scroll it away; folded once its name band has scrolled past,
/// and from the start on a panel too short for the plate and a couple of rows. Under a mouse on
/// a wide panel the plate is one short line, pinned above the rows, and never folds.
/// </summary>
/// <remarks>
/// The wash draws the band behind whichever is showing: as deep as the plate's name band, less
/// what has scrolled, or as deep as the header. On a short panel the description goes into the
/// header's menu rather than pushing the rows down.
/// </remarks>
internal sealed class InspectorFold
{
    /// <summary>How tall the inspector has to be for the plate to show whole at all.</summary>
    public const double Compact = 200;

    /// <summary>The least room the name keeps on the band with the buttons beside it.</summary>
    private const double NameRoom = 140;

    /// <summary>The band's own width before any button: its insets, the mark and the gaps beside it.</summary>
    private const double BandChrome = 24 + 26 + 20;

    /// <summary>What a glyph button takes along the band, and the gap between its groups.</summary>
    private const double ButtonRoom = 40, GroupGap = 10;

    /// <summary>Names the description a module's or a box's panel opens with, so a short panel can leave it to the menu.</summary>
    public const string DescriptionName = "panel-description";

    private readonly Control reading;
    private readonly ScrollViewer scroller;
    private readonly StackPanel rows;
    private readonly ContentControl plateHost;
    private readonly InspectorHeader header;
    private readonly ModuleWash wash;
    private readonly Panel panel;
    private readonly Decorator pinned;
    private readonly LastPress lastPress;
    private readonly PictureAside aside;

    /// <param name="reading">What holds the scroller and the header, and so how big the inspector is.</param>
    /// <param name="rows">What scrolls: the plate, then the panel.</param>
    /// <param name="pinned">Where the plate stands above the scroller when it is pinned there.</param>
    public InspectorFold(Control reading, ScrollViewer scroller, StackPanel rows, Decorator pinned, ContentControl plateHost, InspectorHeader header, ModuleWash wash, Panel panel, LastPress lastPress, PictureAside aside)
    {
        this.reading = reading;
        this.scroller = scroller;
        this.rows = rows;
        this.pinned = pinned;
        this.plateHost = plateHost;
        this.header = header;
        this.wash = wash;
        this.panel = panel;
        this.lastPress = lastPress;
        this.aside = aside;

        header.Aside = aside;
        aside.Changed += Ask;

        lastPress.Changed += Ask;

        scroller.ScrollChanged += (_, _) => Ask();
        reading.SizeChanged += (_, _) => Ask();
        plateHost.SizeChanged += (_, _) => Ask();
        plateHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty) Ask();
        };
        panel.Children.CollectionChanged += (_, _) => Ask();
    }

    private bool asked;

    /// <summary>
    /// Folds once the layout pass that asked has finished: folding moves the plate between
    /// panels, which cannot happen while one of them is being arranged.
    /// </summary>
    private void Ask()
    {
        if (asked) return;

        asked = true;
        Dispatcher.UIThread.Post(() =>
        {
            asked = false;
            Fold();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Whether the panel is too short for the plate, so the header stands in for it from the top.</summary>
    public bool IsCompact => reading.Bounds.Height is > 0 and < Compact;

    /// <summary>
    /// How <paramref name="plate"/> is laid out for the hand working it and the room it has: its
    /// buttons beside the name under a mouse only while the name keeps room of its own.
    /// </summary>
    public PlateLayout Layout(ModulePlate? plate)
    {
        if (lastPress.ByFinger) return PlateLayout.Touch;

        var rows = plate is null ? [] : PlateActions.Rows(plate);
        var needed = BandChrome + NameRoom + rows.Sum(row => row.Count) * ButtonRoom + Math.Max(0, rows.Count - 1) * GroupGap;

        return rows.Count > 0 && reading.Bounds.Width >= needed ? PlateLayout.Wide : PlateLayout.Narrow;
    }

    private void Fold()
    {
        var plate = plateHost.Content as ModulePlate;
        var layout = Layout(plate);

        aside.Holding(plate is not null);

        // Under a mouse on a wide panel the plate is one short line and never needs to fold.
        Pin(plate is not null && layout == PlateLayout.Wide);

        var compact = plate is not null && layout != PlateLayout.Wide && IsCompact;

        plateHost.IsVisible = !compact;
        rows.Margin = new Thickness(0, compact ? InspectorHeader.Depth : 0, 0, 0);

        foreach (var part in panel.Children)
            if (part.Name == DescriptionName) part.IsVisible = !compact;

        if (plate is null)
        {
            header.IsVisible = false;
            header.Show(null);
            wash.BandHeight = 0;
            wash.Below = 0;
            return;
        }

        // Folded after the panel has finished building, so its face and its buttons are on it.
        if (!ReferenceEquals(header.Plate, plate) || plate.Layout != layout) Lay(plate);

        header.ShowAside();
        plate.ShowAside(aside);

        var band = plate.Band;
        var tall = plateHost.Bounds.Height;

        if (layout == PlateLayout.Wide)
        {
            header.IsVisible = false;
            wash.BandHeight = band;
            wash.Below = tall;
            return;
        }

        var offset = scroller.Offset.Y;
        var pinnedIn = compact || (band > 0 && offset >= band);

        header.IsVisible = pinnedIn;

        wash.BandHeight = pinnedIn ? InspectorHeader.Depth : band - offset;
        wash.Below = compact ? InspectorHeader.Depth : Math.Max(pinnedIn ? InspectorHeader.Depth : 0, tall - offset);
    }

    private void Lay(ModulePlate? plate)
    {
        header.Show(plate);
        plate?.Lay(Layout(plate), aside, from => new PlateMenu(plate, plate.BeginRename ?? (() => { })).Open(from));
    }

    /// <summary>Stands the plate above the scroller, or puts it back at the head of what scrolls.</summary>
    private void Pin(bool above)
    {
        if (above == ReferenceEquals(pinned.Child, plateHost)) return;

        if (above)
        {
            rows.Children.Remove(plateHost);
            pinned.Child = plateHost;
        }
        else
        {
            pinned.Child = null;
            rows.Children.Insert(0, plateHost);
        }
    }
}
