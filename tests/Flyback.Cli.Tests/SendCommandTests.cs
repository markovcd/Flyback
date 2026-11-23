using Flyback.Cli.Commands;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Engine.Graph;
using Flyback.Engine.Render;
using Flyback.Plugins.Settings;
using Flyback.Plugins.Video;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>A patch's picture played into a picture output, frame by frame, as the plugin sees it.</summary>
public sealed class SendCommandTests
{
    private const string Rings = """
        x |> sine(freq: 3, phase: t) |> remap(-1..1, 0..1) |> hsv(hue: _, saturation: 1, value: 1) |> out.color
        """;

    /// <summary>Keeps what each frame said, and a copy of its first pixel.</summary>
    private sealed class Recording : IVideoOutput
    {
        public string Id => "test.recording";

        public string Name => "Recording";

        public bool IsSupported => true;

        public VideoFormat? Opened { get; private set; }

        public List<(TimeSpan Time, int Stride, int Length, uint First)> Frames { get; } = [];

        public bool Closed { get; private set; }

        public IVideoFeed Create(VideoFormat format, SettingValues settings)
        {
            Opened = format;
            return new Feed(this);
        }

        private sealed class Feed(Recording owner) : IVideoFeed
        {
            public void Send(in VideoFrame frame) =>
                owner.Frames.Add((frame.Time, frame.Stride, frame.Bgra.Length, BitConverter.ToUInt32(frame.Bgra[..4])));

            public void Dispose() => owner.Closed = true;
        }
    }

    private sealed class Refusing : IVideoOutput
    {
        public string Id => "test.refusing";

        public string Name => "Refusing";

        public bool IsSupported => true;

        public IVideoFeed Create(VideoFormat format, SettingValues settings) =>
            throw new InvalidOperationException("the receiving program is not running.");
    }

    private static Opened Written(string source)
    {
        var built = Engine.Language.PatchLanguage.Build(source);
        built.Ok.ShouldBeTrue(built.Report);

        return new Opened(built.Patch, new SampleLibrary(), new ImageLibrary());
    }

    private static (int Code, string Out, string Error) Send(IVideoOutput target, VideoFormat format, double seconds, bool json = false)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var code = SendCommand.Run(target, Written(Rings), format, seconds, paced: false, json, output, error, PictureBackend.Processor);

        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void Every_frame_reaches_the_output_at_the_size_it_was_opened_for()
    {
        var target = new Recording();

        var (code, _, error) = Send(target, new VideoFormat(32, 18, 10), 1);

        code.ShouldBe(Exit.Ok, error);
        target.Opened.ShouldBe(new VideoFormat(32, 18, 10));
        target.Frames.Count.ShouldBe(10);
        target.Frames.ShouldAllBe(f => f.Stride == 32 * 4 && f.Length == 32 * 4 * 18);
        target.Closed.ShouldBeTrue();
    }

    [Fact]
    public void Each_frame_carries_its_moment_of_the_patch()
    {
        var target = new Recording();

        Send(target, new VideoFormat(16, 9, 4), 1).Code.ShouldBe(Exit.Ok);

        target.Frames.Select(f => f.Time).ShouldBe([TimeSpan.Zero, TimeSpan.FromSeconds(0.25), TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.75)]);
    }

    [Fact]
    public void The_frames_sent_are_the_patch_s_own_picture()
    {
        var target = new Recording();

        Send(target, new VideoFormat(16, 9, 4), 1).Code.ShouldBe(Exit.Ok);

        target.Frames.Select(f => f.First).Distinct().Count().ShouldBeGreaterThan(1, "the rings move, so the corner pixel changes");
        target.Frames.ShouldAllBe(f => (f.First >> 24) == 0xFF);
    }

    [Fact]
    public void The_answer_says_what_was_sent()
    {
        var (code, output, _) = Send(new Recording(), new VideoFormat(16, 9, 5), 1, json: true);

        code.ShouldBe(Exit.Ok);
        output.ShouldContain("\"output\": \"test.recording\"");
        output.ShouldContain("\"frames\": 5");
    }

    [Fact]
    public void An_output_that_cannot_open_is_said_by_its_id()
    {
        var (code, _, error) = Send(new Refusing(), new VideoFormat(16, 9, 5), 1);

        code.ShouldBe(Exit.Failed);
        error.ShouldContain("test.refusing: the receiving program is not running.");
    }

    [Fact]
    public void A_length_out_of_range_is_refused_before_anything_opens()
    {
        var target = new Recording();

        Send(target, new VideoFormat(16, 9, 5), 0).Code.ShouldBe(Exit.Failed);
        target.Opened.ShouldBeNull();
    }
}
