using System.Diagnostics;
using Flyback.Editor.Capture;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Capture;

/// <summary>The preview's frames, driven the way the render thread drives them, reaching a picture output.</summary>
public class PictureSenderTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    /// <summary>Two pixels a row, two rows, bottom row first as OpenGL reads them back.</summary>
    private static readonly byte[] Drawn =
    [
        1, 2, 3, 10, 4, 5, 6, 20,   // bottom row
        7, 8, 9, 30, 11, 12, 13, 40, // top row
    ];

    private static void Until(Func<bool> done)
    {
        var clock = Stopwatch.StartNew();

        while (!done() && clock.Elapsed < Patience) Thread.Sleep(5);

        done().ShouldBeTrue();
    }

    [Fact]
    public void A_frame_reaches_the_output_blue_first_and_top_row_first()
    {
        var output = new HeldOutput();
        var named = new SettingValues(new Dictionary<string, string> { ["name"] = "Stage left" });

        using (var sender = new PictureSender(output, named, 60, _ => { }))
        {
            sender.Accept(Drawn, 2, 2);
            Until(() => !output.Frames.IsEmpty);
        }

        output.Opened.Single().ShouldBe((new Plugins.Video.VideoFormat(2, 2, 60), "Stage left"));
        output.Frames.First().ShouldBe(new byte[] { 9, 8, 7, 30, 13, 12, 11, 40, 3, 2, 1, 10, 6, 5, 4, 20 });
        output.Closed.ShouldBe(1);
    }

    [Fact]
    public void A_preview_drawn_at_another_size_opens_the_output_again_at_that_size()
    {
        var output = new HeldOutput();

        using (var sender = new PictureSender(output, SettingValues.None, 1000, _ => { }))
        {
            sender.Accept(Drawn, 2, 2);
            Until(() => output.Frames.Count == 1);

            sender.Accept(new byte[4 * 3 * 4], 4, 3);
            Until(() => output.Frames.Count == 2);
        }

        output.Opened.Select(o => (o.Format.Width, o.Format.Height)).ShouldBe([(2, 2), (4, 3)]);
        output.Closed.ShouldBe(2);
    }

    [Fact]
    public void An_output_that_will_not_open_says_why_once()
    {
        var output = new HeldOutput(refuses: "The NDI runtime is not installed.");
        var said = new List<string>();

        using (var sender = new PictureSender(output, SettingValues.None, 60, why => { lock (said) said.Add(why); }))
        {
            sender.Accept(Drawn, 2, 2);
            Until(() => { lock (said) return said.Count > 0; });

            sender.Accept(Drawn, 2, 2);
        }

        said.ShouldBe(["Stopped sending the picture to Held (held): The NDI runtime is not installed."]);
    }

    [Fact]
    public void Frames_drawn_faster_than_the_rate_are_skipped_not_queued()
    {
        var output = new HeldOutput();

        using (var sender = new PictureSender(output, SettingValues.None, 1, _ => { }))
        {
            sender.Accept(Drawn, 2, 2);
            Until(() => sender.Sent == 1);

            for (var i = 0; i < 20; i++)
            {
                sender.Accept(Drawn, 2, 2);
                Thread.Sleep(5);
            }
        }

        output.Frames.Count.ShouldBe(1);
    }
}
