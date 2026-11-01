using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>Each level the text opens inside a box laid out as a band of its own, below the box's own modules.</summary>
public class LevelBandTests
{
    private const string Nested =
        """
        def voice(freq) = sine(freq: freq) |> drive(drive: 2)
        group "A" {
          let a = saw(freq: 2) |> drive(drive: 3)
          group "B" {
            let b = voice(110) |> drive(drive: 4)
          }
        }
        a + b |> out.left
        """;

    private static Patch Built(string source, bool bands)
    {
        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn, bands);

        load.Issues.ShouldBeEmpty(load.Report);

        return load.Patch;
    }

    private static NodeInstance Only(Patch patch, string type, float? drive = null) =>
        patch.Nodes.Single(n => n.TypeId == type && (drive is null || n.InputValues[1] == drive));

    private static double Bottom(Patch patch, params NodeInstance[] nodes) =>
        nodes.Max(n => n.Y + PatchLayout.Metrics.Default.Height(NodeCatalog.BuiltIn.Get(n.TypeId)!));

    [Fact]
    public void A_level_inside_a_box_is_a_band_under_the_box_own_modules_set_in_and_shelved()
    {
        var patch = Built(Nested, bands: true);
        var size = PatchLayout.Metrics.Default;

        var saw = Only(patch, "osc.saw");
        var sine = Only(patch, "osc.sine");
        var outerDrive = Only(patch, "audio.drive", 3);
        var innerDrive = Only(patch, "audio.drive", 4);

        var bandTop = Math.Min(sine.Y, innerDrive.Y);
        bandTop.ShouldBeGreaterThanOrEqualTo(Bottom(patch, saw, outerDrive) + size.ShelfHeight);
        Math.Min(sine.X, innerDrive.X).ShouldBe(Math.Min(saw.X, outerDrive.X) + size.BandIndent, 0.001);

        // The call inside B is a band of its own inside B's.
        Math.Min(sine.Y, Only(patch, "audio.drive", 2).Y).ShouldBeGreaterThan(innerDrive.Y);
    }

    [Fact]
    public void Without_bands_the_box_is_laid_out_flat_as_before() =>
        Built(Nested, bands: false).Nodes.Select(n => (n.TypeId, n.X, n.Y))
            .ShouldBe(PatchLanguage.Build(Nested, NodeCatalog.BuiltIn).Patch.Nodes.Select(n => (n.TypeId, n.X, n.Y)));

    [Fact]
    public void A_patch_with_no_level_inside_a_box_is_laid_out_the_same_with_bands()
    {
        const string source =
            """
            def voice(freq) = sine(freq: freq) |> drive(drive: 2)
            group "A" {
              let a = saw(freq: 2) |> drive(drive: 3)
            }
            a + voice(3) |> out.left
            """;

        Built(source, bands: true).Nodes.Select(n => (n.TypeId, n.X, n.Y))
            .ShouldBe(Built(source, bands: false).Nodes.Select(n => (n.TypeId, n.X, n.Y)));
    }

    [Fact]
    public void A_level_that_placed_nothing_and_holds_one_level_shares_its_chip()
    {
        var load = PatchLanguage.Build(
            """
            def voice(freq) = sine(freq: freq) |> drive(drive: 2)
            group "Band" {
              let pads = for pitch in [220, 330] { voice(pitch) }
            }
            sum(pads) |> out.left
            """,
            NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty(load.Report);

        Level.Bands(load.Levels).ShouldHaveSingleItem().Inside
            .Select(band => band.Label).ShouldBe(["1/2 pitch = 220 › voice", "2/2 pitch = 330 › voice"]);
    }
}
