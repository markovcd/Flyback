using System.Runtime.InteropServices;
using Flyback.Plugins.Ndi;
using Flyback.Plugins.Video;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Ndi.Tests;

/// <summary>What a frame is handed to NDI as, laid out as NDI's C header lays it out.</summary>
public class NdiFeedTests
{
    [Fact]
    public void A_frame_is_laid_out_as_NDI_s_header_has_it()
    {
        Marshal.SizeOf<NdiVideoFrame>().ShouldBe(72);
        Marshal.OffsetOf<NdiVideoFrame>(nameof(NdiVideoFrame.FrameFormat)).ShouldBe(24);
        Marshal.OffsetOf<NdiVideoFrame>(nameof(NdiVideoFrame.Timecode)).ShouldBe(32);
        Marshal.OffsetOf<NdiVideoFrame>(nameof(NdiVideoFrame.Data)).ShouldBe(40);
        Marshal.OffsetOf<NdiVideoFrame>(nameof(NdiVideoFrame.LineStride)).ShouldBe(48);
        Marshal.OffsetOf<NdiVideoFrame>(nameof(NdiVideoFrame.Metadata)).ShouldBe(56);
        Marshal.OffsetOf<NdiVideoFrame>(nameof(NdiVideoFrame.Timestamp)).ShouldBe(64);
    }

    [Fact]
    public void A_sender_s_settings_are_laid_out_as_NDI_s_header_has_them()
    {
        Marshal.SizeOf<NdiSendCreate>().ShouldBe(24);
        Marshal.OffsetOf<NdiSendCreate>(nameof(NdiSendCreate.ClockVideo)).ShouldBe(16);
        Marshal.OffsetOf<NdiSendCreate>(nameof(NdiSendCreate.ClockAudio)).ShouldBe(17);
    }

    [Fact]
    public void The_pixels_are_BGRX_as_NDI_spells_it() =>
        BitConverter.GetBytes(NdiVideoFrame.Bgrx).ShouldBe("BGRX"u8.ToArray());

    [Fact]
    public void A_frame_carries_its_moment_as_NDI_s_timecode()
    {
        var frame = NdiFeed.Describe(new VideoFormat(1280, 720, 30), NdiFeed.Rate(30), 5120, TimeSpan.FromSeconds(1.5));

        frame.Timecode.ShouldBe(15_000_000);
        frame.Width.ShouldBe(1280);
        frame.Height.ShouldBe(720);
        frame.LineStride.ShouldBe(5120);
        frame.FrameFormat.ShouldBe(NdiVideoFrame.Progressive);
        frame.AspectRatio.ShouldBe(1280f / 720f);
    }

    [Theory]
    [InlineData(30d, 30000)]
    [InlineData(29.97d, 29970)]
    [InlineData(60d, 60000)]
    public void A_frame_rate_is_a_fraction_over_a_thousand(double fps, int numerator) =>
        NdiFeed.Rate(fps).ShouldBe((numerator, 1000));

    [Fact]
    public void Without_the_runtime_the_output_says_it_cannot_send_and_how_to_get_it()
    {
        Assert.SkipWhen(NdiRuntime.Find() is not null, "This machine has the NDI runtime.");

        var output = new NdiVideoOutput();

        output.IsSupported.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => output.Create(new VideoFormat(64, 36, 30), Flyback.Plugins.Settings.SettingValues.None))
            .Message.ShouldContain(NdiRuntime.Download);
    }
}
