using System.Buffers.Binary;

namespace Flyback.Core.Render;

/// <summary>
/// A WAV whose length is not known when it starts. The header goes down
/// claiming nothing and is patched once the take has ended.
/// </summary>
/// <remarks>
/// The streaming counterpart to <see cref="WavWriter"/>, and it exists for the
/// reason <see cref="AviWriter"/> gives for the same trick: a recording has no
/// length until somebody stops it, so the two sizes RIFF keeps at the front
/// cannot be written at the front. That means this needs a stream it can seek
/// back through, and that <see cref="Dispose"/> is not a formality — a file
/// whose header still claims nothing is one every player treats as empty.
/// <para>
/// Both writers lay the header down through <see cref="WavWriter.WriteHeader"/>
/// and convert through <see cref="WavWriter.ToPcm16"/>, so an exported file and
/// a recorded one differ in nothing but how they learned their length.
/// </para>
/// </remarks>
public sealed class WavStreamWriter : IDisposable
{
    /// <summary>
    /// RIFF counts in unsigned 32-bit and that count includes the 36 bytes of
    /// header following it, so the samples have to stop short of the ceiling
    /// rather than at it.
    /// </summary>
    private const long MaximumDataBytes = uint.MaxValue - 36;

    private readonly Stream output;
    private readonly long start;
    private readonly int channels;

    /// <summary>Reused across calls, since a take is thousands of these.</summary>
    private byte[] pcm = [];

    private long dataBytes;
    private bool closed;

    /// <param name="output">Where the file is written. Left open for the caller to dispose.</param>
    /// <param name="sampleRate">The rate the header claims, which is what a player paces the file by.</param>
    /// <param name="channels">Interleaving of every span handed to <see cref="WriteAudio"/>.</param>
    public WavStreamWriter(Stream output, int sampleRate, int channels)
    {
        if (!output.CanSeek) throw new ArgumentException("A WAV header is patched after the fact, so this has to seek.", nameof(output));
        if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));

        this.output = output;
        this.channels = channels;

        // Measured from here rather than from zero, so this can be written into
        // a stream that already has something in it.
        start = output.Position;

        WavWriter.WriteHeader(output, 0, sampleRate, channels);
    }

    /// <summary>Sample frames written so far — a stereo pair counts as one.</summary>
    public long SampleCount => dataBytes / sizeof(short) / channels;

    /// <summary>
    /// True once there is no room left under RIFF's ceiling. A recorder is
    /// expected to stop and say so, rather than to find out by being thrown at.
    /// </summary>
    public bool IsFull => dataBytes >= MaximumDataBytes;

    /// <summary>Appends interleaved float samples as 16-bit PCM.</summary>
    public void WriteAudio(ReadOnlySpan<float> interleaved)
    {
        if (closed) throw new InvalidOperationException("This file has already been finished.");
        if (interleaved.Length == 0) return;

        var wanted = interleaved.Length * sizeof(short);

        if (dataBytes + wanted > MaximumDataBytes)
            throw new InvalidOperationException("A WAV cannot exceed 4 GB. Record a shorter take.");

        if (pcm.Length < wanted) pcm = new byte[wanted];

        for (var i = 0; i < interleaved.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * sizeof(short)), WavWriter.ToPcm16(interleaved[i]));

        output.Write(pcm.AsSpan(0, wanted));
        dataBytes += wanted;
    }

    /// <summary>Fills in the two lengths the header could not know up front.</summary>
    public void Dispose()
    {
        if (closed) return;
        closed = true;

        // Read before the first patch, because patching seeks and the caller is
        // owed the position it would have had.
        var end = output.Position;

        Patch(WavWriter.RiffSizeOffset, (uint)(36 + dataBytes));
        Patch(WavWriter.DataSizeOffset, (uint)dataBytes);

        output.Seek(end, SeekOrigin.Begin);
        output.Flush();
    }

    private void Patch(int offset, uint value)
    {
        Span<byte> word = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(word, value);

        output.Seek(start + offset, SeekOrigin.Begin);
        output.Write(word);
    }
}
