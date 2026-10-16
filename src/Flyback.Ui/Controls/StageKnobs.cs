using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Flyback.Core.Graph;
using Flyback.Host;

namespace Flyback.Ui.Controls;

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
    private readonly KnobArrangement row;

    private readonly Dictionary<Guid, StageKnob> knobs = [];

    private readonly Dictionary<Guid, StageSwitch> switches = [];

    private Patch? patch;

    private string shape = string.Empty;

    public StageKnobs()
        : this(new KnobArrangement
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            ItemSpacing = 14,
            LineSpacing = 14,
        })
    {
    }

    private StageKnobs(KnobArrangement row)
        : base(row, HorizontalAlignment.Center)
    {
        this.row = row;
        Margin = new Thickness(12);

        // The picture under it takes the window on a double-click, and a knob
        // double-clicked back to the middle is not asking for that.
        DoubleTapped += (_, e) => e.Handled = true;
    }

    /// <summary>A hand turned a knob: its id and where it now sits.</summary>
    public event Action<Guid, float>? Turning;

    /// <summary>The hand came off a knob it had turned.</summary>
    public event Action<Guid>? TurnEnded;

    /// <summary>The fixed grid the knobs stand in, or null to wrap them to the picture's width.</summary>
    public KnobGrid? KnobGrid
    {
        get => row.Grid;
        set => row.Grid = value;
    }

    /// <summary>Whether there is a knob to show at all.</summary>
    public bool Any => knobs.Count + switches.Count > 0;

    /// <summary>The knobs, for the tests that turn one.</summary>
    internal IReadOnlyDictionary<Guid, StageKnob> Knobs => knobs;

    /// <summary>The switches and buttons, for the tests that press one.</summary>
    internal IReadOnlyDictionary<Guid, StageSwitch> Switches => switches;

    /// <summary>
    /// Shows <paramref name="from"/>'s knobs where they sit, rebuilding only where one
    /// came or went.
    /// </summary>
    public void Show(Patch from)
    {
        patch = from;

        var controls = from.Controls ?? [];
        var now = string.Join('|', controls.Select(c => $"{c.Id:N}{c.Kind}"));

        if (now != shape)
        {
            shape = now;
            row.Children.Clear();
            knobs.Clear();
            switches.Clear();

            foreach (var control in controls)
            {
                var id = control.Id;
                ITurnable played;

                if (control.Kind == ControlKind.Knob)
                {
                    var knob = new StageKnob();
                    knobs[id] = knob;
                    row.Children.Add(knob);
                    played = knob;
                }
                else
                {
                    var flip = new StageSwitch { Momentary = control.Kind == ControlKind.Button };
                    switches[id] = flip;
                    row.Children.Add(flip);
                    played = flip;
                }

                played.Turned += turned => Turning?.Invoke(id, (float)turned);
                played.Released += () => TurnEnded?.Invoke(id);
            }
        }

        foreach (var control in controls) Move(control.Id, control.Value);
    }

    /// <summary>Moves a knob without reporting it as turned.</summary>
    public void Move(Guid id, float value)
    {
        Control? knob = knobs.TryGetValue(id, out var turned) ? turned : switches.GetValueOrDefault(id);

        if (knob is not ITurnable played) return;

        played.Value = value;

        var control = patch?.Control(id);
        var reading = patch is null ? null : Reading(patch, id, value);

        ToolTip.SetTip(knob, $"{control?.Name ?? "Knob"}  {reading ?? Position(control?.Kind ?? ControlKind.Knob, value)}");
    }

    /// <summary>Where a knob is, as a number, or a switch or a button, as on or off.</summary>
    internal static string Position(ControlKind kind, float value) =>
        kind == ControlKind.Knob ? value.ToString("0.00", CultureInfo.InvariantCulture) : value >= 0.5f ? "on" : "off";

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
