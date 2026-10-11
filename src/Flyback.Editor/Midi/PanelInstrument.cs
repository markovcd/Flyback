using Flyback.Ui.Midi;

namespace Flyback.Editor.Midi;

/// <summary>One instrument plugged in and known by name: its device id, which a binding stores, and its profile.</summary>
internal sealed record PanelInstrument(string Id, InstrumentProfile Profile)
{
    /// <summary>Every instrument plugged in right now that <paramref name="instruments"/> knows.</summary>
    public static IReadOnlyList<PanelInstrument> PluggedIn(MidiHub midi, InstrumentLibrary instruments) => midi.Sources
        .Select(source => (source, Profile: instruments.For(source)))
        .Where(pair => pair.Profile is not null)
        .Select(pair => new PanelInstrument(pair.source.Id, pair.Profile!))
        .ToList();
}
