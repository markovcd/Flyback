using System.Text.Json.Nodes;
using Flyback.App.Controls;
using Flyback.App.Midi;
using Flyback.Core.Graph;

namespace Flyback.App;

/// <summary>
/// An instrument as a fragment ready to add to a patch: a Clock In where it
/// conducts, and a MIDI In per track, named after the track and listening on
/// its channel, stacked in a column and boxed under the instrument's name.
/// </summary>
/// <remarks>
/// Built from the profile when it is picked rather than kept as a preset,
/// because the one thing a preset could not know is the device id the port has
/// on this machine, and every module here stores it.
/// </remarks>
internal static class InstrumentScaffold
{
    /// <summary>Room between one module's foot and the next one's head.</summary>
    private const double Gap = 14;

    /// <summary>What the module list says the pick adds.</summary>
    public static string Describe(InstrumentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var tracks = profile.Tracks.Count == 1 ? "1 track" : $"{profile.Tracks.Count} tracks";

        return profile.Conducts ? $"{tracks} and a clock, boxed as one." : $"{tracks}, boxed as one.";
    }

    /// <summary>The fragment for <paramref name="profile"/> as it is plugged in under <paramref name="device"/>.</summary>
    public static Patch Build(string device, InstrumentProfile profile, ModuleCatalog? modules = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(profile);

        var builder = new PatchBuilder(modules);
        var members = new List<Guid>();
        var y = 0d;

        if (profile.Conducts)
        {
            var clock = builder.Add(NodeCatalog.ClockTypeId, 0, y);

            clock.Name = "Clock";
            clock.SetState(MidiClockExtra.StateKey, new JsonObject { [MidiClockExtra.DeviceField] = device });
            members.Add(clock.Id);
            y += Below(NodeCatalog.ClockTypeId);
        }

        foreach (var track in profile.Tracks)
        {
            var midi = builder.Add(NodeCatalog.MidiTypeId, 0, y);

            midi.Name = track.Name.Length <= NodeInstance.NameLimit ? track.Name : track.Name[..NodeInstance.NameLimit];
            midi.SetState(MidiExtra.StateKey, new JsonObject
            {
                [MidiExtra.DeviceField] = device,
                [MidiExtra.ChannelField] = (float)track.Channel,
            });
            members.Add(midi.Id);
            y += Below(NodeCatalog.MidiTypeId);
        }

        var patch = builder.Patch;

        if (patch.Group(members) is { } box) box.Name = profile.Name;

        return patch;
    }

    /// <summary>How far down the next module goes to clear this one.</summary>
    private static double Below(string typeId) => NodeGeometry.Height(NodeCatalog.Require(typeId)) + Gap;
}
