using Flyback.Core.Compile;
using Flyback.Core.Graph.Extras;

namespace Flyback.Core.Graph;

/// <summary>
/// The Song: a patch's one sequence of sections, its parts named and each looping at
/// its own length, shown in the editor's Sequence pane (ADR-0229).
/// </summary>
internal static class SongModule
{
    /// <summary>The type id of the Song.</summary>
    public const string TypeId = "seq.song";

    /// <summary>As many sections as one part may hold, on the budget of op count a change of level costs.</summary>
    public const int MaxSections = 256;

    /// <summary>A kick, a bass that comes in after it, and a pad that loops every three, so a Song shows what it is the moment it is dropped.</summary>
    private static readonly SongPart[] DefaultParts =
    [
        new("kick", [new(1f), new(1f), new(1f), new(1f)]),
        new("bass", [new(0f), new(1f), new(1f), new(1f)]),
        new("pad", [new(0f, Glides: false), new(1f, Glides: true), new(0.5f)]),
    ];

    public static NodeDef Def => new(
        TypeId, "Song", ModuleCategories.Timing,
        [
            new PortSpec("in", NormalledTo: NodeCatalog.Clock, Domain: true)
            {
                Help = "What it steps across: Time without a wire, or a Tempo's beats.",
            },
            new PortSpec("rate", PortKind.Scalar, 0.125f, 0f)
            {
                Knee = 0.05f,
                Help = "Sections for each unit of 'in': sections a second on Time, or 1/16 on beats for four bars of four.",
            },
            new PortSpec("fade", PortKind.Scalar, 0f, 0f, 1f)
            {
                Help = "How long a change of level takes at the start of a section, as a fraction of the section.",
            },
        ],
        [
            .. Enumerable.Range(1, NodeCatalog.MaxParts).Select(part => new PortSpec($"part {part}")
            {
                Help = $"Part {part}'s level in its section playing; the text and the canvas call it by the part's name.",
            }),
            new PortSpec("progress", PortKind.Scalar, 0f, 0f, 1f) { Help = "How far through its section it is, 0 to 1." },
            new PortSpec("section") { Help = "Which section of its longest part is playing, counting from 1." },
            new PortSpec("played") { Help = "How many sections it has stepped through, counting up from 0 and never coming round." },
        ],
        Emit,
        "The patch's one Song: named parts, each a row of levels a section, each looping at its own "
        + "length. Shown in the Sequence pane under the canvas. For a smaller sequence of sections, "
        + "or more than one, use an Arrangement.")
    {
        Words = "song sections parts timeline",
        Extras = [new SongExtra(DefaultParts)],
    };

    /// <summary>Each part on edges of its own length (see <see cref="SectionLevels"/>); section one comes from nought the first time through.</summary>
    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var parts = node.Parts;
        var zero = em.Constant(0f);
        var one = em.Constant(1f);
        var outputs = new Slot[NodeCatalog.MaxParts + 3];

        if (parts.Count == 0)
        {
            Array.Fill(outputs, zero);
            return outputs;
        }

        var traveled = em.Mul(node[0], node[1]);
        var progress = em.Unary(OpCode.Fract, traveled);
        var fromInput = em.Sub(one, em.Binary(OpCode.Step, one, traveled));

        var fade = em.Ternary(OpCode.Clamp, node[2], em.Constant(NodeCatalog.ShortestFade), one);
        var eased = em.Ternary(OpCode.Smoothstep, zero, fade, progress);
        var levels = new SectionLevels(em, eased, progress, fromInput);

        var indexes = new Dictionary<int, Slot>();
        var edges = new Dictionary<int, Slot[]>();

        Slot IndexOf(int length)
        {
            if (indexes.TryGetValue(length, out var index)) return index;
            return indexes[length] = em.Unary(OpCode.Floor, em.Binary(OpCode.Mod, traveled, em.Constant(length)));
        }

        for (var p = 0; p < NodeCatalog.MaxParts; p++)
        {
            if (p >= parts.Count)
            {
                outputs[p] = zero;
                continue;
            }

            var length = parts[p].Count;
            if (!edges.TryGetValue(length, out var at)) edges[length] = at = SectionLevels.Edges(em, IndexOf(length), length);

            outputs[p] = levels.Part(parts[p], at, zero);
        }

        outputs[NodeCatalog.MaxParts] = progress;
        outputs[NodeCatalog.MaxParts + 1] = em.Add(IndexOf(parts.Max(part => part.Count)), 1f);
        outputs[NodeCatalog.MaxParts + 2] = traveled;

        return outputs;
    }
}
