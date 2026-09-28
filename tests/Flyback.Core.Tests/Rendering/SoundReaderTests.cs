using Flyback.Core.Compile;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Rendering;

/// <summary>
/// Reading a sound file of either kind: a WAV here, an MP3 through ffmpeg. The MP3s
/// are encoded by the MP3 take format, and the tests that need one are skipped
/// where there is no ffmpeg.
/// </summary>
public class SoundReaderTests : IDisposable
{
    private const int Rate = GlobalConstants.SampleRate;

    private static readonly string? Encoder = Ffmpeg.Resolve(null);

    private readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "flyback-sound-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    private string File(string name)
    {
        Directory.CreateDirectory(folder);

        return Path.Combine(folder, name);
    }

    /// <summary>A 440 Hz tone of <paramref name="seconds"/> at half level, left only where <paramref name="leftOnly"/>.</summary>
    private string Mp3(string name, double seconds, int channels = 1, bool leftOnly = false)
    {
        var path = File(name);
        var frames = (int)(seconds * Rate);
        var interleaved = new float[frames * channels];

        for (var i = 0; i < frames; i++)
        {
            var value = 0.5f * MathF.Sin(2f * MathF.PI * 440f * i / Rate);

            for (var c = 0; c < channels; c++)
                interleaved[i * channels + c] = leftOnly && c > 0 ? 0f : value;
        }

        using (var clip = ClipWriter.Open(new ClipTarget(
            path, ClipFormats.ById("mp3")!, SampleRate: Rate, Channels: channels, Ffmpeg: Encoder)))
        {
            clip.WriteAudio(interleaved);
        }

        return path;
    }

    private static float Peak(LoadedSample clip) => clip.Samples.Max(MathF.Abs);

    /// <summary>ffmpeg trims what the encoder padded either end with, so the length is the tone's to the sample.</summary>
    [Fact]
    public void An_mp3_is_as_long_as_the_sound_it_was_made_from()
    {
        Assert.SkipWhen(Encoder is null, "no ffmpeg on this machine");

        var clip = SoundReader.Read(Mp3("tone.mp3", 0.5), Encoder, out var fault).ShouldNotBeNull();

        fault.ShouldBe(SoundFault.None);
        clip.SampleRate.ShouldBe(Rate);
        clip.Seconds.ShouldBe(0.5f, 1e-3f);
        Peak(clip).ShouldBe(0.5f, 0.05f);
    }

    /// <summary>Mixed down the way a WAV is, the average of the channels.</summary>
    [Fact]
    public void A_stereo_mp3_becomes_the_average_of_its_channels()
    {
        Assert.SkipWhen(Encoder is null, "no ffmpeg on this machine");

        var clip = SoundReader.Read(Mp3("left.mp3", 0.5, channels: 2, leftOnly: true), Encoder, out _).ShouldNotBeNull();

        Peak(clip).ShouldBe(0.25f, 0.03f);
    }

    /// <summary>What a bundle carries is bytes, and they read as the file does.</summary>
    [Fact]
    public void An_mp3_read_from_bytes_is_the_mp3_read_from_its_file()
    {
        Assert.SkipWhen(Encoder is null, "no ffmpeg on this machine");

        var path = Mp3("bytes.mp3", 0.25);
        var fromFile = SoundReader.Read(path, Encoder, out _).ShouldNotBeNull();
        var fromBytes = SoundReader.Read(new MemoryStream(System.IO.File.ReadAllBytes(path)), Encoder, out var fault)
            .ShouldNotBeNull();

        fault.ShouldBe(SoundFault.None);
        fromBytes.Samples.ShouldBe(fromFile.Samples);
    }

    [Fact]
    public void An_mp3_that_ffmpeg_cannot_decode_says_so()
    {
        Assert.SkipWhen(Encoder is null, "no ffmpeg on this machine");

        var path = File("broken.mp3");
        System.IO.File.WriteAllBytes(path, [(byte)'I', (byte)'D', (byte)'3', 4, 0, 0, 0, 0, 0, 0, 1, 2, 3]);

        SoundReader.Read(path, Encoder, out var fault).ShouldBeNull();
        fault.ShouldBe(SoundFault.Undecoded);
    }

    /// <summary>Refused by what it needs, not as a file that is not a sound.</summary>
    [Fact]
    public void An_mp3_with_no_ffmpeg_to_read_it_is_refused_for_want_of_one()
    {
        var path = File("tagged.mp3");
        System.IO.File.WriteAllBytes(path, [(byte)'I', (byte)'D', (byte)'3', 4, 0, 0, 0, 0, 0, 0]);

        SoundReader.Read(path, ffmpeg: null, out var fault).ShouldBeNull();
        fault.ShouldBe(SoundFault.NoFfmpeg);
    }

    [Fact]
    public void A_wav_is_read_without_ffmpeg()
    {
        var path = File("tone.wav");

        using (var file = System.IO.File.Create(path)) WavWriter.Write(file, [0f, 0.5f, -0.5f], Rate, 1);

        var clip = SoundReader.Read(path, ffmpeg: null, out var fault).ShouldNotBeNull();

        fault.ShouldBe(SoundFault.None);
        clip.Samples.Length.ShouldBe(3);
    }

    [Fact]
    public void Text_is_not_a_sound()
    {
        var path = File("notes.mp3");
        System.IO.File.WriteAllText(path, "not a sound at all");

        SoundReader.Read(path, Encoder, out var fault).ShouldBeNull();
        fault.ShouldBe(SoundFault.NotSound);
    }

    [Theory]
    [InlineData(new byte[] { (byte)'I', (byte)'D', (byte)'3', 4 }, true)]
    [InlineData(new byte[] { 0xFF, 0xFB, 0x90, 0x64 }, true)]
    [InlineData(new byte[] { 0xFF, 0xF3, 0x48, 0xC4 }, true)]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, false)]
    [InlineData(new byte[] { 0xFF, 0xFB, 0xF0, 0x64 }, false)]
    [InlineData(new byte[] { 0xFF, 0xF9, 0x90, 0x64 }, false)]
    public void An_mp3_is_known_by_its_first_bytes(byte[] head, bool mp3) =>
        Mp3Reader.Looks(head).ShouldBe(mp3);
}
