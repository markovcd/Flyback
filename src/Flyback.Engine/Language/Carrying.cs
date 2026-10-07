using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Language.Values;

namespace Flyback.Engine.Language;

/// <summary>
/// What a call gives a module to carry: the file it names, or the notes, the
/// scale or the parts its block spells.
/// </summary>
internal sealed class Carrying(ModuleCatalog modules, Wiring wiring, Issues issues)
{
    /// <summary>The file a module names rather than carries (ADR-0052).</summary>
    public void File(Value placed, NodeDef def, string path, int line, int column)
    {
        if (placed is not Placed node || wiring.Patch.Find(node.Id) is not { } instance) return;

        if (def.Extra<SampleExtra>() is not null) SampleExtra.Set(instance, path);
        else if (def.Extra<PictureExtra>() is not null) PictureExtra.Set(instance, path);
        else if (def.Extra<MidiFileExtra>() is not null) MidiFileExtra.Set(instance, path);
        else if (def.Extra<ShapeExtra>() is not null) ShapeExtra.Set(instance, path);
        else issues.Complain(IssueCode.NoFile, line, column, $"'{def.Name}' names no file.");
    }

    /// <summary>
    /// The notes, the scale or the parts a block spells, and the one adjustment
    /// alternation asks for.
    /// </summary>
    /// <param name="opened">Where the block's '[' is, which what is wrong inside it is counted from.</param>
    public void Block(Value placed, NodeDef def, string block, int line, int column, (int Line, int Column) opened)
    {
        if (placed is not Placed value || wiring.Patch.Find(value.Id) is not { } node) return;

        if (def.Extra<StepsExtra>() is { } steps)
        {
            var read = StepNotation.Read(block, steps.Spec.Display == PortDisplay.Note, opened.Line, opened.Column, issues.List);

            StepsExtra.Set(node, read.Steps);

            // '<a b>' was unrolled into a longer list, so the list has to be
            // read more slowly for the pattern to take the time it did.
            if (read.RateDivisor > 1) Slower(node, def, read.RateDivisor);

            return;
        }

        if (def.Extra<ScaleExtra>() is not null)
        {
            ScaleExtra.Set(node, StepNotation.Classes(block, opened.Line, opened.Column, issues.List));
            return;
        }

        if (def.Extra<ArrangementExtra>() is not null)
        {
            ArrangementExtra.Set(node, ArrangementNotation.Read(block, opened.Line, opened.Column, issues.List));
            return;
        }

        issues.Complain(IssueCode.NoBlock, line, column, $"'{def.Name}' carries nothing a block could say.");
    }

    /// <summary>Divides a sequencer's rate, by the knob where there is one and by a Multiply where there is not.</summary>
    private void Slower(NodeInstance node, NodeDef def, int by)
    {
        var rate = SocketNames.Find(def.Inputs, "rate");
        if (rate < 0) return;

        if (wiring.Patch.IncomingTo(node.Id, rate) is not { } wire)
        {
            node.InputValues[rate] /= by;
            return;
        }

        var scale = NodeInstance.Create(modules.Require("math.mul"), 0d, 0d, wiring.Identity.Next());
        wiring.Patch.Nodes.Add(scale);

        scale.InputValues[1] = 1f / by;

        wiring.Patch.Connect(wire.SourceNode, wire.SourcePort, scale.Id, 0);
        wiring.Patch.Connect(scale.Id, 0, node.Id, rate);
    }
}
