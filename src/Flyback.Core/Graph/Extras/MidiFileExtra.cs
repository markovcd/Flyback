using Flyback.Core.Compile;

namespace Flyback.Core.Graph.Extras;

/// <summary>The MIDI file a module plays.</summary>
/// <remarks>
/// <see cref="SampleExtra"/> for the other kind of file: the same shape and a
/// different library and fault. It folds the voice and channel of
/// <see cref="MidiLineExtra"/> in as well, so the module is handed the one voice it
/// plays and never the whole file.
/// </remarks>
public sealed record MidiFileExtra : NodeExtra
{
    /// <inheritdoc cref="StepsExtra.Name"/>
    public const string Name = "midifile";

    /// <inheritdoc/>
    public override string Key => Name;

    /// <inheritdoc cref="SampleExtra.Of"/>
    public static string Of(NodeInstance node) => Read(node.StateOf(Name), string.Empty);

    /// <summary>Points this instance at a MIDI file.</summary>
    public static void Set(NodeInstance node, string path) => node.SetState(Name, Write(path));

    /// <inheritdoc cref="SampleExtra.Seed"/>
    public override void Seed(NodeInstance node) => Set(node, string.Empty);

    /// <inheritdoc cref="SampleExtra.Fold"/>
    public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env)
    {
        var path = Of(node);

        if (string.IsNullOrWhiteSpace(path))
        {
            env.Report(new CompileIssue(
                node.Id,
                $"'{env.Title}' has no MIDI file chosen, so it plays nothing. Pick one in the panel.",
                IssueSeverity.Warning));

            return ctx;
        }

        if (env.Samples?.FindMidi(path) is not { } song)
        {
            env.Report(new CompileIssue(
                node.Id,
                $"'{env.Title}' cannot read {path} — "
                + (env.Samples?.ExplainMidi(path) ?? "nothing here can open a MIDI file.")
                + " A patch names its files rather than carrying them, so this one has to be "
                + "somewhere it can be found."));

            return ctx;
        }

        var line = song.Line(MidiLineExtra.Voice(node), MidiLineExtra.Channel(node));

        if (line.Notes == 0)
        {
            env.Report(new CompileIssue(
                node.Id,
                $"'{env.Title}' hears no notes in {path}"
                + (MidiLineExtra.Channel(node) == 0 ? "." : $" on channel {MidiLineExtra.Channel(node)}."),
                IssueSeverity.Warning));
        }

        return ctx with { Midi = line };
    }

    /// <inheritdoc/>
    public override IEnumerable<string> Files(NodeInstance node)
    {
        var path = Of(node);

        return string.IsNullOrWhiteSpace(path) ? [] : [path];
    }

    /// <inheritdoc/>
    public override void Rebase(NodeInstance node, Func<string, string> renamed)
    {
        var path = Of(node);

        if (!string.IsNullOrWhiteSpace(path)) Set(node, renamed(path));
    }

    /// <inheritdoc/>
    public override string Report(NodeInstance node)
    {
        var path = Of(node);

        return string.IsNullOrWhiteSpace(path)
            ? "No MIDI file chosen, so it plays nothing."
            : $"MIDI file: {path}.";
    }

    /// <inheritdoc/>
    public override string Announce() =>
        "  midifile   a path to a .mid file — not a knob";

    /// <inheritdoc/>
    public override string Help => "The MIDI file it plays. The patch keeps its path, so moving the file breaks it.";
}
