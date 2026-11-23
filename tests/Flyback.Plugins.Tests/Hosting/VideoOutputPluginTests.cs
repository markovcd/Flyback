using Flyback.Plugins.Settings;
using Flyback.Plugins.Video;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Hosting;

/// <summary>A picture output registered by a plugin loaded off disk, and the frames it is handed.</summary>
public class VideoOutputPluginTests
{
    private const string Tally = "flyback.sample.tally";

    private static IVideoOutput Loaded() => ShippedPlugins.Loaded.VideoOutputs.Single(o => o.Id == Tally);

    [Fact]
    public void A_plugin_s_picture_output_reaches_the_catalog()
    {
        ShippedPlugins.Loaded.UsableVideoOutputs.ShouldContain(Loaded());
        ShippedPlugins.Loaded.Provider(Loaded())!.Name.ShouldBe("Sample modules");
    }

    [Fact]
    public void A_feed_takes_frames_of_the_size_it_was_opened_for()
    {
        using var feed = Loaded().Create(new VideoFormat(4, 2, 30), SettingValues.None);
        var pixels = new byte[4 * 4 * 2];

        feed.Send(new VideoFrame(pixels, 16, TimeSpan.Zero));
        feed.Send(new VideoFrame(pixels, 16, TimeSpan.FromSeconds(1 / 30d)));
    }

    [Fact]
    public void A_frame_short_of_pixels_is_refused_by_the_plugin()
    {
        using var feed = Loaded().Create(new VideoFormat(4, 2, 30), SettingValues.None);
        var pixels = new byte[16];

        Should.Throw<ArgumentException>(() => feed.Send(new VideoFrame(pixels, 16, TimeSpan.Zero)));
    }
}
