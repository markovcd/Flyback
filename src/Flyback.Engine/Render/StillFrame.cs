using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;

namespace Flyback.Engine.Render;

/// <summary>
/// Draws one frame of a picture at a second of the patch, as a clip would show it
/// there: a picture with a history is drawn frame by frame from the start first.
/// </summary>
internal static class StillFrame
{
    /// <summary>Whether a frame reads what the frames before it drew: a feedback loop or a plane.</summary>
    public static bool Remembers(CompiledPatch program) =>
        program.PlaneCount > 0 || program.Ops.Any(op => op.Code is OpCode.SampleFeedback);

    /// <summary>Draws the frame at <paramref name="at"/> into <paramref name="pixels"/>, BGRA.</summary>
    /// <param name="fps">How often the frames before it are drawn, where it has a history.</param>
    /// <param name="speaker">Plays <paramref name="sound"/> alongside, for what the picture reads of it; null for none.</param>
    public static void Draw(
        IFrameRenderer frames,
        CompiledPatch picture,
        double at,
        double fps,
        int width,
        int height,
        Span<byte> pixels,
        int stride,
        AudioRenderer? speaker = null,
        CompiledPatch? sound = null)
    {
        var heard = new LiveValues(picture.LiveInputs);
        var played = 0L;
        var chunk = Array.Empty<float>();

        void Hear(double seconds)
        {
            if (speaker is null || sound is null) return;

            for (var due = (long)Math.Round(seconds * speaker.SampleRate); played < due;)
            {
                var count = (int)Math.Min(due - played, speaker.SampleRate);
                if (chunk.Length < count * NodeCatalog.AudioChannels) chunk = new float[count * NodeCatalog.AudioChannels];

                speaker.Render(sound, chunk.AsSpan(0, count * NodeCatalog.AudioChannels));
                played += count;
            }

            Meters.Refresh(sound, speaker.Memory, heard);
            Traces.Refresh(picture, sound, speaker.Memory);
        }

        var before = Remembers(picture) ? (int)Math.Ceiling(at * fps) : 0;

        for (var frame = 0; frame < before; frame++)
        {
            Hear(Math.Min(at, (frame + 1) / fps));
            frames.Render(picture, frame / fps, width, height, pixels, stride, heard);
        }

        Hear(at);
        frames.Render(picture, at, width, height, pixels, stride, heard);
    }
}
