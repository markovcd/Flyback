using System.Text.Json.Nodes;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Effects;

/// <summary>
/// The one preset you have to play: each key plucks a string, four at once, and a little
/// reverb puts them in a room.
/// </summary>
/// <remarks>
/// Four MIDI Ins on voices 1 to 4, so each held note gets a string of its own (ADR-0062).
/// Nothing moves on its own, so with no key down it is silent. The gate is the damper:
/// held, a string rings for two seconds; let go, it stops in a twentieth of one.
/// </remarks>
internal static class PlayedPreset
{
    public const string Name = "Played";

    public const int Voices = 4;

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // Four notes summed; a level of a half each leaves room for a full chord.
        var desk = b.Add("math.mixer", (1, 0.5f), (3, 0.5f), (5, 0.5f), (7, 0.5f));
        var room = b.Add(ReverbModule.TypeId, (1, 0.45f), (2, 0.45f), (3, 0.2f));
        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.6f));

        for (var voice = 1; voice <= Voices; voice++)
        {
            var keys = b.Add(NodeCatalog.MidiTypeId);
            keys.SetState(MidiExtra.StateKey, new JsonObject { [MidiExtra.IndexField] = (float)voice });

            var note = b.Add("audio.note");
            var damper = b.Add("math.remap", (1, 0f), (2, 1f), (3, -1.3f), (4, 0.3f));
            var pluck = b.Add(NodeCatalog.StringTypeId, (4, 0.4f));

            b.Wire(keys, 0, note, 0)
             .Wire(note, 0, pluck, 2)
             .Wire(keys, 3, pluck, 1)
             .Wire(keys, 1, damper, 0)
             .Wire(damper, 0, pluck, 3)
             .Wire(pluck, 0, desk, (voice - 1) * 2);

            b.Group($"Voice {voice}", keys, note, damper, pluck);
        }

        b.Wire(desk, 0, room, 0)
         .Wire(room, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(room, 1, output, NodeCatalog.OutputRightPort);

        return b.Build();
    }
}
