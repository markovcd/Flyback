using Flyback.App.Controls;
using Flyback.App.Midi;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Midi;

/// <summary>
/// An instrument picked from the module list arrives whole and ready to play
/// from: every track a module on its own channel, the clock following the box,
/// and the lot boxed under the instrument's name.
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

    [Fact]
    public void The_modules_are_boxed_under_the_instruments_name()
    {
        var fragment = InstrumentScaffold.Build(Device, Syntakt());

        var box = fragment.Groups.ShouldNotBeNull().ShouldHaveSingleItem();

        box.Name.ShouldBe("Syntakt");
        box.Members.Count.ShouldBe(fragment.Nodes.Count);
    }

    /// <summary>A column, each module clear of the one above, so nothing lands on anything.</summary>
    [Fact]
    public void The_modules_stand_in_a_column_without_overlapping()
    {
        var fragment = InstrumentScaffold.Build(Device, Syntakt());
        var ordered = fragment.Nodes.OrderBy(node => node.Y).ToList();

        for (var i = 1; i < ordered.Count; i++)
        {
            var above = NodeGeometry.Bounds(ordered[i - 1], NodeCatalog.Require(ordered[i - 1].TypeId));

            ordered[i].X.ShouldBe(ordered[i - 1].X);
            ordered[i].Y.ShouldBeGreaterThan(above.Bottom);
        }
    }

    [Fact]
    public void An_instrument_that_does_not_conduct_gets_no_clock()
    {
        var keyboard = new InstrumentProfile("Keys", ["keys"], false, [new InstrumentTrack("Keys", 1, null)], []);

        var fragment = InstrumentScaffold.Build("midi:keys", keyboard);

        fragment.Nodes.Select(node => node.TypeId).ShouldBe([NodeCatalog.MidiTypeId]);
        fragment.Groups.ShouldBeNull();
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
        InstrumentScaffold.Describe(Syntakt()).ShouldBe("13 tracks and a clock, boxed as one.");
    }
}
