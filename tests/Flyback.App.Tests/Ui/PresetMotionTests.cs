using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The picture that plays on a tile while a preset is being tried, on its own:
/// the gallery around it waits a second for the pointer to settle and prepares
/// sound first, neither of which this is about.
/// </summary>
public class PresetMotionTests : UiTest
{
    private static Patch Plasma() => Presets.All.Single(p => p.Name == "Plasma").Build(NodeCatalog.BuiltIn);

    private static Opened Files(Patch patch) => new(patch, new SampleLibrary(), new ImageLibrary());

    /// <summary>A still of the size a tile shows, standing in for the thumbnail.</summary>
    private static WriteableBitmap AStill() => new(
        new PixelSize(PresetThumbnails.Width, PresetThumbnails.Height),
        new Vector(96, 96),
        PixelFormat.Bgra8888,
        AlphaFormat.Opaque);

    /// <summary>
    /// Frames are drawn off the UI thread and handed over on it, so the thread
    /// running this has to be let go of for one to arrive.
    /// </summary>
    private static bool Until(Func<bool> done, double seconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);

        while (!done() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        return done();
    }

    [AvaloniaFact]
    public void A_frame_arrives_in_place_of_the_still()
    {
        var still = AStill();
        var picture = new Image { Source = still };

        using var motion = PresetMotion.Play(Files(Plasma()), picture, () => 0);

        Until(() => !ReferenceEquals(picture.Source, still)).ShouldBeTrue("no frame was ever handed over");

        picture.Source.ShouldBeOfType<WriteableBitmap>().ShouldNotBe(still);
    }

    [AvaloniaFact]
    public void The_still_goes_back_when_it_stops()
    {
        var still = AStill();
        var picture = new Image { Source = still };

        var motion = PresetMotion.Play(Files(Plasma()), picture, () => 0);

        Until(() => !ReferenceEquals(picture.Source, still)).ShouldBeTrue("no frame was ever handed over");

        motion.Dispose();

        picture.Source.ShouldBe(still);
    }

    /// <summary>
    /// A patch drawing nothing is a tile left showing its own still rather than one
    /// that goes blank, which is what a preset that only makes sound looks like.
    /// </summary>
    [AvaloniaFact]
    public void A_patch_with_no_picture_leaves_the_tile_alone()
    {
        var still = AStill();
        var picture = new Image { Source = still };

        using var motion = PresetMotion.Play(Files(new Patch()), picture, () => 0);

        // Short, because this waits to prove nothing happens: the patch is turned
        // away before a renderer is ever built.
        Until(() => !ReferenceEquals(picture.Source, still), seconds: 2)
            .ShouldBeFalse("an empty patch has no frame to show");

        picture.Source.ShouldBe(still);
    }
}
