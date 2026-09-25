using Flyback.App.Canvas;
using Flyback.App.Midi;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Midi;

/// <summary>
/// An instrument picked from the module list arrives whole and ready to play
/// from: every track a module on its own channel, the clock following the box,
/// loose on the canvas so the spare tracks are one Delete away.
/// </summary>
public class InstrumentScaffoldTests
{
    private const string Device = "midi:elektron-syntakt";

    private static InstrumentProfile Syntakt() =>
        InstrumentLibrary.Shipped().Profiles.Single(profile => profile.Name == "Syntakt");

    [Fact]
    public void The_syntakt_arrives_as_a_clock_and_a_module_per_track()
    {
        var fragment = InstrumentScaffold.Build(Device, Syntakt());

        fragment.Nodes.Count(node => node.TypeId == NodeCatalog.ClockTypeId).ShouldBe(1);
        fragment.Nodes.Count(node => node.TypeId == NodeCatalog.MidiTypeId).ShouldBe(13);
        fragment.Connections.ShouldBeEmpty();
    }

    /// <summary>The whole point: nothing is left to type in afterwards.</summary>
    [Fact]
    public void Each_track_listens_to_the_box_on_its_own_channel_under_its_own_name()
    {
        var fragment = InstrumentScaffold.Build(Device, Syntakt());

        foreach (var track in Syntakt().Tracks)
        {
            var module = fragment.Nodes.Single(node => node.Name == track.Name);
            var state = new ExtraState(new MidiExtra().Fields, module.StateOf(MidiExtra.StateKey));

            state.Chosen(MidiExtra.DeviceField).ShouldBe(Device);
            state.Number(MidiExtra.ChannelField).ShouldBe(track.Channel);
        }

        var clock = fragment.Nodes.Single(node => node.TypeId == NodeCatalog.ClockTypeId);
        new ExtraState(new MidiClockExtra().Fields, clock.StateOf(MidiClockExtra.StateKey)).Chosen(MidiClockExtra.DeviceField).ShouldBe(Device);
    }

    /// <summary>A group is drawn as one box with every socket on it, which nobody could wire from.</summary>
    [Fact]
    public void The_modules_arrive_loose_rather_than_boxed()
    {
        InstrumentScaffold.Build(Device, Syntakt()).Groups.ShouldBeNull();
    }

    /// <summary>
    /// Columns of seven, each module clear of the one above and each column clear
    /// of the last, so nothing lands on anything and the box fits beside a preview.
    /// </summary>
    [Fact]
    public void The_modules_stand_in_columns_without_overlapping()
    {
        var fragment = InstrumentScaffold.Build(Device, Syntakt());
        var columns = fragment.Nodes.GroupBy(node => node.X).OrderBy(column => column.Key).ToList();

        columns.Count.ShouldBe(2);
        columns[0].Count().ShouldBe(7);
        columns[1].Count().ShouldBe(7);

        for (var c = 1; c < columns.Count; c++)
            columns[c].Key.ShouldBeGreaterThan(columns[c - 1].Key + NodeGeometry.Width);

        foreach (var column in columns)
        {
            var ordered = column.OrderBy(node => node.Y).ToList();

            for (var i = 1; i < ordered.Count; i++)
            {
                var above = Ui.UiTest.Geometry.Bounds(ordered[i - 1], NodeCatalog.Require(ordered[i - 1].TypeId));

                ordered[i].Y.ShouldBeGreaterThan(above.Bottom);
            }
        }
    }

    [Fact]
    public void An_instrument_that_does_not_conduct_gets_no_clock()
    {
        var keyboard = new InstrumentProfile("Keys", ["keys"], false, [new InstrumentTrack("Keys", 1, null)], []);

        var fragment = InstrumentScaffold.Build("midi:keys", keyboard);

        fragment.Nodes.Select(node => node.TypeId).ShouldBe([NodeCatalog.MidiTypeId]);
    }

    /// <summary>What lands compiles as it is, so the first sound is one wire away.</summary>
    [Fact]
    public void The_fragment_compiles_once_it_has_an_output()
    {
        var fragment = InstrumentScaffold.Build(Device, Syntakt());
        var patch = new PatchBuilder(NodeCatalog.BuiltIn);
        var output = patch.Add(NodeCatalog.OutputTypeId, 400, 0);

        PatchClipboard.Paste(patch.Patch, fragment, 0, 0);

        var track = patch.Patch.Nodes.Single(node => node.Name == "Track 1");
        patch.Patch.Connect(track.Id, 1, output.Id, NodeCatalog.OutputLeftPort);

        patch.Patch.CompileForAudio(NodeCatalog.BuiltIn).Issues.ShouldAllBe(issue => issue.Severity != IssueSeverity.Error);
    }

    [Fact]
    public void The_list_says_what_a_pick_adds()
    {
        InstrumentScaffold.Describe(Syntakt()).ShouldBe("13 tracks and a clock, one module each.");
    }
}
