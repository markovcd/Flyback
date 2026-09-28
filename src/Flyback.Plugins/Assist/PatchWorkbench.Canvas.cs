using System.Text;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Assist;

/// <summary>
/// Keeping the patch being built up with settings changed on the canvas, between
/// turns, so a knob somebody turned is neither a new conversation nor undone by
/// the next proposal.
/// </summary>
public sealed partial class PatchWorkbench
{
    /// <summary>The most settings named in one line; past it the model is pointed at describe_patch.</summary>
    private const int MostTold = 24;

    /// <summary>
    /// Carries onto the patch being built each setting that went from
    /// <paramref name="was"/> to <paramref name="now"/> on the canvas, except where
    /// this workbench changed the same one itself. Not while a turn runs.
    /// </summary>
    internal List<Retuned> Follow(Patch was, Patch now) => Retuning.Carry(was, now, working);

    /// <summary>
    /// Takes every setting on the canvas that differs from the patch being built,
    /// for a conversation carried on over a patch that was saved after it.
    /// </summary>
    internal List<Retuned> Follow(Patch now) => Retuning.Carry(Retuning.Copy(working), now, working);

    /// <summary>
    /// The settings in <paramref name="carried"/>, in the handles the modules
    /// answer to: <c>osc1.freq=330, knob "Cutoff"=0.7</c>.
    /// </summary>
    internal string Told(IReadOnlyCollection<Retuned> carried)
    {
        var told = new StringBuilder();
        var named = 0;

        foreach (var change in carried)
        {
            if (named == MostTold)
            {
                told.Append(", and ").Append(carried.Count - named).Append(" more that describe_patch shows");
                break;
            }

            if (named > 0) told.Append(", ");

            told.Append(Where(change)).Append(change.To is { } to ? "=" + to : " changed");
            named++;
        }

        return told.ToString();
    }

    private string Where(Retuned change)
    {
        if (change.Module is not { } id) return change.Field;

        var node = working.Find(id);
        var handle = Handle(node);

        if (change.Port < 0) return $"{handle}.{change.Field}";

        return node is not null && modules.Get(node.TypeId) is { } def && change.Port < def.Inputs.Count
            ? $"{handle}.{def.Inputs[change.Port].Name.Replace(' ', '_')}"
            : $"{handle}.{change.Port}";
    }
}
