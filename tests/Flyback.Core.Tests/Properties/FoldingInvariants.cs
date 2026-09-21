using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Properties;

/// <summary>
/// Random graphs of Maths modules, Expressions, switched-off modules and buses,
/// folded and printed: folding keeps the program op for op, and the printing
/// reads back as the same program.
/// </summary>
/// <remarks>
/// Every wire carries something. A wire out of a switched-off module with
/// nothing patched into it rests on the knob under it, which neither a formula
/// nor the text has anywhere to write.
/// </remarks>
public class FoldingInvariants
{
    private const int Graphs = 1000;

    private static readonly string[] Folded =
    [
        "math.add", "math.sub", "math.mul", "math.div", "math.mod", "math.pow", "math.min", "math.max",
        "math.neg", "math.abs", "math.sin", "math.floor", "math.fract", "math.atan2",
    ];

    private static readonly string[] Formulas =
    [
        "a * b", "a + a", "sin(a) * 2", "a % 3 + b", "-a", "pi * a", "min(a, b) - c", "a - (b - c)", "a * (b / d)",
        "-(a + b)", "--a", "a % -2", "clamp(a)", "a * 0.1 + b * 0.2 + c * 0.3 + d", "pow(a)", "2 % 3 + a", "a / 0",
    ];

    private static readonly float[] Knobs = [0f, 1f, -1f, 0.5f, -0.5f, 2f, 1e-7f, 3.3333333f, 12345678f, 0.1f];

    private static List<(OpCode, int, int, int, int, float)> Fingerprint(Patch patch) =>
    [
        .. patch.CompileForVideo(NodeCatalog.BuiltIn).Program.Ops.Select(o => (o.Code, o.Out, o.A, o.B, o.C, o.K)),
        .. patch.CompileForAudio(NodeCatalog.BuiltIn).Program.Ops.Select(o => (o.Code, o.Out, o.A, o.B, o.C, o.K)),
    ];

    private static Patch Graph(int seed)
    {
        var random = new Random(seed);
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var made = new List<NodeInstance>();
        var count = random.Next(2, 12);

        for (var i = 0; i < count; i++)
        {
            var pick = random.Next(10);
            var node = b.Add(pick < 6 ? Folded[random.Next(Folded.Length)] : pick < 9 ? NodeCatalog.ExpressionTypeId : "math.clamp");

            if (node.TypeId == NodeCatalog.ExpressionTypeId)
                node.SetState("expression", new JsonObject { ["formula"] = Formulas[random.Next(Formulas.Length)] });

            for (var port = 0; port < node.InputValues.Length; port++)
            {
                var how = random.Next(10);

                if (how < 5 && made.Count > 0) b.Wire(made[random.Next(made.Count)], 0, node, port);
                else if (how < 7) b.Wire(coord, random.Next(5), node, port);
                else node.InputValues[port] = Knobs[random.Next(Knobs.Length)];
            }

            if (random.Next(8) == 0 && b.Patch.IncomingTo(node.Id, 0) is { } fed && !b.Patch.Find(fed.SourceNode)!.Off)
                node.Off = true;

            if (random.Next(8) == 0 && made.Count > 0)
            {
                var bus = new JsonObject { ["bus"] = $"bus{made.Count}" };
                var send = b.Add(NodeCatalog.SendTypeId);
                var receive = b.Add(NodeCatalog.ReceiveTypeId);

                send.SetState("bus", bus.DeepClone());
                receive.SetState("bus", bus);
                b.Wire(made[random.Next(made.Count)], 0, send, 0);
                made.Add(receive);
            }

            made.Add(node);
        }

        var sink = b.Add(NodeCatalog.OutputTypeId);
        b.Wire(made[^1], 0, sink, NodeCatalog.OutputColorPort);
        b.Wire(made[random.Next(made.Count)], 0, sink, NodeCatalog.OutputLeftPort);

        return b.Patch;
    }

    [Fact]
    public void Folding_keeps_the_program()
    {
        var failures = new List<int>();

        for (var seed = 0; seed < Graphs; seed++)
        {
            var patch = Graph(seed);
            var before = Fingerprint(patch);

            ExpressionFusion.Fuse(patch, NodeCatalog.BuiltIn);

            if (!Fingerprint(patch).SequenceEqual(before)) failures.Add(seed);
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    public void A_printing_reads_back_as_the_same_program_and_so_does_its_own()
    {
        var failures = new List<string>();

        for (var seed = 0; seed < Graphs; seed++)
        {
            var patch = Graph(seed);
            var before = Fingerprint(patch);
            var printed = PatchPrinter.Print(patch, NodeCatalog.BuiltIn);

            foreach (var pass in new[] { "first", "second" })
            {
                var load = PatchLanguage.Build(printed, NodeCatalog.BuiltIn);

                if (load.Issues.Count > 0 || !Fingerprint(load.Patch).SequenceEqual(before))
                {
                    failures.Add($"seed {seed}, {pass} printing: {load.Report}{Environment.NewLine}{printed}");
                    break;
                }

                printed = PatchPrinter.Print(load.Patch, NodeCatalog.BuiltIn);
            }
        }

        failures.ShouldBeEmpty();
    }
}
