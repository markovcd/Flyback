using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Properties;

/// <summary>
/// What must hold of a patch however it was edited, rather than of one worked
/// sequence of edits.
/// </summary>
/// <remarks>
/// The edits are drawn at random from what the editor offers — add, wire, delete,
/// group, ungroup, copy, paste, turn a knob, undo, redo — because the orders that
/// break something are the ones nobody would write a test for. Seeded, so a
/// failure is the same failure the next time it is run.
/// </remarks>
public class EditingInvariants
{
    [Fact]
    public void An_edited_patch_stays_a_patch()
    {
        var catalog = NodeCatalog.BuiltIn;
        var addable = catalog.All.Where(d => !NodeCatalog.IsSink(d.TypeId)).ToList();
        var random = new Random(20260920);

        var patch = new Patch();
        patch.EnsureOutput(catalog);

        var history = new PatchHistory(catalog);
        history.Opened(patch);

        for (var step = 0; step < 600; step++)
        {
            switch (random.Next(8))
            {
                case 0:
                    patch.Nodes.Add(NodeInstance.Create(
                        addable[random.Next(addable.Count)], random.Next(900), random.Next(600)));
                    break;

                case 1: Wire(patch, catalog, random); break;

                case 2:
                    if (patch.Nodes.Count > 0) patch.Remove(patch.Nodes[random.Next(patch.Nodes.Count)].Id);
                    break;

                case 3:
                    if (patch.Nodes.Count >= NodeGroup.Fewest)
                        patch.Group(patch.Nodes.OrderBy(_ => random.Next()).Take(NodeGroup.Fewest).Select(n => n.Id));
                    break;

                case 4:
                    if (patch.Groups is { Count: > 0 } groups) patch.Ungroup(groups[random.Next(groups.Count)].Id);
                    break;

                case 5:
                {
                    if (patch.Nodes.Count == 0) break;

                    var picked = patch.Nodes.OrderBy(_ => random.Next()).Take(1 + random.Next(3)).Select(n => n.Id);
                    PatchClipboard.Paste(patch, PatchClipboard.Copy(patch, picked), 20d, 20d);
                    break;
                }

                case 6:
                {
                    if (patch.Nodes.Count == 0) break;

                    var node = patch.Nodes[random.Next(patch.Nodes.Count)];
                    if (node.InputValues.Length == 0) break;

                    node.InputValues[random.Next(node.InputValues.Length)] = (float)(random.NextDouble() * 8d - 4d);
                    break;
                }

                default:
                    patch = (random.Next(2) == 0 ? history.Undo() : history.Redo()) ?? patch;
                    break;
            }

            history.Record(patch);

            Consistent(patch, catalog, $"step {step}");
        }
    }

    /// <summary>Undo and redo are each other's inverse, over a run of real edits.</summary>
    [Fact]
    public void Undo_walks_back_the_way_redo_walks_forward()
    {
        var catalog = NodeCatalog.BuiltIn;
        var addable = catalog.All.Where(d => !NodeCatalog.IsSink(d.TypeId)).ToList();
        var random = new Random(4242);

        var patch = new Patch();
        patch.EnsureOutput(catalog);

        var history = new PatchHistory(catalog);
        history.Opened(patch);

        var forward = new List<string> { PatchIO.ToJson(patch) };

        for (var step = 0; step < 60; step++)
        {
            switch (random.Next(4))
            {
                case 0:
                    patch.Nodes.Add(NodeInstance.Create(addable[random.Next(addable.Count)], step * 40, step * 20));
                    break;

                case 1: Wire(patch, catalog, random); break;

                case 2:
                    if (patch.Nodes.Count > 1) patch.Remove(patch.Nodes[random.Next(patch.Nodes.Count)].Id);
                    break;

                default:
                {
                    var node = patch.Nodes[random.Next(patch.Nodes.Count)];
                    if (node.InputValues.Length > 0) node.InputValues[0] = step;
                    break;
                }
            }

            // Each edit its own step, the way an editor that ended the gesture
            // records one.
            history.GestureEnded();

            if (history.Record(patch)) forward.Add(PatchIO.ToJson(patch));
        }

        var back = new List<string> { forward[^1] };

        while (history.CanUndo)
        {
            patch = history.Undo().ShouldNotBeNull();
            back.Add(PatchIO.ToJson(patch));
        }

        back.AsEnumerable().Reverse().ShouldBe(forward, "undo did not retrace the edits");

        var again = new List<string> { PatchIO.ToJson(patch) };

        while (history.CanRedo)
        {
            patch = history.Redo().ShouldNotBeNull();
            again.Add(PatchIO.ToJson(patch));
        }

        again.ShouldBe(forward, "redo did not replay the edits");
    }

    /// <summary>Everything a patch has to be, whatever was done to it.</summary>
    private static void Consistent(Patch patch, ModuleCatalog catalog, string what)
    {
        var ids = patch.Nodes.Select(n => n.Id).ToHashSet();

        foreach (var wire in patch.Connections)
        {
            ids.ShouldContain(wire.SourceNode, $"{what}: a wire leaves a module that is gone");
            ids.ShouldContain(wire.TargetNode, $"{what}: a wire lands on a module that is gone");

            var source = catalog.Require(patch.Find(wire.SourceNode)!.TypeId);
            var target = catalog.Require(patch.Find(wire.TargetNode)!.TypeId);

            wire.SourcePort.ShouldBeInRange(0, source.Outputs.Count - 1, what);
            wire.TargetPort.ShouldBeInRange(0, target.Inputs.Count - 1, what);
        }

        foreach (var group in patch.Groups ?? [])
        foreach (var member in group.Members)
            ids.ShouldContain(member, $"{what}: a group holds a module that is gone");

        patch.Connections
            .GroupBy(c => (c.TargetNode, c.TargetPort))
            .Where(g => g.Count() > 1)
            .ShouldBeEmpty($"{what}: two wires into one socket");

        var json = PatchIO.ToJson(patch);

        PatchIO.ToJson(PatchIO.Read(json, catalog).Patch)
            .ShouldBe(json, $"{what}: the patch did not survive a trip to disk");

        Should.NotThrow(() => patch.CompileForVideo(catalog), what);
        Should.NotThrow(() => patch.CompileForAudio(catalog), what);
    }

    private static void Wire(Patch patch, ModuleCatalog catalog, Random random)
    {
        if (patch.Nodes.Count < 2) return;

        var from = patch.Nodes[random.Next(patch.Nodes.Count)];
        var to = patch.Nodes[random.Next(patch.Nodes.Count)];

        var source = catalog.Require(from.TypeId);
        var target = catalog.Require(to.TypeId);

        if (source.Outputs.Count == 0 || target.Inputs.Count == 0) return;

        patch.Connect(from.Id, random.Next(source.Outputs.Count), to.Id, random.Next(target.Inputs.Count));
    }
}
