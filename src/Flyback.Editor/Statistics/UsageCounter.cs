using Flyback.App.Canvas;
using Flyback.App.Gallery;
using Flyback.App.Midi;
using Flyback.App.Notices;

namespace Flyback.App.Statistics;

/// <summary>What the run counts about how it was played (ADR-0094), gathered from the parts that know.</summary>
internal sealed class UsageCounter : IReactTo<PlaybackStarted>
{
    private readonly Usage usage;
    private readonly NodeEditor editor;
    private readonly PresetSlot presets;

    public UsageCounter(Usage usage, NodeEditor editor, PresetSlot presets, MidiHub midi)
    {
        this.usage = usage;
        this.editor = editor;
        this.presets = presets;

        midi.Heard += () => usage.Count(Used.Instrument);
    }

    public Task On(PlaybackStarted notice)
    {
        usage.Played(
            editor.History.Patch.Nodes.Select(node => node.TypeId),
            editor.History.Patch.Connections.Count,
            presets.Showing?.Name);

        return Task.CompletedTask;
    }
}
