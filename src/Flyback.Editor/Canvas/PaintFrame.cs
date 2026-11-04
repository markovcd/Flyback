using Flyback.Core.Graph;

namespace Flyback.Editor.Canvas;

/// <summary>The frame being drawn, if one is, and what it has worked out about the patch so far.</summary>
internal sealed class PaintFrame
{
    /// <summary>What this frame has looked up, or null between frames, when the patch may change.</summary>
    public SceneMemo? Memo { get; private set; }

    /// <summary>Starts a frame of <paramref name="patch"/>, which ends when the result is disposed.</summary>
    public Drawing Open(Patch patch)
    {
        Memo = new SceneMemo(patch);
        return new Drawing(this);
    }

    /// <summary>The memo, while it is about <paramref name="patch"/>.</summary>
    public SceneMemo? For(Patch patch) => Memo is { } memo && ReferenceEquals(memo.Patch, patch) ? memo : null;

    internal readonly struct Drawing(PaintFrame frame) : IDisposable
    {
        public void Dispose() => frame.Memo = null;
    }
}
