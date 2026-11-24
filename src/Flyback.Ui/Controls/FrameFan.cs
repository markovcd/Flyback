using Flyback.Gpu;

namespace Flyback.Ui.Controls;

/// <summary>One frame handed to several sinks in turn, so a take and a picture output can read the same preview.</summary>
internal sealed class FrameFan(IFrameSink[] sinks) : IFrameSink
{
    /// <summary>Null for nobody, the one sink alone, or a fan over a copy of them all.</summary>
    public static IFrameSink? Of(IReadOnlyList<IFrameSink> sinks) => sinks.Count switch
    {
        0 => null,
        1 => sinks[0],
        _ => new FrameFan([.. sinks]),
    };

    public void Accept(ReadOnlySpan<byte> rgba, int width, int height)
    {
        foreach (var sink in sinks) sink.Accept(rgba, width, height);
    }
}
