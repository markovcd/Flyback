using System.Runtime.InteropServices;
using Flyback.Plugins.Audio;

namespace Flyback.Plugins.WinIO;

/// <summary>Hands one WASAPI capture packet of interleaved float to the host, without copying it.</summary>
internal static class WasapiPacket
{
    /// <summary>
    /// Delivers <paramref name="bytes"/> as floats, or as many zeros in their place where
    /// Windows marked the packet silent, whose buffer it does not promise to have written.
    /// </summary>
    /// <param name="silence">Zeros, handed over a slice at a time when a silent packet is longer.</param>
    public static void Deliver(ReadOnlySpan<byte> bytes, bool silent, float[] silence, AudioCaptureCallback deliver)
    {
        var samples = MemoryMarshal.Cast<byte, float>(bytes);

        if (!silent)
        {
            if (!samples.IsEmpty) deliver(samples);
            return;
        }

        for (var left = samples.Length; left > 0; left -= silence.Length)
            deliver(silence.AsSpan(0, Math.Min(left, silence.Length)));
    }
}
