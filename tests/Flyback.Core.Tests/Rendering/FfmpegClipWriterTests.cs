using Flyback.Engine.Render;
using Flyback.Engine.Render.ClipWriters;
using Flyback.Tests;
using Shouldly;

namespace Flyback.Core.Tests.Rendering;

/// <summary>
/// Writing a clip through ffmpeg. Skipped where there is no ffmpeg to write with,
/// since the whole point of ADR-0089 is that a machine without one loses these
/// formats and nothing else.
/// </summary>
/// <remarks>
/// What came back is decoded by ffmpeg itself (<see cref="Decoded"/>). What is checked
/// is what this program is responsible for: that a file appears, that it is the
/// container asked for, that it holds as many pictures and as much sound as were fed
/// in, and that neither of the temporary files the sound pass needs is left behind.
/// </remarks>
public class FfmpegClipWriterTests : IDisposable
{
    private const int Width = 64;
    private const int Height = 48;
    private const double Rate = 10d;
    private const int Channels = 2;

    /// <summary>How much longer a decoded sound may be than what was written: AAC pads its last packet out to 1,024 frames.</summary>
    private const double Padding = 1024d / GlobalConstants.SampleRate;

    /// <summary>What is on this machine, asked once.</summary>
    private static readonly string? Encoder = Ffmpeg.Resolve(null);

    private readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "flyback-ffmpeg-" + Guid.NewGuid().ToString("N"));

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

    /// <summary>A frame of something, different every time so no encoder can collapse the lot.</summary>
    private static byte[] Frame(int number)
    {
        var pixels = new byte[Width * Height * 4];

        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)(i + number);
            pixels[i + 1] = (byte)(number * 7);
            pixels[i + 2] = (byte)(i / 4 % 251);
            pixels[i + 3] = 255;
        }

        return pixels;
    }

    /// <summary>How many seconds of sound <paramref name="path"/> decodes to.</summary>
    private static double Heard(string path) =>
        Decoded.Sound(Encoder!, path, GlobalConstants.SampleRate) / (double)GlobalConstants.SampleRate;

    private static float[] Samples(int frames)
    {
        var samples = new float[frames * Channels];

        for (var i = 0; i < samples.Length; i++) samples[i] = MathF.Sin(i * 0.01f) * 0.5f;

        return samples;
    }

    /// <summary>
    /// Writes a clip of <paramref name="frames"/> frames and the sound that goes
    /// under them, and gives back the file it wrote.
    /// </summary>
    private string Write(ClipFormat format, string name, int frames = 5, bool sound = true)
    {
        var path = File(name);

        using (var clip = ClipWriter.Open(new ClipTarget(
            path,
            format,
            Width,
            Height,
            Rate,
            SampleRate: sound ? GlobalConstants.SampleRate : 0,
            Channels: sound ? Channels : 0,
            Ffmpeg: Encoder)))
        {
            for (var frame = 0; frame < frames; frame++)
            {
                if (format.HasPicture) clip.WriteFrame(Frame(frame), Width * 4);

                if (sound) clip.WriteAudio(Samples((int)(GlobalConstants.SampleRate / Rate)));
            }

            if (format.HasPicture) clip.FrameCount.ShouldBe(frames);
        }

        return path;
    }

    /// <summary>
    /// The index ahead of the data, which is what <c>+faststart</c> asks for and
    /// what a page needs to start playing before the file has arrived. Asked of
    /// the pass that writes the finished file, whichever that is.
    /// </summary>
    [Theory]
    [TestCategory(TestCategory.Ffmpeg)]
    [InlineData(true)]
    [InlineData(false)]
    public void An_mp4_is_indexed_at_the_front_with_or_without_sound(bool sound)
    {
        TestCategory.Ffmpeg.Require(Encoder is not null, "no ffmpeg on this machine");

        var bytes = System.IO.File.ReadAllBytes(Write(ClipFormats.H264Mp4, "fast.mp4", sound: sound));

        var text = System.Text.Encoding.Latin1.GetString(bytes);

        text.IndexOf("moov", StringComparison.Ordinal).ShouldBeGreaterThan(0);
        text.IndexOf("moov", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("mdat", StringComparison.Ordinal));
    }

    /// <summary>An ffmpeg that will not start leaves nothing of the sound pass behind it.</summary>
    [Fact]
    public void An_encoder_that_will_not_start_leaves_no_files()
    {
        var path = File("never.mp4");
        var impostor = File("not-ffmpeg.txt");

        System.IO.File.WriteAllText(impostor, "not a program");

        Should.Throw<Exception>(() => new FfmpegClipWriter(
            new ClipTarget(
                path,
                ClipFormats.H264Mp4,
                Width,
                Height,
                Rate,
                SampleRate: GlobalConstants.SampleRate,
                Channels: Channels),
            impostor));

        Directory.GetFiles(folder).ShouldBe([impostor]);
    }

    [Theory]
    [TestCategory(TestCategory.Ffmpeg)]
    [InlineData("mp4")]
    [InlineData("hevc")]
    [InlineData("webm")]
    [InlineData("prores")]
    public void A_clip_of_both_streams_is_written(string id)
    {
        TestCategory.Ffmpeg.Require(Encoder is not null, "no ffmpeg on this machine");

        var format = ClipFormats.ById(id)!;
        var path = Write(format, $"both{format.Extension}");

        Decoded.Pictures(Encoder!, path).ShouldBe(5);
        Heard(path).ShouldBe(5 / Rate, Padding);
    }

    /// <summary>
    /// The sound is written beside the picture and muxed in at the end, so a
    /// failure to tidy up would leave a second copy of everything recorded next
    /// to the file — which on a long take is gigabytes.
    /// </summary>
    [Fact]
    [TestCategory(TestCategory.Ffmpeg)]
    public void The_two_files_the_sound_pass_needs_are_gone_afterwards()
    {
        TestCategory.Ffmpeg.Require(Encoder is not null, "no ffmpeg on this machine");

        var path = Write(ClipFormats.H264Mp4, "tidy.mp4");

        Directory.GetFiles(folder).ShouldBe([path]);
    }

    /// <summary>One process and no second pass, which is the path a patch with nothing in its 'left' takes.</summary>
    [Fact]
    [TestCategory(TestCategory.Ffmpeg)]
    public void A_clip_with_no_sound_is_written_straight_through()
    {
        TestCategory.Ffmpeg.Require(Encoder is not null, "no ffmpeg on this machine");

        var path = Write(ClipFormats.H264Mp4, "silent.mp4", sound: false);

        Decoded.Pictures(Encoder!, path).ShouldBe(5);
        Directory.GetFiles(folder).ShouldBe([path]);
    }

    [Theory]
    [TestCategory(TestCategory.Ffmpeg)]
    [InlineData("mp3")]
    [InlineData("m4a")]
    [InlineData("flac")]
    public void A_sound_on_its_own_is_written(string id)
    {
        TestCategory.Ffmpeg.Require(Encoder is not null, "no ffmpeg on this machine");

        var format = ClipFormats.ById(id)!;
        var path = Write(format, $"sound{format.Extension}", frames: 20);

        Heard(path).ShouldBe(20 / Rate, Padding);
    }

    /// <summary>
    /// A frame the source did not redraw in time. The pacer asks for it more than
    /// once so the file keeps its rate, and every copy has to reach the encoder —
    /// a clip a second short of what it claims is the failure this prevents.
    /// </summary>
    [Fact]
    [TestCategory(TestCategory.Ffmpeg)]
    public void A_repeated_frame_is_written_as_many_times_as_it_is_asked_for()
    {
        TestCategory.Ffmpeg.Require(Encoder is not null, "no ffmpeg on this machine");

        var path = File("repeated.mp4");

        using (var clip = ClipWriter.Open(new ClipTarget(
            path, ClipFormats.H264Mp4, Width, Height, Rate, Ffmpeg: Encoder)))
        {
            clip.WriteFrame(Frame(0), Width * 4, repeat: 7);
            clip.WriteFrame(Frame(1), Width * 4, repeat: 3);

            clip.FrameCount.ShouldBe(10);
        }

        Decoded.Pictures(Encoder!, path).ShouldBe(10);
    }

    /// <summary>
    /// The buffer a caller hands over need not be packed tight, and a row stride
    /// wider than the frame is the shape a cropped readback arrives in.
    /// </summary>
    [Fact]
    [TestCategory(TestCategory.Ffmpeg)]
    public void A_frame_wider_in_memory_than_on_screen_is_written()
    {
        TestCategory.Ffmpeg.Require(Encoder is not null, "no ffmpeg on this machine");

        var stride = Width * 4 + 64;
        var pixels = new byte[stride * Height];

        for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)i;

        var path = File("strided.mp4");

        using (var clip = ClipWriter.Open(new ClipTarget(
            path, ClipFormats.H264Mp4, Width, Height, Rate, Ffmpeg: Encoder)))
        {
            for (var frame = 0; frame < 4; frame++) clip.WriteFrame(pixels, stride);
        }

        Decoded.Pictures(Encoder!, path).ShouldBe(4);
    }

    /// <summary>
    /// A frame size ffmpeg's own pixel format will not take. Padded to the next
    /// even size rather than refused, since the alternative is a settings list
    /// that silently excludes sizes — see ADR-0089.
    /// </summary>
    [Fact]
    [TestCategory(TestCategory.Ffmpeg)]
    public void An_odd_frame_size_is_written_rather_than_refused()
    {
        TestCategory.Ffmpeg.Require(Encoder is not null, "no ffmpeg on this machine");

        var path = File("odd.mp4");
        var stride = 65 * 4;

        using (var clip = ClipWriter.Open(new ClipTarget(
            path, ClipFormats.H264Mp4, 65, 49, Rate, Ffmpeg: Encoder)))
        {
            for (var frame = 0; frame < 4; frame++) clip.WriteFrame(new byte[stride * 49], stride);
        }

        Decoded.Pictures(Encoder!, path).ShouldBe(4);
    }

    /// <summary>
    /// A format needing ffmpeg, opened without one. Refused where it is asked for
    /// rather than where the first frame is written, so that nothing has been
    /// rendered by the time anybody is told.
    /// </summary>
    [Fact]
    public void A_format_needing_ffmpeg_is_refused_without_one() =>
        Should.Throw<InvalidOperationException>(() => ClipWriter.Open(
            new ClipTarget(File("none.mp4"), ClipFormats.H264Mp4, Width, Height, Rate)));

    /// <summary>
    /// Arguments ffmpeg will not take. What it said on the way out is the only
    /// account of why, so it has to survive as far as the caller.
    /// </summary>
    [Fact]
    [TestCategory(TestCategory.Ffmpeg)]
    public void What_ffmpeg_refused_is_said_rather_than_swallowed()
    {
        TestCategory.Ffmpeg.Require(Encoder is not null, "no ffmpeg on this machine");

        var nonsense = ClipFormats.H264Mp4 with { Picture = "-c:v no-such-encoder" };

        var trouble = Should.Throw<InvalidOperationException>(() =>
            Write(nonsense, "refused.mp4", frames: 2, sound: false));

        trouble.Message.ShouldContain("no-such-encoder");
    }

    /// <summary>
    /// A path pointing at nothing. Falls back to <c>PATH</c> rather than failing,
    /// because a setting left pointing at an ffmpeg somebody moved should not cost
    /// them the formats the one on <c>PATH</c> can still write.
    /// </summary>
    [Fact]
    [TestCategory(TestCategory.Ffmpeg)]
    public void An_ffmpeg_that_is_not_there_falls_back_to_the_one_on_the_path()
    {
        TestCategory.Ffmpeg.Require(Encoder is not null, "no ffmpeg on this machine");

        Ffmpeg.Resolve(Path.Combine(folder, "not-ffmpeg")).ShouldBe(Encoder);
    }

    [Fact]
    [TestCategory(TestCategory.Ffmpeg)]
    public void The_ffmpeg_found_says_what_it_is()
    {
        TestCategory.Ffmpeg.Require(Encoder is not null, "no ffmpeg on this machine");

        Ffmpeg.Version(Encoder).ShouldStartWith("ffmpeg version");
    }

    /// <summary>A path that is not a program at all, asked what it is.</summary>
    [Fact]
    public void Something_that_is_not_ffmpeg_says_nothing() =>
        Ffmpeg.Version(File("empty")).ShouldBeNull();
}
