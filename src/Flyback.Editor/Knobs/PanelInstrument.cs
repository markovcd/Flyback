using Flyback.App.Midi;

namespace Flyback.App.Knobs;

/// <summary>One instrument plugged in and known by name: its device id, which a binding stores, and its profile.</summary>
internal sealed record PanelInstrument(string Id, InstrumentProfile Profile);