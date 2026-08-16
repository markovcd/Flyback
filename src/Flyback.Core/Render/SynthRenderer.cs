using System.Runtime.CompilerServices;
using Flyback.Core.Compile;

namespace Flyback.Core.Render;

/// <summary>
/// Evaluates a compiled patch over every pixel of a frame, in parallel across
/// rows. Also owns the frame history that the Feedback module reads, kept in
/// linear float RGB so a feedback loop doesn't grind itself down to 8-bit steps.
/// </summary>
public sealed class SynthRenderer
{
    /// <summary>
    /// One core is deliberately left alone. A frame at 960x540 costs more than a
    /// frame interval, so this loop runs essentially back to back — and taking
    /// every core with it leaves the audio callback nowhere to be scheduled. It
    /// needs a twentieth of a core to keep up and gets none, which is heard as
    /// the sound choking while the picture is busy.
    /// </summary>
    /// <remarks>
    /// Reserving one costs a twelfth of the frame rate on a twelve-core machine
    /// and buys back every dropout. On a single-core machine there is nothing to
    /// reserve and this is 1, which is what the loop would have done anyway.
    /// </remarks>
    private static readonly ParallelOptions Spare = new()
    {
        MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
    };

    private float[] currentFrame = [];
    private float[] previousFrame = [];
    private int bufferWidth;
    private int bufferHeight;

    /// <summary>Coordinates run -1..1 vertically and -aspect..aspect horizontally.</summary>
    public static float AspectOf(int width, int height) => height == 0 ? 1f : (float)width / height;

    /// <summary>Clears the feedback history, so the next frame starts from black.</summary>
    public void Reset() => Array.Clear(previousFrame);

    /// <summary>Renders one frame into a BGRA8888 buffer.</summary>
    public void Render(CompiledPatch patch, double time, int width, int height, Span<byte> destination, int stride)
    {
        if (width <= 0 || height <= 0) return;
        if (destination.Length < (long)stride * height)
            throw new ArgumentException("Destination is smaller than stride * height.", nameof(destination));

        unsafe
        {
            fixed (byte* pinned = destination)
            {
                Render(patch, time, width, height, pinned, stride);
            }
        }
    }

    /// <summary>Renders one frame into a BGRA8888 buffer that the caller has already pinned or mapped.</summary>
    private unsafe void Render(CompiledPatch patch, double time, int width, int height, byte* destination, int stride)
    {
        if (width <= 0 || height <= 0) return;

        EnsureBuffers(width, height);

        var feedback = new FeedbackFrame(previousFrame, width, height);
        var current = currentFrame;
        var aspect = AspectOf(width, height);
        var outputBase = patch.OutputBase;
        var origin = (nint)destination;

        Parallel.For(
            0,
            height,
            Spare,
            patch.AllocateRegisters,
            (y, _, registers) =>
            {
                var row = (byte*)origin + (nint)y * stride;
                var scanline = y * width * 3;

                // Screen y grows downwards; patch y grows upwards.
                var py = 1d - 2d * (y + 0.5d) / height;

                for (var x = 0; x < width; x++)
                {
                    var px = (2d * (x + 0.5d) / width - 1d) * aspect;

                    patch.Evaluate(px, py, time, registers, feedback);

                    var r = Saturate(registers[outputBase + 0]);
                    var g = Saturate(registers[outputBase + 1]);
                    var b = Saturate(registers[outputBase + 2]);

                    // The frame history is a picture, so it is kept at the width
                    // a picture needs — see DelayState on the same trade.
                    var sample = scanline + x * 3;
                    current[sample + 0] = (float)r;
                    current[sample + 1] = (float)g;
                    current[sample + 2] = (float)b;

                    var pixel = row + x * 4;
                    pixel[0] = ToByte(b);
                    pixel[1] = ToByte(g);
                    pixel[2] = ToByte(r);
                    pixel[3] = 255;
                }

                return registers;
            },
            _ => { });

        (previousFrame, currentFrame) = (currentFrame, previousFrame);
    }

    private void EnsureBuffers(int width, int height)
    {
        if (bufferWidth == width && bufferHeight == height) return;

        bufferWidth = width;
        bufferHeight = height;
        currentFrame = new float[width * height * 3];
        previousFrame = new float[width * height * 3];
    }

    /// <summary>Clamps to the displayable range, which is also what stops a feedback loop running away.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Saturate(double v) => double.IsFinite(v) ? Math.Clamp(v, 0d, 1d) : 0d;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ToByte(double v) => (byte)(v * 255d + 0.5d);
}
