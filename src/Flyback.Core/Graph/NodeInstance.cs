using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Flyback.Core.Graph.Extras;

namespace Flyback.Core.Graph;

/// <summary>One placed module: a node type, where it sits, and its knob values.</summary>
public sealed class NodeInstance
{
    /// <summary>
    /// The longest a module may be renamed to, so that a name pasted from
    /// somewhere else cannot make a patch file enormous or a header undrawable.
    /// </summary>
    public const int NameLimit = 26;

    /// <summary>
    /// How far from the origin a module may sit to either side, in graph units.
    /// Fifteen thousand holds the widest drawing in the box with no room to get
    /// lost in: framing clamps its zoom, so a module flung further cannot be got
    /// back. Held on the coordinate, so it is true however the module was placed.
    /// </summary>
    public const double Across = 7_500d;

    /// <summary>
    /// The same going down: ten thousand. Smaller than <see cref="Across"/>
    /// because a signal chain runs left to right, so patches grow across faster
    /// than they grow down.
    /// </summary>
    public const double Down = 5_000d;

    public required Guid Id { get; init; }

    public required string TypeId { get; init; }

    /// <summary>Where it sits. Always inside the canvas — see <see cref="Across"/>.</summary>
    public double X
    {
        get;
        set => field = Inside(value, Across);
    }

    /// <inheritdoc cref="X"/>
    public double Y
    {
        get;
        set => field = Inside(value, Down);
    }

    /// <summary>
    /// What this one has been renamed to, and null where it has not been, so an
    /// unrenamed module writes no name into the file. A label and nothing more:
    /// nothing is ever found by name. Set through <see cref="Rename"/>.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Whether this module is switched off: out of the signal path, handing on
    /// whatever is patched into it and nothing where nothing is.
    /// </summary>
    /// <remarks>
    /// Which socket it hands on is <see cref="NodeDef.Through"/>'s answer.
    /// Written into the file only while it is true, so a patch with nothing off
    /// reads as it always did.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Off { get; set; }

    /// <summary>
    /// Per-input constants, used for any input with nothing wired into it.
    /// Length always matches the definition's input count.
    /// </summary>
    public float[] InputValues { get; set; } = [];

    /// <summary>
    /// Everything this instance carries that is not a knob — a sequencer's
    /// notes, a quantiser's scale, a player's file — each under its
    /// <see cref="NodeExtra.Key"/>, and null for the modules that carry nothing.
    /// </summary>
    /// <remarks>
    /// One store rather than a field per kind (ADR-0061), holding
    /// <see cref="JsonNode"/> because the engine must round-trip a plugin's
    /// shape without understanding it. <see cref="StepsExtra.Of"/> and its
    /// siblings are what read a typed shape back out.
    /// </remarks>
    public Dictionary<string, JsonNode>? State { get; set; }

    /// <summary>
    /// What the extra called <paramref name="key"/> has stored here, or null
    /// where it has stored nothing.
    /// </summary>
    public JsonNode? StateOf(string key) =>
        State is { } held && held.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// Stores an extra's state under its key, making the dictionary on first use
    /// and taking it away with the last entry, so a module that carries nothing
    /// writes no empty object into the file.
    /// </summary>
    public void SetState(string key, JsonNode? value)
    {
        if (value is null)
        {
            State?.Remove(key);
            if (State is { Count: 0 }) State = null;

            return;
        }

        (State ??= [])[key] = value;
    }

    /// <summary>
    /// One coordinate held inside the canvas. Not a number at all becomes the
    /// origin rather than the near edge: NaN is a coordinate that was never
    /// computed, and it would poison every comparison looking for the corners.
    /// </summary>
    private static double Inside(double value, double edge) =>
        double.IsNaN(value) ? 0d : Math.Clamp(value, -edge, edge);

    /// <summary>
    /// What to call this one: the name it was given, or its definition's. The one
    /// way anything should ask, so a renamed module reads the same on the canvas,
    /// in the panel and in a compiler complaint.
    /// </summary>
    public string Title(NodeDef def) => Name ?? def.Name;

    /// <summary>
    /// Renames this module, or puts it back to its definition's name.
    /// </summary>
    /// <param name="def">The definition, which is what "no name" means.</param>
    /// <param name="to">
    /// The new name. Blank puts it back, and so does the definition's own name:
    /// storing that would leave a file claiming a name that changes under it the
    /// day the module is renamed in the catalog.
    /// </param>
    public void Rename(NodeDef def, string? to)
    {
        var trimmed = to?.Trim();

        if (trimmed is { Length: > NameLimit }) trimmed = trimmed[..NameLimit].TrimEnd();

        Name = string.IsNullOrEmpty(trimmed) || trimmed == def.Name ? null : trimmed;
    }

    /// <summary>
    /// A deep copy of this module, optionally with a fresh identity and shifted
    /// on the canvas.
    /// </summary>
    /// <remarks>
    /// Deep, so a clipboard holds the patch as it was rather than a view onto
    /// one still being edited. A copy must not need a definition — a fragment
    /// naming a module this build has no plugin for still has to keep its notes
    /// — and cloning <see cref="State"/> keeps them without knowing what they are.
    /// </remarks>
    /// <param name="id">The copy's identity, or null to keep this one's.</param>
    /// <param name="dx">How far to move it across.</param>
    /// <param name="dy">How far to move it down.</param>
    public NodeInstance Clone(Guid? id = null, double dx = 0d, double dy = 0d) => new()
    {
        Id = id ?? Id,
        TypeId = TypeId,
        Name = Name,
        Off = Off,
        X = X + dx,
        Y = Y + dy,
        InputValues = [.. InputValues],

        // Deep here too, and it has to be said explicitly: a JsonNode is a
        // mutable tree, so copying the dictionary alone would hand the copy
        // the very nodes the original goes on being edited through.
        State = State is { } held
            ? held.ToDictionary(entry => entry.Key, entry => entry.Value.DeepClone())
            : null,
    };

    /// <param name="id">
    /// What to call it, or null for a name nothing has had before. Supplying one
    /// says this is the same module as something that existed before, which is
    /// what lets a patch rebuilt from its source keep the memory, the positions
    /// and the selection of the one it replaces. Two nodes sharing an id is a
    /// patch that cannot be wired.
    /// </param>
    /// <param name="def"></param>
    /// <param name="x"></param>
    /// <param name="y"></param>
    public static NodeInstance Create(NodeDef def, double x, double y, Guid? id = null)
    {
        var node = new NodeInstance
        {
            Id = id ?? Guid.NewGuid(),
            TypeId = def.TypeId,
            X = x,
            Y = y,
            InputValues = [.. def.Inputs.Select(p => p.Default)],
        };

        // Whatever this module carries that is not a knob, each kind writing
        // under its own key — see NodeExtra. A module with none, which is nearly
        // all of them, leaves State null.
        foreach (var extra in def.Extras) extra.Seed(node);

        return node;
    }
}