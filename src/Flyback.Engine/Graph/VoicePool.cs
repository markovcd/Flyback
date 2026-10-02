using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;

namespace Flyback.Engine.Graph;

/// <summary>
/// One instrument's voices, and which of them a note goes to: the voice already
/// holding it, else the first free one the running programs read, else the first
/// they read, over what it held.
/// </summary>
/// <remarks>
/// Knows nothing of devices or threads: whoever owns it hands it the programs'
/// blocks and does its own locking.
/// </remarks>
/// <param name="source">The instrument, or one of its channels, as <see cref="MidiSignal.Channeled"/> names it.</param>
internal sealed class VoicePool(string source)
{
    public const int Count = 8;

    private readonly List<MidiVoice> voices = [.. Enumerable.Range(0, Count).Select(_ => new MidiVoice())];

    public string Source => source;

    public IReadOnlyList<MidiVoice> Voices => voices;

    /// <summary>Whether any voice has a key down.</summary>
    public bool Playing => voices.Any(voice => voice.Playing);

    /// <summary>A key pressed, given to a voice <paramref name="blocks"/> read.</summary>
    public void Down(int note, float velocity, IReadOnlyList<LiveValues> blocks)
    {
        var indexes = ReadIndexes(blocks).Select(index => index.Voice).ToList();
        var voice = indexes
            .Select(index => voices[index - 1])
            .FirstOrDefault(candidate => candidate.Holds(note));

        if (voice is null)
        {
            voice = indexes
                .Select(index => voices[index - 1])
                .FirstOrDefault(candidate => !candidate.Playing);
        }

        if (voice is null)
        {
            // No configured voice is free: the first module plays it, over what it
            // held, and goes back to that when this one is let go.
            voice = voices[indexes[0] - 1];
        }

        voice.Down(note, velocity);
    }

    /// <summary>A key let go, wherever it is held, sounding or under a note that took its voice.</summary>
    public void Up(int note)
    {
        foreach (var voice in voices)
        {
            if (voice.Holds(note))
            {
                voice.Up(note);
                return;
            }
        }
    }

    /// <summary>Everything let go.</summary>
    public void Silence()
    {
        foreach (var voice in voices) voice.Silence();
    }

    /// <summary>Writes every voice <paramref name="blocks"/> read into <paramref name="block"/>, one of them.</summary>
    public void WriteTo(LiveValues block, IReadOnlyList<LiveValues> blocks)
    {
        foreach (var indexed in ReadIndexes(blocks))
        {
            if (indexed.Auto is { } node)
            {
                voices[indexed.Voice - 1].WriteTo(block, signal => MidiSignal.AutoKey(source, node, signal));
                if (indexed.Voice == 1) voices[indexed.Voice - 1].WriteTo(block, source, indexed.Voice);
            }
            else
                voices[indexed.Voice - 1].WriteTo(block, source, indexed.Voice);
        }
    }

    private IReadOnlyList<(int Voice, Guid? Auto)> ReadIndexes(IReadOnlyList<LiveValues> blocks)
    {
        var explicitIndexes = Enumerable.Range(1, Count)
            .Where(index => blocks.Any(block => Reads(block, index)))
            .Select(index => (Voice: index, Auto: (Guid?)null));
        var automatic = blocks
            .SelectMany(block => block.Keys)
            .Select(key => key.Split('/'))
            .Where(parts => parts.Length == 4 && parts[0] == source && parts[1] == "auto")
            .Select(parts => Guid.TryParse(parts[2], out var node) ? (Guid?)node : null)
            .Where(node => node is not null)
            .Distinct()
            .Select((node, offset) => (Voice: Enumerable.Range(1, Count)
                .Except(explicitIndexes.Select(index => index.Item1))
                .ElementAtOrDefault(offset), Auto: node));

        var indexes = explicitIndexes.Concat(automatic).Where(index => index.Voice > 0).ToList();
        return indexes.Count > 0 ? indexes : Enumerable.Range(1, Count).Select(index => (index, (Guid?)null)).ToArray();
    }

    private bool Reads(LiveValues block, int index) =>
        (index == 1 && (
            block.Reads(MidiSignal.Key(source, MidiSignal.Pitch))
            || block.Reads(MidiSignal.Key(source, MidiSignal.Gate))
            || block.Reads(MidiSignal.Key(source, MidiSignal.Velocity))
            || block.Reads(MidiSignal.Key(source, MidiSignal.Strikes))))
        || block.Reads(MidiSignal.Key(source, index, MidiSignal.Pitch))
        || block.Reads(MidiSignal.Key(source, index, MidiSignal.Gate))
        || block.Reads(MidiSignal.Key(source, index, MidiSignal.Velocity))
        || block.Reads(MidiSignal.Key(source, index, MidiSignal.Strikes));
}
