using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Language.Values;

namespace Flyback.Engine.Language;

/// <summary>
/// The patch as the text builds it: modules placed, wires run and knobs set,
/// each refused where the text already did it once.
/// </summary>
internal sealed class Wiring(ModuleCatalog modules, Issues issues, SourceSites sites)
{
    public Patch Patch { get; } = new();

    /// <summary>Names each module for the statement that placed it, so a rebuild knows it again (ADR-0067).</summary>
    public NodeIdentity Identity { get; } = new();

    /// <summary>The line that wired each socket the text wires.</summary>
    private readonly Dictionary<(Guid Node, int Port), int> wired = [];

    /// <summary>The line that set each knob the text sets.</summary>
    private readonly Dictionary<(Guid Node, int Port), int> turned = [];

    private Guid coordinates;
    private Guid clock;

    /// <summary>The one clock a bare <c>t</c> reads, placed the first time it is asked for.</summary>
    public Placed Clock() => Shared(ref clock, NodeCatalog.TimeTypeId);

    /// <summary>The one Coordinates a bare <c>x</c> or <c>y</c> reads, placed the first time it is asked for.</summary>
    public Placed Coordinates() => Shared(ref coordinates, NodeCatalog.CoordTypeId);

    /// <summary>Whether a module is the clock or the coordinates, which are the whole patch's and in no group.</summary>
    public bool IsShared(Guid id) => id == clock || id == coordinates;

    /// <summary>Gives an Expression the formula the text wrote, and records where.</summary>
    public void Formula(Value value, string formula, int line, int column)
    {
        if (value is not Placed { Id: var id } || Patch.Find(id) is not { } node) return;

        node.SetState(FormulaExtra.StateKey, new JsonObject { [FormulaExtra.FormulaField] = formula });
        sites.Mention(new Site(line, column), id);
    }

    private Placed Shared(ref Guid held, string typeId)
    {
        if (held != Guid.Empty) return new Placed(held, modules.Require(typeId));

        // Named for what it is rather than for where it was first mentioned:
        // there is one clock and one pair of coordinates in a patch however many
        // lines reach for them, and moving the first mention should not make it
        // a different module.
        var node = NodeInstance.Create(modules.Require(typeId), 0d, 0d, NodeIdentity.FromName("~" + typeId));

        Patch.Nodes.Add(node);
        held = node.Id;

        return new Placed(held, modules.Require(typeId));
    }

    /// <param name="given">Where the text gives each socket its value, where it gives it one; the call's own place otherwise.</param>
    public Value Place(NodeDef def, IReadOnlyList<(int Port, Value Value)> inputs, int line, int column, IReadOnlyDictionary<int, Site>? given = null)
    {
        var node = NodeInstance.Create(def, 0d, 0d, Identity.Next());
        Patch.Nodes.Add(node);

        foreach (var (port, value) in inputs)
        {
            var (atLine, atColumn) = given is not null && given.TryGetValue(port, out var site) ? (site.Line, site.Column) : (line, column);

            if (value is Figure figure) Knob(node, def, port, figure, atLine, atColumn);
            else if (value is Dial dial) Link(node, def, port, dial, atLine, atColumn);
            else if (value is Named) issues.Complain(IssueCode.NotASignal, atLine, atColumn, $"'{def.Inputs[port].Name}' takes a number or a signal, not text.");
            else Feed(value, 0, node.Id, port, atLine, atColumn);
        }

        return new Placed(node.Id, def);
    }

    /// <summary>Wires one signal of <paramref name="value"/> into a socket.</summary>
    public void Feed(Value value, int index, Guid target, int port, int line, int column)
    {
        switch (value)
        {
            case Socket socket:
                Wire(socket.Id, socket.Port, target, port, line, column);
                break;

            case Placed placed:
                Wire(placed.Id, 0, target, port, line, column);
                break;

            case Several several when several.Items.Count > index:
                Feed(several.Items[index], 0, target, port, line, column);
                break;

            case Figure figure:
                if (Patch.Find(target) is { } node && modules.Get(node.TypeId) is { } def)
                    Knob(node, def, port, figure, line, column);

                break;

            case Dial dial:
                issues.Complain(IssueCode.PanelNotASignal, line, column,
                    $"'{dial.Word}' is a panel knob, which a socket follows where a number would go: 'freq: {dial.Word}'.");
                break;

            default:
                issues.Complain(IssueCode.NotASignal, line, column, "this is not a signal, so nothing can be wired from it.");
                break;
        }
    }

    /// <summary>
    /// A wire, refused where the text already wired that socket: the graph keeps
    /// one, and dropping the other without a word is a patch that reads wrong.
    /// </summary>
    private void Wire(Guid source, int output, Guid target, int port, int line, int column)
    {
        if (wired.TryGetValue((target, port), out var first))
        {
            if (Patch.IncomingTo(target, port) is { } wire && wire.SourceNode == source && wire.SourcePort == output) return;

            issues.Complain(IssueCode.WiredTwice, line, column,
                $"'{SocketName(target, port)}' is already wired on line {first}. A socket takes one wire.");
            return;
        }

        wired[(target, port)] = line;
        Patch.Connect(source, output, target, port);
    }

    /// <summary>A socket as the text would say it: the module's name, a dot and the socket.</summary>
    private string SocketName(Guid node, int port) =>
        Patch.Find(node) is { } instance && modules.Get(instance.TypeId) is { } def
            ? (sites.NameOf(node) ?? def.Name) + "." + def.Inputs[port].Name.Replace(' ', '_')
            : "this socket";

    /// <summary>
    /// Sets a knob, having first asked whether the socket has one and whether it
    /// reads numbers on the scale this one was written on.
    /// </summary>
    public void Knob(NodeInstance node, NodeDef def, int port, Figure figure, int line, int column)
    {
        if (!Settable(node, def, port, line, column)) return;

        var spec = def.Inputs[port];

        if (!Reads(spec, figure, line, column)) return;

        node.InputValues[port] = (float)figure.Amount;
        turned[(node.Id, port)] = line;

        // Only once a knob has actually been set, so that a refused number is
        // not offered as a place to write another one into. By the socket's own
        // spelling, because that is what a caller asking for it will have.
        sites.Write(node.Id, spec.Name.Replace(' ', '_'), figure.Where);
    }

    /// <summary>
    /// Has a socket follow a panel knob, over the range the text gives or, where
    /// it gives none, the socket's own.
    /// </summary>
    public void Link(NodeInstance node, NodeDef def, int port, Dial dial, int line, int column)
    {
        if (!Settable(node, def, port, line, column)) return;

        var spec = def.Inputs[port];
        ControlLink link;

        if (dial is { Low: { } low, High: { } high })
        {
            if (!Reads(spec, low, line, column) || !Reads(spec, high, line, column)) return;
            if (dial.Knee is { } bend && !Holds(spec, bend, line, column)) return;

            link = new ControlLink(dial.Control.Id, (float)low.Amount, (float)high.Amount)
            {
                Knee = dial.Knee is { } knee ? (float)knee.Amount : spec.Knee,
            };
        }
        else
        {
            link = ControlLink.For(dial.Control.Id, spec, node.InputValues[port]);
        }

        node.InputValues[port] = link.At(dial.Control.Value);
        ControlMap.Link(node, port, link);
        turned[(node.Id, port)] = line;
    }

    /// <summary>Whether a socket's knob may be set here, said where it may not.</summary>
    private bool Settable(NodeInstance node, NodeDef def, int port, int line, int column)
    {
        if (port < 0 || port >= def.Inputs.Count) return false;

        // One number per knob: a second would win without a word, and the first
        // would read as though it still counted.
        if (turned.TryGetValue((node.Id, port), out var first))
        {
            issues.Complain(IssueCode.KnobSetTwice, line, column,
                $"'{SocketName(node.Id, port)}' is already set on line {first}. A knob is set once.");
            return false;
        }

        var spec = def.Inputs[port];

        // A normalled socket is already carrying something and the stored value
        // is never read, so a number here would be a knob nobody can turn
        // (ADR-0050). The same refusal the assistant's set_knobs makes.
        if (modules.Normalled(spec) is { } driver)
        {
            issues.Complain(IssueCode.NormalledSocket, line, column,
                $"'{spec.Name}' is normalled to {driver} and has no knob. "
                + "Patch a Value in if it really should stand still.");
            return false;
        }

        return true;
    }

    /// <summary>Whether a number is written the way a socket reads, said where it is not.</summary>
    private bool Reads(PortSpec spec, Figure figure, int line, int column)
    {
        var wanted = figure.Style switch
        {
            NumberStyle.Note => PortDisplay.Note,
            NumberStyle.Duration => PortDisplay.Duration,
            _ => spec.Display,
        };

        if (wanted != spec.Display)
        {
            var written = figure.Style == NumberStyle.Note ? "a note" : "a length of time";

            issues.Complain(IssueCode.WrongLiteral, line, column, $"'{spec.Name}' is not read as {written}.");
            return false;
        }

        // A bare number on a socket that holds time is the trap the literal was
        // added to remove: the socket holds a power of ten, so "attack: 0.01"
        // meaning ten milliseconds is a second, and the drum is a drone. Nothing
        // about the value says which was meant, so the complaint says both.
        if (spec.Display == PortDisplay.Duration && figure.Style == NumberStyle.Plain)
        {
            issues.Complain(IssueCode.BareDuration, line, column,
                $"'{spec.Name}' is a length of time, and a bare number on one is a power of ten: "
                + $"{Number(figure.Amount)} means {spec.Format((float)figure.Amount)}. "
                + $"Write {Literal(figure.Amount)} if you meant {Number(figure.Amount)} seconds.");
            return false;
        }

        return Holds(spec, figure, line, column);
    }

    /// <summary>Whether a number fits the float a socket keeps, said where it does not.</summary>
    private bool Holds(PortSpec spec, Figure figure, int line, int column)
    {
        if (float.IsFinite((float)figure.Amount)) return true;

        issues.Complain(IssueCode.OutOfRange, line, column, $"'{spec.Name}' cannot hold that.");
        return false;
    }

    /// <summary>A number as it was written, for saying it back in a complaint.</summary>
    private static string Number(double value) =>
        value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// <paramref name="seconds"/> written as the literal that would mean it, for
    /// offering back to somebody who wrote a bare number meaning seconds.
    /// </summary>
    private static string Literal(double seconds)
    {
        var (scale, unit) = Math.Abs(seconds) switch
        {
            < 1e-3d => (1e6d, "us"),
            < 1d => (1e3d, "ms"),
            _ => (1d, "s"),
        };

        return Number(seconds * scale) + unit;
    }
}
