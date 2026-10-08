using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Flyback.Editor.Inspect;

/// <summary>
/// Whether the inspector shows its plate whole or folded into the pinned header: whole at the
/// top of the scrolling rows, which scroll it away; folded once its name band has scrolled past,
/// and from the start on a panel too short for the plate and a couple of rows.
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

    /// <summary>Names the description a module's or a box's panel opens with, so a short panel can leave it to the menu.</summary>
    public const string DescriptionName = "panel-description";

    private readonly Control reading;
    private readonly ScrollViewer scroller;
    private readonly Control rows;
    private readonly ContentControl plateHost;
    private readonly InspectorHeader header;
    private readonly ModuleWash wash;
    private readonly Panel panel;

    /// <param name="reading">What holds the scroller and the header, and so how tall the inspector is.</param>
    /// <param name="rows">What scrolls: the plate, then the panel.</param>
    public InspectorFold(Control reading, ScrollViewer scroller, Control rows, ContentControl plateHost, InspectorHeader header, ModuleWash wash, Panel panel)
    {
        this.reading = reading;
        this.scroller = scroller;
        this.rows = rows;
        this.plateHost = plateHost;
        this.header = header;
        this.wash = wash;
        this.panel = panel;

        scroller.ScrollChanged += (_, _) => Fold();
        reading.SizeChanged += (_, _) => Fold();
        plateHost.SizeChanged += (_, _) => Fold();
        plateHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty) Fold();
        };
        panel.Children.CollectionChanged += (_, _) => Fold();
    }

    /// <summary>Whether the panel is too short for the plate, so the header stands in for it from the top.</summary>
    public bool IsCompact => reading.Bounds.Height is > 0 and < Compact;

    private void Fold()
    {
        var plate = plateHost.Content as ModulePlate;
        var compact = plate is not null && IsCompact;

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

        var offset = scroller.Offset.Y;
        var band = plate.Band;
        var tall = plateHost.Bounds.Height;
        var pinned = compact || (band > 0 && offset >= band);

        // After the panel has finished building, since its face and its buttons are put on it
        // after it is shown.
        if (!ReferenceEquals(header.Plate, plate))
            Dispatcher.UIThread.Post(() => header.Show(plateHost.Content as ModulePlate), DispatcherPriority.Background);

        header.IsVisible = pinned;

        wash.BandHeight = pinned ? InspectorHeader.Depth : band - offset;
        wash.Below = compact ? InspectorHeader.Depth : Math.Max(pinned ? InspectorHeader.Depth : 0, tall - offset);
    }
}
