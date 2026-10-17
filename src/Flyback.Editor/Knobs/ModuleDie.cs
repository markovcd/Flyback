using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Editor.Notices;
using Flyback.Editor.Settings;
using Flyback.Editor.Statistics;

namespace Flyback.Editor.Knobs;

/// <summary>
/// Sends every knob of its own on the selected modules somewhere new, within the
/// panel die's amount; fired from the inspector's die or Ctrl+Shift+D.
/// </summary>
/// <remarks>
/// One undo step, so Ctrl+Z steps back through the rolls and Ctrl+Y forward again
/// (ADR-0209). A wired input, or one on a panel knob, is left where it is.
/// </remarks>
internal sealed class ModuleDie(
    NodeEditor editor,
    Reactions reactions,
    OutputSettingRepository settings,
    ReportLine report,
    Usage usage) : IReactTo<ModulesRollAsked>
{
    /// <summary>Where each roll lands, seeded by a test that wants to know.</summary>
    public Random Random { get; set; } = Random.Shared;

    public Task On(ModulesRollAsked notice)
    {
        Roll();
        return Task.CompletedTask;
    }

    /// <summary>The inputs of <paramref name="node"/> a roll moves.</summary>
    public static IEnumerable<(int Port, PortSpec Spec)> Rollable(Patch patch, NodeInstance node)
    {
        if (NodeCatalog.IsSink(node.TypeId) || NodeCatalog.Get(node.TypeId) is not { } def) yield break;

        for (var port = 0; port < def.Inputs.Count && port < node.InputValues.Length; port++)
        {
            var spec = def.Inputs[port];

            if (spec.Ranged && SocketDial.HasOwnKnob(patch, node, port, spec)) yield return (port, spec);
        }
    }

    /// <summary>Rolls every knob of its own on the selected modules, as one edit.</summary>
    public void Roll()
    {
        var patch = editor.History.Patch;
        var amount = settings.Current.Randomize.Amount;
        var rolled = new List<SocketPick>();

        foreach (var node in editor.Selection.Nodes)
        {
            foreach (var (port, spec) in Rollable(patch, node))
            {
                var travel = (float)spec.Travel(node.InputValues[port], spec.Min, spec.Max);
                var next = spec.At(KnobRoll.Next(travel, amount, Random), spec.Min, spec.Max);

                node.InputValues[port] = spec.Stepped ? MathF.Round(next) : next;
                rolled.Add(new SocketPick(node.Id, port));
            }
        }

        if (rolled.Count == 0)
        {
            report.Say(editor.Selection.Count == 0
                ? "Select a module to roll its knobs."
                : "Nothing to roll: every input here is wired, on a panel knob, or has no knob.");
            return;
        }

        usage.Count(Used.RolledModule);
        editor.History.Record();

        foreach (var pick in rolled) reactions.Raise(new InputTurned(pick));
        reactions.Raise(new InputLetGo(rolled[^1]));

        report.Say(rolled.Count == 1 ? "Rolled one knob. Ctrl+Z takes it back." : $"Rolled {rolled.Count} knobs. Ctrl+Z takes them back.");
    }
}
