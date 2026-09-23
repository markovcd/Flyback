using System.Text.Json.Nodes;
using Flyback.App.Controls;
using Flyback.App.Midi;
using Flyback.Core.Graph;

namespace Flyback.App;

/// <summary>
/// An instrument as a fragment ready to add to a patch: a Clock In where it
/// conducts, and a MIDI In per track, named after the track and listening on
/// its channel, stacked in columns. Loose rather than boxed: a group is drawn
/// as one box with every socket on it, and fifty sockets on a box is not a
/// thing anybody wires from.
/// </summary>
/// <remarks>
/// Built from the profile when it is picked rather than kept as a preset,
/// because the one thing a preset could not know is the device id the port has
/// on this machine, and every module here stores it.
/// </remarks>
internal static class InstrumentScaffold
{
    /// <summary>Room between one module's foot and the next one's head, and between columns.</summary>
    private const double Gap = 14;

    /// <summary>
    /// Modules to a column. A drum machine's dozen tracks in one column stand
    /// taller than a screen at any zoom a wire can be read at; two columns of
    /// seven fit beside a preview.
    /// </summary>
    private const int Rows = 7;

    /// <summary>What the module list says the pick adds.</summary>
    public static string Describe(InstrumentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var tracks = profile.Tracks.Count == 1 ? "1 track" : $"{profile.Tracks.Count} tracks";

        return profile.Conducts ? $"{tracks} and a clock, one module each." : $"{tracks}, one module each.";
    }

    /// <summary>The fragment for <paramref name="profile"/> as it is plugged in under <paramref name="device"/>.</summary>
    public static Patch Build(string device, InstrumentProfile profile, ModuleCatalog? modules = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(profile);

        var builder = new PatchBuilder(modules);
        var placed = 0;
        var y = 0d;

        // Down a column, then on to the next; the row count is what wraps.
        (double X, double Y) Next(string typeId)
        {
            if (placed > 0 && placed % Rows == 0) y = 0d;

            var at = ((placed / Rows) * (NodeGeometry.Width + Gap), y);

            y += NodeGeometry.Height(NodeCatalog.Require(typeId)) + Gap;
            placed++;

            return at;
        }

        if (profile.Conducts)
        {
            var (x, top) = Next(NodeCatalog.ClockTypeId);
            var clock = builder.Add(NodeCatalog.ClockTypeId, x, top);

            clock.Name = "Clock";
            clock.SetState(MidiClockExtra.StateKey, new JsonObject { [MidiClockExtra.DeviceField] = device });
        }

        foreach (var track in profile.Tracks)
        {
            var (x, top) = Next(NodeCatalog.MidiTypeId);
            var midi = builder.Add(NodeCatalog.MidiTypeId, x, top);

            midi.Name = track.Name.Length <= NodeInstance.NameLimit ? track.Name : track.Name[..NodeInstance.NameLimit];
            midi.SetState(MidiExtra.StateKey, new JsonObject
            {
                [MidiExtra.DeviceField] = device,
                [MidiExtra.ChannelField] = (float)track.Channel,
            });
        }

        return builder.Patch;
    }
}
