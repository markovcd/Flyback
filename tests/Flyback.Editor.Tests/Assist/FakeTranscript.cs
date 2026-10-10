using Flyback.Assist;
using Flyback.Plugins.Assist;

namespace Flyback.Editor.Tests.Assist;

/// <summary>A transcript kept in memory, with everything it was handed in order.</summary>
internal sealed class FakeTranscript : ITranscript
{
    private readonly List<TranscriptLine> lines = [];

    public List<Spoken> Said { get; } = [];

    public IReadOnlyList<TranscriptLine> Lines => lines;

    public bool IsEmpty => lines.Count == 0;

    public void Clear()
    {
        lines.Clear();
        Said.Clear();
    }

    public void Put(Spoken spoken)
    {
        Said.Add(spoken);

        if (spoken.Keep) lines.Add(spoken.Line);
    }
}
