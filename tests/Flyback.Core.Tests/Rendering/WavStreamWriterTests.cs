using System.Buffers.Binary;
using System.IO.Pipes;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Rendering;

/// <summary>
/// A recorded WAV and an exported one are the same file, and these exist to keep
/// them that way: the streaming writer learns its length at the end rather than
/// the start, and that is the only thing about it that is allowed to differ.
/// </summary>
public class WavStreamWriterTests
{
    private const int SampleRate = GlobalConstants.SampleRate;
    private const int Channels = 2;

    /// <summary>A ramp through the whole range, including both rails.</summary>
    private static float[] Signal(int frames)
    {
        var samples = new float[frames * Channels];

        for (var i = 0; i < samples.Length; i++)
            samples[i] = -1f + 2f * i / (samples.Length - 1);

        return samples;
    }

    private static byte[] Streamed(float[] samples, int chunk)
    {
        using var memory = new MemoryStream();

        using (var writer = new WavStreamWriter(memory, SampleRate, Channels))
            for (var at = 0; at < samples.Length; at += chunk)
                writer.WriteAudio(samples.AsSpan(at, Math.Min(chunk, samples.Length - at)));

        return memory.ToArray();
    }

    private static byte[] OneShot(float[] samples)
    {
        using var memory = new MemoryStream();
        WavWriter.Write(memory, samples, SampleRate, Channels);

        return memory.ToArray();
    }

    /// <summary>
    /// The point of the whole class. Chunked into buffer-sized pieces the way a
    /// sound callback would deliver them, the bytes are the export's bytes.
    /// </summary>
    [Fact]
    public void A_streamed_take_is_byte_for_byte_the_exported_file()
    {
        var samples = Signal(1000);

        Streamed(samples, chunk: 512).ShouldBe(OneShot(samples));
    }

    /// <summary>
    /// And it does not matter where the buffer boundaries fell — a device that
    /// hands over an odd number of frames is not a different file.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(37)]
    [InlineData(4096)]
    public void The_chunking_does_not_show(int chunk)
    {
        var samples = Signal(1000);

        Streamed(samples, chunk).ShouldBe(OneShot(samples));
    }

    /// <summary>An empty take is still a valid file, just one with no samples in it.</summary>
    [Fact]
    public void A_take_with_nothing_in_it_is_a_header_and_no_more()
    {
        using var memory = new MemoryStream();

        using (var _ = new WavStreamWriter(memory, SampleRate, Channels)) { }

        memory.ToArray().Length.ShouldBe(44);
        memory.ToArray().ShouldBe(OneShot([]));
    }

    [Fact]
    public void Frames_are_counted_rather_than_samples()
    {
        using var memory = new MemoryStream();
        using var writer = new WavStreamWriter(memory, SampleRate, Channels);

        writer.WriteAudio(new float[200]);

        writer.SampleCount.ShouldBe(100);
    }

    /// <summary>
    /// Until it is disposed the header still claims nothing, which is the whole
    /// reason disposing matters.
    /// </summary>
    [Fact]
    public void The_sizes_land_only_once_the_take_has_ended()
    {
        using var memory = new MemoryStream();
        var writer = new WavStreamWriter(memory, SampleRate, Channels);

        writer.WriteAudio(new float[200]);

        DataSize(memory).ShouldBe(0u);

        writer.Dispose();

        DataSize(memory).ShouldBe(400u);
        RiffSize(memory).ShouldBe(436u);

        static uint DataSize(MemoryStream memory) => BinaryPrimitives.ReadUInt32LittleEndian(memory.ToArray().AsSpan(40, 4));
        static uint RiffSize(MemoryStream memory) => BinaryPrimitives.ReadUInt32LittleEndian(memory.ToArray().AsSpan(4, 4));
    }

    /// <summary>
    /// Written into a stream that already holds something, the patch has to find
    /// the header where it actually is rather than at the front of the file.
    /// </summary>
    [Fact]
    public void The_header_is_patched_where_it_was_written()
    {
        using var memory = new MemoryStream();
        memory.Write(new byte[16]);

        var samples = Signal(50);

        using (var writer = new WavStreamWriter(memory, SampleRate, Channels))
            writer.WriteAudio(samples);

        memory.ToArray().AsSpan(16).ToArray().ShouldBe(OneShot(samples));
    }

    [Fact]
    public void A_finished_file_refuses_more()
    {
        using var memory = new MemoryStream();
        var writer = new WavStreamWriter(memory, SampleRate, Channels);
        writer.Dispose();

        Should.Throw<InvalidOperationException>(() => writer.WriteAudio(new float[2]));
    }

    /// <summary>Disposing twice is how a `using` around an explicit stop behaves.</summary>
    [Fact]
    public void Disposing_twice_is_harmless()
    {
        using var memory = new MemoryStream();
        var writer = new WavStreamWriter(memory, SampleRate, Channels);

        writer.WriteAudio(new float[200]);
        writer.Dispose();
        var after = memory.ToArray();

        writer.Dispose();

        memory.ToArray().ShouldBe(after);
    }

    /// <summary>
    /// A pipe cannot be seeked and therefore cannot carry a WAV. Saying so at
    /// construction beats discovering it when the take ends.
    /// </summary>
    [Fact]
    public void A_stream_that_cannot_seek_is_refused()
    {
        using var pipe = new AnonymousPipeServerStream();

        Should.Throw<ArgumentException>(() => new WavStreamWriter(pipe, SampleRate, Channels));
    }
}
