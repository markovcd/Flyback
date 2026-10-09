using Flyback.Core.Compile;

namespace Flyback.Core.Graph.Extras;

/// <summary>
/// Which instrument a Clock In follows. Filed under the same key as
/// <see cref="MidiExtra"/>, so a Clock In and a MIDI In name their instrument
/// the same way in a patch file and in the text.
/// </summary>
/// <remarks>
/// A fresh one follows the instrument known to conduct, or else the first one
/// plugged in, because the computer's keyboard has no clock and a module that
/// defaulted to it would be a module that did nothing until somebody found the
/// field.
/// </remarks>
public sealed record MidiClockExtra : NodeExtra
{
    /// <summary>What this is filed under, in a saved patch and on a context.</summary>
    public const string StateKey = MidiExtra.StateKey;

    /// <summary>The field choosing which instrument's clock to follow.</summary>
    public const string DeviceField = MidiExtra.DeviceField;

    /// <inheritdoc/>
    public override string Key => StateKey;

    /// <inheritdoc/>
    public override IReadOnlyList<ExtraField> Fields => FieldsFor(MidiSources.All);

    internal static IReadOnlyList<ExtraField> FieldsFor(IReadOnlyList<MidiSource> sources) =>
    [
        new ExtraField.Choice(
            DeviceField,
            "follows",
            [.. sources.Select(source => new ChoiceOption(source.Id, source.Name))],
            Conductor(sources)) { Help = "The instrument whose clock it keeps time to." },
    ];

    private static string Conductor(IReadOnlyList<MidiSource> sources)
    {
        return (sources.FirstOrDefault(source => source.Conducts) is { Id: not null } conductor
                   ? conductor
                   : sources.FirstOrDefault(source => source.Id != MidiSources.Keyboard)).Id
               ?? MidiSources.Keyboard;
    }

    /// <summary>
    /// The ordinary fold, and a word where the chosen instrument is gone or is the
    /// computer's keyboard, which keeps no clock. Reported rather than repaired,
    /// for the reason <see cref="MidiExtra"/> gives. One left unset says nothing:
    /// it follows whatever is plugged in, and nothing plugged in is not a mistake.
    /// </summary>
    public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env)
    {
        var picked = node.StateOf(Key)?[DeviceField];
        var chosen = Fields[0] is ExtraField.Choice field
            ? field.Value(picked)
            : MidiSources.Keyboard;

        if (chosen == MidiSources.Keyboard && picked is not null)
        {
            env.Report(new CompileIssue(
                node.Id,
                $"'{env.Title}' is following the computer keyboard, which keeps no clock. "
                + "Its beats stay at nought until an instrument is picked in the panel.",
                IssueSeverity.Warning));
        }
        else if (!MidiSources.All.Any(source => source.Id == chosen))
        {
            env.Report(new CompileIssue(
                node.Id,
                $"'{env.Title}' is following {chosen}, which is not here. Its beats stay where they were "
                + "until that instrument is plugged in, or until another is picked in the panel.",
                IssueSeverity.Warning));
        }

        return base.Fold(ctx, node, env);
    }

    /// <inheritdoc/>
    public override string Announce()
    {
        var offered = string.Join(", ", MidiSources.All.Where(source => source.Id != MidiSources.Keyboard).Select(source => source.Id));

        return $"  midi   device, which instrument's clock it follows — one of {offered}, as a string; not a knob";
    }
}
