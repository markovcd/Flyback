using System.Runtime.CompilerServices;
using VideoSynth.Core.Compile;

namespace VideoSynth.Core.Render;

/// <summary>
/// Evaluates a compiled patch over every pixel of a frame, in parallel across
/// rows. Also owns the frame history that the Feedback module reads, kept in
/// linear float RGB so a feedback loop doesn't grind itself down to 8-bit steps.
/// </summary>
public sealed class SynthRenderer
{
    private float[] _current = [];
    private float[] _previous = [];
    private int _width;
    private int _height;

    /// <summary>Coordinates run -1..1 vertically and -aspect..aspect horizontally.</summary>
    public static float AspectOf(int width, int height) => height == 0 ? 1f : (float)width / height;

    /// <summary>Clears the feedback history, so the next frame starts from black.</summary>
    public void Reset() => Array.Clear(_previous);

    /// <summary>Renders one frame into a BGRA8888 buffer.</summary>
    public void Render(CompiledPatch patch, float time, int width, int height, Span<byte> destination, int stride)
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
    public unsafe void Render(CompiledPatch patch, float time, int width, int height, byte* destination, int stride)
    {
        if (width <= 0 || height <= 0) return;

        EnsureBuffers(width, height);

        var feedback = new FeedbackFrame(_previous, width, height);
        var current = _current;
        var aspect = AspectOf(width, height);
        var outputBase = patch.OutputBase;
        var origin = (nint)destination;

        Parallel.For(
            0,
            height,
            patch.AllocateRegisters,
            (y, _, registers) =>
            {
                var row = (byte*)origin + (nint)y * stride;
                var scanline = y * width * 3;

                // Screen y grows downwards; patch y grows upwards.
                var py = 1f - 2f * (y + 0.5f) / height;

                for (var x = 0; x < width; x++)
                {
                    var px = (2f * (x + 0.5f) / width - 1f) * aspect;

                    patch.Evaluate(px, py, time, registers, feedback);

                    var r = Saturate(registers[outputBase + 0]);
                    var g = Saturate(registers[outputBase + 1]);
                    var b = Saturate(registers[outputBase + 2]);

                    var sample = scanline + x * 3;
                    current[sample + 0] = r;
                    current[sample + 1] = g;
                    current[sample + 2] = b;

                    var pixel = row + x * 4;
                    pixel[0] = ToByte(b);
                    pixel[1] = ToByte(g);
                    pixel[2] = ToByte(r);
                    pixel[3] = 255;
                }

                return registers;
            },
            _ => { });

        (_previous, _current) = (_current, _previous);
    }

    private void EnsureBuffers(int width, int height)
    {
        if (_width == width && _height == height) return;

        _width = width;
        _height = height;
        _current = new float[width * height * 3];
        _previous = new float[width * height * 3];
    }

    /// <summary>Clamps to the displayable range, which is also what stops a feedback loop running away.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Saturate(float v) => float.IsFinite(v) ? Math.Clamp(v, 0f, 1f) : 0f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ToByte(float v) => (byte)(v * 255f + 0.5f);
}
