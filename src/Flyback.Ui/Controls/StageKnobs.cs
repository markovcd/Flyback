using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// The patch's knobs over a full-window picture, for playing and nothing else: no
/// names, no numbers, no menu, and tucked behind three dots bottom center until
/// reached for. The tip is the only text, and it says both.
/// </summary>
/// <remarks>
/// Knows nothing of where a knob's value goes. The owner lays the knobs out with <see cref="Show"/>,
/// moves one a controller turned with <see cref="Move"/>, and hears a hand turn one
/// through <see cref="Turning"/>.
/// </remarks>
public sealed class StageKnobs : TuckedAway
{
    private readonly WrapPanel row;

    private readonly Dictionary<Guid, StageKnob> knobs = [];

    private Patch? patch;

    private string shape = string.Empty;

    public StageKnobs()
        : this(new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            ItemSpacing = 14,
            LineSpacing = 14,
        })
    {
    }

    private StageKnobs(WrapPanel row)
        : base(row, HorizontalAlignment.Center)
    {
        this.row = row;
        Margin = new Thickness(12);

        // The picture under it takes the window on a double-click, and a knob
        // double-clicked back to the middle is not asking for that.
        DoubleTapped += (_, e) => e.Handled = true;
    }

    /// <summary>
    /// The transport opens on the same row, so its corner is kept clear on both sides
    /// and a long row wraps upward instead; a picture too narrow for that gets the lot.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var clear = TransportOverlay.Span - Margin.Left + 8;
        var margin = availableSize.Width >= 2 * clear + 3 * 54 ? new Thickness(clear, 0) : default;

        if (row.Margin != margin) row.Margin = margin;

        return base.MeasureOverride(availableSize);
    }

    /// <summary>A hand turned a knob: its id and where it now sits.</summary>
    public event Action<Guid, float>? Turning;

    /// <summary>Whether there is a knob to show at all.</summary>
    public bool Any => knobs.Count > 0;

    /// <summary>The knobs, for the tests that turn one.</summary>
    internal IReadOnlyDictionary<Guid, StageKnob> Knobs => knobs;

    /// <summary>
    /// Shows <paramref name="from"/>'s knobs where they sit, rebuilding only where one
    /// came or went.
    /// </summary>
    public void Show(Patch from)
    {
        patch = from;

        var controls = from.Controls ?? [];
        var now = string.Join('|', controls.Select(c => c.Id.ToString("N")));

        if (now != shape)
        {
            shape = now;
            row.Children.Clear();
            knobs.Clear();

            foreach (var control in controls)
            {
                var id = control.Id;
                var knob = new StageKnob();

                knob.Turned += turned => Turning?.Invoke(id, (float)turned);

                knobs[id] = knob;
                row.Children.Add(knob);
            }
        }

        foreach (var control in controls) Move(control.Id, control.Value);
    }

    /// <summary>Moves a knob without reporting it as turned.</summary>
    public void Move(Guid id, float value)
    {
        if (!knobs.TryGetValue(id, out var knob)) return;

        knob.Value = value;

        var name = patch?.Control(id)?.Name ?? "Knob";
        var reading = patch is null ? null : Reading(patch, id, value);

        ToolTip.SetTip(knob, $"{name}  {reading ?? value.ToString("0.00", CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// What a knob at <paramref name="value"/> reads as in the units of the one socket
    /// it drives, or null where it drives none or several.
    /// </summary>
    public static string? Reading(Patch patch, Guid id, float value)
    {
        var following = ControlMap.Following(patch, id).Take(2).ToList();

        return following is [var (node, port, link)] && NodeCatalog.Get(node.TypeId) is { } def && port < def.Inputs.Count
            ? def.Inputs[port].Format(link.At(value))
            : null;
    }
}

/// <summary>
/// A knob drawn as nothing but a ring, its travel and a pointer, on no face at all:
/// light strokes over dark ones, so it reads on a white frame and a black one alike.
/// </summary>
internal sealed class StageKnob : Knob
{
    /// <summary>How much wider the dark stroke under each light one is, either side together.</summary>
    private const double Halo = 3;

    private static readonly IBrush Light = new SolidColorBrush(Avalonia.Media.Colors.White);
    private static readonly IBrush Dark = new SolidColorBrush(Avalonia.Media.Colors.Black, 0.65);
    private static readonly IBrush FaintLight = new SolidColorBrush(Avalonia.Media.Colors.White, 0.45);
    private static readonly IBrush FaintDark = new SolidColorBrush(Avalonia.Media.Colors.Black, 0.4);

    private static readonly IPen Track = new Pen(FaintLight, 1.5, lineCap: PenLineCap.Round);
    private static readonly IPen TrackUnder = new Pen(FaintDark, 1.5 + Halo, lineCap: PenLineCap.Round);
    private static readonly IPen Arc = new Pen(Light, 2.5, lineCap: PenLineCap.Round);
    private static readonly IPen ArcUnder = new Pen(Dark, 2.5 + Halo, lineCap: PenLineCap.Round);

    public StageKnob()
    {
        Width = 40;
        Height = 40;
    }

    public override void Render(DrawingContext context)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - 3;

        // Filled with nothing, so a press on the middle still lands on the knob.
        context.DrawEllipse(Brushes.Transparent, null, center, radius, radius);

        var track = ArcGeometry(center, radius, Start, Sweep);

        context.DrawGeometry(null, TrackUnder, track);
        context.DrawGeometry(null, Track, track);

        var angle = Radians(Start + Sweep * Value);
        var pointer = new LineGeometry(Along(center, radius * 0.2, angle), Along(center, radius, angle));
        var travel = Value > 0.001 ? ArcGeometry(center, radius, Start, Sweep * Value) : null;

        // Every dark stroke before any light one, so no halo cuts across a line.
        context.DrawGeometry(null, ArcUnder, pointer);
        if (travel is not null) context.DrawGeometry(null, ArcUnder, travel);

        if (travel is not null) context.DrawGeometry(null, Arc, travel);
        context.DrawGeometry(null, Arc, pointer);
    }
}
