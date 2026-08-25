using System.Buffers.Binary;
using System.Text;
using Flyback.Core.Compile;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Rendering;

/// <summary>
/// Reading a WAV back. The half of the format this repository did not have, and
/// the one that has to survive files it did not write.
/// </summary>
/// <remarks>
/// The round trip against <see cref="WavWriter"/> is the easy half and the least
/// interesting: both were written here, so agreeing proves only that they agree.
/// What matters is the rest — a file from an editor carries chunks between the
/// header and the audio, comes in depths this repository never writes, and is
/// sometimes truncated.
/// </remarks>
public class WavReaderTests
{
    private static byte[] Written(float[] interleaved, int rate, int channels)
    {
        var memory = new MemoryStream();
        WavWriter.Write(memory, interleaved, rate, channels);
        return memory.ToArray();
    }

    private static LoadedSample Read(byte[] bytes)
    {
        var clip = WavReader.Read(new MemoryStream(bytes), out var fault);

        fault.ShouldBe(WavFault.None);
        return clip.ShouldNotBeNull();
    }

    private static WavFault Refused(byte[] bytes)
    {
        WavReader.Read(new MemoryStream(bytes), out var fault).ShouldBeNull();
        return fault;
    }

    [Fact]
    public void What_this_writes_it_reads_back()
    {
        var written = Written([0f, 0.5f, -0.5f, 0.25f], 44100, 1);
        var clip = Read(written);

        clip.SampleRate.ShouldBe(44100);
        clip.Samples.Length.ShouldBe(4);

        // 16-bit PCM, so the values come back quantised rather than exact.
        clip.Samples[1].ShouldBe(0.5f, 0.001f);
        clip.Samples[2].ShouldBe(-0.5f, 0.001f);
    }

    /// <summary>
    /// Mixed down on the way in, because the op that reads a clip is scalar like
    /// every other signal here.
    /// </summary>
    [Fact]
    public void A_stereo_file_becomes_the_sum_of_its_channels()
    {
        // Two frames: (1, 0) and (0.5, -0.5), which average to 0.5 and 0.
        var clip = Read(Written([1f, 0f, 0.5f, -0.5f], 48000, 2));

        clip.Samples.Length.ShouldBe(2);
        clip.Samples[0].ShouldBe(0.5f, 0.001f);
        clip.Samples[1].ShouldBe(0f, 0.001f);
    }

    [Fact]
    public void The_length_is_the_samples_over_the_rate()
    {
        Read(Written(new float[4800], 48000, 1)).Seconds.ShouldBe(0.1f, 1e-5f);
        Read(Written(new float[2205], 44100, 1)).Seconds.ShouldBe(0.05f, 1e-5f);
    }

    /// <summary>
    /// The case this reader exists to survive and a naive one does not: an editor
    /// puts LIST, cue and fact chunks between the header and the audio, and a
    /// reader that assumed the data began at byte 44 would read them as sound.
    /// </summary>
    [Fact]
    public void A_chunk_between_the_header_and_the_audio_is_stepped_over()
    {
        var plain = Written([0.25f, -0.25f], 22050, 1);
        var clip = Read(WithChunkBeforeData(plain, "LIST", [1, 2, 3, 4, 5, 6]));

        clip.SampleRate.ShouldBe(22050);
        clip.Samples.Length.ShouldBe(2);
        clip.Samples[0].ShouldBe(0.25f, 0.001f);
    }

    /// <summary>
    /// A chunk of odd length is followed by a pad byte that its size does not
    /// count. Everything after it is one byte out for a reader that misses this.
    /// </summary>
    [Fact]
    public void An_odd_length_chunk_is_padded_and_the_pad_is_stepped_over_too()
    {
        var plain = Written([0.75f], 8000, 1);
        var clip = Read(WithChunkBeforeData(plain, "note", [7, 7, 7]));

        clip.Samples.Length.ShouldBe(1);
        clip.Samples[0].ShouldBe(0.75f, 0.001f);
    }

    /// <summary>
    /// What was read is still a sound. Refusing a file that plays perfectly
    /// everywhere else, over bytes nobody would have heard, is the worse answer.
    /// </summary>
    [Theory]
    [InlineData(2, 3)]  // one whole frame gone, at two bytes a frame
    [InlineData(3, 2)]  // and a frame and a half, which is two whole ones left
    public void A_truncated_file_gives_back_what_was_there(int cut, int left)
    {
        var written = Written([0.5f, 0.5f, 0.5f, 0.5f], 8000, 1);
        var clip = Read(written[..^cut]);

        clip.Samples.Length.ShouldBe(left);
        clip.Samples[0].ShouldBe(0.5f, 0.001f);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a wave at all, just some text")]
    public void Something_that_is_not_a_wave_is_refused_as_one(string text)
    {
        Refused(Encoding.ASCII.GetBytes(text)).ShouldBe(WavFault.NotWave);
    }

    [Fact]
    public void A_wave_with_no_audio_in_it_is_refused()
    {
        Refused(Written([], 44100, 1)).ShouldBe(WavFault.Empty);
    }

    /// <summary>
    /// A compressed payload is a codec each. Said out loud rather than guessed
    /// at — a sample nobody can read should say so and name itself.
    /// </summary>
    [Fact]
    public void A_wave_this_cannot_decode_says_so_rather_than_guessing()
    {
        var written = Written([0.5f, 0.5f], 44100, 1);

        // Format tag 85, which is MPEG layer 3 in a WAVE wrapper.
        BinaryPrimitives.WriteInt16LittleEndian(written.AsSpan(20, 2), 85);

        Refused(written).ShouldBe(WavFault.Unsupported);
    }

    [Fact]
    public void A_file_that_is_not_there_is_missing_rather_than_a_throw()
    {
        WavReader.Read(Path.Combine(Path.GetTempPath(), "flyback-no-such-sample.wav"), out var fault)
            .ShouldBeNull();

        fault.ShouldBe(WavFault.Missing);
    }

    /// <summary>Puts a chunk between the format chunk and the audio, as an editor does.</summary>
    private static byte[] WithChunkBeforeData(byte[] wav, string name, byte[] body)
    {
        // WavWriter's layout is fixed: 36 bytes of RIFF and fmt, then "data".
        const int dataAt = 36;

        var padded = body.Length + (body.Length & 1);
        var chunk = new byte[8 + padded];

        Encoding.ASCII.GetBytes(name).CopyTo(chunk, 0);
        BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(4, 4), body.Length);
        body.CopyTo(chunk, 8);

        var grown = new byte[wav.Length + chunk.Length];

        wav.AsSpan(0, dataAt).CopyTo(grown);
        chunk.CopyTo(grown, dataAt);
        wav.AsSpan(dataAt).CopyTo(grown.AsSpan(dataAt + chunk.Length));

        // The RIFF size counts everything after itself.
        BinaryPrimitives.WriteInt32LittleEndian(
            grown.AsSpan(WavWriterSizes.RiffSizeOffset, 4), grown.Length - 8);

        return grown;
    }

    /// <summary>The two offsets this test writes back into, named rather than counted twice.</summary>
    private static class WavWriterSizes
    {
        public const int RiffSizeOffset = 4;
    }
}
